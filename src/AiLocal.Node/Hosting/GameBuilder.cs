using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AiLocal.Node.Hosting;

/// <summary>P2: "Bygg spel" - takes a scaffolded (or hand-written) Godot/Unity
/// project and produces a standalone .exe via the engine's headless build.
///
/// The builder does NOT download engines: that is gigabyte-scale, offline-fragile
/// and unsafe. It locates an already-installed engine on the machine, and if none
/// is found it returns a clear, actionable error so the agent/user can install it.
/// The shell plumbing is injected as a Func (reuses the host's process runner).
/// </summary>
public sealed class GameBuilder
{
    /// <summary>Build the game in <paramref name="root"/>. engine may be "godot",
    /// "unity", or "auto" (detect by project files). Returns (success, output, exePath).
    /// godotFinder/unityFinder override engine detection (used by tests).</summary>
    public async Task<(bool Success, string Output, string? ExePath)> BuildAsync(
        string engine, string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct,
        Func<string?>? godotFinder = null,
        Func<string?>? unityFinder = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return (false, "root ([ADDRESS]) kravs och maste finnas.", null);

        engine = (engine ?? "auto").Trim().ToLowerInvariant();
        if (engine == "auto")
            engine = DetectEngine(root);

        if (engine is not ("godot" or "unity"))
        {
            // html5 needs no engine binary - it already runs in a browser.
            if (engine == "html5")
                return (true, "html5-projekt kors i en webblasare (ingen build behovs).", null);
            return (false, $"okant motor '{engine}' - forvantad 'godot', 'unity' eller 'auto'.", null);
        }

        return engine == "godot"
            ? await BuildGodot(root, runCommand, ct, godotFinder)
            : await BuildUnity(root, runCommand, ct, unityFinder);
    }

    internal static string DetectEngine(string root)
    {
        if (File.Exists(Path.Combine(root, "project.godot"))) return "godot";
        if (File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"))
            || Directory.Exists(Path.Combine(root, "Assets"))) return "unity";
        // A scaffolded HTML5 game IS the artifact - 'auto' must recognize it
        // instead of erroring with "okant motor 'unknown'" on the most common
        // project type (BuildAsync then reports that no build is needed).
        if (File.Exists(Path.Combine(root, "index.html"))) return "html5";
        return "unknown";
    }

