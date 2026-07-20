using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks the fixes from the user's chat transcript: the reviewer
/// contradiction ("AVVISA: ... så godkänd"), the planner inventing subtasks
/// for "hej", contextual build requests ("kan du bygga den?"), and the
/// write_file truncation tripwire + append mode.</summary>
public class TranscriptBugFixTests : IDisposable
{
    private readonly string _dir;

    public TranscriptBugFixTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-transcript-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Granskarens motsägelse (sedd ordagrant i transkriptet) ------------

    [Fact]
    public void ParseVerdict_RejectionWhoseReasonApproves_FailsOpen()
    {
        var (approved, _) = ChangeReviewer.ParseVerdict(
            "AVVISA: Innehållet har överensstämt med uppgiftens beskrivning och är tekniskt korrekt, så godkänd.");
        Assert.True(approved);
    }

    [Fact]
    public void ParseVerdict_GenuineRejection_StillRejects()
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(
            "AVVISA: filen skriver över hela index.html med tom sträng - behåll befintligt innehåll.");
        Assert.False(approved);
        Assert.Contains("index.html", reason);
    }

    // ---- Planner-gaten ("hej" -> Emotion Detection + Greeting Response) ----

    [Theory]
    [InlineData("hej")]
    [InlineData("dzfsghdfgsdfgs")]
    [InlineData("vad är klockan?")]
    public void TrivialPrompts_NeverGoToThePlanner(string prompt)
    {
        Assert.False(HostRole.LooksMultiPart(prompt));
    }

    [Fact]
    public void GenuinelyMultiPartPrompt_StillGoesToThePlanner()
    {
        Assert.True(HostRole.LooksMultiPart(
            "undersök marknaden för elcyklar och skriv en rapport, gör sedan en sammanfattning för styrelsen med de viktigaste punkterna"));
    }

    // ---- Kontextuella byggfraser ("kan du bygga den?") ---------------------

    [Theory]
    [InlineData("kan du bygga den?")]
    [InlineData("could you start programming it?")]
    public void ContextualBuildPhrases_HaveVerbAndBackReference(string prompt)
    {
        Assert.True(HostRole.HasBuildVerb(prompt));
        Assert.True(HostRole.RefersBack(prompt));
    }

    [Fact]
    public void NamedArtifact_IsADirectBuildRequest_NoContextNeeded()
    {
        // "projektet" innehåller artefaktordet - detta är en direkt
        // byggbegäran, inte en bakreferens (transkriptets exakta fras).
        Assert.True(HostRole.IsBuildRequest("kan du börja bygga projektet?"));
    }

    [Fact]
    public void PlanTextInHistory_CountsAsArtifact()
    {
        Assert.True(HostRole.HasArtifactWord("Här är en plan för ett spel som kombinerar element..."));
        Assert.False(HostRole.HasArtifactWord("vi pratade om vädret imorgon"));
    }

    // ---- write_file: append + trunkeringstripwire --------------------------

    [Fact]
    public async Task WriteFile_AppendMode_ExtendsTheFile()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _dir);
        ToolCall Call(object args) => new(Guid.NewGuid().ToString("n"), "write_file",
            System.Text.Json.JsonSerializer.Serialize(args));

        await executor.ExecuteAsync(Call(new { path = "a.txt", content = "del1-" }), CancellationToken.None);
        var result = await executor.ExecuteAsync(
            Call(new { path = "a.txt", content = "del2", append = true }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("appended", result.Output);
        Assert.Equal("del1-del2", await File.ReadAllTextAsync(Path.Combine(_dir, "a.txt")));
    }

    [Fact]
    public void TruncatedHtml_GetsActionableWarning()
    {
        var warning = AgentToolExecutor.DetectTruncation("index.html",
            "<!DOCTYPE html><html><body><script>const x = 1; function f( {");
        Assert.NotNull(warning);
        Assert.Contains("append", warning);
    }

    [Fact]
    public void CompleteHtml_GetsNoWarning()
    {
        Assert.Null(AgentToolExecutor.DetectTruncation("index.html",
            "<!DOCTYPE html><html><body><script>const x = 1;</script></body></html>"));
    }

    // ---- Python: provisionerbar + lokaliserbar (rapporten: agenten
    // skippade verify i stallet for att installera python) ------------------

    [Fact]
    public async Task Provision_KnowsPython_AndUnknownToolListsIt()
    {
        var result = await new ToolProvisioner().ProvisionAsync("definitivt-okant", _dir, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("python", result.Output); // katalogen listar python som tillatet namn
    }

    [Fact]
    public void PythonLocator_CommandOrDefault_AlwaysYieldsARunnableShape()
    {
        var cmd = PythonLocator.CommandOrDefault();
        // Antingen bara "python" (PATH-fallet) eller en citerad absolut vag.
        Assert.True(cmd == "python" || (cmd.StartsWith('"') && cmd.EndsWith('"') && cmd.Contains("python", StringComparison.OrdinalIgnoreCase)),
            $"ovantat kommando: {cmd}");
    }

    // ---- Generell provisionering (node/git/java/dotnet) --------------------

    [Theory]
    [InlineData("node")]
    [InlineData("git")]
    [InlineData("java")]
    [InlineData("dotnet")]
    public async Task Provision_KnowsTheWholeToolchainCatalog(string tool)
    {
        // Okant namn listar hela katalogen - varje verktyg agenten kan
        // behova ska finnas dar sa "verktyg saknas" alltid ar atgardbart.
        var result = await new ToolProvisioner().ProvisionAsync("definitivt-okant", _dir, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains(tool, result.Output);
    }

    [Theory]
    [InlineData("node")]
    [InlineData("npm")]
    [InlineData("git")]
    [InlineData("java")]
    [InlineData("dotnet")]
    [InlineData("godot")]
    public void ToolLocator_CommandOrDefault_NeverThrows_AndFallsBackToBareName(string tool)
    {
        var cmd = ToolLocator.CommandOrDefault(tool);
        Assert.True(cmd == tool || (cmd.StartsWith('"') && cmd.EndsWith('"')),
            $"ovantat kommando for {tool}: {cmd}");
    }

    [Fact]
    public void ToolLocator_UnknownTool_ReturnsNull()
    {
        Assert.Null(ToolLocator.Find("helt-okant-verktyg"));
    }
}
