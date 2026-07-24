using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.29: karaktarsidentiteten. Agarens rapport var "gubben ser annorlunda ut
/// hela tiden" - rotorsaken var att identiteten var hash(promptstrangen), sa
/// varje omformulering (och scaffoldens egna suffix " (3d)") gav en ny figur.
/// Testerna nedan later de driftkallorna en och en.
///
/// OBS: jamfor ALDRIG PNG-bytes. EncodePng gar via deflate vars utdata inte ar
/// kontraktsstabil over .NET-versioner - allt identitetsarbete jamfor
/// avkodade RGBA-buffertar.
/// </summary>
public class CharacterIdentityTests
{
    private static ArtBible Bible(string genre = "platformer", string identity = "ett spel") =>
        ArtBibleStore.Derive(genre, identity);

    private static uint Seed(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return h;
    }

    [Fact]
    public void SammaNamn_GerPixelidentiskFigur()
    {
        var b = Bible();
        var a1 = CharacterSpecFactory.Derive("player", "", "player", b, 12345);
        var a2 = CharacterSpecFactory.Derive("player", "", "player", b, 12345);
        Assert.Equal(a1.Palette.ShirtRamp, a2.Palette.ShirtRamp);
        Assert.Equal(a1.Traits, a2.Traits);
        for (var i = 0; i < PoseLib.Standard.Length; i++)
            Assert.Equal(
                CharacterRenderer.Draw(a1, PoseLib.Standard[i], 24),
                CharacterRenderer.Draw(a2, PoseLib.Standard[i], 24));
    }

    [Fact]
    public void OlikaNamn_GerOlikaFigurer()
    {
        var b = Bible();
        var p = CharacterSpecFactory.Derive("player", "", "player", b, 999);
        var e = CharacterSpecFactory.Derive("enemy", "", "enemy", b, 999);
        // Skiljer sig i BADE farg och siluett - fienden ska inte vara en
        // omfargad klon (det var precis vad kiten hade fore v2.29).
        Assert.NotEqual(p.Palette.ShirtRamp[1], e.Palette.ShirtRamp[1]);
        Assert.NotEqual(
            CharacterRenderer.Draw(p, PoseLib.Standard[0], 24),
            CharacterRenderer.Draw(e, PoseLib.Standard[0], 24));
    }

    [Fact]
    public void FiendenHarEgnaDrag_OchRodaOgonUtanGlans()
    {
        var b = Bible();
        var e = CharacterSpecFactory.Derive("enemy", "", "enemy", b, 4242);
        Assert.False(e.Palette.EyeGlint);
        Assert.NotEqual("18161F", e.Palette.Eye);
        Assert.Contains(e.Traits.Mark, new[] { "horns", "ears" });
    }

    [Fact]
    public void AllaKaraktarer_DelarKonturOchHudramp_UrBibeln()
    {
        var b = Bible("party", "partyspel");
        var slugs = new[] { "player", "bubble", "berry", "lime" };
        var specs = slugs.Select(s => CharacterSpecFactory.Derive(s, "", "player", b, 77)).ToList();
        // Stilkoherens: samma kontur och samma hudramp ur bibeln.
        Assert.All(specs, s => Assert.Equal(b.OutlineHex, s.Palette.Outline));
        Assert.All(specs, s => Assert.Equal(b.SkinRampHex, s.Palette.SkinRamp));
    }

