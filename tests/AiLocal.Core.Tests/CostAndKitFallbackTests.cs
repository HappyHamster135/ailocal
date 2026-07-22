using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.94: fyra rotorsaker ur ägarens live-rapport ("fotbollsmanager
/// gav Pixel Rush, platshållarvägar, JS-GDScript, Claude auto-vald för 500kr").
/// Låser: genrefallback via taskHint, platshållar-vakten, GDScript-tripwiren
/// (inkl. den EXAKTA fil som sågs live), legacy-läkningen av lagrad
/// leverantörsordning/opus-default, och ForTask-fallbacken till openrouter.</summary>
public class CostAndKitFallbackTests : IDisposable
{
    private readonly string _dir;

    public CostAndKitFallbackTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-v194-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* städning */ }
    }

    // ---- Fel A: fotbollsmanager -> Pixel Rush ------------------------------

    [Fact]
    public void DetectGenre_FelstavadManagemeant_ArManagement()
    {
        // Ägarens exakta prompt (inkl. felstavningen "managemeant").
        Assert.Equal("management", GameScaffoldService.DetectGenre(
            "Kan du bygga ett 2d fotbolls simulator managemeant spel där man börjar från inget"));
    }

    [Fact]
    public async Task ScaffoldGame_UtanPrompt_FallerTillbakaPaUppdragstexten()
    {
        string? seenPrompt = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir,
            gameScaffolder: (engine, prompt, root, ct) =>
            {
                seenPrompt = prompt;
                return Task.FromResult((true, "ok"));
            },
            taskHint: "bygg ett 2d fotbolls managemeant spel");

        var result = await executor.ExecuteAsync(
            new ToolCall("1", "scaffold_game", """{"engine":"godot"}"""), CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        // Genrevalet lever på prompten - tom sträng gav plattformaren (live).
        Assert.Equal("bygg ett 2d fotbolls managemeant spel", seenPrompt);
    }

    // ---- Fel B: platshållarvägar ------------------------------------------

    [Fact]
    public async Task WriteFile_PlatshallarVag_FarKorrigerandeFel()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        var result = await executor.ExecuteAsync(new ToolCall("1", "write_file",
            """{"path":"/path/to/your/project/root/main.gd","content":"extends Node"}"""),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("PLATSHÅLLARE", result.Output);
    }

    // ---- Fel C: JS-ismer i GDScript ---------------------------------------

    [Fact]
    public void GdScriptLint_ExaktaLiveFilen_Flaggas()
    {
        // Den fil som skrevs live: //-kommentar + funcar utan riktig kropp.
        var live = "// Godot Script\nextends Node\n\nfunc _ready():\n    # Initialize game state here\n\nfunc update(delta):\n    # Update game logic here\n";
        var err = GdScriptLint.Check(live);
        Assert.NotNull(err);
        Assert.Contains("//", err);   // första felet: JS-kommentaren
    }

    [Theory]
    [InlineData("function start():\n\tpass\n", "function")]
    [InlineData("extends Node\nfunc _ready():\n\t# bara kommentar\nfunc x():\n\tpass\n", "kropp")]
    public void GdScriptLint_JsIsmer_Flaggas(string content, string expectedInError)
    {
        var err = GdScriptLint.Check(content);
        Assert.NotNull(err);
        Assert.Contains(expectedInError, err!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GdScriptLint_GiltigGdScript_PasserarTyst()
    {
        var ok = "extends Node2D\n# riktig kommentar\nfunc _ready() -> void:\n\trandomize()\n\nfunc _process(delta: float) -> void:\n\tposition.x += delta\n";
        Assert.Null(GdScriptLint.Check(ok));
    }

    [Fact]
    public void DetectTruncation_GdFil_BarTripwiren()
    {
        var note = AgentToolExecutor.DetectTruncation("main.gd", "// js-kommentar\nextends Node\n");
        Assert.NotNull(note);
        Assert.Contains("edit_file", note);
    }

    // ---- Fel F: Claude auto-vald (legacy-läkningen) ------------------------

    [Fact]
    public void HealLegacyPriority_GamlaDefaulten_BlirBilligForst()
    {
        var (healed, wasLegacy) = PersistentSettingsStore.HealLegacyPriority(
            ["anthropic", "gemini", "openrouter", "ollama"]);
        Assert.True(wasLegacy);
        Assert.Equal("openrouter", healed[0]);   // billig-först
    }

    [Fact]
    public void HealLegacyPriority_EgenOrdning_RorsAldrig()
    {
        var custom = new List<string> { "anthropic", "openrouter" };   // aktivt val
        var (healed, wasLegacy) = PersistentSettingsStore.HealLegacyPriority(custom);
        Assert.False(wasLegacy);
        Assert.Same(custom, healed);
    }

    [Fact]
    public void HealLegacyAnthropicModel_OpusDefault_BlirHaiku()
    {
        Assert.Equal("claude-haiku-4-5", PersistentSettingsStore.HealLegacyAnthropicModel("claude-opus-4-8"));
        Assert.Equal("claude-sonnet-5", PersistentSettingsStore.HealLegacyAnthropicModel("claude-sonnet-5"));
    }

    [Fact]
    public void ForTask_UtanRoute_FallerTillOpenrouter_AldrigAnthropic()
    {
        var tiers = new ModelTiers { Routes = [] };   // inga routes alls
        var (provider, model) = tiers.ForTask("nagot-okant-skill", 3);
        Assert.Equal("openrouter", provider);
        Assert.False(string.IsNullOrWhiteSpace(model));
    }
}
