using System.Diagnostics;
using System.IO.Compression;
using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.90: verifierad Android-APK. Provisioneringstestet är env-gated
/// (laddar ner ~150 MB SDK + ev. ~900 MB exportmallar - körs skarpt EN gång
/// per maskin, gjordes på dev-maskinen inför releasen med ägarens
/// godkännande). Exporttestet är presence-gated som övriga godot-livetester:
/// finns kedjan (godot + mallar + SDK + JDK) exporteras en RIKTIG debug-APK
/// av plattformar-kittet och innehållet verifieras (zip + AndroidManifest).</summary>
// v2.36: alla klasser som STARTAR godot delar samma xunit-samling och
// kor darfor aldrig parallellt. Fonstersonden foll slumpvis i full svit
// men var gron riktad: dess WaitForVisibleWindow pa 15 s hann inte nar
// atton andra godot-processer slogs om cpu:n. Riktad gron + full svit
// rod = parallellkrock, aldrig logikfel.
[Collection("GodotProcess")]
public class AndroidExportTests
{
    [Fact]
    public async Task AndroidKedjan_ProvisionerasSkarpt_NarBegard()
    {
        // AILOCAL_LIVE_ANDROID_PROVISION=1 = uttrycklig begäran (stora
        // nedladdningar). Idempotent: befintliga delar hoppas över.
        if (Environment.GetEnvironmentVariable("AILOCAL_LIVE_ANDROID_PROVISION") != "1") return;

        var templatesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot", "export_templates", "4.3.stable.mono");
        if (!File.Exists(Path.Combine(templatesDir, "android_debug.apk")))
        {
            var tpl = await new ToolProvisioner().ProvisionAsync("godot-templates", "", CancellationToken.None);
            Assert.True(tpl.Success, tpl.Output);
            Assert.True(File.Exists(Path.Combine(templatesDir, "android_debug.apk")),
                "exportmallarna extraherade men android_debug.apk saknas");
        }

        if (ToolLocator.AndroidSdkRoot() is null
            || ToolLocator.Find("godot-standard") is null
            || !File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "Godot", "export_templates", "4.3.stable", "android_debug.apk")))
        {
            // Idempotent aven nar SDK-delen redan finns: efterkedjan fyller pa
            // det som saknas (standard-godot + standardmallar, v1.90).
            var sdk = await new ToolProvisioner().ProvisionAsync("android-sdk", "", CancellationToken.None);
            Assert.True(sdk.Success, sdk.Output);
        }
        var root = ToolLocator.AndroidSdkRoot();
        Assert.NotNull(root);
        Assert.True(File.Exists(Path.Combine(root!, "debug.keystore")), "debug.keystore saknas efter provisionering");
        Assert.True(Directory.Exists(Path.Combine(root!, "platform-tools")), "platform-tools saknas");
        Assert.True(Directory.Exists(Path.Combine(root!, "build-tools")), "build-tools saknas");
        // Standard-Godot + standardmallar (mono-editorn blockerar Android headless).
        Assert.NotNull(ToolLocator.Find("godot-standard"));
        Assert.True(File.Exists(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot", "export_templates", "4.3.stable", "android_debug.apk")),
            "standardmallarnas android_debug.apk saknas");
    }

    [Fact]
    public async Task AndroidApk_ExporterasSkarpt_NarKedjanFinns()
    {
        // Presence-gated som övriga godot-livetester: utan kedjan finns inget
        // att köra här (provisioneringstestet ovan bygger den en gång).
        var godot = ToolLocator.Find("godot-standard");   // mono blockerar Android headless (v1.90)
        var sdkRoot = ToolLocator.AndroidSdkRoot();
        var java = ToolLocator.Find("java");
        var androidTemplate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Godot", "export_templates", "4.3.stable", "android_debug.apk");
        if (godot is null || sdkRoot is null || java is null || !File.Exists(androidTemplate)) return;

        var parent = Path.Combine(Path.GetTempPath(), "ailocal-apk-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(parent);
        try
        {
            var scaffold = new GameScaffoldService().Scaffold("godot", "bygg ett 2d plattformsspel i godot", parent);
            Assert.True(scaffold.Success, scaffold.Output);

            var (success, output, apkPath) = await new GameBuilder().BuildAndroidAsync(
                scaffold.Path, RunRealCommandAsync, CancellationToken.None);

            Assert.True(success, output);
            Assert.NotNull(apkPath);
            var bytes = await File.ReadAllBytesAsync(apkPath!);
            Assert.True(bytes.Length > 500_000, $"APK misstänkt liten ({bytes.Length} B)");
            // Zip-signatur + riktigt APK-innehåll - inte bara "en fil skrevs".
            Assert.Equal((byte)'P', bytes[0]);
            Assert.Equal((byte)'K', bytes[1]);
            using var zip = ZipFile.OpenRead(apkPath!);
            Assert.Contains(zip.Entries, e => e.FullName == "AndroidManifest.xml");
            Assert.Contains(zip.Entries, e => e.FullName.StartsWith("assets/"));
        }
        finally { try { Directory.Delete(parent, recursive: true); } catch { /* städning */ } }
    }

    private static Task<(int ExitCode, string Output)> RunRealCommandAsync(
        string cmd, string workdir, CancellationToken ct)
    {
        // Wrappad /c "{cmd}" + EVENT-baserad läsning, exakt som produktionens
        // runCmd: exporten startar adb-servern som ÄRVER pipe-handtagen och
        // lever kvar - ReadToEnd fick aldrig EOF och hängde testet i 10+ min
        // (upptäckt live v1.90). Eventläsning + WaitForExit(process, inte
        // pipes) är immun mot kvarlevande barnprocesser.
        var psi = new ProcessStartInfo("cmd.exe", "/c \"" + cmd + "\"")
        {
            WorkingDirectory = workdir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        using var proc = new Process { StartInfo = psi };
        var so = new System.Text.StringBuilder();
        var se = new System.Text.StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        if (!proc.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
        {
            try { proc.Kill(entireProcessTree: true); } catch { /* redan död */ }
            return Task.FromResult((-1, $"tidsgräns: {so}\n{se}"));
        }
        return Task.FromResult((proc.ExitCode, $"{so}\n{se}"));
    }
}