    [Fact]
    public void Rollistan_LaserTillbakaOforandrat_AvenMedNyBeskrivning()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-cast-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\n");
            var b = Bible();
            var (first, created1) = CharacterCast.Resolve(dir, "player", "Hjalten", "player", b, 5);
            Assert.True(created1);
            // Andrad beskrivning OCH annat fro far INTE ge en ny figur -
            // det ar hela laset mot promptdrift.
            var (second, created2) = CharacterCast.Resolve(dir, "player", "En helt annan gubbe", "player", b, 999999);
            Assert.False(created2);
            Assert.Equal(first.Palette.ShirtRamp, second.Palette.ShirtRamp);
            Assert.Equal(first.Traits, second.Traits);
            Assert.Equal(
                CharacterRenderer.Draw(first, PoseLib.Standard[0], 24),
                CharacterRenderer.Draw(second, PoseLib.Standard[0], 24));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void TrasigRollistefil_KraschadeInte_UtanHarleds()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-cast-").FullName;
        try
        {
            Directory.CreateDirectory(CharacterCast.DirFor(dir));
            File.WriteAllText(CharacterCast.PathFor(dir, "player"), "{ trasig json");
            var (spec, created) = CharacterCast.Resolve(dir, "player", null, "player", Bible(), 3);
            Assert.True(created);
            Assert.Equal("player", spec.Slug);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void PoseLib_RaknarStartFrame_SaIndexenAldrigGarIsar()
    {
        var anims = PoseLib.AnimsFor(PoseLib.Standard);
        var idle = anims.Single(a => a.Name == "idle");
        var walk = anims.Single(a => a.Name == "walk");
        // Kontraktet kiten redan bygger pa: idle 0-1, walk 2-5.
        Assert.Equal(0, idle.StartFrame);
        Assert.Equal(2, idle.FrameCount);
        Assert.Equal(2, walk.StartFrame);
        Assert.Equal(4, walk.FrameCount);
    }

    [Fact]
    public void FotradenArIdentisk_IAllaPoser_SaFigurenInteSvavar()
    {
        var spec = CharacterSpecFactory.Derive("player", "", "player", Bible(), 8);
        int LowestOpaqueRow(byte[] rgba, int frame)
        {
            for (var y = frame - 1; y >= 0; y--)
                for (var x = 0; x < frame; x++)
                    if (rgba[(y * frame + x) * 4 + 3] > 0) return y;
            return -1;
        }
        var rows = PoseLib.Standard
            .Select(p => LowestOpaqueRow(CharacterRenderer.Draw(spec, p, 24), 24))
            .Distinct().ToList();
        Assert.Single(rows);
        Assert.True(rows[0] > 0);
    }

    [Fact]
    public void VarjeDragkombination_KlipperInteMotRamkanten()
    {
        var b = Bible();
        string[] bodies = ["slim", "normal", "broad"];
        string[] hairs = ["bald", "short", "long", "spiky", "ponytail"];
        string[] faces = ["plain", "beard", "visor"];
        string[] marks = ["none", "horns", "ears"];
        foreach (var body in bodies)
            foreach (var hair in hairs)
                foreach (var face in faces)
                    foreach (var mark in marks)
                    {
                        var spec = CharacterSpecFactory.Derive("x", "", "player", b, 1) with
                        {
                            Traits = new CharacterTraits(body, hair, face, mark)
                        };
                        foreach (var pose in PoseLib.Standard)
                        {
                            var px = CharacterRenderer.Draw(spec, pose, 24);
                            // Vanster/hoger ytterkolumn ska vara tom - annars
                            // ar figuren avklippt av ramen.
                            for (var y = 0; y < 24; y++)
                            {
                                Assert.Equal(0, px[(y * 24 + 0) * 4 + 3]);
                                Assert.Equal(0, px[(y * 24 + 23) * 4 + 3]);
                            }
                        }
                    }
    }

    [Fact]
    public void VisualStyleLib_ArNuDeterministisk_AvenForOkandGenre()
    {
        // Fallbacken anvande Random.Shared -> art-bibeln vilade pa ett
        // rorligt fundament for varje genre som inte listas explicit.
        var a = VisualStyleLib.PickForGenre("snake");
        var b = VisualStyleLib.PickForGenre("snake");
        var c = VisualStyleLib.PickForGenre("snake");
        Assert.Equal(a.Name, b.Name);
        Assert.Equal(b.Name, c.Name);
    }

    [Fact]
    public void TvaScaffolds_AvSammaUppdrag_GerPixelidentiskGubbe()
    {
        // REGRESSIONSTESTET for hela buggrapporten. Scaffolden fick fore
        // v2.29 sin identitet ur promptstrangen, och WorkerRole lagger pa ett
        // stil-suffix i parentes pa kit-prompten - alltsa ny gubbe for samma
        // uppdrag. Nu normaliseras suffixet bort och identiteten lagras.
        var a = Directory.CreateTempSubdirectory("ailocal-ida-").FullName;
        var b = Directory.CreateTempSubdirectory("ailocal-idb-").FullName;
        try
        {
            var svc = new GameScaffoldService();
            svc.Scaffold("auto", "bygg ett 2d plattformsspel i godot", a);
            svc.Scaffold("auto", "bygg ett 2d plattformsspel i godot (pixelart)", b);

            foreach (var f in new[] { "player.png", "enemy.png" })
            {
                var pa = PixelArtPipeline.DecodePng(File.ReadAllBytes(Path.Combine(a, f)));
                var pb = PixelArtPipeline.DecodePng(File.ReadAllBytes(Path.Combine(b, f)));
                Assert.NotNull(pa);
                Assert.NotNull(pb);
                // RGBA-buffertar, aldrig PNG-bytes: deflate ar inte
                // kontraktsstabil over .NET-versioner.
                Assert.Equal(pa!.Value.Rgba, pb!.Value.Rgba);
            }
        }
        finally
        {
            try { Directory.Delete(a, true); } catch { }
            try { Directory.Delete(b, true); } catch { }
        }
    }

    [Fact]
    public void Scaffolden_SkriverRollistaOchBibel_SomLasesTillbaka()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-scaf-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 2d plattformsspel i godot", dir);
            Assert.True(File.Exists(ArtBibleStore.PathFor(dir)), "artbible.json saknas");
            Assert.True(File.Exists(CharacterCast.PathFor(dir, "player")), "player.json saknas");
            Assert.True(File.Exists(CharacterCast.PathFor(dir, "enemy")), "enemy.json saknas");
            // Speglingen till DESIGN.md ar det som later AssetStyle (som redan
            // har lasaren) ge molnbilder samma palett som de procedurella.
            var design = File.ReadAllText(Path.Combine(dir, "DESIGN.md"));
            Assert.Contains("## Art direction", design);
            Assert.Contains("## Palett", design);
            Assert.Equal(2, CharacterCast.All(dir).Count);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public async Task GenerateAsset_MedCharacterId_AteranvanderFiguren()
    {
        // Agentsidan: tva anrop for samma id, med HELT olika beskrivning, ska
        // ge samma gubbe. Fore v2.29 malade varje anrop en ny figur.
        var dir = Directory.CreateTempSubdirectory("ailocal-char-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"T\"\n");
            var gen = new AssetGenerator();
            var target = Path.Combine(dir, "char_hero.png");

            var r1 = await gen.GenerateAsync("character:hero", "a brave knight in blue",
                null, null, target, CancellationToken.None);
            Assert.True(r1.Success, r1.Output);
            var first = File.ReadAllBytes(Path.Combine(dir, "char_hero.png"));

            var r2 = await gen.GenerateAsync("character:hero", "an old wizard with a red robe",
                null, null, target, CancellationToken.None);
            Assert.True(r2.Success, r2.Output);
            Assert.Contains("redan", r2.Output);

            var second = File.ReadAllBytes(Path.Combine(dir, "char_hero.png"));
            var a = PixelArtPipeline.DecodePng(first);
            var b = PixelArtPipeline.DecodePng(second);
            Assert.NotNull(a);
            Assert.NotNull(b);
            Assert.Equal(a!.Value.Rgba, b!.Value.Rgba);
            Assert.True(File.Exists(Path.Combine(dir, "char_hero_frames.tres")));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ArtBibeln_SkrivsEnGang_OchAndrasAldrig()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-bible-").FullName;
        try
        {
            var first = ArtBibleStore.LoadOrCreate(dir, "platformer", "uppdrag A");
            // Helt annan genre och identitet - befintlig bibel ska vinna.
            var second = ArtBibleStore.LoadOrCreate(dir, "party", "uppdrag B");
            Assert.Equal(first.OutlineHex, second.OutlineHex);
            Assert.Equal(first.AccentRampHex, second.AccentRampHex);
            Assert.Equal(first.StyleName, second.StyleName);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
