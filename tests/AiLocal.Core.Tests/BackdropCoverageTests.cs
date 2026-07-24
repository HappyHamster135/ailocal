using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.32: bara 4 av 21 kit hade en bakgrund - resten ritade PLATT GRÅTT, och
/// värre: deras _draw började med "if state != playing: return", så
/// titelskärmen ritade ingenting alls. Det var den tydligaste
/// "browserspel"-signalen i skärmdumpsjämförelsen.
/// </summary>
public class BackdropCoverageTests
{
    public static TheoryData<string> Prompts() =>
    [
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
        "bygg ett 2d plattformsspel i godot",
        "bygg ett mario party liknande spel i godot med minigames",
    ];

    [Theory]
    [MemberData(nameof(Prompts))]
    public void VarjeKit_FarEnBakgrundsbild(string prompt)
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-bg-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", prompt, dir);
            var root = AiLocal.Core.Agent.ProjectRootDetector.Detect(dir) ?? dir;
            var bg = Directory.GetFiles(root, "b*.png").Any(f =>
                Path.GetFileName(f) is "background.png" or "bg_night.png");
            Assert.True(bg, $"{prompt}: ingen bakgrundsbild scaffoldad");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Theory]
    [InlineData("bygg ett snake spel i godot")]
    [InlineData("bygg ett breakout spel i godot")]
    [InlineData("bygg ett memory kortspel i godot")]
    [InlineData("bygg ett roguelike dungeon spel i godot")]
    public void BakgrundenRitas_FORE_StateVakten(string prompt)
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-bg2-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", prompt, dir);
            var root = AiLocal.Core.Agent.ProjectRootDetector.Detect(dir) ?? dir;
            var main = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("_draw_backdrop()", main);
            // Ordningen ar hela poangen: ritas bakgrunden EFTER
            // "if state != playing: return" star titelskarmen kvar tom.
            var draw = main.IndexOf("func _draw() -> void:", StringComparison.Ordinal);
            Assert.True(draw >= 0);
            var call = main.IndexOf("_draw_backdrop()", draw, StringComparison.Ordinal);
            var guard = main.IndexOf("if state != \"playing\":", draw, StringComparison.Ordinal);
            Assert.True(call > 0 && (guard < 0 || call < guard),
                $"{prompt}: bakgrunden ritas efter state-vakten - titelskarmen blir tom");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void GenrenGerOlikaTeman_SaSpelenInteSerLikadanaUt()
    {
        // Utan detta foll varje prompt utan temaord till SAMMA standardang,
        // sa ett halvdussin olika spel fick identisk bakgrund.
        var roguelike = GameScaffoldService.BackdropHint("roguelike", "bygg ett spel");
        var space = GameScaffoldService.BackdropHint("breakout", "bygg ett spel");
        var city = GameScaffoldService.BackdropHint("idle", "bygg ett spel");
        Assert.NotEqual(roguelike, space);
        Assert.NotEqual(space, city);
        Assert.NotEqual(PixelBackdrop.Build(roguelike, 96, 54), PixelBackdrop.Build(space, 96, 54));

        // Anvandarens EGNA temaord maste vinna over genrens standard.
        var egen = GameScaffoldService.BackdropHint("breakout", "ett breakout i en mork grotta");
        Assert.Contains("grotta", egen);
    }
}
