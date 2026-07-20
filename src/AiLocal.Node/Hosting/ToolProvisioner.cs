using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using Microsoft.Extensions.Logging;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Lets a Full-access Worker self-provision the tools a task needs
/// (Godot portable, Blender portable, Unity via the official Hub installer)
/// so it can complete a mission without the operator hand-installing first.
///
/// Security model - deliberately locked down:
///  - The agent never supplies a URL. It passes a TOOL NAME only
///    ("godot" | "blender" | "unity"). Any attempt to pass a URL,
///    an unlisted name, or an alternate source is rejected.
///  - Every entry points at a pinned, well-known, trusted source
///    (official project site / the vendor's own CDN). No third-party
///    mirrors, no "latest" redirect to an unknown host.
///  - Downloads are SHA-256 verified against a pinned known-good hash
///    where one is published; if a hash is unknown we still refuse
///    arbitrary sources but allow the single pinned trusted URL.
///  - The final response must stay on the trusted host (redirect guard),
///    and CommandGuard still vets any installer invocation.
///  - Every attempt (success or rejection) is logged via CrashLog so
///    there is a full audit trail of what was fetched and where it went.
/// </summary>
public sealed class ToolProvisioner
{
    // Allowed target only. agentName -> trusted spec. No URLs from the agent.
    private sealed record Spec(
        string Display,
        string TrustedUrl,
        string? ExpectedSha256,   // null = trust-by-source only (still pinned URL)
        string? ArchiveEntry,      // for portable zips: the exe to extract/verify
        string InstallArgs);       // for installer-based tools

    private static readonly IReadOnlyDictionary<string, Spec> Catalog = new Dictionary<string, Spec>(StringComparer.OrdinalIgnoreCase)
    {
        // Godot portable: official GitHub releases (godotengine/godot-builds is the
        // official portable-build mirror). Pinned to a known-good 4.x stable.
        ["godot"] = new("Godot (portable)",
            "https://github.com/godotengine/godot-builds/releases/download/4.3-stable/Godot_v4.3-stable_mono_win64.zip",
            null,
            "Godot_v4.3-stable_mono_win64/Godot_v4.3-stable_mono_win64.exe",
            ""),
        // Blender portable: official developer site (no installer).
        ["blender"] = new("Blender (portable)",
            "https://download.blender.org/release/Blender4.3/blender-4.3.0-windows-x64.zip",
            null,
            "blender-4.3.0-windows-x64/blender.exe",
            ""),
        // Unity: official Hub installer from Unity's own CDN. Installer-based.
        ["unity"] = new("Unity (via Hub installer)",
            "https://public-cdn.cloud.unity3d.com/public/download/latest/UnitySetup64.exe",
            null,
            null,
            "--version 6000.2.13f1 --accept-license"),
        // Python: officiella python.org-installern, tyst per-user (ingen
        // admin), pip inkluderat, PATH uppdaterad for NYA processer. Den
        // redan korande noden ser inte nya PATH - darfor loser
        // PythonLocator (Core) upp den absoluta sokvagen for verify/run.
        ["python"] = new("Python 3.12 (officiell installer)",
            "https://www.python.org/ftp/python/3.12.8/python-3.12.8-amd64.exe",
            null,
            null,
            "/quiet InstallAllUsers=0 PrependPath=1 Include_test=0 SimpleInstall=1"),
    };

    public ToolProvisioner()
    {
    }

    public record ProvisionResult(bool Success, string Output);