    /// <summary>Output exe name derived from the project folder ("mitt-spel"
    /// -> "mitt-spel.exe") so every built game isn't shipped as PixelRush.exe.
    /// Falls back to "Game" when the folder name has no usable characters.</summary>
    internal static string DeriveExeName(string root)
    {
        var raw = Path.GetFileName(Path.TrimEndingDirectorySeparator(root ?? "")) ?? "";
        var name = new string(raw.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.').ToArray()).Trim().TrimEnd('.');
        return name.Length == 0 ? "Game" : name;
    }

    // ---- Godot -----------------------------------------------------------
    async Task<(bool, string, string?)> BuildGodot(string root,
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct,
        Func<string?>? godotFinder = null)
    {
        var godot = (godotFinder ?? FindGodot)();
        if (godot is null)
            return (false,
                "Godot ar inte installerat pa denna maskin. Installera Godot 4.3 (https://godotengine.org) " +
                "och se till att 'godot' eller '[ADDRESS]' finns i PATH eller " +
                "C:/Program Files/Godot/. 'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        // project.godot already names the preset "Windows Desktop" (ScaffoldGodot writes export_presets.cfg).
        // NOTE: do NOT pass --quit before --export-release - Godot 4 would exit before exporting.
        // --export-release performs the import+export and then exits on its own.
        // The CLI output path overrides the preset's export_path, so the exe
        // is named after the project folder rather than a hardcoded PixelRush.
        var preset = "Windows Desktop";
        var outExe = Path.Combine(root, "build", DeriveExeName(root) + ".exe");
        // v2.7 (skarpt e2e-fynd): Godot SKAPAR INTE exportmappen - utan den
        // faller exporten med "Prepare Template: The given export path
        // doesn't exist" (exit 1). Forklarar aven partytranskriptets tomma
        // exportfel.
        Directory.CreateDirectory(Path.GetDirectoryName(outExe)!);
        var cmd = MakeGodotCommand(godot, preset, outExe);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
        {
            // v1.99: exporten kan falla med HELT TOM utskrift (live-sett) -
            // diagnostisera de vanligaste orsakerna deterministiskt sjalva
            // i stallet for att visa en tom felrad ingen kan agera pa.
            var detail = string.IsNullOrWhiteSpace(output)
                ? "(ingen felutskrift fran godot)" + GodotExportHints(root)
                : output;
            return (false, $"godot export misslyckades (exit {exit}):\n{detail}", null);
        }
        if (!File.Exists(outExe))
            return (false, $"godot avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    /// <summary>Deterministiska ledtradar nar godot-exporten faller utan
    /// utskrift: kontrollera presets och exportmallar SJALVA sa felraden
    /// alltid ar agerbar. Public for test.</summary>
    public static string GodotExportHints(string root)
    {
        var hints = new List<string>();
        if (!File.Exists(Path.Combine(root, "export_presets.cfg")))
            hints.Add("export_presets.cfg saknas i projektroten - preseten 'Windows Desktop' finns inte.");
        var tplBase = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot", "export_templates");
        if (!Directory.Exists(Path.Combine(tplBase, "4.3.stable.mono"))
            && !Directory.Exists(Path.Combine(tplBase, "4.3.stable")))
            hints.Add($"exportmallarna saknas ({tplBase}) - provisionera 'godot-templates' och forsok igen.");
        if (hints.Count == 0)
            hints.Add("presets och mallar finns - vanligaste kvarvarande orsakerna ar fel mallversion for editorn eller en preset med ogiltig exportvag.");
        return " Trolig orsak: " + string.Join(" ", hints);
    }

    /// <summary>Web (HTML5/WASM) export of a Godot project - the "spela i webblasaren
    /// / dela en lank"-vagen bredvid Windows-exen. Uses the kit's "Web" preset and
    /// outputs build/web/index.html. Needs the web export template provisioned
    /// (same godot-templates .tpz som desktop) - annars rapporteras det arligt.</summary>
    public async Task<(bool Success, string Output, string? WebPath)> BuildWebAsync(
        string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct, Func<string?>? godotFinder = null)
    {
        var godot = (godotFinder ?? FindGodot)();
        if (godot is null)
            return (false, "Godot ar inte installerat pa denna maskin - webbexport kraver Godot 4.3.", null);
        if (!File.Exists(Path.Combine(root, "project.godot")))
            return (false, "Ingen Godot-projektfil (project.godot) - webbexport galler bara Godot-spel.", null);

        var outHtml = Path.Combine(root, "build", "web", "index.html");
        Directory.CreateDirectory(Path.GetDirectoryName(outHtml)!);
        var cmd = MakeGodotCommand(godot, "Web", outHtml);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"godot webbexport misslyckades (exit {exit}) - saknas web-exportmallen (provisionera godot-templates)?\n{output}", null);
        if (!File.Exists(outHtml))
            return (false, $"godot avslutade utan fel men index.html saknas: {outHtml}\n{output}", null);
        return (true, $"Webbexport klar: {outHtml} - oppna i en webblasare eller hosta build/web/.", outHtml);
    }

    /// <summary>C9/v1.90: Android APK-export av ett Godot-projekt. Hela kedjan
    /// kan numera SJÄLVPROVISIONERAS (provision("android-sdk") laddar ner
    /// cmdline-tools, godkänner licenser, installerar platform-tools/
    /// build-tools och genererar debug-keystore) - så när SDK:t finns pekas
    /// Godots editor settings + Android-preset ut automatiskt här och APK:n
    /// byggs debug-signerad (release-signering kräver ägarens egen nyckel).
    /// Utan SDK guidar vägen ärligt till provisioneringen.</summary>
    public async Task<(bool Success, string Output, string? ApkPath)> BuildAndroidAsync(
        string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct, Func<string?>? godotFinder = null,
        Func<string?>? sdkRootFinder = null, Func<string?>? javaFinder = null,
        string? godotConfigDir = null)
    {
        // Standard-Godot (icke-mono) KRÄVS för Android: mono-editorn blockerar
        // exporten headless ("Exporting to Android when using C#/.NET is
        // experimental" är ett blockerande konfigurationsfel - upptäckt live
        // v1.90). Kiten är ren GDScript sedan v1.85, så standardbygget
        // exporterar dem felfritt. provision("android-sdk") hämtar den.
        var godot = (godotFinder ?? (() => AiLocal.Core.Agent.ToolLocator.Find("godot-standard")))();
        if (godot is null)
            return (false,
                "Standard-Godot (icke-mono) saknas - Android-exporten kräver den (mono-editorn blockerar " +
                "Android headless). Kör provision(\"android-sdk\") så hämtas hela kedjan inklusive den.", null);
        if (!File.Exists(Path.Combine(root, "project.godot")))
            return (false, "Ingen Godot-projektfil (project.godot) - Android-export gäller bara Godot-spel.", null);

        var sdkRoot = (sdkRootFinder ?? AiLocal.Core.Agent.ToolLocator.AndroidSdkRoot)();
        if (sdkRoot is null)
            return (false,
                "Android-SDK saknas på maskinen. Kör provision(\"android-sdk\") EN gång - den laddar ner " +
                "command-line tools från Googles CDN, godkänner SDK-licenserna, installerar platform-tools/" +
                "build-tools och genererar debug-keystore. Därefter bygger den här vägen APK:n åt dig.", null);
        var java = (javaFinder ?? (() => AiLocal.Core.Agent.ToolLocator.Find("java")))();
        if (java is null)
            return (false, "JDK saknas - kör provision(\"java\") (Android-exporten behöver Java för signeringen).", null);
        var javaHome = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(java)!, ".."));
        var keystore = Path.Combine(sdkRoot, "debug.keystore");
        if (!File.Exists(keystore))
            return (false, $"debug-keystore saknas ({keystore}) - kör provision(\"android-sdk\") igen så genereras den.", null);

        // Det Godot annars kräver att man klickar ihop i editorn en gång per
        // maskin görs deterministiskt här: editor settings + Android-preset +
        // etc2/astc-projektinställningen (vars avsaknad fäller exporten med
        // ett TOMT felmeddelande - verifierat mot 4.3-källkoden, v1.90).
        EnsureAndroidEditorSettings(sdkRoot, javaHome, keystore, godotConfigDir);
        EnsureAndroidPreset(root);
        EnsureEtc2AstcImport(root);

        var outApk = Path.Combine(root, "build", "android", Path.GetFileName(Path.GetFullPath(root)) + ".apk");
        Directory.CreateDirectory(Path.GetDirectoryName(outApk)!);

        // Godots align-steg kan falla med "Could not unzip temporary unaligned
        // APK" fast den o-alignade APK:n är en KOMPLETT, giltig zip (verifierat
        // live: Godots egen ZIPReader läser filen felfritt i en frisk process -
        // uppströmsegenhet i exportens processtillstånd). Reservkedjan tar då
        // vid med SDK:ts officiella verktyg: zipalign + apksigner. Rensa gamla
        // tmp-filer FÖRE körningen så reserven vet vilken fil som är vår.
        var godotCache = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Godot");
        try
        {
            if (Directory.Exists(godotCache))
                foreach (var stale in Directory.GetFiles(godotCache, "tmpexport-*"))
                    File.Delete(stale);
        }
        catch { /* städning - reserven kontrollerar antalet själv */ }

        // --export-debug: signeras med debug-keystoren. Release-signering
        // kräver en riktig release-nyckel som bara ägaren kan äga.
        var cmd = MakeGodotDebugCommand(godot, "Android", outApk);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (File.Exists(outApk))
            return (true, $"Android-APK klar (debug-signerad): {outApk}", outApk);

        // Reservkedjan: exakt EN kvarlämnad unaligned = vår.
        var unaligned = Directory.Exists(godotCache)
            ? Directory.GetFiles(godotCache, "tmpexport-unaligned.*.apk")
            : [];
        if (unaligned.Length == 1)
        {
            var (ok, reserveOut) = await AlignAndSignAsync(
                unaligned[0], outApk, sdkRoot, javaHome, keystore, runCommand, root, ct);
            try { File.Delete(unaligned[0]); } catch { /* städning */ }
            if (ok && File.Exists(outApk))
                return (true,
                    $"Android-APK klar (debug-signerad): {outApk}\n" +
                    "(Godots eget align-steg föll - reservkedjan alignade och signerade med SDK:ts zipalign/apksigner.)",
                    outApk);
            return (false, $"godot Android-export misslyckades (exit {exit}) och reservkedjan likaså:\n{reserveOut}\n{output}", null);
        }

        return (false, $"godot Android-export misslyckades (exit {exit}):\n{output}", null);
    }

    /// <summary>Reservkedjan när Godots align-steg faller: SDK:ts zipalign
    /// (4-byte) + apksigner (debug-keystoren) på den o-alignade APK:n Godot
    /// redan skrivit komplett. Samma två steg Godot själv hade gjort.</summary>
    internal static async Task<(bool Success, string Output)> AlignAndSignAsync(
        string unalignedApk, string outApk, string sdkRoot, string javaHome, string keystore,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        string workdir, CancellationToken ct)
    {
        var buildTools = Directory.Exists(Path.Combine(sdkRoot, "build-tools"))
            ? Directory.GetDirectories(Path.Combine(sdkRoot, "build-tools")).OrderByDescending(d => d).FirstOrDefault()
            : null;
        if (buildTools is null)
            return (false, "build-tools saknas i SDK:t - kör provision(\"android-sdk\").");
        var zipalign = Path.Combine(buildTools, "zipalign.exe");
        var apksigner = Path.Combine(buildTools, "apksigner.bat");
        if (!File.Exists(zipalign) || !File.Exists(apksigner))
            return (false, $"zipalign/apksigner saknas i {buildTools}.");

        // Dubblett-vakt: Godot force-adderar projektikonen en extra gang -
        // apksigner vagrar signera en zip med dubblerade namn. Skriv om zipen
        // utan dubbletter (forsta vinner) innan align/sign.
        DedupeZipEntries(unalignedApk);

        var (alignExit, alignOut) = await runCommand(
            $"\"{zipalign}\" -f 4 \"{unalignedApk}\" \"{outApk}\"", workdir, ct);
        if (alignExit != 0 || !File.Exists(outApk))
            return (false, $"zipalign misslyckades (exit {alignExit}): {alignOut}");

        // apksigner.bat behöver java - JAVA_HOME sätts i samma cmd-rad.
        var (signExit, signOut) = await runCommand(
            $"set \"JAVA_HOME={javaHome}\" && \"{apksigner}\" sign --ks \"{keystore}\" " +
            "--ks-pass pass:android --ks-key-alias androiddebugkey --key-pass pass:android " +
            $"\"{outApk}\"", workdir, ct);
        if (signExit != 0)
            return (false, $"apksigner misslyckades (exit {signExit}): {signOut}");
        return (true, "zipalign + apksigner klara.");
    }

    /// <summary>Nästa lediga [preset.N]-index i en export_presets.cfg-text.</summary>
    internal static int NextPresetIndex(string cfgText)
    {
        var max = -1;
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(
                     cfgText, @"^\[preset\.(\d+)\]", System.Text.RegularExpressions.RegexOptions.Multiline))
            max = Math.Max(max, int.Parse(m.Groups[1].Value));
        return max + 1;
    }

