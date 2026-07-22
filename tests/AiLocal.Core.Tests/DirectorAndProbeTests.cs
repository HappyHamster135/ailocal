using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
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
        Assert.Contains(game.Criteria, c => c.Contains("banor") || c.Contains("nivåer"));

        var app = DirectorPass.FallbackContract("bygg ett budgetverktyg");
        Assert.Contains(app.Criteria, c => c.Contains("tester"));
    }

    [Fact]
    public void FallbackContract_ManagementFarKarriarstege_IntePlattformsmallen()
    {
        // v1.97 (ägarens fynd): "om man skriver simulator kommer den göra
        // exakt samma typ av spel bara med annat substantiv" - fallbacken var
        // EN mall för alla genrer. Nu formuleras progressionen per genre.
        var mgmt = DirectorPass.FallbackContract("bygg ett fotbolls management simulator spel");
        Assert.Contains(mgmt.Criteria, c => c.Contains("divisioner"));
        Assert.DoesNotContain(mgmt.Criteria, c => c.Contains("nivåer/vågor"));
    }

    [Fact]
    public void FallbackContract_ReplayMekanismenVarierar_SvarighetsgraderArInteTvang()
    {
        // v1.97: "3 svårighetsgrader måste inte vara hela tiden" - replay-
        // mekanismen slumpas (svårighetsgrader/upplåsningar/new game+).
        // 30 dragningar mot 3 alternativ: kräv minst 2 olika (flakefritt).
        var seen = new HashSet<string>();
        for (var i = 0; i < 30; i++)
        {
            var c = DirectorPass.FallbackContract("bygg ett plattformsspel");
            var replay = c.Criteria.First(x =>
                x.Contains("svårighetsgrader", StringComparison.OrdinalIgnoreCase)
                || x.Contains("Upplåsbart") || x.Contains("New game+"));
            seen.Add(replay);
        }
        Assert.True(seen.Count >= 2, $"bara {seen.Count} replay-variant på 30 dragningar");
    }

    [Fact]
    public async Task RunAsync_SkickarKreativVinkel_TillRegissorsmodellen()
    {
        // v1.97: en slumpad kreativ vinkel per körning - samma prompt ska inte
        // ge samma designangrepp två gånger.
        Assert.True(DirectorPass.CreativeLenses.Length >= 4);
        string? seenPrompt = null;
        Func<AiLocal.Core.Contracts.ChatRequest, CancellationToken, Task<AiLocal.Core.Providers.ProviderResponse>> complete = (req, _) =>
        {
            seenPrompt = req.Messages[^1].Content;
            return Task.FromResult(AiLocal.Core.Providers.ProviderResponse.Ok(
                new AiLocal.Core.Contracts.ChatResponse
                {
                    Content = """{"pillars":"p","twist":"t","criteria":["5 banor","3 fiendetyper","boss pa bana 5","dagligt mal-system"]}""",
                    Model = "m",
                    Provider = "test"
                }));
        };
        var contract = await DirectorPass.RunAsync("bygg ett spel", _dir, null, complete, CancellationToken.None);
        Assert.Contains("KREATIV VINKEL", seenPrompt);
        Assert.Contains(contract.Criteria, c => c.Contains("5 banor"));
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

    [Fact]
    public async Task ReviewAsync_KorPaReviewModel_OberoendeGranskare()
    {
        // Cross-modell-granskning: granskaren ska köra på reviewModelHint (en
        // ANNAN/starkare modell än byggaren), inte på standardmodellen - det är
        // hela poängen (olika modeller fångar olika felmoder).
        string? capturedHint = "NOT-SET";
        string? capturedPrompt = null;
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete = (req, _) =>
        {
            capturedHint = req.ModelHint;
            capturedPrompt = req.Messages[^1].Content;
            return Task.FromResult(ProviderResponse.Ok(new ChatResponse
            {
                Content = "{\"unmet\":[]}",
                Model = "reviewer",
                Provider = "test"
            }));
        };

        await DirectorPass.ReviewAsync(
            new[] { "5 banor med stigande svårighet" }, _dir, complete, CancellationToken.None,
            reviewModelHint: "z-ai/glm-5.2");

        Assert.Equal("z-ai/glm-5.2", capturedHint);
        // C2: den oberoende granskaren analyserar ocksa BALANS (ovinnbart/
        // trivialt/identiska svarighetsgrader) ur tuning-vardena i koden.
        Assert.Contains("BALANCE", capturedPrompt);
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
        // B3-uppfoljning: HTML5-repris (animerad PNG) spelas in bredvid dumpen -
        // RGBA-rutor hamtas via getImageData i sidan (ingen PNG-avkodning).
        // Giltig PNG-signatur + acTL-chunk = faktisk APNG-animation.
        Assert.NotNull(result.ReplayPath);
        Assert.True(File.Exists(result.ReplayPath), "reprisen saknas: " + result.Notes);
        var replayBytes = await File.ReadAllBytesAsync(result.ReplayPath!);
        Assert.True(replayBytes.Length > 500, "reprisen misstänkt liten");
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, replayBytes[..8]);
        Assert.Contains("acTL", System.Text.Encoding.ASCII.GetString(replayBytes));
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