    /// <summary>Provision a named tool. <paramref name="agentName"/> must be one of
    /// the catalog keys - never a URL.</summary>
    public async Task<ProvisionResult> ProvisionAsync(string agentName, string destinationDir, CancellationToken ct)
    {
        var key = (agentName ?? "").Trim();
        if (!Catalog.TryGetValue(key, out var spec))
        {
            CrashLog.Write("ProvisionRejected", new Exception($"refused unknown tool name '{agentName}' - not in trusted catalog"));
            return new(false, $"Okändt verktyg '{agentName}'. Tillåtna: {string.Join(", ", Catalog.Keys)}. Endast namn, inga URL:er.");
        }

        // Hard gate: never accept a URL / path from the caller.
        if (key.Contains("://") || key.Contains("/") || key.Contains("\\"))
        {
            CrashLog.Write("ProvisionRejected", new Exception($"refused tool name that looks like a path/url: '{agentName}'"));
            return new(false, "Du får bara skicka ett verktygsnamn (t.ex. 'godot'), inte en URL.");
        }

        var dest = string.IsNullOrWhiteSpace(destinationDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiLocal", "tools")
            : destinationDir;
        Directory.CreateDirectory(dest);

        CrashLog.Write("ProvisionStart", new Exception($"fetching {spec.Display} from pinned trusted source: {spec.TrustedUrl}"));

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("AiLocal/1.0 provisioner");

            var trustedHost = new Uri(spec.TrustedUrl).Host;
            using var resp = await http.GetAsync(spec.TrustedUrl, ct);
            if (!resp.IsSuccessStatusCode)
                return Reject(spec, $"nedladdning misslyckades: HTTP {(int)resp.StatusCode}");

            // Guard against redirect to a different host (MITM / bad mirror).
            if (!string.Equals(resp.RequestMessage!.RequestUri!.Host, trustedHost, StringComparison.OrdinalIgnoreCase))
                return Reject(spec, "nedladdningen omdirigerades till en annan värd - blockerad.");

            var bytes = await resp.Content.ReadAsByteArrayAsync(ct);

            // Hash verification when a known-good hash is pinned.
            if (spec.ExpectedSha256 is not null)
            {
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, spec.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                    return Reject(spec, $"hash mismatch (förvant {spec.ExpectedSha256[..12]}..., fick {actual[..12]}...) - filen avvisas.");
            }

            var zipPath = Path.Combine(dest, $"{key}.zip");
            await File.WriteAllBytesAsync(zipPath, bytes, ct);

            // Extract the portable archive entry (if any) into dest.
            if (spec.ArchiveEntry is not null)
            {
                await ExtractEntryAsync(zipPath, spec.ArchiveEntry, dest, ct);
            }
            else if (!string.IsNullOrWhiteSpace(spec.InstallArgs))
            {
                // Installer-based: run the official installer with pinned args.
                var exit = await RunTrustedInstallerAsync(zipPath, spec.InstallArgs, ct);
                if (exit != 0)
                    return Reject(spec, $"installer avslutades med kod {exit}");
            }

            CrashLog.Write("ProvisionOK", new Exception($"{spec.Display} provisionerad till {dest}"));
            var note = spec.ArchiveEntry is not null
                ? $"{spec.Display} nedladdad och extraherad till {dest}."
                : $"{spec.Display} installerad (körde officiell installer med fasta argument).";
            if (key.Equals("python", StringComparison.OrdinalIgnoreCase))
            {
                // Den korande processen ser inte nya PATH - ge agenten den
                // absoluta sokvagen direkt sa den kan fortsatta utan omstart.
                var python = AiLocal.Core.Agent.PythonLocator.Find();
                note += python is not null
                    ? $" python.exe: {python} - verify/run hittar den automatiskt; anvand den absoluta sokvagen i run_command."
                    : " OBS: kunde inte lokalisera python.exe efter installationen - kontrollera %LOCALAPPDATA%\\Programs\\Python\\.";
            }
            return new(true, note);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProvisionError", ex);
            return new(false, $"provisioneringsfel: {ex.Message}");
        }
    }

    private ProvisionResult Reject(Spec spec, string why)
    {
        CrashLog.Write("ProvisionRejected", new Exception($"{spec.Display}: {why}"));
        return new(false, why);
    }

    private async Task ExtractEntryAsync(string zipPath, string entry, string dest, CancellationToken ct)
    {
        await using var archive = new ZipArchive(File.OpenRead(zipPath), ZipArchiveMode.Read);
        var e = archive.GetEntry(entry) ??
            throw new InvalidOperationException($"arkivet saknar förvantad post '{entry}'");
        var outPath = Path.Combine(dest, Path.GetFileName(entry));
        await using var outStream = File.Create(outPath);
        await e.Open().CopyToAsync(outStream, ct);
    }

    private async Task<int> RunTrustedInstallerAsync(string installerPath, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = installerPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        // CommandGuard still gets a look (defense in depth) even though we
        // trust the installer by source - it would block e.g. a destructive flag.
        var guard = new CommandGuard(CommandGuardLevel.Block);
        var cmd = $"{installerPath} {args}";
        if (guard.IsBlocked(cmd))
            throw new InvalidOperationException($"CommandGuard blockerade installer-kommandot: {cmd}");
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync(ct);
        return proc.ExitCode;
    }
}
