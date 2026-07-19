using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The HTML5 playtest analysis is the agent's "definition of done"
/// signal for the production bar (its system prompt says every reported issue
/// is remaining work) - so a bare-bones game MUST get polish issues and a
/// polished game must NOT, or the loop either never finishes or ships slop.</summary>
public class GamePlaytesterTests : IDisposable
{
    private readonly string _dir;

    public GamePlaytesterTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-playtest-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private async Task<PlaytestResult> Analyze(string html)
    {
        var path = Path.Combine(_dir, "index.html");
        await File.WriteAllTextAsync(path, html);
        return await new GamePlaytester().TestHtml5Async(path, TimeSpan.FromSeconds(1), CancellationToken.None);
    }

    [Fact]
    public async Task BareBonesGame_GetsProductionPolishIssues()
    {
        var result = await Analyze("""
            <canvas id="c"></canvas>
            <script>
            addEventListener('keydown', e => {});
            function loop(){ requestAnimationFrame(loop); } loop();
            </script>
            """);

        Assert.True(result.Success);
        Assert.Contains(result.Issues, i => i.Contains("ljud", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, i => i.Contains("animation", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, i => i.Contains("game over", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(result.Issues, i => i.Contains("localStorage", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task PolishedGame_GetsNoPolishIssues()
    {
        var result = await Analyze("""
            <canvas id="c"></canvas>
            <div id="over">Game Over</div>
            <script>
            const ac = new AudioContext();
            let frame = 0; // sprite anim
            const high = localStorage.getItem('highscore');
            addEventListener('keydown', e => {});
            function loop(){ frame ^= 1; requestAnimationFrame(loop); } loop();
            </script>
            """);

        Assert.True(result.Success);
        Assert.Empty(result.Issues);
    }
}
