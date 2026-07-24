using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.31: SPELBARHETSKONTRAKTET. De tio kit som lades till i v2.28 laddade,
/// renderade och skarmdumpades korrekt - men gick inte att spela klart:
/// _game_over satte aldrig state, sa spelloopens vakt passerade, dodsvillkoret
/// triggade om och UI:t revs/byggdes 60 ggr/sek. "Play Again" gick inte att
/// klicka. Buggen shippade i tre releaser.
///
/// LARDOMEN: att ett kit laddar och renderar bevisar INGENTING om det gar att
/// spela. Testerna nedan later de kontrakt som skiljer ett spel fran en demo.
/// </summary>
public class KitPlayabilityTests
{
    /// <summary>Alla 21 kit - de nya var aldrig med i AllaKit-testet.</summary>
    public static TheoryData<string> AllaKitPrompts() =>
    [
        "bygg ett fotbolls management spel i godot",
        "top-down äventyr i godot där man överlever vågor",
        "bygg ett racingspel i godot med bilar och tre varv",
        "bygg ett 3d samlarspel i godot",
        "bygg ett 2d plattformsspel i godot",
        "bygg ett artillerispel som shellshock live i godot",
        "bygg ett pusselspel som 2048 i godot",
        "bygg ett mario party liknande spel i godot med minigames",
        "bygg ett 3d mario party spel i godot med minigames",
        "bygg ett litet fps spel i godot",
        "bygg ett isometriskt aventyr i godot",
        "bygg ett tower defense spel i godot",
        "bygg ett snake spel i godot",
        "bygg ett breakout spel i godot",
        "bygg ett quiz fragesport spel i godot",
        "bygg ett memory kortspel i godot",
        "bygg ett minesweeper minroj spel i godot",
        "bygg ett idle clicker spel i godot",
        "bygg ett tetris block spel i godot",
        "bygg ett roguelike dungeon spel i godot",
        "bygg ett rpg aventyr i godot",
    ];

    [Theory]
    [MemberData(nameof(AllaKitPrompts))]
    public void GameOver_LamnarSpelState_SaSkarmenGarAttKlicka(string prompt)
    {
        var (root, _) = Scaffold(prompt);
        try
        {
            var main = File.ReadAllText(Path.Combine(root, "Main.gd"));
            // Kitet maste ha ett slutlage OCH lamna "playing" nar det nas.
            // Utan detta river _game_over UI:t varje bildruta.
            var harSlut = main.Contains("func _game_over") || main.Contains("func _finish")
                || main.Contains("func _show_results");
            if (!harSlut) return;   // vissa kit (idle) har inget slutlage - ok

            foreach (var fn in new[] { "func _game_over", "func _finish", "func _show_results" })
            {
                var i = main.IndexOf(fn, StringComparison.Ordinal);
                if (i < 0) continue;
                var body = KroppEfter(main, i);
                Assert.True(
                    body.Contains("state = ") || body.Contains("state="),
                    $"{prompt}: {fn} lamnar aldrig spel-state - UI:t byggs om varje bildruta " +
                    "och knapparna gar inte att klicka.");
            }
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [MemberData(nameof(AllaKitPrompts))]
    public void DeklareratSparformat_AnvandsFaktiskt(string prompt)
    {
        var (root, _) = Scaffold(prompt);
        try
        {
            var main = File.ReadAllText(Path.Combine(root, "Main.gd"));
            if (!main.Contains("SAVE_PATH")) return;
            // Att deklarera en sokvag och aldrig skriva till den ar samma sak
            // som att inte spara alls - "Best" nollstalldes vid varje start.
            Assert.True(main.Contains("FileAccess.open(SAVE_PATH"),
                $"{prompt}: SAVE_PATH deklareras men skrivs aldrig - inget sparas mellan korningar.");
        }
        finally { Cleanup(root); }
    }

    [Theory]
    [MemberData(nameof(AllaKitPrompts))]
    public void HudSomSkapas_MasteOcksaUppdateras(string prompt)
    {
        var (root, _) = Scaffold(prompt);
        try
        {
            var main = File.ReadAllText(Path.Combine(root, "Main.gd"));
            if (!main.Contains("\"Hud\"")) return;
            // Fem kit skapade en HUD-etikett med statisk text och rorde den
            // aldrig igen - spelaren sag "Score: 0" genom hela spelet.
            Assert.True(
                main.Contains("_update_hud") || main.Contains("get_node_or_null(\"Hud\")")
                || main.Contains("hud_label"),
                $"{prompt}: HUD skapas men uppdateras aldrig - siffrorna star stilla.");
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void TowerSiege_GarAttForlora()
    {
        var (root, _) = Scaffold("bygg ett tower defense spel i godot");
        try
        {
            var main = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("lives -= 1", main);
            // lives raknades ner men lastes ALDRIG - man kunde na -1000 liv
            // och spela vidare. Ett tower defense utan forlust ar inget spel.
            Assert.Contains("if lives <= 0:", main);
        }
        finally { Cleanup(root); }
    }

    /// <summary>Kroppen efter en funktionsrubrik (till nasta 'func ' i vanster
    /// marginal).</summary>
    private static string KroppEfter(string src, int start)
    {
        var nasta = src.IndexOf("\nfunc ", start + 1, StringComparison.Ordinal);
        return nasta < 0 ? src[start..] : src[start..nasta];
    }

    private static (string Root, string[] Files) Scaffold(string prompt)
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-play-").FullName;
        var r = new GameScaffoldService().Scaffold("auto", prompt, dir);
        var root = ProjectRootDetector.Detect(dir) ?? dir;
        _parents[root] = dir;
        return (root, r.Files);
    }

    private static readonly Dictionary<string, string> _parents = [];

    private static void Cleanup(string root)
    {
        var target = _parents.TryGetValue(root, out var p) ? p : root;
        try { Directory.Delete(target, recursive: true); } catch { }
    }
}
