using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.29 etapp 1-2: husets aterbrukbara 3D-karaktar. Riggen byggs ur SAMMA
/// CharacterSpec som ritar 2D-pixelgubben, sa figuren kanns igen i bada.
/// Testerna later proportionerna, determinismen och kit-inkopplingen.
/// Att riggen SER BRA UT kan inget C#-test svara pa - det verifieras med
/// riktig motorkorning och skarmdump.
/// </summary>
public class Rig3DTests
{
    private static readonly string[] Bodies = ["slim", "normal", "broad"];
    private static readonly string[] Hairs = ["bald", "short", "long", "spiky", "ponytail"];
    private static readonly string[] Faces = ["plain", "beard", "visor"];
    private static readonly string[] Marks = ["none", "horns", "ears"];

    [Fact]
    public void Segmenten_SummerarTillHojden_SaFigurenInteSvavar()
    {
        foreach (var body in Bodies)
        {
            var m = RigMetricsFactory.For(new CharacterTraits(body, "short", "plain", "none"));
            // Hoften sitter dar benen slutar; axeln dar balen slutar; huvudet
            // ovanpa. Summan MASTE bli totalhojden, annars star figuren i
            // marken eller svavar.
            Assert.Equal(m.UpperLeg + m.LowerLeg, m.HipY, 6);
            Assert.Equal(m.HipY + m.TorsoH, m.ShoulderY, 6);
            Assert.Equal(m.HeightM, m.ShoulderY + m.HeadH, 6);
        }
    }

    [Fact]
    public void AllaDragkombinationer_GerGiltigaMatt()
    {
        foreach (var body in Bodies)
            foreach (var hair in Hairs)
                foreach (var face in Faces)
                    foreach (var mark in Marks)
                    {
                        var m = RigMetricsFactory.For(new CharacterTraits(body, hair, face, mark));
                        Assert.True(m.UpperLeg > 0 && m.LowerLeg > 0, $"ben <= 0 for {body}");
                        Assert.True(m.TorsoH > 0 && m.HeadH > 0);
                        Assert.True(m.LimbW > 0 && m.BodyW > 0 && m.HeadW > 0);
                        Assert.True(m.EyeY > m.ChestY && m.ChestY > 0);
                        Assert.True(m.CapsuleRadius > 0 && m.CapsuleHeight > 0);
                    }
    }

    [Fact]
    public void Kroppstypen_ArDenSammaKallanSom2D()
    {
        // Samma trait som gor 2D-gubben smal ska gora 3D-riggen smal - annars
        // ar det inte samma figur, bara samma namn.
        var slim = RigMetricsFactory.For(new CharacterTraits("slim", "short", "plain", "none"));
        var normal = RigMetricsFactory.For(new CharacterTraits("normal", "short", "plain", "none"));
        var broad = RigMetricsFactory.For(new CharacterTraits("broad", "short", "plain", "none"));
        Assert.True(slim.BodyW < normal.BodyW);
        Assert.True(normal.BodyW < broad.BodyW);
        Assert.True(slim.LimbW < broad.LimbW);
        // Hojden ar densamma - kroppstyp andrar bredd, inte langd.
        Assert.Equal(slim.HeightM, broad.HeightM, 6);
    }

    [Fact]
    public void Matten_ArDeterministiska()
    {
        var t = new CharacterTraits("broad", "spiky", "beard", "horns");
        var a = RigMetricsFactory.For(t);
        var b = RigMetricsFactory.For(t);
        Assert.Equal(a, b);
        Assert.Equal(RigMetricsFactory.ToGd(a), RigMetricsFactory.ToGd(b));
        // Invariant kultur: en svensk nod far inte skriva "1,70" i GDScript.
        Assert.DoesNotContain(",", RigMetricsFactory.ToGd(a).Replace(", ", ""));
    }

    [Fact]
    public void Cast3DSkriptet_ArDeterministiskt_OchHarPlayer()
    {
        var bible = ArtBibleStore.Derive("platformer", "ett spel");
        var specs = new[]
        {
            CharacterSpecFactory.Derive("player", "", "player", bible, 7),
            CharacterSpecFactory.Derive("enemy", "", "enemy", bible, 7),
        };
        var a = Cast3DScript.Build(specs, bible);
        var b = Cast3DScript.Build(specs, bible);
        Assert.Equal(a, b);
        Assert.Contains("class_name Cast3D", a);
        Assert.Contains("\"player\"", a);
        Assert.Contains("\"enemy\"", a);
        Assert.Contains("Color8(", a);
        Assert.Contains("\"metrics\"", a);
        Assert.Contains("static func spec(", a);
        // Rollen maste folja med - riggen behover veta vem som ar fiende.
        Assert.Contains("\"role\": \"enemy\"", a);
    }

