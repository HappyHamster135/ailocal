using System.Diagnostics;
using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.46.0: fönstersonden (motorspelens interaktiva QA) och regissörens
/// spelkänsla-kriterier för godot/unity-kontrakt.
/// </summary>
public class WindowProbeAndFeelTests
{
    // ---- Pixeljämförelsen --------------------------------------------------

    [Fact]
    public void PixelDiffRatio_IdentisktNollOlikaEttHalvtOchLangdmissEtt()
    {
        var a = new byte[64 * 64 * 4];
        for (var i = 0; i < a.Length; i += 4) { a[i] = 100; a[i + 1] = 100; a[i + 2] = 100; a[i + 3] = 255; }

        Assert.Equal(0.0, GodotWindowProbe.PixelDiffRatio(a, (byte[])a.Clone()));

        // Övre halvan helt annan färg -> ungefär hälften av proven skiljer.
        var b = (byte[])a.Clone();
        for (var i = 0; i < b.Length / 2; i += 4) { b[i] = 240; b[i + 1] = 20; b[i + 2] = 20; }
        var ratio = GodotWindowProbe.PixelDiffRatio(a, b);
        Assert.InRange(ratio, 0.35, 0.65);

        Assert.Equal(1.0, GodotWindowProbe.PixelDiffRatio(a, new byte[8]));
    }

    // ---- Sondens ärliga degradering ---------------------------------------

    [Fact]
    public async Task PlayAsync_ProcessUtanFonster_DegraderarArligt()
    {
        var psi = new ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false };
        using var proc = Process.Start(psi)!;
        var output = Path.Combine(Path.GetTempPath(), "ailocal-probe-" + Guid.NewGuid().ToString("n") + ".png");

        var result = await GodotWindowProbe.PlayAsync(proc, output, CancellationToken.None);

