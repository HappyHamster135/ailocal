using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.95: sex rotorsaker ur team-byggets live-krasch. Låser:
/// route-läkningen (claude-fossiler ersätts, egna billiga routes rörs ej),
/// maskningsvakten på SKRIVNINGAR (materialiserade [ADDRESS] på disk live),
/// worktree-inhägnaden (spår skrev förbi isolationen med absoluta vägar)
/// och granskarens scope-nonsens-filter (avvisade legitim vidareutveckling).</summary>
public class TeamRobustnessTests : IDisposable
{
    private readonly string _dir;

    public TeamRobustnessTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-v195-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* städning */ }
    }

    // ---- Route-läkningen (claude-opus-fossilen) ----------------------------

    [Fact]
    public void HealRetired_ClaudeRoute_LaksTillBilligDefault()
    {
        // Live: "Modellval: claude-opus-4-8 (anthropic-rutt) - komplexitet 5/5".
        var stored = new List<ModelRoute>
        {
            new("coding", "anthropic", "claude-opus-4-8", 4),
            new("coding", "openrouter", "deepseek/deepseek-v4-flash", 1),
        };
        var healed = ModelRoute.HealRetired(stored);
        Assert.DoesNotContain(healed, r => r.Model.StartsWith("claude-"));
        Assert.DoesNotContain(healed, r => r.Provider == "anthropic");
        Assert.Contains(healed, r => r is { Skill: "coding", Model: "z-ai/glm-5.2", MinComplexity: 4 });
        Assert.Contains(healed, r => r is { Skill: "coding", Model: "deepseek/deepseek-v4-flash" });  // egen billig rörs ej
    }

    [Fact]
    public void HealRetired_ClaudeRouteForOkandSkill_Slapps()
    {
        var healed = ModelRoute.HealRetired([new("mystisk-skill", "anthropic", "claude-opus-4-8", 5)]);
        Assert.DoesNotContain(healed, r => r.Model.StartsWith("claude-") || r.Provider == "anthropic");
    }

    // ---- Maskningsvakten på skrivningar ------------------------------------

    [Fact]
    public async Task WriteFile_MedAddressMarkoren_Blockeras()
    {
        // Live: PackedVector2Array blev "[ADDRESS]" I FILEN PÅ DISK.
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        var result = await executor.ExecuteAsync(new ToolCall("1", "write_file",
            """{"path":"Main.gd","content":"var buf := [ADDRESS]()\n"}"""), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("maskning", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(_dir, "Main.gd")), "korruptionen fick aldrig landa på disk");
    }

    [Fact]
    public async Task EditFile_MedAddressINyaTexten_Blockeras()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.gd"), "extends Node\nfunc _ready() -> void:\n\tpass\n");
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        var result = await executor.ExecuteAsync(new ToolCall("1", "edit_file",
            """{"path":"a.gd","oldText":"pass","newText":"var x := [ADDRESS]()"}"""), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("maskning", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Worktree-inhägnaden ----------------------------------------------

    [Fact]
    public async Task ConfineToRoot_AbsolutVagUtanfor_AvvisasMedFacit()
    {
        var outside = Path.Combine(Path.GetTempPath(), "ailocal-v195-utanfor-" + Guid.NewGuid().ToString("n") + ".gd");
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _dir) { ConfineToRoot = true };
        var result = await executor.ExecuteAsync(new ToolCall("1", "write_file",
            $$"""{"path":{{System.Text.Json.JsonSerializer.Serialize(outside)}},"content":"extends Node"}"""),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("ISOLERAT", result.Output);
        Assert.False(File.Exists(outside));
    }

    [Fact]
    public async Task ConfineToRoot_RelativVag_FungerarSomVanligt()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _dir) { ConfineToRoot = true };
        var result = await executor.ExecuteAsync(new ToolCall("1", "write_file",
            """{"path":"Spar.gd","content":"extends Node\nfunc _ready() -> void:\n\tpass\n"}"""),
            CancellationToken.None);
        Assert.False(result.IsError, result.Output);
        Assert.True(File.Exists(Path.Combine(_dir, "Spar.gd")));
    }

    // ---- Granskarens scope-nonsens-filter ---------------------------------

    [Theory]
    [InlineData("AVVISA: The change introduces an autoload section, but the task was not to add any new features or modify the project.")]
    [InlineData("AVVISA: This code appears to be modifying an existing file rather than creating the game.")]
    [InlineData("AVVISA: There is no indication that this change is related to building a 2D football game.")]
    public void ParseVerdict_ScopeNonsens_FailarOpen(string reply)
    {
        // Live: granskaren avvisade LEGITIM vidareutveckling med exakt de här
        // motiveringarna - att utöka ett befintligt kit ÄR uppdraget.
        var (approved, _) = ChangeReviewer.ParseVerdict(reply);
        Assert.True(approved);
    }

    [Fact]
    public void ParseVerdict_AktaAvslag_StarKvar()
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(
            "AVVISA: Ändringen raderar hela spelet och ersätter Main.gd med en dikt - det motsäger uppgiften. Återställ filen.");
        Assert.False(approved);
        Assert.Contains("dikt", reason);
    }
}
