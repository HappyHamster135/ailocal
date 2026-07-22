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
        string InstallArgs,        // for installer-based tools
        // Full toolchain zips (node/git/jdk/dotnet) - extract the WHOLE
        // archive: null = not a full-extract entry; "" = the zip has its own
        // root folder; a name = extract into that subfolder (zips whose files
        // sit at the archive root, e.g. MinGit and dotnet-sdk).
        string? ExtractAllTo = null);

    private static readonly IReadOnlyDictionary<string, Spec> Catalog = new Dictionary<string, Spec>(StringComparer.OrdinalIgnoreCase)
    {
        // Godot portable: official GitHub releases (godotengine/godot-builds is the
        // official portable-build mirror). Pinned to a known-good 4.x stable.
        ["godot"] = new("Godot (portable)",
            "https://github.com/godotengine/godot-builds/releases/download/4.3-stable/Godot_v4.3-stable_mono_win64.zip",
            null,
            "Godot_v4.3-stable_mono_win64/Godot_v4.3-stable_mono_win64.exe",
            ""),
        // Godots exportmallar (.tpz = zip) - KRÄVS för att `godot --headless
        // --export-release` ska kunna producera en körbar exe. Versionen
        // måste matcha godot-posten ovan exakt; destinationen tvingas till
        // %APPDATA%\Godot\export_templates\4.3.stable.mono (se ProvisionAsync)
        // eftersom det är den enda plats Godot letar på.
        ["godot-templates"] = new("Godot exportmallar 4.3 (mono)",
            "https://github.com/godotengine/godot-builds/releases/download/4.3-stable/Godot_v4.3-stable_mono_export_templates.tpz",
            null,
            null,
            "",
            ExtractAllTo: ""),
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
        // Node.js portable: officiella nodejs.org-zipen (node + npm), ingen
        // installer/admin. Zipen har egen rotmapp (node-v20...-win-x64).
        ["node"] = new("Node.js 20 LTS (portabel)",
            "https://nodejs.org/dist/v20.18.1/node-v20.18.1-win-x64.zip",
            null,
            null,
            "",
            ExtractAllTo: ""),
        // Git: officiella MinGit fran git-for-windows (kommandoraden, inget
        // UI). Filerna ligger i zipens rot -> extraheras till MinGit/.
        ["git"] = new("Git (MinGit, portabel)",
            "https://github.com/git-for-windows/git/releases/download/v2.47.1.windows.1/MinGit-2.47.1-64-bit.zip",
            null,
            null,
            "",
            ExtractAllTo: "MinGit"),
        // Java: Eclipse Temurin JDK 21 (officiella Adoptium-releasen),
        // portabel zip med egen rotmapp (jdk-21...).
        ["java"] = new("Java JDK 21 (Temurin, portabel)",
            "https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.5%2B11/OpenJDK21U-jdk_x64_windows_hotspot_21.0.5_11.zip",
            null,
            null,
            "",
            ExtractAllTo: ""),
        // .NET SDK: officiella Microsoft-CDN:en, portabel zip (dotnet.exe i
        // zipens rot) -> extraheras till dotnet/.
        ["dotnet"] = new(".NET SDK 8 (portabel)",
            "https://builds.dotnet.microsoft.com/dotnet/Sdk/8.0.404/dotnet-sdk-8.0.404-win-x64.zip",
            null,
            null,
            "",
            ExtractAllTo: "dotnet"),
        // Android SDK (v1.90): officiella command-line tools fran Googles egen
        // CDN (dl.google.com). Zipen har en inre "cmdline-tools"-rot ->
        // extraheras till android-sdk/. Efterat kor ProvisionAsync HELA
        // kedjan: sdkmanager-licenser + platform-tools/build-tools/platform
        // (via provisionerad JDK) + debug-keystore - sa "provisionera
        // android-sdk" lamnar en komplett APK-exportkedja for Godot.
        ["android-sdk"] = new("Android SDK (command-line tools)",
            "https://dl.google.com/android/repository/commandlinetools-win-11076708_latest.zip",
            null,
            null,
            "",
            ExtractAllTo: "android-sdk"),
        // Godot STANDARD (icke-mono) + dess mallar: mono-editorn VAGRAR
        // Android-export headless ("Exporting to Android when using C#/.NET
        // is experimental" ar ett blockerande konfigurationsfel, upptackt
        // live v1.90). Kiten ar ren GDScript sedan v1.85 - standardbygget
        // exporterar dem felfritt. Anvands BARA for Android-exporten.
        ["godot-standard"] = new("Godot 4.3 standard (for Android-export)",
            "https://github.com/godotengine/godot-builds/releases/download/4.3-stable/Godot_v4.3-stable_win64.exe.zip",
            null,
            null,
            "",
            ExtractAllTo: "Godot_v4.3-stable_win64"),
        ["godot-templates-standard"] = new("Godot exportmallar 4.3 (standard)",
            "https://github.com/godotengine/godot-builds/releases/download/4.3-stable/Godot_v4.3-stable_export_templates.tpz",
            null,
            null,
            "",
            ExtractAllTo: ""),
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
        // Exportmallarna har EN giltig plats - Godot letar bara i
        // %APPDATA%\Godot\export_templates\<version>. Anroparens destination
        // ignoreras medvetet, annars hamnar mallarna där ingen hittar dem.
        if (key.Equals("godot-templates", StringComparison.OrdinalIgnoreCase))
            dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Godot", "export_templates", "4.3.stable.mono");
        // Standardmallarna har sin EGEN versionsmapp (utan .mono-suffix).
        if (key.Equals("godot-templates-standard", StringComparison.OrdinalIgnoreCase))
            dest = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Godot", "export_templates", "4.3.stable");
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
            // GitHubs releaser omdirigeras ALLTID till githubusercontent.com
            // (GitHubs egen CDN) - det är den förväntade, betrodda vägen och
            // blockerade tidigare varje github-hostad post (godot, git, java).
            var finalHost = resp.RequestMessage!.RequestUri!.Host;
            var redirectOk = string.Equals(finalHost, trustedHost, StringComparison.OrdinalIgnoreCase)
                || (trustedHost.EndsWith("github.com", StringComparison.OrdinalIgnoreCase)
                    && finalHost.EndsWith("githubusercontent.com", StringComparison.OrdinalIgnoreCase));
            if (!redirectOk)
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
            if (spec.ExtractAllTo is not null)
            {
                // Hela verktygskedjan (node/git/jdk/dotnet) - packa upp allt.
                var target = spec.ExtractAllTo.Length == 0 ? dest : Path.Combine(dest, spec.ExtractAllTo);
                Directory.CreateDirectory(target);
                ZipFile.ExtractToDirectory(zipPath, target, overwriteFiles: true);
                TryDelete(zipPath);

                // .tpz-arkivet har en inre "templates/"-mapp men Godot förväntar
                // sig filerna DIREKT i versionsmappen - platta ut.
                var inner = Path.Combine(target, "templates");
                if ((key.Equals("godot-templates", StringComparison.OrdinalIgnoreCase)
                        || key.Equals("godot-templates-standard", StringComparison.OrdinalIgnoreCase))
                    && Directory.Exists(inner))
                {
                    foreach (var file in Directory.EnumerateFiles(inner))
                        File.Move(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
                    try { Directory.Delete(inner, recursive: true); } catch { /* best effort */ }
                }

                // Android SDK: cmdline-tools är bara nyckeln in - de riktiga
                // paketen (platform-tools, build-tools, platform) installeras
                // av sdkmanager, som också kräver licensgodkännande. Hela
                // kedjan körs här + debug-keystore genereras, så EN
                // provisionering lämnar en komplett Godot-APK-exportkedja.
                if (key.Equals("android-sdk", StringComparison.OrdinalIgnoreCase))
                {
                    var setup = await RunAndroidSdkSetupAsync(target, ct);
                    if (!setup.Success)
                        return Reject(spec, setup.Output);
                    return new(true,
                        $"{spec.Display} provisionerad till {target}. {setup.Output} " +
                        "Godot-APK-export (preset \"Android\") pekas hit automatiskt av build_game.");
                }
            }
            else if (spec.ArchiveEntry is not null)
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
            var note = spec.ExtractAllTo is not null || spec.ArchiveEntry is not null
                ? $"{spec.Display} nedladdad och extraherad till {dest}."
                : $"{spec.Display} installerad (körde officiell installer med fasta argument).";
            // Den korande processen ser inte nya PATH - ge agenten den
            // absoluta sokvagen direkt sa den kan fortsatta utan omstart
            // (verify/build/run loser samma vag via ToolLocator).
            var located = AiLocal.Core.Agent.ToolLocator.Find(key);
            note += located is not null
                ? $" Körbar: {located} - verify/run hittar den automatiskt; använd den absoluta sökvägen i run_command."
                : " OBS: kunde inte lokalisera den körbara filen efter installationen.";
            return new(true, note);
        }
        catch (Exception ex)
        {
            CrashLog.Write("ProvisionError", ex);
            return new(false, $"provisioneringsfel: {ex.Message}");
        }
    }

    /// <summary>Kör hela Android-SDK-efterkedjan: licensgodkännande + paket
    /// via sdkmanager (kräver JDK - provisioneras automatiskt vid behov) och
    /// debug-keystore via keytool. Licensgodkännandet sker HÄR, öppet: att
    /// provisionera android-sdk är att acceptera Googles SDK-licensvillkor
    /// (samma automatiserade godkännande som CI-system gör).</summary>
    private async Task<ProvisionResult> RunAndroidSdkSetupAsync(string sdkRoot, CancellationToken ct)
    {
        var sdkManager = Path.Combine(sdkRoot, "cmdline-tools", "bin", "sdkmanager.bat");
        if (!File.Exists(sdkManager))
            return new(false, $"sdkmanager saknas efter extrahering: {sdkManager}");

        // sdkmanager är Java-baserad - se till att en JDK finns (Temurin ur
        // samma katalog) och peka JAVA_HOME på den.
        var java = ToolLocator.Find("java");
        if (java is null)
        {
            var jdk = await ProvisionAsync("java", "", ct);
            if (!jdk.Success)
                return new(false, "Android SDK kräver en JDK och java-provisioneringen misslyckades: " + jdk.Output);
            java = ToolLocator.Find("java");
            if (java is null)
                return new(false, "JDK provisionerad men java.exe kunde inte lokaliseras efteråt.");
        }
        var javaHome = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(java)!, ".."));

        // 1) Licenser: sdkmanager frågar y/N per licens - jakande svar matas
        //    på stdin. Detta är det öppna godkännandesteget.
        var (licExit, licOut) = await RunAndroidToolAsync(sdkManager,
            $"--sdk_root=\"{sdkRoot}\" --licenses", javaHome, stdinYes: true, ct);
        if (licExit != 0)
            return new(false, $"sdkmanager --licenses misslyckades (exit {licExit}): {Tail(licOut)}");

        // 2) Paketen Godots APK-export behöver: platform-tools (adb),
        //    build-tools (apksigner/zipalign) och en Android-plattform.
        var (pkgExit, pkgOut) = await RunAndroidToolAsync(sdkManager,
            $"--sdk_root=\"{sdkRoot}\" \"platform-tools\" \"build-tools;34.0.0\" \"platforms;android-34\"",
            javaHome, stdinYes: true, ct);
        if (pkgExit != 0)
            return new(false, $"sdkmanager-paketinstallationen misslyckades (exit {pkgExit}): {Tail(pkgOut)}");

        // 3) Debug-keystore med Godots standardvärden (androiddebugkey/android).
        var keystore = Path.Combine(sdkRoot, "debug.keystore");
        if (!File.Exists(keystore))
        {
            var keytool = Path.Combine(javaHome, "bin", "keytool.exe");
            var (ksExit, ksOut) = await RunAndroidToolAsync(keytool,
                $"-genkeypair -keystore \"{keystore}\" -storepass android -alias androiddebugkey " +
                "-keypass android -keyalg RSA -keysize 2048 -validity 9999 " +
                "-dname \"CN=Android Debug,O=Android,C=US\"", javaHome, stdinYes: false, ct);
            if (ksExit != 0 || !File.Exists(keystore))
                return new(false, $"debug-keystore kunde inte genereras (exit {ksExit}): {Tail(ksOut)}");
        }
        // 4) Standard-Godot + standardmallar: mono-editorn blockerar Android-
        //    export headless (".NET experimental"), sa APK-exporten kor det
        //    rena standardbygget - kiten ar ren GDScript sedan v1.85.
        var extras = new List<string>();
        if (ToolLocator.Find("godot-standard") is null)
        {
            var g = await ProvisionAsync("godot-standard", "", ct);
            if (!g.Success) return new(false, "godot-standard kunde inte provisioneras: " + g.Output);
            extras.Add("godot-standard");
        }
        var stdTemplates = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot", "export_templates", "4.3.stable");
        if (!File.Exists(Path.Combine(stdTemplates, "android_debug.apk")))
        {
            var t = await ProvisionAsync("godot-templates-standard", "", ct);
            if (!t.Success) return new(false, "standardmallarna kunde inte provisioneras: " + t.Output);
            extras.Add("standardmallar");
        }
        return new(true,
            "sdkmanager: licenser godkända, platform-tools + build-tools;34.0.0 + platforms;android-34 " +
            "installerade, debug-keystore på plats" +
            (extras.Count > 0 ? ", " + string.Join(" + ", extras) + " provisionerade" : "") + ".");
    }

    /// <summary>Kör sdkmanager.bat (via cmd.exe) eller keytool.exe med
    /// JAVA_HOME satt; stdinYes matar jakande licens-svar. 15 min-tak med
    /// kill - en hängd nedladdning får aldrig blockera noden för evigt.</summary>
    private static async Task<(int ExitCode, string Output)> RunAndroidToolAsync(
        string exe, string args, string javaHome, bool stdinYes, CancellationToken ct)
    {
        var isBat = exe.EndsWith(".bat", StringComparison.OrdinalIgnoreCase);
        var psi = new ProcessStartInfo(
            isBat ? "cmd.exe" : exe,
            isBat ? $"/c \"\"{exe}\" {args}\"" : args)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdinYes,
            CreateNoWindow = true
        };
        psi.Environment["JAVA_HOME"] = javaHome;
        psi.Environment["PATH"] = Path.Combine(javaHome, "bin") + ";" + Environment.GetEnvironmentVariable("PATH");
        using var proc = Process.Start(psi)!;
        try
        {
            if (stdinYes)
            {
                for (var i = 0; i < 40; i++)
                    await proc.StandardInput.WriteLineAsync("y");
                proc.StandardInput.Close();
            }
            var stdout = proc.StandardOutput.ReadToEndAsync(ct);
            var stderr = proc.StandardError.ReadToEndAsync(ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(15));
            await proc.WaitForExitAsync(timeout.Token);
            return (proc.ExitCode, await stdout + "\n" + await stderr);
        }
        catch (OperationCanceledException)
        {
            try { if (!proc.HasExited) proc.Kill(entireProcessTree: true); } catch { /* redan död */ }
            throw;
        }
    }

    private static string Tail(string s) => s.Length <= 800 ? s : s[^800..];

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
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