    /// <summary>Lägger till en Android-preset (icke-gradle, arm64, debug-
    /// signerad via editor settings-keystoren) om den saknas. Idempotent -
    /// befintliga presets (Windows/Web) behåller sina index.</summary>
    internal static void EnsureAndroidPreset(string root)
    {
        if (ExportPresetExists(root, "Android")) return;
        var cfg = Path.Combine(root, "export_presets.cfg");
        var existing = File.Exists(cfg) ? File.ReadAllText(cfg) : "";
        var n = NextPresetIndex(existing);
        var preset =
            (existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "") +
            $"\n[preset.{n}]\n" +
            "name=\"Android\"\n" +
            "platform=\"Android\"\n" +
            "runnable=true\n" +
            "advanced_options=false\n" +
            "dedicated_server=false\n" +
            "custom_features=\"\"\n" +
            "export_filter=\"all_resources\"\n" +
            "include_filter=\"\"\n" +
            // icon.ico exkluderas MEDVETET: Godot force-adderar projektikonen
            // en extra gang i varje export -> dubblettpost i APK-zipen ->
            // apksigner vagrar ("Multiple ZIP entries with the same name").
            // Exkluderingen tar bort resurspassets kopia; force-addens blir kvar.
            // build/* haller gamla byggen utanfor APK:n.
            "exclude_filter=\"icon.ico,build/*\"\n" +
            "export_path=\"build/android/spel.apk\"\n" +
            "patches=\"\"\n" +
            "encryption_include_filters=\"\"\n" +
            "encryption_exclude_filters=\"\"\n" +
            "seed=0\n" +
            "encrypt_pck=false\n" +
            "encrypt_directory=false\n" +
            $"\n[preset.{n}.options]\n" +
            "custom_template/debug=\"\"\n" +
            "custom_template/release=\"\"\n" +
            "gradle_build/use_gradle_build=false\n" +
            "gradle_build/export_format=0\n" +
            "gradle_build/min_sdk=\"\"\n" +
            "gradle_build/target_sdk=\"\"\n" +
            "architectures/armeabi-v7a=false\n" +
            "architectures/arm64-v8a=true\n" +
            "architectures/x86=false\n" +
            "architectures/x86_64=false\n" +
            "version/code=1\n" +
            "version/name=\"\"\n" +
            // Explicit sanerat namn, INTE $genname: projektnamn med mellanslag
            // ("Pixel Rush") gav en blockerande paketnamnsvarning headless.
            $"package/unique_name=\"org.ailocal.{SanitizePackageSegment(Path.GetFileName(Path.GetFullPath(root)))}\"\n" +
            "package/name=\"\"\n" +
            "package/signed=true\n" +
            "package/app_category=2\n" +
            "screen/immersive_mode=true\n" +
            "package/exclude_from_recents=false\n" +
            "package/show_in_android_tv=false\n" +
            "package/show_in_app_library=true\n" +
            "package/show_as_launcher_app=false\n";
        File.AppendAllText(cfg, preset);
    }

