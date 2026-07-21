using AiLocal.Core.Agent;
using AiLocal.Core.Hardware;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.29.0: the node-enforced quality gate (a model's "Klar"
/// with zero files written must fail), the most-recently-active-project
/// detector, scaffold-into-subfolder on non-empty roots, the "göra en fil av
/// det"-intent gap, and the coder-model recommendation ladder.</summary>
public class QualityGateTests : IDisposable
{
    private readonly string _dir;

    public QualityGateTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-quality-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static Func<string, string, CancellationToken, Task<(int, string)>> NoCommands =>
        (_, _, _) => throw new InvalidOperationException("no toolchain command should run in this test");

    private const string ValidGame = """
        <!DOCTYPE html><html><head><title>t</title></head>
        <body><canvas id="game"></canvas><script>const x = 1;</script></body></html>
        """;

    // ---- Grind-vakt: speluppdrag maste ge en spelmotor-titel ---------------

    private void WriteConsoleApp(string subfolder)
    {
        var proj = Path.Combine(_dir, subfolder);
        Directory.CreateDirectory(proj);
        File.WriteAllText(Path.Combine(proj, subfolder + ".csproj"),
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><OutputType>Exe</OutputType>" +
            "<TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(proj, "Program.cs"), "System.Console.WriteLine(\"hi\");");
    }

    [Fact]
    public async Task Gate_GameRequested_ButConsoleApp_HardFails()
    {
        // Rapporterat: agenten frilansade en C#-konsolapp for "Football Manager"
        // och grinden godkande den. Ett speluppdrag ska underkannas nar motorn
        // inte ar godot/unity/html5 (DetectEngine => "unknown").
        WriteConsoleApp("FootballManager");
        var findings = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.MinValue,
            runCommand: (_, _, _) => Task.FromResult((0, "Build succeeded")),
            playtest: null, CancellationToken.None, gameExpected: true);

        Assert.True(findings.HardFail);
        Assert.Contains("ingen spelmotor-titel", findings.Report);
    }

    [Fact]
    public async Task Gate_AppRequested_ConsoleApp_NotBlockedByGameGuard()
    {
        // Samma konsolapp men gameExpected:false (ett verktyg, inte ett spel):
        // spelmotor-vakten far INTE sla till - app-vagen ska leva kvar.
        WriteConsoleApp("budget");
        var findings = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.MinValue,
            runCommand: (_, _, _) => Task.FromResult((0, "Build succeeded")),
            playtest: null, CancellationToken.None, gameExpected: false);

        Assert.DoesNotContain("ingen spelmotor-titel", findings.Report);
    }

    // ---- ProjectRootDetector ----------------------------------------------

    [Fact]
    public void Detector_EmptyWorkspace_ReturnsNull()
    {
        Assert.Null(ProjectRootDetector.Detect(_dir));
    }

    [Fact]
    public void Detector_ProjectAtRoot_ReturnsRoot()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), ValidGame);
        Assert.Equal(Path.GetFullPath(_dir), ProjectRootDetector.Detect(_dir));
    }

    [Fact]
    public void Detector_StaleRootProject_LosesToNewerSubfolderProject()
    {
        // Transkriptets läge: gammalt HTML5-spel i roten, nytt Python-projekt
        // i football_sim/ - verify graderade fel projekt.
        var rootGame = Path.Combine(_dir, "index.html");
        File.WriteAllText(rootGame, ValidGame);
        File.SetLastWriteTimeUtc(rootGame, DateTime.UtcNow.AddDays(-7));

        var sub = Path.Combine(_dir, "football_sim");
        Directory.CreateDirectory(sub);
        var req = Path.Combine(sub, "requirements.txt");
        File.WriteAllText(req, "pygame");
        File.SetLastWriteTimeUtc(req, DateTime.UtcNow);

        Assert.Equal(Path.GetFullPath(sub), ProjectRootDetector.Detect(_dir));
    }

    [Fact]
    public void Detector_IgnoresNoiseDirectories()
    {
        var noise = Path.Combine(_dir, "node_modules");
        Directory.CreateDirectory(noise);
        File.WriteAllText(Path.Combine(noise, "package.json"), "{}");
        Assert.Null(ProjectRootDetector.Detect(_dir));
    }

    // ---- AssignmentQualityGate --------------------------------------------

    [Fact]
    public async Task Gate_BuildIntent_EmptyWorkspace_HardFails()
    {
        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands, playtest: null, CancellationToken.None);
        Assert.False(f.Clean);
        Assert.True(f.HardFail);
        Assert.Contains("inget igenkännbart projekt", f.Report);
    }

    [Fact]
    public async Task Gate_BuildIntent_NothingWrittenDuringRun_HardFails()
    {
        // Transkriptet: 35-stegs deluppgift markerad "Klar" utan en enda fil.
        var game = Path.Combine(_dir, "index.html");
        File.WriteAllText(game, ValidGame);
        File.SetLastWriteTimeUtc(game, DateTime.UtcNow.AddHours(-2));

        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, runStartUtc: DateTime.UtcNow.AddMinutes(-5), NoCommands, playtest: null, CancellationToken.None);
        Assert.True(f.HardFail);
        Assert.Contains("inga filer skapades eller ändrades", f.Report);
    }

    [Fact]
    public async Task Gate_ValidHtml5Game_IsClean()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), ValidGame);
        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands,
            playtest: (_, _, _) => Task.FromResult((true, "ok", (IReadOnlyList<string>)[])), CancellationToken.None);
        Assert.True(f.Clean);
        Assert.False(f.HardFail);
        Assert.Equal("html5", f.Engine);
    }

    [Fact]
    public async Task Gate_SyntaxErrorInGame_HardFails_WithActionableReport()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"),
            "<!DOCTYPE html><html><body><script>function broken( {</script></body></html>");
        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands, playtest: null, CancellationToken.None);
        Assert.False(f.Clean);
        Assert.True(f.HardFail);
        Assert.Contains("VERIFY FAILED", f.Report);
    }

    [Fact]
    public async Task Gate_PlaytestPolishIssues_AreSoft_NotHardFail()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), ValidGame);
        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands,
            playtest: (_, _, _) => Task.FromResult((true, "spelbart", (IReadOnlyList<string>)["Ljudeffekter saknas"])), CancellationToken.None);
        Assert.False(f.Clean);
        Assert.False(f.HardFail);
        Assert.Contains("Ljudeffekter", f.Report);
    }

    [Fact]
    public async Task Gate_PlaytestCrash_HardFails()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), ValidGame);
        var f = await AssignmentQualityGate.InspectAsync(
            _dir, buildIntent: true, DateTime.UtcNow.AddMinutes(-1), NoCommands,
            playtest: (_, _, _) => Task.FromResult((false, "kraschade vid start", (IReadOnlyList<string>)["TypeError"])), CancellationToken.None);
        Assert.True(f.HardFail);
        Assert.Contains("Playtest misslyckades", f.Report);
    }

    [Fact]
    public void Gate_FixPrompt_TellsTheModelToRepairNotRestart()
    {
        var prompt = AssignmentQualityGate.FixPrompt(new QualityFindings(false, true, "fel X", _dir, "html5"));
        Assert.Contains("fel X", prompt);
        Assert.Contains("BEFINTLIGA", prompt);
        Assert.Contains("provision", prompt);
    }

    // ---- Scaffold på icke-tom root -> undermapp ---------------------------

    [Fact]
    public void GameScaffold_NonEmptyRoot_LandsInDerivedSubfolder()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), ValidGame);
        var r = new GameScaffoldService().Scaffold("auto", "bygg ett fotbolls managerspel", _dir);
        Assert.True(r.Success, r.Output);
        Assert.NotEqual(Path.GetFullPath(_dir), Path.GetFullPath(r.Path));
        Assert.StartsWith(Path.GetFullPath(_dir), Path.GetFullPath(r.Path));
        // auto = godot sedan v1.33.0 - undermappen ska vara ett riktigt projekt.
        Assert.True(File.Exists(Path.Combine(r.Path, "project.godot")));
    }

    [Fact]
    public void GameScaffold_SamePromptTwice_GetsSuffixedFolder()
    {
        File.WriteAllText(Path.Combine(_dir, "somefile.txt"), "x");
        var first = new GameScaffoldService().Scaffold("auto", "bygg ett rymdspel", _dir);
        var second = new GameScaffoldService().Scaffold("auto", "bygg ett rymdspel", _dir);
        Assert.True(first.Success && second.Success);
        Assert.NotEqual(first.Path, second.Path);
    }

    [Fact]
    public void AppScaffold_NonEmptyRoot_LandsInSubfolder()
    {
        File.WriteAllText(Path.Combine(_dir, "old.txt"), "x");
        var r = new AppScaffoldService().Scaffold("python", "skapa ett budgetverktyg", _dir);
        Assert.True(r.Success, r.Output);
        Assert.NotEqual(Path.GetFullPath(_dir), Path.GetFullPath(r.Path));
    }

    [Fact]
    public void ScaffoldSlug_DropsVerbsAndFillers()
    {
        Assert.Equal("plattformsspel", ScaffoldPaths.Slug("bygg ett 2d plattformsspel", "spel"));
        Assert.Equal("spel", ScaffoldPaths.Slug("", "spel"));
    }

    // ---- Intentluckan från transkriptet -----------------------------------

    [Theory]
    [InlineData("kan du göra en fil av det?")]
    [InlineData("kan du gora en fil av det?")]
    public void GoraEnFil_IsRecognizedAsBuildIntent(string prompt)
    {
        Assert.True(HostRole.HasBuildVerb(prompt));
        Assert.True(HostRole.RefersBack(prompt));
        Assert.True(HostRole.IsBuildRequest(prompt)); // "fil" är nu ett artefaktord
    }

    // ---- Coder-modellstegen -----------------------------------------------

    [Theory]
    [InlineData(8, "qwen2.5-coder:7b")]
    [InlineData(12, "qwen2.5-coder:14b")]
    [InlineData(24, "qwen2.5-coder:32b")]
    public void Recommender_PicksCoderModelsForVram(double vramGb, string expectedTag)
    {
        var hw = new HardwareProfile("test", 8, 32, "RTX", vramGb, CudaAvailable: true);
        Assert.Equal(expectedTag, ModelRecommender.Recommend(hw).OllamaTag);
    }
}
