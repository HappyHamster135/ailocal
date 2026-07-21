using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>#2 - fun/speltest-till-design-loopen: playtestern bedömer nu SPELBARHET
/// och balans (inte bara "kör det?"), grinden eskalerar ett OSPELBART spel till en
/// designfixrunda och FixPrompt ger designer-vägledning.</summary>
public class PlaytestDesignTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ailocal-design-" + Guid.NewGuid().ToString("n"));
    public PlaytestDesignTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void DesignIssuesFrom_Ospelbart_BlirHartFynd()
    {
        var issues = GamePlaytester.DesignIssuesFrom("Ser tomt ut.\nSPELBART: nej - spelaren dör direkt utan chans");
        var only = Assert.Single(issues);
        Assert.Contains("INTE spelbart", only);
        Assert.Contains("dör direkt", only);
    }

    [Fact]
    public void DesignIssuesFrom_Spelbart_UtanBalansord_GerInga()
    {
        Assert.Empty(GamePlaytester.DesignIssuesFrom("Tydligt mål, rimlig utmaning. SPELBART: ja"));
    }

    [Theory]
    [InlineData("Spelaren dör direkt men SPELBART: ja", "för svårt")]
    [InlineData("Inget händer, ingen utmaning. SPELBART: ja", "för lätt")]
    public void DesignIssuesFrom_Balansord_GerMjukNote(string text, string expect)
    {
        var issues = GamePlaytester.DesignIssuesFrom(text);
        Assert.Contains(issues, i => i.Contains("SPELDESIGN") && i.Contains(expect));
    }

    [Fact]
    public async Task Gate_GodotSpelas_OchOspelbart_EskalererarTillHart()
    {
        // Bevisar att grinden nu PLAYTESTAR godot (inte bara html5) och att ett
        // ospelbart designutlatande blir ett hart fynd.
        var proj = Path.Combine(_dir, "spel");
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, "project.godot"), "config_version=5\nrun/main_scene=\"res://Main.tscn\"\n");
        File.WriteAllText(Path.Combine(proj, "Main.tscn"), "[gd_scene format=3]\n[node name=\"Main\" type=\"Node2D\"]\n");

        var findings = await AssignmentQualityGate.InspectAsync(
            proj, buildIntent: true, DateTime.MinValue,
            runCommand: (_, _, _) => Task.FromResult((0, "OK")),
            playtest: (root, engine, ct) => Task.FromResult((
                true, "spelsession", (IReadOnlyList<string>)new List<string>
                {
                    "SPELDESIGN: spelet bedöms INTE spelbart som det är - spelaren dör direkt"
                })),
            CancellationToken.None, gameExpected: true);

        Assert.True(findings.HardFail);
        Assert.Contains("INTE spelbart", findings.Report);
    }

    [Fact]
    public async Task Gate_GodotSpeltestKundeInteKoras_DegraderarUtanFalsktHartFynd()
    {
        // Pre-export utan godot-verktyg => FullTestAsync ger Success=false. Det
        // FAR INTE bli ett hart "Playtest misslyckades" (verify star for korrekthet).
        var proj = Path.Combine(_dir, "spel2");
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, "project.godot"), "config_version=5\nrun/main_scene=\"res://Main.tscn\"\n");
        File.WriteAllText(Path.Combine(proj, "Main.tscn"), "[gd_scene format=3]\n[node name=\"Main\" type=\"Node2D\"]\n");

        var findings = await AssignmentQualityGate.InspectAsync(
            proj, buildIntent: true, DateTime.MinValue,
            runCommand: (_, _, _) => Task.FromResult((0, "OK")),
            playtest: (root, engine, ct) => Task.FromResult((
                false, "Hittade inget spelbart (ingen godot/exe)", (IReadOnlyList<string>)new List<string>())),
            CancellationToken.None, gameExpected: true);

        Assert.DoesNotContain("Playtest misslyckades", findings.Report);
    }

    [Fact]
    public void FixPrompt_MedSpeldesignFynd_GerDesignerVagledning()
    {
        var withDesign = new QualityFindings(false, true, "Playtest: SPELDESIGN: spelet är ospelbart", "/x", "godot");
        Assert.Contains("balans", AssignmentQualityGate.FixPrompt(withDesign));

        var withoutDesign = new QualityFindings(false, true, "VERIFY FAILED: syntaxfel", "/x", "godot");
        Assert.DoesNotContain("balans/svårighet", AssignmentQualityGate.FixPrompt(withoutDesign));
    }
}
