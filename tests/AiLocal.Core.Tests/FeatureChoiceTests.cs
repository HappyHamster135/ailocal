using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.30: funktionsvalen. Poangen ar att game_module-bibliotekets atta
/// fardiga system (combat/dialog/enemyai/inventory/particles/quest/save/xp)
/// fanns men bara nåddes om agenten SJALV rakade valja dem - ett erbjudande,
/// inte ett krav. Nu blir ett kryss bade en modulhamtning och en HARD
/// kontraktspunkt som kvalitetsgrinden foljer upp.
/// </summary>
public class FeatureChoiceTests
{
    [Fact]
    public void Funktionstaggen_ParsasOchTasBortUrTexten()
    {
        var c = BuildDirectives.Parse("[FUNKTIONER: shop,achievements,inventory]\nbygg ett rpg");
        Assert.Equal(3, c.FeatureList.Count);
        Assert.Contains("shop", c.FeatureList);
        Assert.Contains("achievements", c.FeatureList);
        Assert.Contains("inventory", c.FeatureList);
        // Taggen ska INTE lacka in i uppdragstexten agenten laser.
        Assert.Equal("bygg ett rpg", c.CleanAssignment);
        Assert.DoesNotContain("FUNKTIONER", c.CleanAssignment);
    }

    [Fact]
    public void AllaTreTaggar_SamtidigtUtanAttStoraVarandra()
    {
        var c = BuildDirectives.Parse(
            "[STIL: pixelart]\n[OMFANG: stort]\n[FUNKTIONER: quest,dialog]\n[FORHANDSFRAGOR]\nbygg ett aventyr");
        Assert.Equal("pixelart", c.Style);
        Assert.Equal("stort", c.Scope);
        Assert.True(c.AskFirst);
        Assert.Equal(2, c.FeatureList.Count);
        Assert.Equal("bygg ett aventyr", c.CleanAssignment);
    }

    [Fact]
    public void OkandFunktion_IgnorerasTyst()
    {
        var c = BuildDirectives.Parse("[FUNKTIONER: shop,rymdhissar,quest]\nbygg nagot");
        Assert.Equal(2, c.FeatureList.Count);
        Assert.DoesNotContain("rymdhissar", c.FeatureList);
    }

    [Fact]
    public void UtanTagg_GerTomLista_InteNull()
    {
        var c = BuildDirectives.Parse("bygg ett spel");
        Assert.Empty(c.FeatureList);
        Assert.Empty(BuildDirectives.FeatureCriteria(c.FeatureList));
        Assert.Null(BuildDirectives.ModuleHint(c.FeatureList));
    }

    [Fact]
    public void VarjeFunktion_HarEnKonkretKontraktspunkt()
    {
        foreach (var (key, feat) in BuildDirectives.Catalog)
        {
            Assert.Equal(key, feat.Key);
            Assert.False(string.IsNullOrWhiteSpace(feat.Label));
            // Kriteriet maste vara MATBART - grinden foljer upp text, sa
            // "hart krav" och en konkret siffra/beteende ska finnas.
            Assert.Contains("hårt krav", feat.Criterion);
            Assert.True(feat.Criterion.Length > 80, $"{key}: kriteriet ar for vagt");
        }
    }

    [Fact]
    public void ModulHinten_PekarUtDeFardigaSystemen()
    {
        var hint = BuildDirectives.ModuleHint(["inventory", "quest", "shop"]);
        Assert.NotNull(hint);
        Assert.Contains("game_module", hint!);
        Assert.Contains("'inventory'", hint);
        Assert.Contains("'quest'", hint);
        // shop har ingen fardig modul - far inte hittas pa.
        Assert.DoesNotContain("'shop'", hint);
    }

    [Fact]
    public void FunktionerUtanModul_GerAndaKontraktspunkt()
    {
        // shop/achievements/hotseat/tutorial saknar modul men ar precis de
        // som skiljer ett indiespel fran en prototyp - de MASTE bli krav.
        var crit = BuildDirectives.FeatureCriteria(["shop", "achievements", "hotseat", "tutorial"]);
        Assert.Equal(4, crit.Count);
        Assert.Contains(crit, c => c.Contains("BUTIK"));
        Assert.Contains(crit, c => c.Contains("PRESTATIONER"));
        Assert.Contains(crit, c => c.Contains("LOKAL FLERSPELARE"));
        Assert.Contains(crit, c => c.Contains("INLÄRNING"));
    }

    [Fact]
    public void ModulerSomPekasUt_FinnsFaktiskt_IBiblioteket()
    {
        // Falskt-gront-vakt: en hint som pekar pa en modul som inte finns
        // skickar agenten pa ett omojligt arende.
        var lib = File.ReadAllText(Path.Combine(
            RepoRoot(), "src", "AiLocal.Node", "Hosting", "GameModules", "GameModuleLibrary.cs"));
        foreach (var f in BuildDirectives.Catalog.Values.Where(f => f.Module is not null))
            Assert.Contains("\"" + f.Module + "\"", lib);
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