    /// <summary>Skriver/patchar Godots editor settings (per maskin) så
    /// Android-exporten hittar SDK, JDK och debug-keystoren - det man annars
    /// klickar ihop i editorn en gång. Regler (upptäckta LIVE på dev-maskinen):
    /// editorn skriver nycklarna med TOMMA värden som default, så "nyckeln
    /// finns" räcker inte - ett tomt värde fylls i, ett riktigt värde
    /// respekteras (ägarens setup vinner), och en keystore-väg som pekar på en
    /// fil som INTE finns ersätts (trasig default hjälper ingen). Basdir
    /// injicerbar för tester.</summary>
    internal static string EnsureAndroidEditorSettings(
        string sdkRoot, string javaHome, string keystorePath, string? godotConfigDir = null)
    {
        var dir = godotConfigDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Godot");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "editor_settings-4.3.tres");
        var text = File.Exists(file)
            ? File.ReadAllText(file)
            : "[gd_resource type=\"EditorSettings\" format=3]\n\n[resource]\n";
        static string Fwd(string p) => p.Replace('\\', '/');
        var wanted = new (string Key, string Value, bool ReplaceBrokenFile)[]
        {
            ("export/android/android_sdk_path", Fwd(sdkRoot), false),
            ("export/android/java_sdk_path", Fwd(javaHome), false),
            ("export/android/debug_keystore", Fwd(keystorePath), true),
            ("export/android/debug_keystore_user", "androiddebugkey", false),
            ("export/android/debug_keystore_pass", "android", false),
        };
        foreach (var (key, value, replaceBrokenFile) in wanted)
        {
            var rx = new System.Text.RegularExpressions.Regex(
                "^" + System.Text.RegularExpressions.Regex.Escape(key) + " = \"(.*)\"\\r?$",
                System.Text.RegularExpressions.RegexOptions.Multiline);
            var m = rx.Match(text);
            if (!m.Success)
            {
                text = text.Replace("[resource]\n", "[resource]\n" + key + " = \"" + value + "\"\n");
                continue;
            }
            var current = m.Groups[1].Value;
            var broken = replaceBrokenFile && current.Length > 0 && !File.Exists(current);
            if (current.Length == 0 || broken)
                text = rx.Replace(text, key + " = \"" + value + "\"", 1);
            // annars: ägarens riktiga värde vinner - rör det aldrig.
        }
        File.WriteAllText(file, text);
        return file;
    }

