using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AiLocal.Node.Hosting;

internal sealed record GitHubAssetDto(string Name, string BrowserDownloadUrl, long Size);
internal sealed record GitHubReleaseDto(string TagName, string? Body, List<GitHubAssetDto>? Assets);

public sealed record UpdateCheckResult(
    bool Enabled,
    string CurrentVersion,
    string? LatestVersion,
    bool UpdateAvailable,
    string? DownloadUrl,
    string? Notes,
    bool CanSelfUpdate,
    string? Error);

public sealed record UpdateApplyResult(bool Started, string? Error);

/// <summary>Live progress of an in-flight self-update, polled by the dashboard
/// so the operator sees the 100+ MB download move instead of a frozen screen.</summary>
public sealed record UpdateProgress(string Phase, long Downloaded, long Total, string? Error = null)
{
    public double Fraction => Total > 0 ? Math.Min(1.0, (double)Downloaded / Total) : 0.0;
}

/// <summary>
/// Checks GitHub Releases for a newer version of this exact repo, and - only
/// when the operator explicitly clicks "Uppdatera" in Settings, never on its
/// own - downloads and installs it. The repo is a hardcoded constant, not a
/// setting: this value is never remotely configurable (not even by a Host
/// pushing settings to a Worker), because that would let anything that can
/// reach a node's settings endpoint point its self-updater at an arbitrary
/// repo and get it to download and run attacker-supplied code the next time
/// the operator clicks Update.
///
/// A running .exe can't be deleted or overwritten on Windows, but it CAN be
/// renamed out of the way while still executing - so ApplyAsync downloads the
/// new build under a temp name, then hands off to a tiny external .cmd
/// script (this process can't safely replace its own file while its own code
/// is still running from it) that renames the old exe aside, drops the new
/// one in its place, relaunches with the same arguments, and cleans up.
/// </summary>
public static class SelfUpdater
{
    private const string Repo = "HappyHamster135/ailocal";
    private const long MinPlausibleExeBytes = 1_000_000;

    /// <summary>Current update state, or idle. Read by /api/update-progress.</summary>
    public static UpdateProgress Current { get; private set; } = new("idle", 0, 0);

    private static readonly JsonSerializerOptions GitHubJson = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static string CurrentVersion =>
        typeof(SelfUpdater).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<UpdateCheckResult> CheckAsync(IHttpClientFactory httpFactory, CancellationToken ct)
    {
        var (release, error) = await FetchLatestReleaseAsync(httpFactory, ct);
        if (release is null)
            return new UpdateCheckResult(true, CurrentVersion, null, false, null, null, false, error);

        var latestVersion = release.TagName.TrimStart('v', 'V');
        var updateAvailable =
            Version.TryParse(latestVersion, out var latest) &&
            Version.TryParse(CurrentVersion, out var current) &&
            latest > current;

        // A "real" update is available whenever this install's package (every
        // .exe sitting next to the running one) has a matching asset in the
        // latest release - not just the single binary that happens to be
        // answering this request. Basing canSelfUpdate on the lone
        // Environment.ProcessPath breaks when the launcher spawns the node as
        // a child process (or any path where ProcessPath is null/odd), which
        // wrongly forced the operator into the manual Edge download.
        var exeDir = Path.GetDirectoryName(Environment.ProcessPath ?? "") ?? ".";
        var updates = CollectUpdates(release, exeDir);
        var downloadUrl = updates.Count > 0
            ? updates[0].Asset.BrowserDownloadUrl
            : $"https://github.com/{Repo}/releases/latest";

        return new UpdateCheckResult(
            true, CurrentVersion, latestVersion, updateAvailable,
            downloadUrl, release.Body, updates.Count > 0, null);
    }

