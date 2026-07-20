using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.32.0's fixes from the "den blir aldrig klar"-report:
/// provider redaction artifacts ([ADDRESS] & co) must never send the agent
/// on ghost hunts, the iteration cap is a checkpoint (HitIterationCap flag),
/// and reviewer nitpick rejections fail open deterministically.</summary>
public class IterationCapAndArtifactTests : IDisposable
{
    private readonly string _dir;

    public IterationCapAndArtifactTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-itercap-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Granskarens nitpick-avslag (ordagrant ur transkriptet) -----------

    [Theory]
    [InlineData("AVVISA: The TeamTrainingManager class lacks proper error handling for null reference exceptions in the team and player indices; use TryGet.")]
    [InlineData("AVVISA: The team name in `GameTeam` should be a constant, not a mutable string field. Use a `readonly string teamName;` and initialize it.")]
    [InlineData("AVVISA: Missing null check for `teamManager` before calling its `SelectTeam()` method in `OnTeamSelected()`.")]
    [InlineData("AVVISA: The file __init__.py should be named main.py or football_sim/__init__.py to match the project name, following conventional Python.")]
    public void ParseVerdict_NitpickRejections_FailOpen(string reply)
    {
        var (approved, _) = ChangeReviewer.ParseVerdict(reply);
        Assert.True(approved);
    }

    [Fact]
    public void ParseVerdict_RealDefectRejection_StillRejects()
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(
            "AVVISA: koden raderar hela spellogiken i update() - återställ den innan något annat.");
        Assert.False(approved);
        Assert.Contains("spellogiken", reason);
    }

    // ---- Maskningsartefakter ([ADDRESS] m.fl.) ----------------------------

    [Theory]
    [InlineData("for i in [ADDRESS]):", "[ADDRESS]")]
    [InlineData("m_Name: [address]", "[ADDRESS]")]
    [InlineData("kontakta [EMAIL] för mer info", "[EMAIL]")]
    public void RedactionArtifactIn_FindsMarkers(string text, string expected)
    {
        Assert.Equal(expected, AgentToolExecutor.RedactionArtifactIn(text), ignoreCase: true);
    }

    [Fact]
    public void RedactionArtifactIn_CleanText_ReturnsNull()
    {
        Assert.Null(AgentToolExecutor.RedactionArtifactIn("const version = 1; // C:\\Users\\jon\\spel"));
    }

    [Fact]
    public async Task EditFile_WithArtifactInOldText_ExplainsInsteadOfGenericError()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "scen.txt"), "m_Name: Main Camera");
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        var result = await executor.ExecuteAsync(new ToolCall(
            Guid.NewGuid().ToString("n"), "edit_file",
            System.Text.Json.JsonSerializer.Serialize(new { path = "scen.txt", oldText = "m_Name: [ADDRESS]", newText = "m_Name: Main Camera" })),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("maskningsartefakt", result.Output);
        Assert.Contains("oskadad", result.Output);
    }

    [Fact]
    public async Task Search_ForArtifactPattern_AddsDoNotChaseNote()
    {
        await File.WriteAllTextAsync(Path.Combine(_dir, "a.txt"), "helt vanligt innehåll");
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        var result = await executor.ExecuteAsync(new ToolCall(
            Guid.NewGuid().ToString("n"), "search",
            System.Text.Json.JsonSerializer.Serialize(new { pattern = "\\[ADDRESS\\]" })),
            CancellationToken.None);
        Assert.Contains("maskningsartefakt", result.Output);
    }

    // ---- Iterationstaket som kontrollpunkt --------------------------------

    [Fact]
    public void AgentRunResult_HitIterationCap_DefaultsFalse_AndSurvivesWith()
    {
        var result = new AgentRunResult(true, "klar", [], 3, [], new TokenUsage(1, 2));
        Assert.False(result.HitIterationCap);

        var capped = result with { HitIterationCap = true, Success = false };
        Assert.True(capped.HitIterationCap);
        Assert.Equal(2, capped.TotalUsage.OutputTokens);
    }
}
