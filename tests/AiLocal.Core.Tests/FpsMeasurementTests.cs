using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.31: FPS-mätningen var teater. EstimateFps returnerade "cpuTime > 0 ? 60 : 0"
/// - alltså exakt 60 för varje process som körde - och grinden if (avgFps &lt; 30)
/// kunde därför bara utlösa för ett spel som ALDRIG STARTADE. Ett hackigt spel
/// gick rakt igenom kvalitetsgrinden i alla releaser fram till nu.
///
/// Nu läses motorns egen siffra ur --print-fps.
/// </summary>
// v2.36: alla klasser som STARTAR godot delar samma xunit-samling och
// kor darfor aldrig parallellt. Fonstersonden foll slumpvis i full svit
// men var gron riktad: dess WaitForVisibleWindow pa 15 s hann inte nar
// atton andra godot-processer slogs om cpu:n. Riktad gron + full svit
// rod = parallellkrock, aldrig logikfel.
[Collection("GodotProcess")]
public class FpsMeasurementTests
{
    [Theory]
    [InlineData("Project FPS: 145 (6.89 mspf)", 145)]
    [InlineData("Project FPS: 60 (16.67 mspf)", 60)]
    [InlineData("Project FPS: 8 (125.00 mspf)", 8)]
    [InlineData("  Project FPS: 144", 144)]
    public void ParsarMotornsEgnaFpsRader(string line, double expected)
    {
        var v = GamePlaytester.ParseGodotFps(line);
        Assert.NotNull(v);
        Assert.Equal(expected, v!.Value, 1);
    }

    [Theory]
    [InlineData("Godot Engine v4.3.stable.official")]
    [InlineData("Vulkan 1.4.341 - Forward+")]
    [InlineData("SCRIPT ERROR: Parse Error: nagot gick fel")]
    [InlineData("")]
    [InlineData("Requested V-Sync mode: Enabled - FPS will likely be capped")]
    public void IgnorerarRaderSomInteArFpsRapporter(string line)
    {
        // Sista fallet ar viktigt: raden NAMNER FPS men innehaller ingen
        // siffra att lasa - en slarvig parser hade gett skrap.
        Assert.Null(GamePlaytester.ParseGodotFps(line));
    }

    [Fact]
    public void NollOchNegativtRaknasInteSomMatning()
    {
        // Ett spel som rapporterar 0 FPS har inte ritat nagot an - det ar
        // uppstart, inte en prestandamatning.
        Assert.Null(GamePlaytester.ParseGodotFps("Project FPS: 0 (0.00 mspf)"));
    }

    [Fact]
    public void GamlaFalskaMatningen_FinnsInteKvar()
    {
        // Regressionsvakt: den gamla EstimateFps svarade 60 pa allt. Om nagon
        // aterinfor det monstret ar FPS-grinden dod igen utan att nagot syns.
        var all = File.ReadAllText(Path.Combine(RepoRoot(),
            "src", "AiLocal.Node", "Hosting", "GamePlaytester.cs"));
        // Bara KOD granskas - kommentarerna FORKLARAR den gamla buggen och
        // ska fa namna den. En grovare matchning hade slagit pa sin egen
        // dokumentation, vilket ar precis den sortens falska rodgront som
        // gor tester otillforlitliga.
        var code = string.Join("\n", File.ReadAllLines(Path.Combine(RepoRoot(),
                "src", "AiLocal.Node", "Hosting", "GamePlaytester.cs"))
            .Where(l => !l.TrimStart().StartsWith("//", StringComparison.Ordinal)
                     && !l.TrimStart().StartsWith("///", StringComparison.Ordinal)));
        Assert.DoesNotContain("cpuTime > 0 ? 60 : 0", code);
        Assert.DoesNotContain("private static double EstimateFps", code);
        // Och sonden maste faktiskt be motorn rapportera.
        Assert.Contains("--print-fps", all);
    }

    [Fact]
    public async Task SkarptMotRiktigtSpel_GerVERKLIGAOchVARIERANDESiffror()
    {
        // Gated men SKARPT dar godot finns (agarens dev-maskin). Detta ar det
        // enda testet som bevisar hela kedjan: att motorn faktiskt rapporterar,
        // att stdout lases lopande, och att siffrorna ar verkliga.
        // Enhetstest pa parsern bevisar bara parsern.
        var godot = AiLocal.Core.Agent.ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var dir = Directory.CreateTempSubdirectory("ailocal-fps-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett snake spel i godot", dir);
            var root = AiLocal.Core.Agent.ProjectRootDetector.Detect(dir) ?? dir;

            // Import forst - annars startar spelet utan sina resurser.
            using (var imp = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(godot)
            {
                ArgumentList = { "--headless", "--path", root, "--import" },
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            })!)
            {
                _ = imp.StandardOutput.ReadToEndAsync();
                _ = imp.StandardError.ReadToEndAsync();
                await imp.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            }

            var psi = new System.Diagnostics.ProcessStartInfo(godot)
            {
                ArgumentList = { "--path", root, "--print-fps" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var samples = new List<double>();
            var reader = Task.Run(async () =>
            {
                string? line;
                while ((line = await proc.StandardOutput.ReadLineAsync()) is not null)
                    if (GamePlaytester.ParseGodotFps(line) is { } f)
                        lock (samples) samples.Add(f);
            });
            _ = Task.Run(async () => { while (await proc.StandardError.ReadLineAsync() is not null) { } });

            await Task.Delay(TimeSpan.FromSeconds(8));
            try { proc.Kill(); } catch { }
            try { await reader.WaitAsync(TimeSpan.FromSeconds(3)); } catch { }

            double[] fps;
            lock (samples) fps = [.. samples];
            Assert.True(fps.Length >= 3,
                $"motorn rapporterade bara {fps.Length} FPS-varden - matningen ar inte inkopplad");
            // Karnan: den GAMLA matningen gav exakt 60 varje gang. Riktiga
            // siffror ar bade rimliga OCH varierande.
            Assert.False(fps.All(f => f == 60.0), "alla varden exakt 60 - den falska matningen ar kvar");
            Assert.All(fps, f => Assert.InRange(f, 1, 1000));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static string RepoRoot()
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && d is not null; i++)
        {
            if (Directory.Exists(Path.Combine(d, "src", "AiLocal.Node"))) return d;
            d = Path.GetDirectoryName(d);
        }
        throw new DirectoryNotFoundException("hittar inte repo-roten");
    }
}