    public static async Task<UpdateApplyResult> ApplyAsync(
        IHttpClientFactory httpFactory, IHostApplicationLifetime lifetime, ILogger log, CancellationToken ct)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath) || !exePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            return new UpdateApplyResult(false, "Den här processen kör inte som en publicerad .exe - kan inte uppdatera automatiskt.");

        var (release, fetchError) = await FetchLatestReleaseAsync(httpFactory, ct);
        if (release is null)
            return new UpdateApplyResult(false, fetchError ?? "Kunde inte hämta senaste versionen från GitHub.");

        var exeDir = Path.GetDirectoryName(exePath)!;
        var updates = CollectUpdates(release, exeDir);
        if (updates.Count == 0)
            return new UpdateApplyResult(false, "Hittade ingen matchande fil i senaste GitHub-releasen.");

        // Relaunch the launcher when one ships alongside the node (the process
        // the operator actually sees) - it re-spawns the freshly-swapped node
        // itself, so a single Apply click restarts the whole app. Fall back to
        // relaunching the running exe directly when there's no separate launcher.
        var launcherPath = Path.Combine(exeDir, "ailocal-app.exe");
        var relaunchExe = File.Exists(launcherPath) ? launcherPath : exePath;
        var relaunchArgs = File.Exists(launcherPath)
            ? ""
            : string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(QuoteArg));

        // Download every binary in the package to a .new next to it, so the
        // swap below is atomic per file and the whole release lands together.
        long totalBytes = 0;
        var downloaded = new List<(string CurrentPath, string NewPath, GitHubAssetDto Asset)>();
        try
        {
            var client = httpFactory.CreateClient("github");
            foreach (var (currentPath, asset) in updates)
            {
                var newPath = currentPath + ".new";
                using var response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                response.EnsureSuccessStatusCode();
                totalBytes += response.Content.Headers.ContentLength ?? 0;

                const int BufferSize = 64 * 1024;
                var buffer = new byte[BufferSize];
                await using (var fileStream = File.Create(newPath))
                await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                {
                    long downloadedBytes = 0;
                    int read;
                    while ((read = await httpStream.ReadAsync(buffer, ct)) > 0)
                    {
                        await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
                        downloadedBytes += read;
                        Current = new UpdateProgress("downloading", downloadedBytes, totalBytes);
                    }
                }
                var info = new FileInfo(newPath);
                if (!info.Exists || info.Length < MinPlausibleExeBytes)
                    throw new InvalidOperationException($"Nedladdad fil {asset.Name} ser inte korrekt ut (för liten).");
                downloaded.Add((currentPath, newPath, asset));
            }
        }
        catch (Exception ex)
        {
            foreach (var (_, newPath, _) in downloaded) TryDelete(newPath);
            Current = new UpdateProgress("error", 0, 0, ex.Message);
            return new UpdateApplyResult(false, $"Nedladdning misslyckades: {ex.Message}");
        }

        Current = new UpdateProgress("installing", totalBytes, totalBytes);

        string scriptPath;
        try
        {
            scriptPath = WriteSwapScript(exeDir, downloaded, relaunchExe, relaunchArgs);
        }
        catch (Exception ex)
        {
            foreach (var (_, newPath, _) in downloaded) TryDelete(newPath);
            Current = new UpdateProgress("error", 0, 0, ex.Message);
            return new UpdateApplyResult(false, $"Kunde inte förbereda omstarten: {ex.Message}");
        }

        // Respond to the caller before this process's own port disappears -
        // the handler that called ApplyAsync still needs to return an HTTP
        // response over that same port. This runs detached from the request.
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(700, CancellationToken.None);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    ArgumentList = { "/c", scriptPath },
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                await Task.Delay(300, CancellationToken.None);
            }
            catch (Exception ex)
            {
                log.LogError(ex, "self-update: could not launch the swap script");
                Current = new UpdateProgress("error", 0, 0, "Kunde inte starta omstarter-skriptet.");
                return;
            }
            Current = new UpdateProgress("restarting", totalBytes, totalBytes);
            lifetime.StopApplication();
        });

        return new UpdateApplyResult(true, null);
    }

    private static async Task<(GitHubReleaseDto? Release, string? Error)> FetchLatestReleaseAsync(
        IHttpClientFactory httpFactory, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient("github");
            using var response = await client.GetAsync($"https://api.github.com/repos/{Repo}/releases/latest", ct);
            if (!response.IsSuccessStatusCode)
                return (null, $"GitHub svarade {(int)response.StatusCode}.");

            var release = await response.Content.ReadFromJsonAsync<GitHubReleaseDto>(GitHubJson, ct);
            return release is null ? (null, "Tomt svar från GitHub.") : (release, null);
        }
        catch (Exception ex)
        {
            return (null, ex.Message);
        }
    }

    /// <summary>Every .exe sitting next to the one currently running that also
    /// exists as an asset in the latest release - i.e. the full self-contained
    /// package (ailocal.exe + ailocal-app.exe), not just the single binary the
    /// user launched. A "real" updater swaps all of them in one pass so the
    /// launcher and the node never drift onto different versions.</summary>
    internal static List<(string ExePath, GitHubAssetDto Asset)> CollectUpdates(GitHubReleaseDto release, string exeDir)
    {
        var result = new List<(string, GitHubAssetDto)>();
        if (release.Assets is null || !Directory.Exists(exeDir))
            return result;

        foreach (var file in Directory.EnumerateFiles(exeDir, "*.exe"))
        {
            var name = Path.GetFileName(file);
            var asset = release.Assets.FirstOrDefault(a =>
                string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (asset is not null)
                result.Add((file, asset));
        }
        return result;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    /// <summary>
    /// Windows won't let this process delete or overwrite the very file it's
    /// executing from, but it WILL let it rename that file - so the external
    /// script renames the running exe aside first (retrying briefly in case
    /// something else has it locked for a moment), moves the already-verified
    /// download into its place, relaunches with the original arguments, then
    /// best-effort cleans up the renamed original and itself.
    /// </summary>
    private static string WriteSwapScript(
        string exeDir,
        IReadOnlyList<(string CurrentPath, string NewPath, GitHubAssetDto Asset)> downloaded,
        string relaunchExe,
        string relaunchArgs)
    {
        var logPath = Path.Combine(exeDir, "update.log");
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ailocal-update-{Guid.NewGuid():N}.cmd");

        // Build a rename+swap block per binary so the whole package updates
        // together: each running exe is moved to .old, each verified .new is
        // moved into its place, and on any failure we jump to :done (don't
        // half-swap the package - leave the rest for the next update attempt).
        var swaps = new StringBuilder();
        var cleanups = new StringBuilder();
        foreach (var (currentPath, newPath, _) in downloaded)
        {
            var oldPath = currentPath + ".old";
            var loopLabel = "renameLoop_" + Guid.NewGuid().ToString("N")[..12];
            swaps.AppendLine($"if exist \"{oldPath}\" del /f /q \"{oldPath}\" >nul 2>&1");
            swaps.AppendLine("set RETRIES=0");
            swaps.AppendLine($":{loopLabel}");
            swaps.AppendLine($"move /y \"{currentPath}\" \"{oldPath}\" >nul 2>&1");
            swaps.AppendLine($"if exist \"{currentPath}\" (");
            swaps.AppendLine("  set /a RETRIES+=1");
            swaps.AppendLine("  if %RETRIES% GEQ 30 (");
            swaps.AppendLine($"    echo [%date% %time%] update: gave up renaming \"{currentPath}\" >> \"{logPath}\"");
            swaps.AppendLine("    goto :done");
            swaps.AppendLine("  )");
            swaps.AppendLine("  ping -n 2 127.0.0.1 >nul");
            swaps.AppendLine($"  goto :{loopLabel}");
            swaps.AppendLine(")");
            swaps.AppendLine($"move /y \"{newPath}\" \"{currentPath}\" >nul 2>&1");
            swaps.AppendLine($"if not exist \"{currentPath}\" (");
            swaps.AppendLine($"  echo [%date% %time%] update: \"{currentPath}\" missing after move, restoring >> \"{logPath}\"");
            swaps.AppendLine($"  move /y \"{oldPath}\" \"{currentPath}\" >nul 2>&1");
            swaps.AppendLine("  goto :done");
            swaps.AppendLine(")");
            cleanups.AppendLine($"if exist \"{oldPath}\" del /f /q \"{oldPath}\" >nul 2>&1");
        }

        var script = $$"""
            @echo off
            echo [%date% %time%] update: swapping {{downloaded.Count}} binary/ies >> "{{logPath}}"
            {{swaps}}
            echo [%date% %time%] update: swapped ok, relaunching >> "{{logPath}}"
            start "" "{{relaunchExe}}" {{relaunchArgs}}
            :done
            {{cleanups}}
            (goto) 2>nul & del "%~f0"
            """;

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string QuoteArg(string arg) =>
        arg.Length == 0 || arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}
