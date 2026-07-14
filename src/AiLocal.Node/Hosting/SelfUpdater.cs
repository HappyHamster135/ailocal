using System.Diagnostics;
using System.Net.Http.Json;
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

        var asset = MatchingAsset(release);
        var downloadUrl = asset?.BrowserDownloadUrl ?? $"https://github.com/{Repo}/releases/latest";

        return new UpdateCheckResult(
            true, CurrentVersion, latestVersion, updateAvailable,
            downloadUrl, release.Body, asset is not null, null);
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

        var asset = MatchingAsset(release);
        if (asset is null)
            return new UpdateApplyResult(false, "Hittade ingen matchande fil i senaste GitHub-releasen.");

        var exeName = Path.GetFileName(exePath);
        var exeDir = Path.GetDirectoryName(exePath)!;
        var newPath = Path.Combine(exeDir, exeName + ".new");

        try
        {
            var client = httpFactory.CreateClient("github");
            using var response = await client.GetAsync(asset.BrowserDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var fileStream = File.Create(newPath))
            await using (var httpStream = await response.Content.ReadAsStreamAsync(ct))
                await httpStream.CopyToAsync(fileStream, ct);
        }
        catch (Exception ex)
        {
            TryDelete(newPath);
            return new UpdateApplyResult(false, $"Nedladdning misslyckades: {ex.Message}");
        }

        var downloaded = new FileInfo(newPath);
        if (!downloaded.Exists || downloaded.Length < MinPlausibleExeBytes)
        {
            TryDelete(newPath);
            return new UpdateApplyResult(false, "Den nedladdade filen ser inte korrekt ut (för liten).");
        }

        string scriptPath;
        try
        {
            scriptPath = WriteSwapScript(exeDir, exeName);
        }
        catch (Exception ex)
        {
            TryDelete(newPath);
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
                return;
            }
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

    private static GitHubAssetDto? MatchingAsset(GitHubReleaseDto release)
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            return null;

        var exeName = Path.GetFileName(exePath);
        return release.Assets?.FirstOrDefault(a => string.Equals(a.Name, exeName, StringComparison.OrdinalIgnoreCase));
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
    private static string WriteSwapScript(string exeDir, string exeName)
    {
        var exePath = Path.Combine(exeDir, exeName);
        var newPath = exePath + ".new";
        var oldPath = exePath + ".old";
        var logPath = Path.Combine(exeDir, "update.log");
        var relaunchArgs = string.Join(' ', Environment.GetCommandLineArgs().Skip(1).Select(QuoteArg));
        var scriptPath = Path.Combine(Path.GetTempPath(), $"ailocal-update-{Guid.NewGuid():N}.cmd");

        var script = $"""
            @echo off
            echo [%date% %time%] update: renaming current exe out of the way >> "{logPath}"
            if exist "{oldPath}" del /f /q "{oldPath}" >nul 2>&1
            set RETRIES=0
            :renameLoop
            move /y "{exePath}" "{oldPath}" >nul 2>&1
            if exist "{exePath}" (
              set /a RETRIES+=1
              if %RETRIES% GEQ 30 (
                echo [%date% %time%] update: gave up waiting to rename the running exe >> "{logPath}"
                goto :cleanup
              )
              ping -n 2 127.0.0.1 >nul
              goto :renameLoop
            )
            move /y "{newPath}" "{exePath}" >nul 2>&1
            if not exist "{exePath}" (
              echo [%date% %time%] update: new exe missing after move, restoring old >> "{logPath}"
              move /y "{oldPath}" "{exePath}" >nul 2>&1
              goto :cleanup
            )
            echo [%date% %time%] update: swapped ok, relaunching >> "{logPath}"
            start "" "{exePath}" {relaunchArgs}
            :cleanup
            set DRETRIES=0
            :deleteLoop
            if exist "{oldPath}" (
              del /f /q "{oldPath}" >nul 2>&1
              if exist "{oldPath}" (
                set /a DRETRIES+=1
                if %DRETRIES% LSS 10 (
                  ping -n 2 127.0.0.1 >nul
                  goto :deleteLoop
                )
              )
            )
            (goto) 2>nul & del "%~f0"
            """;

        File.WriteAllText(scriptPath, script);
        return scriptPath;
    }

    private static string QuoteArg(string arg) =>
        arg.Length == 0 || arg.Contains(' ') ? $"\"{arg.Replace("\"", "\\\"")}\"" : arg;
}
