using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.30: fysikgolvet. Matningen over alla 21 kit gav NOLL forekomster av
/// RigidBody, Area och PhysicsMaterial - all rorelse var kinematisk och varje
/// traff en manuell avstandsberakning. Phys.gd ger de byggstenarna, och
/// Kuben demonstrerar dem med knuffbara lador.
///
/// Att fysiken FAKTISKT simulerar kan inget C#-test svara pa - det ar
/// verifierat med en sond i riktig Godot (6/6 lador flyttade av en kraftpuls).
/// </summary>
public class PhysicsFloorTests
{
    [Fact]
    public void FysikbiblioteketSkrivs_TillAllaGodotProjekt()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-phys-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 2d plattformsspel i godot", dir);
            var p = Path.Combine(dir, "Phys.gd");
            Assert.True(File.Exists(p), "Phys.gd saknas");
            var src = File.ReadAllText(p);
            // De tre saker som helt saknades i kiten fore v2.30.
            Assert.Contains("RigidBody3D", src);
            Assert.Contains("RigidBody2D", src);
            Assert.Contains("Area3D", src);
            Assert.Contains("Area2D", src);
            Assert.Contains("PhysicsMaterial", src);
            // API:t.
            foreach (var api in new[] { "static func prop3d(", "static func ball3d(", "static func prop2d(",
                "static func trigger3d(", "static func trigger2d(", "static func bouncy(",
                "static func blast3d(", "static func blast2d(", "static func push_bodies(" })
                Assert.Contains(api, src);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Kuben_HarKnuffbaraLador_OchEnFallvakt()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-cube3-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 3d samlarspel i godot", dir);
            var main = File.ReadAllText(Path.Combine(dir, "Main.gd"));
            Assert.Contains("const Phys = preload(\"res://Phys.gd\")", main);
            Assert.Contains("Phys.prop3d(", main);
            // En CharacterBody3D knuffar inte rigidbodies av sig sjalv.
            Assert.Contains("Phys.push_bodies(", main);
            // FALLVAKT: kitet hade INGEN hantering alls - gick man over
            // arenakanten foll man for evigt i tomma intet.
            Assert.Contains("player.position.y < -8.0", main);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Nav3D_SkrivsOchByggerPaServerfragan()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-nav-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett litet fps spel i godot", dir);
            var p = Path.Combine(dir, "Nav3D.gd");
            Assert.True(File.Exists(p), "Nav3D.gd saknas");
            var src = File.ReadAllText(p);
            Assert.Contains("NavigationRegion3D", src);
            Assert.Contains("bake_from_source_geometry_data", src);
            // MEDVETET server-fragan och INTE NavigationAgent3D: agenten
            // begar sin vag asynkront och gav tom vag i skarp sond, medan
            // map_get_path returnerade en korrekt vag runt hindret direkt.
            Assert.Contains("NavigationServer3D.map_get_path", src);
            Assert.DoesNotContain("NavigationAgent3D", src);
            foreach (var api in new[] { "static func bake(", "static func route(",
                "static func advance(", "static func dir_along(", "static func follow(" })
                Assert.Contains(api, src);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void StrikeArena_BakarNavmesh_OchFoljerVag()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-nav2-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett litet fps spel i godot", dir);
            var main = File.ReadAllText(Path.Combine(dir, "Main.gd"));
            Assert.Contains("const Nav3D = preload(\"res://Nav3D.gd\")", main);
            Assert.Contains("Nav3D.bake(", main);
            Assert.Contains("Nav3D.route(", main);
            Assert.Contains("Nav3D.follow(", main);
            // Vagen raknas om med jamna mellanrum, inte varje bildruta.
            Assert.Contains("repath", main);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void Phys_AnvanderInteFarligaGDScriptMonster()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-phys2-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", "bygg ett 2d plattformsspel i godot", dir);
            // Bara KOD granskas - anvandningsexemplen i kommentarshuvudet ska
            // sjalvklart namna Phys, de riktar sig till konsumenter som
            // preloadar biblioteket.
            var code = string.Join("\n", File.ReadAllLines(Path.Combine(dir, "Phys.gd"))
                .Where(l => !l.TrimStart().StartsWith('#')));
            // Biblioteket far inte referera sitt eget class_name i KOD - det
            // registreras forst vid importen och faller annars i en korning
            // utan foregaende import (samma falla som Rig3D.gd traffade).
            Assert.DoesNotContain("Phys.new()", code);
            Assert.DoesNotContain("Phys.", code);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
