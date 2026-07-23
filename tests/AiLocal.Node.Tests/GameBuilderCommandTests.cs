using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>Locks the actual engine command shape (P2): the headless export
/// must NOT pass --quit before --export-release (Godot 4 would exit first),
/// and BuildAsync must drive runCommand with that command and surface the .exe.
/// No real engine is needed - the finder and shell are injected.</summary>
public class GameBuilderCommandTests
{
    [Fact]
    public void MakeGodotCommand_NoPrematureQuit()
    {
        var cmd = GameBuilder.MakeGodotCommand("C:/Godot/godot.exe", "Windows Desktop", "C:/g/build/PixelRush.exe");
        Assert.Contains("--headless", cmd);
        Assert.Contains("--export-release", cmd);
        Assert.Contains("\"Windows Desktop\"", cmd);
        Assert.Contains("\"C:/g/build/PixelRush.exe\"", cmd);
        // The bug we locked out: --quit before --export-release makes Godot exit without exporting.
        Assert.DoesNotContain("--quit", cmd);
    }

    [Fact]
    public void MakeUnityCommand_BatchModeBuild()
    {
        var cmd = GameBuilder.MakeUnityCommand("C:/Unity/Unity.exe", "C:/g", "C:/g/build/PixelRush.exe");
        Assert.Contains("-batchmode", cmd);
        Assert.Contains("-quit", cmd);
        Assert.Contains("-projectPath \"C:/g\"", cmd);
        Assert.Contains("-buildWindows64Player \"C:/g/build/PixelRush.exe\"", cmd);
    }

    [Fact]
    public void MakeGodotDebugCommand_Android_ExportDebug()
    {
        // v1.90: Android exporteras --export-debug (debug-keystoren signerar;
        // release kraver agarens egen nyckel) - annars samma kommandoform.
        var cmd = GameBuilder.MakeGodotDebugCommand("C:/Godot/godot.exe", "Android", "C:/g/build/android/spel.apk");
        Assert.Contains("--export-debug", cmd);
        Assert.Contains("\"Android\"", cmd);
        Assert.Contains("spel.apk", cmd);
        Assert.DoesNotContain("--quit", cmd);
    }

