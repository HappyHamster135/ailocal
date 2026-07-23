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
    public void ForeEfterVakten_KomponerarSidaVidSida_OchTalSaknadeBilder()
    {
        // v2.13: vakten far ALDRIG kasta - saknade bilder ger null (fail-open),
        // tva riktiga PNG:er ger en komposit pa disk.
        var dir = Directory.CreateTempSubdirectory("ailocal-ba-").FullName;
        try
        {
            Assert.Null(PolishPass.ComposeBeforeAfter(
                Path.Combine(dir, "saknas-a.png"), Path.Combine(dir, "saknas-b.png"),
                Path.Combine(dir, "ut.png")));

            var rgba = new byte[6 * 6 * 4];
            for (var i = 0; i < rgba.Length; i += 4) { rgba[i] = 180; rgba[i + 3] = 255; }
            var a = Path.Combine(dir, "a.png");
            var b = Path.Combine(dir, "b.png");
            File.WriteAllBytes(a, AssetGenerator.EncodePng(6, 6, rgba));
            File.WriteAllBytes(b, AssetGenerator.EncodePng(6, 6, rgba));
            var outPath = PolishPass.ComposeBeforeAfter(a, b, Path.Combine(dir, "ut.png"));
            Assert.NotNull(outPath);
            Assert.True(File.Exists(outPath));
            Assert.True(new FileInfo(outPath!).Length > 0);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task ForeEfterVakten_SamreFallerUt_OkSlapperIgenom()
    {
        // Bara ett svar som BORJAR med SAMRE far falla rundan; allt annat
        // (OK, tvekan, fel) slapper igenom - vakten far aldrig kassera bra
        // arbete pa en gissning.
        var dir = Directory.CreateTempSubdirectory("ailocal-bav-").FullName;
        try
        {
            var rgba = new byte[4 * 4 * 4];
            for (var i = 0; i < rgba.Length; i += 4) { rgba[i + 1] = 200; rgba[i + 3] = 255; }
            var a = Path.Combine(dir, "ref.png");
            var b = Path.Combine(dir, "nu.png");
            File.WriteAllBytes(a, AssetGenerator.EncodePng(4, 4, rgba));
            File.WriteAllBytes(b, AssetGenerator.EncodePng(4, 4, rgba));

            Task<(bool, string)> Verdict(string verdict) => Task.FromResult((true, verdict));

            Assert.True(await PolishPass.LooksWorseAsync(a, b, dir,
                (_, _, _) => Verdict("SAMRE - hoger sida ar tommare"), CancellationToken.None));
            Assert.False(await PolishPass.LooksWorseAsync(a, b, dir,
                (_, _, _) => Verdict("OK - ny bana men samma niva"), CancellationToken.None));
            // Saknad referens = ingen bedomning = inte samre.
            Assert.False(await PolishPass.LooksWorseAsync(
                Path.Combine(dir, "finns-ej.png"), b, dir,
                (_, _, _) => Verdict("SAMRE"), CancellationToken.None));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task CritiqueAsync_BildbevisenNarKritiken()
    {
        // v2.4: art director-passet - sondens skarmdumpar gar genom visionen
        // och OMDOMET ska sta i kritikerprompten (inte bara kod/textbevis).
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-polish-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.gd"), "func _ready():\n\tpass");
            var shot = Path.Combine(dir, "playtest.png");
            File.WriteAllBytes(shot, [1, 2, 3]);

            string? seenPrompt = null;
            Func<AiLocal.Core.Contracts.ChatRequest, CancellationToken, Task<AiLocal.Core.Providers.ProviderResponse>> complete = (req, _) =>
            {
                seenPrompt = req.Messages[^1].Content;
                return Task.FromResult(AiLocal.Core.Providers.ProviderResponse.Ok(new AiLocal.Core.Contracts.ChatResponse
                {
                    Content = "{\"done\": true, \"improvements\": []}",
                    Model = "m",
                    Provider = "test"
                }));
            };
            Func<string, string, CancellationToken, Task<(bool, string)>> vision = (_, _, _) =>
                Task.FromResult((true, "- the right half of the screen is completely empty"));

            await PolishPass.CritiqueAsync(dir, "bygg ett spel", "BUILD OK", complete,
                CancellationToken.None, null, vision, [shot]);

            Assert.NotNull(seenPrompt);
            Assert.Contains("ART DIRECTOR review", seenPrompt);
            Assert.Contains("completely empty", seenPrompt);
            // v2.10: genrens kvalitetsribba ska nå kritikern.
            Assert.Contains("QUALITY BAR", seenPrompt);
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
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
