using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.37.0: the creative director's contract parsing/fallback
/// and DESIGN.md round-trip, the interactive CDP probe (a game that responds
/// to keys vs one that ignores them - real Chromium), and the milestone
/// registry's approve/adjust/timeout semantics.</summary>
public class DirectorAndProbeTests : IDisposable
{
    private readonly string _dir;

    public DirectorAndProbeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-dir-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Regissören --------------------------------------------------------

    [Fact]
    public void Director_ParsesContractJson_EvenWithProseAround()
    {
        var contract = DirectorPass.ParseContract(
            "Här är kontraktet:\n{\"pillars\":\"snabbt och rättvist\",\"twist\":\"tiden är valuta\"," +
            "\"criteria\":[\"5 banor\",\"3 fiendetyper\",\"highscore sparas\"]}\nLycka till!");
        Assert.NotNull(contract);
        Assert.Equal(3, contract.Criteria.Count);
        Assert.Contains("tiden är valuta", contract.Twist);
    }

    [Theory]
    [InlineData("bara prosa utan json")]
    [InlineData("{\"criteria\":[\"en enda punkt\"]}")]
    public void Director_GarbageOrTooFew_ReturnsNull(string content)
    {
        Assert.Null(DirectorPass.ParseContract(content));
    }

    [Fact]
    public void Director_FallbackContract_IsMeasurable()
    {
        var game = DirectorPass.FallbackContract("bygg ett plattformsspel");
        Assert.True(game.Criteria.Count >= 4);
        Assert.Contains(game.Criteria, c => c.Contains("nivåer") || c.Contains("vågor"));

        var app = DirectorPass.FallbackContract("bygg ett budgetverktyg");
        Assert.Contains(app.Criteria, c => c.Contains("tester"));
    }

    [Fact]
    public void Director_DesignRoundTrip_WritesAndReadsCriteria()
    {
        var contract = DirectorPass.FallbackContract("bygg ett spel");
        File.WriteAllText(Path.Combine(_dir, "DESIGN.md"), "# Spelet\n\n" + contract.ToMarkdown());

        Assert.True(DirectorPass.AlreadyContracted(_dir));
        var read = DirectorPass.ReadCriteria(_dir);
        Assert.Equal(contract.Criteria.Count, read.Count);
        Assert.Equal(contract.Criteria[0], read[0]);
    }

    // ---- Interaktiv QA (riktig Chromium) -----------------------------------

    private const string RespondingGame = """
        <!DOCTYPE html><html><head><title>t</title></head><body>
        <canvas id="g" width="400" height="300"></canvas>
        <script>
        const c = document.getElementById('g').getContext('2d');
        let x = 10;
        c.fillStyle = '#222'; c.fillRect(0, 0, 400, 300);
        document.addEventListener('keydown', () => {
          x += 25;
          c.fillStyle = '#0f0'; c.fillRect(x, 100, 20, 20);
        });
        </script></body></html>
        """;

    private const string DeafGame = """
        <!DOCTYPE html><html><head><title>t</title></head><body>
        <canvas id="g" width="400" height="300"></canvas>
        <script>
        const c = document.getElementById('g').getContext('2d');
        c.fillStyle = '#222'; c.fillRect(0, 0, 400, 300);
        // Ingen input-hantering alls - spelet är dövt.
        </script></body></html>
        """;

    [Fact]
    public async Task Probe_GameThatListens_CountsAsResponsive()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return; // maskin utan Chromium

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), RespondingGame);
        var result = await new InteractiveProbe().PlayAsync(
            Path.Combine(_dir, "index.html"), Path.Combine(_dir, "probe.png"), CancellationToken.None);

        Assert.True(result.Ran, result.Notes);
        Assert.True(result.Responded, result.Notes);
        Assert.True(File.Exists(result.FinalScreenshotPath));
    }

    [Fact]
    public async Task Probe_DeafGame_IsFlagged()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return;

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DeafGame);
        var result = await new InteractiveProbe().PlayAsync(
            Path.Combine(_dir, "index.html"), Path.Combine(_dir, "probe.png"), CancellationToken.None);

        Assert.True(result.Ran, result.Notes);
        Assert.False(result.Responded, result.Notes);
    }

    // ---- Milstolperegistret ------------------------------------------------

    [Fact]
    public async Task Milestone_ResolveApprove_Unblocks()
    {
        var wait = MilestoneRegistry.WaitAsync("t1", TimeSpan.FromSeconds(30), CancellationToken.None);
        await Task.Delay(50);
        Assert.True(MilestoneRegistry.Resolve("t1", approve: true, note: null));
        var (approved, note) = await wait;
        Assert.True(approved);
        Assert.Null(note);
    }

    [Fact]
    public async Task Milestone_AdjustWithNote_ComesThrough()
    {
        var wait = MilestoneRegistry.WaitAsync("t2", TimeSpan.FromSeconds(30), CancellationToken.None);
        await Task.Delay(50);
        MilestoneRegistry.Resolve("t2", approve: false, note: "gör den svårare");
        var (approved, note) = await wait;
        Assert.False(approved);
        Assert.Equal("gör den svårare", note);
    }

    [Fact]
    public async Task Milestone_Timeout_AutoApproves_AndUnknownIdRejected()
    {
        var (approved, _) = await MilestoneRegistry.WaitAsync("t3", TimeSpan.FromMilliseconds(200), CancellationToken.None);
        Assert.True(approved); // obemannad nod får aldrig hänga
        Assert.False(MilestoneRegistry.Resolve("t3", true, null));
    }
}
