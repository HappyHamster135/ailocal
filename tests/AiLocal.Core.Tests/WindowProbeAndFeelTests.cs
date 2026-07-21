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
    public void EnsureEngineFeelCriteria_GodotFarAllaFyra_Html5RorsAldrig()
    {
        var basic = new[] { "5 banor", "3 fiendetyper" };

        var godot = DirectorPass.EnsureEngineFeelCriteria(basic, "godot");
        Assert.Equal(6, godot.Count);
        Assert.Contains(godot, c => c.Contains("Ljudeffekt"));
        Assert.Contains(godot, c => c.Contains("feedback", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(godot, c => c.Contains("övergångar"));
        Assert.Contains(godot, c => c.Contains("Svårighetsgraderna"));

        Assert.Equal(basic, DirectorPass.EnsureEngineFeelCriteria(basic, "html5"));
        Assert.Equal(basic, DirectorPass.EnsureEngineFeelCriteria(basic, null));
    }

    [Fact]
    public void EnsureEngineFeelCriteria_RegissorensEgenFormuleringVinner()
    {
        var withSound = new[] { "ljudeffekter för hopp/mynt/träff", "5 banor" };
        var result = DirectorPass.EnsureEngineFeelCriteria(withSound, "godot");

        // Ljud-punkten är redan täckt av regissören - bara de tre övriga läggs.
        Assert.Equal(5, result.Count);
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
}
