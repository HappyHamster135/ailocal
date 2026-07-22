using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.88: cross-modell KODgranskaren i grinden. Låser filurvalet
/// (huvudkodfiler, störst först, skip-mappar, total budget), parsningen
/// (bugs-JSON, max 4) och fail-open-beteendet (fel/parse-miss = tom lista -
/// granskaren får aldrig sänka ett bygge på sin egen krasch).</summary>
public class CodeReviewPassTests : IDisposable
{
    private readonly string _dir;

    public CodeReviewPassTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-codereview-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* städning */ }
    }

    [Fact]
    public void BuildCodeSample_TarHuvudkodStorstForst_SkipparByggmappar()
    {
        File.WriteAllText(Path.Combine(_dir, "Main.gd"), new string('a', 500) + "\nfunc _ready():\n\tpass");
        File.WriteAllText(Path.Combine(_dir, "liten.js"), "let x = 1;");
        Directory.CreateDirectory(Path.Combine(_dir, "node_modules"));
        File.WriteAllText(Path.Combine(_dir, "node_modules", "dep.js"), "SKA_ALDRIG_SYNAS");
        Directory.CreateDirectory(Path.Combine(_dir, "build"));
        File.WriteAllText(Path.Combine(_dir, "build", "ut.js"), "SKA_INTE_HELLER_SYNAS");
        File.WriteAllText(Path.Combine(_dir, "README.md"), "ingen kodfil");

        var sample = CodeReviewPass.BuildCodeSample(_dir);

        Assert.Contains("Main.gd", sample);
        Assert.Contains("liten.js", sample);
        Assert.DoesNotContain("SKA_ALDRIG_SYNAS", sample);
        Assert.DoesNotContain("SKA_INTE_HELLER_SYNAS", sample);
        Assert.DoesNotContain("README.md", sample);
        // Störst först - spellogiken (den stora filen) ska komma före småfiler.
        Assert.True(sample.IndexOf("Main.gd") < sample.IndexOf("liten.js"));
    }

    [Fact]
    public void BuildCodeSample_RespekterarTotalbudgeten()
    {
        File.WriteAllText(Path.Combine(_dir, "enorm.js"), new string('x', 60_000));
        var sample = CodeReviewPass.BuildCodeSample(_dir);
        Assert.True(sample.Length < 26_000, $"sample är {sample.Length} tecken - budgeten läckte");
    }

    [Fact]
    public async Task ReviewAsync_ParsarBuggar_OchBararKodenIPrompten()
    {
        File.WriteAllText(Path.Combine(_dir, "Main.gd"), "func _ready():\n\tstart_knappen_ar_okopplad()");
        string? seenPrompt = null;
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete = (req, _) =>
        {
            seenPrompt = req.Messages[^1].Content;
            return Task.FromResult(ProviderResponse.Ok(new ChatResponse
            {
                Content = "Här är analysen: {\"bugs\":[\"Main.gd/_ready: startknappen kopplas aldrig\"]}",
                Model = "m",
                Provider = "test"
            }));
        };

        var bugs = await CodeReviewPass.ReviewAsync(_dir, "bygg ett spel", complete, CancellationToken.None, "stark-modell");

        Assert.Single(bugs);
        Assert.Contains("startknappen", bugs[0]);
        Assert.Contains("start_knappen_ar_okopplad", seenPrompt);   // HELA koden bärs i prompten
        Assert.Contains("DIFFERENT model", seenPrompt);              // cross-modell-inramningen
    }

    [Theory]
    [InlineData("ingen json alls")]
    [InlineData("{\"fel_falt\":[\"x\"]}")]
    [InlineData("{trasig json")]
    public async Task ReviewAsync_FailOpen_PaKonstigaSvar(string svar)
    {
        File.WriteAllText(Path.Combine(_dir, "Main.gd"), "func _ready():\n\tpass");
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete = (_, _) =>
            Task.FromResult(ProviderResponse.Ok(new ChatResponse { Content = svar, Model = "m", Provider = "test" }));
        Assert.Empty(await CodeReviewPass.ReviewAsync(_dir, "spel", complete, CancellationToken.None));
    }

    [Fact]
    public async Task ReviewAsync_TomProjektmapp_GerTomListaUtanAnrop()
    {
        var called = false;
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete = (_, _) =>
        {
            called = true;
            return Task.FromResult(ProviderResponse.Ok(new ChatResponse { Content = "{\"bugs\":[]}", Model = "m", Provider = "t" }));
        };
        Assert.Empty(await CodeReviewPass.ReviewAsync(_dir, "spel", complete, CancellationToken.None));
        Assert.False(called);   // ingen kod -> inget (betalt) modellanrop alls
    }

    [Fact]
    public async Task ReviewAsync_TarMax4Buggar()
    {
        File.WriteAllText(Path.Combine(_dir, "Main.gd"), "func _ready():\n\tpass");
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete = (_, _) =>
            Task.FromResult(ProviderResponse.Ok(new ChatResponse
            {
                Content = "{\"bugs\":[\"a\",\"b\",\"c\",\"d\",\"e\",\"f\"]}",
                Model = "m",
                Provider = "test"
            }));
        Assert.Equal(4, (await CodeReviewPass.ReviewAsync(_dir, "spel", complete, CancellationToken.None)).Count);
    }
}