    [Fact]
    public void Scaffolden_SkriverRig3DOchCast3D_TillGodotProjekt()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-rig-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 3d samlarspel i godot", dir);
            Assert.True(File.Exists(Path.Combine(dir, "Rig3D.gd")), "Rig3D.gd saknas");
            Assert.True(File.Exists(Path.Combine(dir, "Cast3D.gd")), "Cast3D.gd saknas");
            var rig = File.ReadAllText(Path.Combine(dir, "Rig3D.gd"));
            // API:t kiten och agenterna bygger pa.
            foreach (var api in new[] { "static func actor(", "func play(", "func set_speed(",
                "func face_dir(", "func socket(", "func self_check(", "func height(" })
                Assert.Contains(api, rig);
            // Alla klipp finns.
            foreach (var clip in new[] { "idle", "walk", "run", "jump", "fall", "attack", "hit", "cheer", "down" })
                Assert.Contains("\"" + clip + "\"", rig);
            // Fastpunkter att hanga vapen/effekter pa.
            foreach (var s in new[] { "hand_l", "hand_r", "head_top", "aim" })
                Assert.Contains(s, rig);
            // get_aabb far INTE anvandas: i headless spyr dummy-renderaren
            // "Parameter m is null" och forgiftar husets egen felkanal.
            Assert.DoesNotContain("get_aabb", rig);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StrikeArena_HarRiggadeFiender_IStalletForKapslar()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-fps-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett litet fps spel i godot", dir);
            var main = File.ReadAllText(Path.Combine(dir, "Main.gd"));
            Assert.Contains("Rig3D.actor(Cast3D.spec(\"enemy\"))", main);
            Assert.DoesNotContain("cm.height = 1.8", main);   // den gamla kapseln
            // Fienden vander sig mot spelaren och reagerar pa traff.
            Assert.Contains("face_dir(", main);
            Assert.Contains(".play(\"hit\")", main);
            // Sikthojden kommer ur figurens matt, inte en literal.
            Assert.Contains("chest_height()", main);
            // Loopvariabeln MASTE vara otypad - en typad deklaration fran en
            // Rig3D avbryter _physics_process varje bildruta (buggen som
            // sankte 3D-partyts minispel fran v2.24).
            Assert.DoesNotContain("var node: MeshInstance3D = en[\"node\"]", main);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Party3D_KanValjaRigg_MenBehallerSpriten()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-p3d-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 3d mario party spel i godot med minigames", dir);
            var main = File.ReadAllText(Path.Combine(dir, "Main.gd"));
            // Kapaciteten finns...
            Assert.Contains("const REPRESENTATION", main);
            Assert.Contains("Rig3D.actor(Cast3D.spec(\"player\"), col)", main);
            // ...men spriten ar default: vid kitets kamera ar figuren ~35 px
            // och designad pixelart slar en blockfigur i den storleken.
            // Beslutet ar taget pa en sida-vid-sida-bild, inte pa en gissning.
            Assert.Contains("const REPRESENTATION := \"sprite\"", main);
            // Fotankare sa spriten star PA brickan i stallet for att sväva.
            Assert.Contains("s.offset = Vector2(0, 12)", main);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Kuben_AnvanderRiggen_IStalletForEnBoxSpelare()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-cube-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 3d samlarspel i godot", dir);
            var main = File.ReadAllText(Path.Combine(dir, "Main.gd"));
            Assert.Contains("const Rig3D = preload(\"res://Rig3D.gd\")", main);
            Assert.Contains("const Cast3D = preload(\"res://Cast3D.gd\")", main);
            Assert.Contains("Rig3D.actor(", main);
            // Spelaren far inte langre vara en naken kub.
            Assert.DoesNotContain("pbox.size = Vector3(1, 1, 1)", main);
            // Riggen ska animeras av rorelsen, inte sta stilla.
            Assert.Contains("rig.play(", main);
            Assert.Contains("face_dir(", main);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
