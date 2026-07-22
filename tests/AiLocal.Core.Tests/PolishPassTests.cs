using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.0.0: utvecklingsrundorna - "grindens gröna leverans är PROTOTYPEN".
/// Låser kritikpromptens fyra axlar (större/snyggare/ljud/stabilare),
/// JSON-tolkningen med radlist-fallback, done-signalen och byggpromptens
/// skyddsregler (bygg ovanpå, riv aldrig, engelska).
/// </summary>
public class PolishPassTests
{
    [Fact]
    public void ParseImprovements_GiltigJson_PlockarPunkterna()
    {
        var reply = "Here is my review:\n{\"done\": false, \"improvements\": [\"Add a second boss arena to level 3\", \"Add a background music loop\"]}\nHope this helps.";
        var result = PolishPass.ParseImprovements(reply);
        Assert.Equal(2, result.Count);
        Assert.Contains(result, s => s.Contains("boss arena"));
    }

    [Fact]
    public void ParseImprovements_DoneTrue_GerTomLista()
    {
        // done=true = kritikern bedömer spelet färdigt -> rundan avstår ärligt.
        var reply = "{\"done\": true, \"improvements\": []}";
        Assert.Empty(PolishPass.ParseImprovements(reply));
    }

    [Fact]
    public void ParseImprovements_CapparVidMax()
    {
        var many = string.Join(",", Enumerable.Range(1, 12).Select(i => $"\"improvement number {i} with enough text\""));
        var result = PolishPass.ParseImprovements("{\"done\": false, \"improvements\": [" + many + "]}");
        Assert.Equal(PolishPass.MaxImprovements, result.Count);
    }

    [Fact]
    public void ParseImprovements_PunktlistaUtanJson_Fallback()
    {
        // Svaga modeller svarar ibland med punktlista trots JSON-instruktionen.
        var reply = "- Add three more levels with new enemy types\n- Replace the flat background with a parallax sky\nShort\n1. Add a victory jingle and per-action sounds";
        var result = PolishPass.ParseImprovements(reply);
        Assert.Equal(3, result.Count);   // "Short" är för kort för att vara en riktig punkt
        Assert.Contains(result, s => s.StartsWith("Add a victory jingle"));
    }

    [Fact]
    public void ParseImprovements_Skrap_GerTomLista()
    {
        Assert.Empty(PolishPass.ParseImprovements(""));
        Assert.Empty(PolishPass.ParseImprovements("ok"));
    }

    [Fact]
    public void CritiquePrompt_BarDeFyraAxlarnaOchBevisen()
    {
        var p = PolishPass.CritiquePrompt("bygg ett artillerispel", "BUILD/VERIFY PASSED + playtest ok", "--- Main.gd ---\nextends Node2D");
        // Ägarens fyra axlar: större, snyggare, bättre ljud, mindre buggigt.
        Assert.Contains("BIGGER", p);
        Assert.Contains("BETTER LOOKING", p);
        Assert.Contains("BETTER SOUND", p);
        Assert.Contains("LESS BUGGY", p);
        // Prototyp-inramningen och bevisen är med.
        Assert.Contains("prototype", p, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bygg ett artillerispel", p);
        Assert.Contains("playtest ok", p);
        Assert.Contains("Main.gd", p);
        // Byggbarhetskravet - aldrig vaga råd.
        Assert.Contains("buildable", p);
    }

    [Fact]
    public void BuildPrompt_BarRundaPunkterOchSkyddsregler()
    {
        var p = PolishPass.BuildPrompt(2, 3, ["Add a boss to wave 5", "Add a background music loop"]);
        Assert.Contains("UTVECKLINGSRUNDA 2/3", p);
        Assert.Contains("1. Add a boss to wave 5", p);
        Assert.Contains("2. Add a background music loop", p);
        // Skyddsreglerna: bygg ovanpå, riv aldrig, klarbart, engelska.
        Assert.Contains("riv aldrig", p);
        Assert.Contains("engelska", p);
        Assert.Contains("klarbart", p);
        Assert.Contains("verify", p);
    }
}