        Assert.False(result.Ran);
        Assert.False(File.Exists(output));
    }

    // ---- Regissörens spelkänsla-kriterier ----------------------------------

    [Fact]
    public void EnsureEngineFeelCriteria_GodotFarAllaSex_Html5RorsAldrig()
    {
        var basic = new[] { "5 banor", "3 fiendetyper" };

        var godot = DirectorPass.EnsureEngineFeelCriteria(basic, "godot");
        Assert.Equal(8, godot.Count); // 2 basic + 6 spelkänsla-/kvalitetskriterier
        Assert.Contains(godot, c => c.Contains("Ljudeffekt"));
        Assert.Contains(godot, c => c.Contains("feedback", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(godot, c => c.Contains("övergångar"));
        Assert.Contains(godot, c => c.Contains("Svårighetsgraderna"));
        Assert.Contains(godot, c => c.Contains("juice", StringComparison.OrdinalIgnoreCase));     // C1
        Assert.Contains(godot, c => c.Contains("prestanda", StringComparison.OrdinalIgnoreCase));  // C3

        Assert.Equal(basic, DirectorPass.EnsureEngineFeelCriteria(basic, "html5"));
        Assert.Equal(basic, DirectorPass.EnsureEngineFeelCriteria(basic, null));
    }

    [Fact]
    public void EnsureEngineFeelCriteria_RegissorensEgenFormuleringVinner()
    {
        var withSound = new[] { "ljudeffekter för hopp/mynt/träff", "5 banor" };
        var result = DirectorPass.EnsureEngineFeelCriteria(withSound, "godot");

        // Ljud-punkten är redan täckt av regissören - bara de fem övriga
        // (feedback, övergångar, svårighet, juice, prestanda) läggs till.
        Assert.Equal(7, result.Count);
        Assert.Single(result, c => c.Contains("ljud", StringComparison.OrdinalIgnoreCase));
    }

    // ---- Livesond mot riktig Godot (gated - skarp där godot finns) ---------

    [Fact]
    public async Task PlayAsync_MotRiktigGodot_KorOchLamnarDump()
    {
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot) || !OperatingSystem.IsWindows()) return;

        // Scaffolda top-down-kitet och KÖR det - fönstret syns kort på
        // skärmen; det är den riktiga speltestningen, inte en bugg.
        var parent = Path.Combine(Path.GetTempPath(), "ailocal-probe-live-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(parent);
        var scaffold = new GameScaffoldService().Scaffold("godot", "top-down äventyr där man överlever vågor", parent);
        Assert.True(scaffold.Success, scaffold.Output);
        var screenshot = Path.Combine(parent, "probe.png");

        var psi = new ProcessStartInfo(godot)
        {
            Arguments = $"--path \"{scaffold.Path}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        };
        using var proc = Process.Start(psi)!;
        try
        {
            var result = await GodotWindowProbe.PlayAsync(proc, screenshot, CancellationToken.None);

            Assert.True(result.Ran, result.Notes);
            Assert.True(File.Exists(screenshot), "sonden lämnade ingen dump: " + result.Notes);
            Assert.True(new FileInfo(screenshot).Length > 1000, "dumpen misstänkt liten");
            // v1.47.0: titeldumpen sparas separat - visionens underlag för
            // "finns spelnamn/startval?" (mittspelsdumpen kan inte svara).
            Assert.NotNull(result.TitleScreenshotPath);
            Assert.True(File.Exists(result.TitleScreenshotPath), "titeldumpen saknas");
            Assert.True(new FileInfo(result.TitleScreenshotPath!).Length > 1000, "titeldumpen misstänkt liten");
            // v1.65.0 (B3): reprisen (animerad PNG) spelas in bredvid dumpen -
            // giltig PNG-signatur + acTL-chunk = faktisk APNG-animation.
            Assert.NotNull(result.ReplayPath);
            Assert.True(File.Exists(result.ReplayPath), "reprisen saknas: " + result.Notes);
            var replayBytes = await File.ReadAllBytesAsync(result.ReplayPath!);
            Assert.True(replayBytes.Length > 1000, "reprisen misstänkt liten");
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, replayBytes[..8]);
            Assert.Contains("acTL", System.Text.Encoding.ASCII.GetString(replayBytes));
            // Responded/ContinuouslyAnimating asserteras inte - titelskärmen
            // är statisk och knappstyrd, så ett ärligt "reagerar inte på
            // piltangenter" är ett giltigt utfall här. Kedjan (fönster, input
            // utan krasch, pixlar, PNG) är det som bevisas.
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { /* redan död */ }
            await Task.Delay(300);
            try { Directory.Delete(parent, recursive: true); } catch { /* städning */ }
        }
    }

    [Fact]
    public async Task PlayAsync_3dKitMotRiktigGodot_StartarOchSpelas()
    {
        // v1.74.0 (C1 del 3): runtime-bevis att 3D-kitet (Kuben) med juice
        // faktiskt STARTAR och gar att spela. Den adversariella granskningen
        // fangade att en slarvig edit hade lagt _ready:s boot-rader (ui +
        // _show_title) inuti _burst3d, sa spelet aldrig visade en titel - ett
        // LOGIKfel som headless-parsen inte kan se. Det har testet kor spelet
        // pa riktigt (fonster + input utan krasch) sa regressionen inte aterkommer.
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot) || !OperatingSystem.IsWindows()) return;

        var parent = Path.Combine(Path.GetTempPath(), "ailocal-probe-3d-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(parent);
        var scaffold = new GameScaffoldService().Scaffold("godot", "bygg ett 3d samlarspel i godot", parent);
        Assert.True(scaffold.Success, scaffold.Output);
        var screenshot = Path.Combine(parent, "probe.png");

        var psi = new ProcessStartInfo(godot)
        {
            Arguments = $"--path \"{scaffold.Path}\"",
            UseShellExecute = false,
            CreateNoWindow = false
        };
        using var proc = Process.Start(psi)!;
        try
        {
            var result = await GodotWindowProbe.PlayAsync(proc, screenshot, CancellationToken.None);
            Assert.True(result.Ran, result.Notes); // startar, visar fonster, kraschar inte
            Assert.True(File.Exists(screenshot), "3D-kitet lamnade ingen dump: " + result.Notes);
            Assert.True(new FileInfo(screenshot).Length > 1000, "dumpen misstänkt liten");
        }
        finally
        {
            try { if (!proc.HasExited) proc.Kill(); } catch { /* redan död */ }
            await Task.Delay(300);
            try { Directory.Delete(parent, recursive: true); } catch { /* städning */ }
        }
    }
}