    /// <summary>Som MakeGodotCommand men --export-debug - Android signeras med
    /// debug-keystoren (release kräver ägarens egen nyckel).</summary>
    internal static string MakeGodotDebugCommand(string godotPath, string preset, string outFile)
        => $"\"{godotPath}\" --headless --export-debug \"{preset}\" \"{outFile}\"";

    /// <summary>Skriver om en zip utan dubblerade postnamn (första vinner).
    /// apksigner:s strikta parser vägrar signera dubbletter, och Godots
    /// export kan producera dem (force-adderad projektikon).</summary>
    internal static void DedupeZipEntries(string zipPath)
    {
        try
        {
            using (var read = System.IO.Compression.ZipFile.OpenRead(zipPath))
                if (read.Entries.GroupBy(e => e.FullName).All(g => g.Count() == 1))
                    return;   // inga dubbletter - rör inte filen
            var tmp = zipPath + ".dedupe";
            using (var read = System.IO.Compression.ZipFile.OpenRead(zipPath))
            using (var write = new System.IO.Compression.ZipArchive(
                File.Create(tmp), System.IO.Compression.ZipArchiveMode.Create))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in read.Entries)
                {
                    if (!seen.Add(entry.FullName)) continue;
                    var target = write.CreateEntry(entry.FullName, System.IO.Compression.CompressionLevel.Optimal);
                    using var src = entry.Open();
                    using var dst = target.Open();
                    src.CopyTo(dst);
                }
            }
            File.Move(tmp, zipPath, overwrite: true);
        }
        catch { /* reserven för reserven: align/sign får avgöra */ }
    }

    /// <summary>Android-exporten kräver projektinställningen
    /// rendering/textures/vram_compression/import_etc2_astc=true - annars
    /// fäller Godots validering exporten med ett TOMT felmeddelande (4.3-
    /// källkoden: should_import_etc2_astc -> valid=false utan text). Patchar
    /// project.godot idempotent; befintlig [rendering]-sektion återanvänds.</summary>
    internal static void EnsureEtc2AstcImport(string root)
    {
        var file = Path.Combine(root, "project.godot");
        if (!File.Exists(file)) return;
        var text = File.ReadAllText(file);
        if (text.Contains("import_etc2_astc", StringComparison.Ordinal)) return;
        text = text.Contains("[rendering]", StringComparison.Ordinal)
            ? text.Replace("[rendering]\n", "[rendering]\ntextures/vram_compression/import_etc2_astc=true\n")
            : text + (text.EndsWith('\n') ? "" : "\n") + "[rendering]\ntextures/vram_compression/import_etc2_astc=true\n";
        File.WriteAllText(file, text);
    }

    /// <summary>Ett giltigt java-paketsegment ur ett mappnamn: gemener a-z0-9,
    /// aldrig tomt, aldrig sifferinlett ("2048" -> "g2048").</summary>
    internal static string SanitizePackageSegment(string name)
    {
        var chars = (name ?? "").ToLowerInvariant().Where(c => c is >= 'a' and <= 'z' or >= '0' and <= '9').ToArray();
        var s = new string(chars);
        if (s.Length == 0) s = "spel";
        if (char.IsDigit(s[0])) s = "g" + s;
        return s;
    }

    /// <summary>True om export_presets.cfg innehåller en preset med det namnet.</summary>
    internal static bool ExportPresetExists(string root, string presetName)
    {
        try
        {
            var cfg = Path.Combine(root, "export_presets.cfg");
            return File.Exists(cfg) && File.ReadAllText(cfg).Contains($"name=\"{presetName}\"", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    internal static string MakeGodotCommand(string godotPath, string preset, string outExe)
        => $"\"{godotPath}\" --headless --export-release \"{preset}\" \"{outExe}\"";

    static string? FindGodot()
    {
        // Provisionerad godot (ToolLocator: %LOCALAPPDATA%\AiLocal\tools)
        // vinner - den finns garanterat inte pa PATH i den korande processen.
        if (AiLocal.Core.Agent.ToolLocator.Find("godot") is { } provisioned)
            return provisioned;
        var fromPath = FindOnPath("godot.exe") ?? FindOnPath("godot");
        if (fromPath is not null) return fromPath;
        var candidates = new[]
        {
            @"C:\Program Files\Godot\Godot_v4.3-stable_mono_win64.exe",
            @"C:\Program Files\Godot\godot.exe",
            @"C:\Program Files (x86)\Godot\godot.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Godot", "godot.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    // ---- Unity ------------------------------------------------------------
    async Task<(bool, string, string?)> BuildUnity(string root,
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct,
        Func<string?>? unityFinder = null)
    {
        var unity = (unityFinder ?? FindUnity)();
        if (unity is null)
            return (false,
                "Unity ar inte installerat pa denna maskin. Installera Unity 6000.x " +
                "(https://unity.com) och se till att 'Unity.exe' finns under C:/Program Files/Unity/Hub/Editor/<version>/Editor/. " +
                "'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        var outExe = Path.Combine(root, "build", DeriveExeName(root) + ".exe");
        // -buildWindows64Player respects the scenes registered in EditorBuildSettings.asset.
        var cmd = MakeUnityCommand(unity, root, outExe);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"unity build misslyckades (exit {exit}):\n{output}", null);
        if (!File.Exists(outExe))
            return (false, $"unity avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    internal static string MakeUnityCommand(string unityPath, string projectRoot, string outExe)
        => $"\"{unityPath}\" -batchmode -quit -projectPath \"{projectRoot}\" -buildWindows64Player \"{outExe}\"";

    static string? FindUnity()
    {
        var fromPath = FindOnPath("Unity.exe");
        if (fromPath is not null) return fromPath;
        var hub = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity", "Hub", "Editor");
        if (Directory.Exists(hub))
        {
            var editor = Directory.GetDirectories(hub)
                .Select(d => Path.Combine(d, "Editor", "Unity.exe"))
                .FirstOrDefault(File.Exists);
            if (editor is not null) return editor;
        }
        return null;
    }

    static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var full = Path.Combine(p, fileName);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore bad PATH entries */ }
        }
        return null;
    }
}