    [Fact]
    public async Task BuildAndroidAsync_UtanSdk_GuidarTillProvisionering_UtanAttKoraGodot()
    {
        // v1.90: utan Android-SDK ska vagen GUIDA till provision("android-sdk")
        // (sjalvprovisionering finns nu) - och godot far aldrig koras.
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-android-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "project.godot"), "; godot\nconfig_version=5\n");
            var runCalled = false;

            var (success, output, apk) = await new GameBuilder().BuildAndroidAsync(
                dir,
                (_, _, _) => { runCalled = true; return Task.FromResult((0, "")); },
                CancellationToken.None,
                godotFinder: () => "C:/Godot/godot.exe",
                sdkRootFinder: () => null);

            Assert.False(success);
            Assert.Null(apk);
            Assert.Contains("provision(\"android-sdk\")", output);
            Assert.False(runCalled);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* städning */ } }
    }

    [Fact]
    public async Task BuildAndroidAsync_MedSdk_SakerstallerPresetOchSettings_OchExporterarDebug()
    {
        // v1.90 huvudvagen: med SDK+JDK+keystore pa plats ska vagen sjalv
        // (1) skriva editor settings (SDK/JDK/keystore, forward slashes),
        // (2) lagga till Android-preseten med NASTA index utan att rora
        // befintliga, och (3) kora --export-debug "Android".
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-android-" + Guid.NewGuid().ToString("N"));
        var sdk = Path.Combine(dir, "sdk");
        var jdkBin = Path.Combine(dir, "jdk", "bin");
        var cfgDir = Path.Combine(dir, "godot-config");
        Directory.CreateDirectory(dir);
        Directory.CreateDirectory(sdk);
        Directory.CreateDirectory(jdkBin);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "project.godot"), "; godot\nconfig_version=5\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "export_presets.cfg"), "[preset.0]\nname=\"Windows Desktop\"\n");
            await File.WriteAllTextAsync(Path.Combine(sdk, "debug.keystore"), "fejk-keystore");
            await File.WriteAllTextAsync(Path.Combine(jdkBin, "java.exe"), "fejk-java");

            string? seenCmd = null;
            var (success, output, apk) = await new GameBuilder().BuildAndroidAsync(
                dir,
                (cmd, _, _) =>
                {
                    seenCmd = cmd;
                    // "godot" skapar APK:n - exporten lyckas bara om filen finns.
                    var outApk = Path.Combine(dir, "build", "android", Path.GetFileName(dir) + ".apk");
                    Directory.CreateDirectory(Path.GetDirectoryName(outApk)!);
                    File.WriteAllText(outApk, "PK-fejk");
                    return Task.FromResult((0, "export ok"));
                },
                CancellationToken.None,
                godotFinder: () => "C:/Godot/godot.exe",
                sdkRootFinder: () => sdk,
                javaFinder: () => Path.Combine(jdkBin, "java.exe"),
                godotConfigDir: cfgDir);

            Assert.True(success, output);
            Assert.NotNull(apk);
            Assert.Contains("--export-debug", seenCmd);
            Assert.Contains("\"Android\"", seenCmd);
            // Preseten lades till med nasta index; befintlig preset ororad.
            var presets = await File.ReadAllTextAsync(Path.Combine(dir, "export_presets.cfg"));
            Assert.Contains("name=\"Windows Desktop\"", presets);
            Assert.Contains("[preset.1]", presets);
            Assert.Contains("name=\"Android\"", presets);
            Assert.Contains("gradle_build/use_gradle_build=false", presets);
            // Editor settings skrevs med forward slashes och keystore-varden.
            var settings = await File.ReadAllTextAsync(Path.Combine(cfgDir, "editor_settings-4.3.tres"));
            Assert.Contains("export/android/android_sdk_path = \"" + sdk.Replace('\\', '/') + "\"", settings);
            Assert.Contains("export/android/debug_keystore_user = \"androiddebugkey\"", settings);
            Assert.DoesNotContain("\\", settings.Split('\n').First(l => l.Contains("android_sdk_path")));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* städning */ } }
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("[preset.0]\nname=\"W\"\n", 1)]
    [InlineData("[preset.0]\nname=\"W\"\n[preset.1]\nname=\"Web\"\n", 2)]
    public void NextPresetIndex_RaknarRatt(string cfg, int expected)
        => Assert.Equal(expected, GameBuilder.NextPresetIndex(cfg));

    [Theory]
    [InlineData("Pixel Rush", "pixelrush")]      // mellanslag + versaler bort
    [InlineData("2048-spelet", "g2048spelet")]   // sifferinlett prefixas
    [InlineData("!!!", "spel")]                  // aldrig tomt
    public void SanitizePackageSegment_GerGiltigaJavaSegment(string input, string expected)
        => Assert.Equal(expected, GameBuilder.SanitizePackageSegment(input));

    [Fact]
    public void EnsureAndroidEditorSettings_TommaVardenFylls_RiktigaRespekteras_TrasigKeystoreErsatts()
    {
        // LIVE-upptackt pa dev-maskinen: Godot-editorn skriver nycklarna med
        // TOMMA varden som default - "nyckeln finns" racker inte som koll.
        // Och editorns default-keystore-vag kan peka pa en fil som inte finns.
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-godotcfg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "editor_settings-4.3.tres"),
                "[gd_resource type=\"EditorSettings\" format=3]\n\n[resource]\n" +
                "export/android/android_sdk_path = \"D:/agarens/egen/sdk\"\n" +      // riktigt varde
                "export/android/java_sdk_path = \"\"\n" +                            // tomt (editor-default)
                "export/android/debug_keystore = \"C:/finns/inte/debug.keystore\"\n"); // trasig vag

            GameBuilder.EnsureAndroidEditorSettings("C:/ny/sdk", "C:/jdk", "C:/ny/sdk/debug.keystore", dir);

            var text = File.ReadAllText(Path.Combine(dir, "editor_settings-4.3.tres"));
            Assert.Contains("android_sdk_path = \"D:/agarens/egen/sdk\"", text);   // agarens varde vinner
            Assert.DoesNotContain("android_sdk_path = \"C:/ny/sdk\"", text);
            Assert.Contains("java_sdk_path = \"C:/jdk\"", text);                    // tomt fylldes i
            Assert.Contains("debug_keystore = \"C:/ny/sdk/debug.keystore\"", text); // trasig vag ersatt
            Assert.Contains("debug_keystore_user = \"androiddebugkey\"", text);     // saknad nyckel infogad
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* städning */ } }
    }

    [Fact]
    public async Task BuildAsync_Godot_ExportsAndReportsExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "build"));
        // The exe is named after the project folder (DeriveExeName), not a
        // hardcoded PixelRush - every shipped game gets its own name.
        var outExe = Path.Combine(root, "build", GameBuilder.DeriveExeName(root) + ".exe");
        File.WriteAllText(outExe, "MZ"); // pretend the export succeeded

        string? capturedCmd = null;
        Func<string, string, CancellationToken, Task<(int, string)>> run = (c, dir, ct) =>
        {
            capturedCmd = c;
            return Task.FromResult((0, "export ok"));
        };

        var builder = new GameBuilder();
        var (success, output, exePath) = await builder.BuildAsync("godot", root, run,
            CancellationToken.None, godotFinder: () => "C:/Godot/godot.exe");

        Assert.True(success);
        Assert.Equal(outExe, exePath);
        Assert.NotNull(capturedCmd);
        Assert.Contains("--export-release", capturedCmd);
        Assert.DoesNotContain("--quit", capturedCmd);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_Godot_ExportFailure_SurfacesExit()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        // 'auto' detects the engine from project files - without this marker
        // detection yields 'unknown' and the export path is never reached.
        File.WriteAllText(Path.Combine(root, "project.godot"), "");

        string? capturedCmd = null;
        Func<string, string, CancellationToken, Task<(int, string)>> run = (c, dir, ct) =>
        {
            capturedCmd = c;
            return Task.FromResult((1, "ERROR: preset not found"));
        };

        var builder = new GameBuilder();
        var (success, output, exePath) = await builder.BuildAsync("auto", root, run,
            CancellationToken.None, godotFinder: () => "C:/Godot/godot.exe");

        Assert.False(success);
        Assert.Null(exePath);
        Assert.Contains("godot export misslyckades", output);
        Assert.Contains("Windows Desktop", capturedCmd); // confirms it used the right preset

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_Godot_SkaparExportmappenForeKorningen()
    {
        // v2.7, skarpt e2e-fynd: Godot skapar INTE build/-mappen sjalv -
        // exporten foll alltid med "The given export path doesn't exist".
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.godot"), "");

        var dirExistedAtRun = false;
        Func<string, string, CancellationToken, Task<(int, string)>> run = (c, dir, ct) =>
        {
            dirExistedAtRun = Directory.Exists(Path.Combine(root, "build"));
            return Task.FromResult((1, "stub"));
        };

        var builder = new GameBuilder();
        await builder.BuildAsync("auto", root, run, CancellationToken.None, godotFinder: () => "C:/Godot/godot.exe");

        Assert.True(dirExistedAtRun, "build/-mappen fanns inte nar godot kordes");
        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_Godot_ExportFailureUtanUtskrift_FarDiagnos()
    {
        // v1.99, live-sett: exporten föll med exit 1 och HELT TOM utskrift -
        // felraden var oanvändbar. Nu diagnostiserar vi presets/mallar själva.
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.godot"), "");
        // Ingen export_presets.cfg skrivs -> diagnosen ska peka på just den.

        var builder = new GameBuilder();
        var (success, output, _) = await builder.BuildAsync("auto", root,
            (c, dir, ct) => Task.FromResult((1, "   ")), CancellationToken.None,
            godotFinder: () => "C:/Godot/godot.exe");

        Assert.False(success);
        Assert.Contains("ingen felutskrift", output);
        Assert.Contains("export_presets.cfg saknas", output);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public void GodotExportHints_MedPresets_PekarPaMallarna()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "export_presets.cfg"), "[preset.0]");
        try
        {
            var hints = GameBuilder.GodotExportHints(root);
            // Presets finns -> diagnosen handlar om mallarna ELLER säger
            // ärligt att båda finns (dev-maskinen har mallar provisionerade).
            Assert.DoesNotContain("export_presets.cfg saknas", hints);
            Assert.Contains("Trolig orsak", hints);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task BuildAsync_AutoOnHtml5Project_ReportsNoBuildNeeded()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "index.html"), "<canvas></canvas>");

        var builder = new GameBuilder();
        var (success, output, exePath) = await builder.BuildAsync("auto", root,
            (c, dir, ct) => Task.FromResult((0, "")), CancellationToken.None);

        // The most common project type must not error with "okant motor" -
        // an html5 game needs no engine build, and 'auto' should know that.
        Assert.True(success, output);
        Assert.Null(exePath);
        Assert.Contains("webblasare", output);

        Directory.Delete(root, recursive: true);
    }

    [Fact]
    public async Task BuildAsync_GodotNotFound_ReturnsActionableError()
    {
        var root = Path.Combine(Path.GetTempPath(), "ailocal-gb-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var builder = new GameBuilder();
        var (success, output, exePath) = await builder.BuildAsync("godot", root,
            (c, dir, ct) => Task.FromResult((0, "")),
            CancellationToken.None, godotFinder: () => null);

        Assert.False(success);
        Assert.Null(exePath);
        Assert.Contains("Godot ar inte installerat", output);

        Directory.Delete(root, recursive: true);
    }
}
