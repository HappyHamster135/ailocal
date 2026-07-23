using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.18: förhandsvalen (stil/omfång/fråga-först ur composern), förhands-
/// frågorna och stafettens överlämningar - riktning FÖRE första token och
/// konstant kontextstorlek per pass i stället för 8M-token-svällning.
/// </summary>
public class PreBuildAndRelayTests
{
    // ---- BuildDirectives ---------------------------------------------------

    [Fact]
    public void Parse_TaggarPlockasOchStrippas()
    {
        var c = BuildDirectives.Parse("[STIL: pixelart]\n[OMFANG: stort]\n[FORHANDSFRAGOR]\nbygg ett mario party spel");
        Assert.Equal("pixelart", c.Style);
        Assert.Equal("stort", c.Scope);
        Assert.True(c.AskFirst);
        Assert.Equal("bygg ett mario party spel", c.CleanAssignment);
    }

    [Fact]
    public void Parse_UtanTaggar_AlltAr_Auto()
    {
        var c = BuildDirectives.Parse("bygg ett plattformsspel i godot");
        Assert.Null(c.Style);
        Assert.Null(c.Scope);
        Assert.False(c.AskFirst);
        Assert.Equal("bygg ett plattformsspel i godot", c.CleanAssignment);
        // Text som råkar nämna orden mitt i en mening är INTE en tagg.
        var c2 = BuildDirectives.Parse("gör spelet i stil: pixelart tack");
        Assert.Null(c2.Style);
    }

    [Fact]
    public void StyleOchScope_BlirKontraktspunkter()
    {
        Assert.Contains("PIXELART", BuildDirectives.StyleCriterion("pixelart"));
        Assert.Contains("ISOMETRISK", BuildDirectives.StyleCriterion("iso"));
        Assert.Contains("3D", BuildDirectives.StyleCriterion("3d"));
        Assert.Contains("VEKTOR", BuildDirectives.StyleCriterion("vektor"));
        Assert.Null(BuildDirectives.StyleCriterion(null));
        Assert.Contains("LITET", BuildDirectives.ScopeCriterion("litet"));
        Assert.Contains("STORT", BuildDirectives.ScopeCriterion("stort"));
        Assert.Null(BuildDirectives.ScopeCriterion("standard"));
    }

    [Fact]
    public void MaxPasses_FoljerOmfanget()
    {
        Assert.Equal(3, BuildDirectives.MaxPasses("litet"));
        Assert.Equal(4, BuildDirectives.MaxPasses(null));
        Assert.Equal(6, BuildDirectives.MaxPasses("stort"));
    }

    // ---- PreBuildQuestions -------------------------------------------------

    [Fact]
    public void ParseQuestions_GiltigJson_MaxTre()
    {
        var qs = PreBuildQuestions.ParseQuestions(
            "Här är frågorna:\n{\"questions\": [\"Simulator eller arkad? (realistisk / arkad)\", " +
            "\"Öppen värld eller banor? (öppen värld / banor)\", \"En eller flera spelare?\", \"En fjärde för mycket?\"]}");
        Assert.Equal(3, qs.Count);
        Assert.Contains(qs, q => q.Contains("Simulator"));
    }

    [Fact]
    public void ParseQuestions_SkrapEllerTomt_GerTomLista()
    {
        Assert.Empty(PreBuildQuestions.ParseQuestions("inga frågor här"));
        Assert.Empty(PreBuildQuestions.ParseQuestions("{\"questions\": []}"));
        Assert.Empty(PreBuildQuestions.ParseQuestions("{\"other\": true}"));
    }

    // ---- RelayHandover -----------------------------------------------------

    [Fact]
    public void LooksUsable_KraverSubstansOchRubrik()
    {
        Assert.False(RelayHandover.LooksUsable(null));
        Assert.False(RelayHandover.LooksUsable("Klart!"));
        Assert.False(RelayHandover.LooksUsable(new string('x', 200))); // långt men utan rubrik
        Assert.True(RelayHandover.LooksUsable(
            "## KLART\n- Main.gd: brädet och tärningen byggda och verifierade\n" +
            "## ATERSTAR\n- minigame 2 och 3 (MgDodge.gd, MgMemory.gd)\n" +
            "## NASTA STEG\n- börja med MgDodge.gd enligt kontraktet"));
    }

    [Fact]
    public void RelayPrompt_BarUppdragOverlamningOchSkyddsregler()
    {
        var p = RelayHandover.RelayPrompt("bygg ett partyspel", "## KLART\n- brädet", 3, 6);
        Assert.Contains("pass 3 av 6", p);
        Assert.Contains("bygg ett partyspel", p);
        Assert.Contains("## KLART", p);
        Assert.Contains("får inte rivas", p);
        Assert.Contains("verify", p);
    }

    [Fact]
    public void RequestPrompt_KraverRubrikernaOchIngenKod()
    {
        Assert.Contains("## KLART", RelayHandover.RequestPrompt);
        Assert.Contains("## ATERSTAR", RelayHandover.RequestPrompt);
        Assert.Contains("## NASTA STEG", RelayHandover.RequestPrompt);
        Assert.Contains("ingen kod", RelayHandover.RequestPrompt);
    }

    // ---- DirectorPass: operatörens kriterier väger tyngst ------------------

    [Fact]
    public async Task Director_OperatorskriterierLaggsForst()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-dir-").FullName;
        try
        {
            // Modellanropet failar => fallbackkontraktet används - operatörens
            // stilval ska ändå ligga FÖRST i kriterielistan.
            Task<ProviderResponse> FailingComplete(ChatRequest _, CancellationToken __) =>
                Task.FromResult(ProviderResponse.Fail(ProviderOutcome.TransientError, "test"));

            var contract = await DirectorPass.RunAsync(
                "bygg ett plattformsspel", dir, null, FailingComplete, CancellationToken.None,
                engine: "godot",
                operatorCriteria: [BuildDirectives.StyleCriterion("pixelart")!]);

            Assert.Contains("PIXELART", contract.Criteria[0]);
            Assert.Contains(contract.Criteria, c => c.Contains("SPELSKAL")); // stående kriterier finns kvar
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
