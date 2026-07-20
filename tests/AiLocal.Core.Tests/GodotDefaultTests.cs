using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.33.0: Godot is the default game engine (the app builds
/// studio games, not browser toys), the verifier/quality gate actually
/// UNDERSTANDS Godot projects (they were invisible before - the gate graded
/// a stale html5 game at the root while the agent built Godot in a subfolder,
/// seen verbatim in a user transcript), and export templates are
/// provisionable so build_game can produce a runnable exe.</summary>
public class GodotDefaultTests : IDisposable
{
    private readonly string _dir;

    public GodotDefaultTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-godot-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static Func<string, string, CancellationToken, Task<(int, string)>> NoCommands =>
        (_, _, _) => Task.FromResult((0, "should not run without a godot binary"));

    // ---- Motorvalet --------------------------------------------------------

    [Theory]
    [InlineData("bygg ett 2d plattformsspel", "godot")]
    [InlineData("bygg ett fotbolls managerspel med tre svårighetsgrader", "godot")]
    [InlineData("bygg ett webbspel med snake", "html5")]
    [InlineData("bygg ett spel som körs i webbläsaren", "html5")]
    [InlineData("bygg ett unity-spel", "unity")]
    public void Scaffold_Auto_PicksEngineByIntent(string prompt, string expectedEngine)
    {
        var sub = Path.Combine(_dir, Guid.NewGuid().ToString("n")[..6]);
        var result = new GameScaffoldService().Scaffold("auto", prompt, sub);
        Assert.True(result.Success, result.Output);
        Assert.Equal(expectedEngine, result.Engine);
    }

    // ---- Detektorn ---------------------------------------------------------

    [Fact]
    public void Detect_GodotProject_WinsOverItsOwnCsproj()
    {
        // Godot-mono-kittet innehåller .csproj - utan godot-först-regeln
        // klassades projektet som DotNet och verify körde `dotnet build`.
        File.WriteAllText(Path.Combine(_dir, "project.godot"), "[application]\nconfig/name=\"Spel\"");
        File.WriteAllText(Path.Combine(_dir, "Spel.csproj"), "<Project Sdk=\"Godot.NET.Sdk/4.3.0\"></Project>");
        Assert.Equal(ProjectVerifier.ProjectKind.Godot, new ProjectVerifier().Detect(_dir));
    }

    [Fact]
    public void ProjectRootDetector_SeesGodotProjects()
    {
        File.WriteAllText(Path.Combine(_dir, "project.godot"), "[application]");
        Assert.Equal(Path.GetFullPath(_dir), ProjectRootDetector.Detect(_dir));
    }

    // ---- Verifieringen -----------------------------------------------------

    [Fact]
    public async Task VerifyGodot_ScaffoldedProject_PassesStaticCheck()
    {
        // Kittets egna scener MÅSTE ha hela res://-refererade filer på plats -
        // annars underkänner grinden varje standardbygge från första stund.
        var result = new GameScaffoldService().Scaffold("godot", "ett plattformsspel", _dir);
        Assert.True(result.Success, result.Output);

        var verify = await new ProjectVerifier().VerifyAsync(_dir, NoCommands, CancellationToken.None);
        Assert.True(verify.Success, verify.Report);
        Assert.Equal(ProjectVerifier.ProjectKind.Godot, verify.Kind);
    }

    [Fact]
    public async Task VerifyGodot_BrokenSceneReference_Fails()
    {
        File.WriteAllText(Path.Combine(_dir, "project.godot"), "[application]");
        File.WriteAllText(Path.Combine(_dir, "Main.tscn"),
            "[gd_scene]\n[ext_resource type=\"Script\" path=\"res://scripts/SaknasHelt.gd\" id=\"1\"]");
        var verify = await new ProjectVerifier().VerifyAsync(_dir, NoCommands, CancellationToken.None);
        Assert.False(verify.Success);
        Assert.Contains("SaknasHelt.gd", verify.Report);
    }

    [Fact]
    public async Task QualityGate_GodotProject_IsGraded_NotInvisible()
    {
        var scaffold = new GameScaffoldService().Scaffold("godot", "ett plattformsspel", _dir);
        Assert.True(scaffold.Success, scaffold.Output);

        var findings = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands, playtest: null, CancellationToken.None);
        Assert.True(findings.Clean, findings.Report);
        Assert.Equal("godot", findings.Engine);
    }

    // ---- Exportmallarna ----------------------------------------------------

    [Fact]
    public async Task ProvisionCatalog_ListsGodotTemplates()
    {
        var result = await new ToolProvisioner().ProvisionAsync("definitivt-okant", _dir, CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("godot-templates", result.Output);
    }
}
