using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// The production bar, enforced: every HTML5 scaffold (all five genres + the
/// platformer) must (1) parse cleanly with the real JS parser - a syntax
/// error means a black screen - and (2) pass the playtest polish checks
/// (sound, animation, game over, highscore) with zero issues, since those
/// same checks are the agent's definition of done. A scaffold that fails its
/// own polish gate would send every build into a pointless fix loop.
/// Also locks that the genre DISPATCH works - before v1.21 every prompt got
/// the platformer regardless of genre keywords.
/// </summary>
public class GameScaffoldProductionTests : IDisposable
{
    private readonly string _dir;

    public GameScaffoldProductionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-scaffold-prod-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    public static IEnumerable<object[]> Genres() =>
    [
        ["ett 2d plattformsspel", "platformer"],
        ["ett rpg aventyr", "rpg"],
        ["ett racing spel med bilar", "racing"],
        ["ett match-3 pussel", "puzzle"],
        ["ett tower defense spel", "towerdefense"],
        ["en top-down shooter", "shooter"],
    ];

    [Theory]
    [MemberData(nameof(Genres))]
    public void EveryGenreScaffold_ParsesAndMeetsTheProductionBar(string prompt, string expectedGenre)
    {
        Assert.Equal(expectedGenre, GameScaffoldService.DetectGenre(prompt));

        var root = Path.Combine(_dir, expectedGenre);
        var result = new GameScaffoldService().Scaffold("html5", prompt, root);
        Assert.True(result.Success, result.Output);

        var html = File.ReadAllText(Path.Combine(root, "index.html"));

        // (1) Compiler-grade: the game's JS must parse.
        var syntaxErrors = JsSyntaxChecker.CheckHtml(html);
        Assert.True(syntaxErrors.Count == 0,
            $"{expectedGenre}: scaffold has JS syntax errors:\n{string.Join("\n", syntaxErrors)}");

        // (2) The polish gate the agent itself is held to.
        var playtest = new GamePlaytester()
            .TestHtml5Async(Path.Combine(root, "index.html"), TimeSpan.FromSeconds(1), CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.True(playtest.Success, playtest.Summary);
        Assert.True(playtest.Issues.Count == 0,
            $"{expectedGenre}: scaffold fails its own production bar:\n{string.Join("\n", playtest.Issues)}");

        // Every scaffold documents itself.
        Assert.True(File.Exists(Path.Combine(root, "DESIGN.md")));
        Assert.True(File.Exists(Path.Combine(root, "README.md")));
    }

    [Fact]
    public void GenreScaffolds_AreActuallyDifferentGames()
    {
        var racing = new GameScaffoldService().Scaffold("html5", "ett racing spel", Path.Combine(_dir, "r1"));
        var puzzle = new GameScaffoldService().Scaffold("html5", "ett match-3 pussel", Path.Combine(_dir, "p1"));
        Assert.True(racing.Success && puzzle.Success);
        var racingHtml = File.ReadAllText(Path.Combine(_dir, "r1", "index.html"));
        var puzzleHtml = File.ReadAllText(Path.Combine(_dir, "p1", "index.html"));
        Assert.NotEqual(racingHtml, puzzleHtml);
        Assert.Contains("Racer", racingHtml);
        Assert.Contains("Match-3", puzzleHtml);
    }
}
