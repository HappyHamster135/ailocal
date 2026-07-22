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
    public void MakeGodotCommand_Android_HarRattPreset()
    {
        // C9: samma kommandoform som desktop/webb, fast Android-preset -> APK.
        var cmd = GameBuilder.MakeGodotCommand("C:/Godot/godot.exe", "Android", "C:/g/build/android/spel.apk");
        Assert.Contains("--export-release", cmd);
        Assert.Contains("\"Android\"", cmd);
        Assert.Contains("spel.apk", cmd);
        Assert.DoesNotContain("--quit", cmd);
    }

    [Fact]
    public async Task BuildAndroidAsync_UtanAndroidPreset_GuidarArligtUtanAttKoraGodot()
    {
        // C9 best-effort: utan en Android-preset ska den GUIDA agaren till
        // setupen (SDK/keystore/preset) i stallet for att tyst misslyckas -
        // och godot far aldrig koras (guarden slar till forst).
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-android-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(dir, "project.godot"), "; godot\nconfig_version=5\n");
            await File.WriteAllTextAsync(Path.Combine(dir, "export_presets.cfg"), "[preset.0]\nname=\"Windows Desktop\"\n");
            var runCalled = false;

            var (success, output, apk) = await new GameBuilder().BuildAndroidAsync(
                dir,
                (_, _, _) => { runCalled = true; return Task.FromResult((0, "")); },
                CancellationToken.None,
                godotFinder: () => "C:/Godot/godot.exe");

            Assert.False(success);
            Assert.Null(apk);
            Assert.Contains("Android-preset", output);
            Assert.False(runCalled);
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
