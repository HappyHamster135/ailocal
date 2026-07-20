using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.34.0's visual playtest level: real Chromium-headless
/// screenshots of HTML5 games plus AI review of what the game LOOKS like.
/// Browser-dependent tests skip silently on machines without Edge/Chrome
/// (every Windows 10/11 has Edge, so they run everywhere that matters).</summary>
public class VisionPlaytestTests : IDisposable
{
    private readonly string _dir;

    public VisionPlaytestTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-vision-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private const string DrawingGame = """
        <!DOCTYPE html><html><head><title>t</title></head><body>
        <canvas id="game" width="640" height="480"></canvas>
        <script>
        const c = document.getElementById('game').getContext('2d');
        c.fillStyle = '#3a7d2c'; c.fillRect(0, 0, 640, 480);
        c.fillStyle = '#fff'; c.font = '40px sans-serif'; c.fillText('SPELET', 200, 240);
        function loop() { requestAnimationFrame(loop); } loop();
        </script></body></html>
        """;

    [Fact]
    public async Task Screenshotter_CapturesRealPixels()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return; // maskin utan Edge/Chrome

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DrawingGame);
        var shot = await new BrowserScreenshotter().CaptureHtmlAsync(
            Path.Combine(_dir, "index.html"), Path.Combine(_dir, "shot.png"),
            TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.True(shot.Success, shot.Output);
        Assert.True(new FileInfo(shot.ImagePath!).Length > 1000, "PNG:n är misstänkt liten");
    }

    [Fact]
    public async Task Playtest_AttachesScreenshot_AndVisionVerdict()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return;

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DrawingGame);
        var tester = new GamePlaytester(
            visionReview: (_, _, _) => Task.FromResult((true, "Ser ut som ett riktigt spel med titeltext och grön spelplan.")));
        var result = await tester.FullTestAsync(_dir, "html5", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.NotNull(result.ScreenshotPath);
        Assert.True(File.Exists(result.ScreenshotPath));
        Assert.Contains("Visuell granskning (AI)", result.Summary);
        Assert.Contains("riktigt spel", result.Summary);
    }

    [Fact]
    public async Task Playtest_BlackCanvasVerdict_BecomesAnIssue()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return;

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DrawingGame);
        var tester = new GamePlaytester(
            visionReview: (_, _, _) => Task.FromResult((true, "Ytan är helt svart - ingenting renderas.")));
        var result = await tester.FullTestAsync(_dir, "html5", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.Contains(result.Issues, i => i.Contains("tom/svart"));
    }

    [Fact]
    public async Task Playtest_WithoutVision_StillSavesScreenshotWithHonestNote()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return;

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DrawingGame);
        var result = await new GamePlaytester().FullTestAsync(_dir, "html5", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.NotNull(result.ScreenshotPath);
        Assert.Contains("Ingen vision-modell konfigurerad", result.Summary);
    }

    [Fact]
    public async Task Playtest_VisionCrash_NeverFailsTheRun()
    {
        if (BrowserScreenshotter.FindBrowser() is null) return;

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), DrawingGame);
        var tester = new GamePlaytester(
            visionReview: (_, _, _) => throw new HttpRequestException("nätet nere"));
        var result = await tester.FullTestAsync(_dir, "html5", TimeSpan.FromSeconds(2), CancellationToken.None);

        Assert.NotNull(result.ScreenshotPath); // dumpen finns trots vision-kraschen
    }
}
