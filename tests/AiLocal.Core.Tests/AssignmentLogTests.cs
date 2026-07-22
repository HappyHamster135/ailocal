using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.30.0's persistent assignment history: steps survive a
/// reload/relaunch, stale Running entries are marked Failed on restart, and
/// caps keep the file bounded.</summary>
public class AssignmentLogTests : IDisposable
{
    private readonly string _dir;
    private string LogPath => Path.Combine(_dir, "assignment-log.json");

    public AssignmentLogTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-asglog-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void AterupptagningsData_OverleverNodomstart()
    {
        // v1.87 (C5+): kärnfallet - noden dör MITT i bygget. Projektmappen
        // sattes så fort den var känd (SetProject), så omstartsmarkeringen
        // (Running -> Failed) behåller den och Återuppta-knappen kan visas.
        var log = new AssignmentLog(LogPath);
        var entry = log.Begin("bygg ett plattformsspel", "NOD-A");
        log.SetProject(entry, "pixel-rush");
        // ingen Complete - noden "dör" här; ny instans = omstart

        var reloaded = new AssignmentLog(LogPath);
        var e = Assert.Single(reloaded.Snapshot());
        Assert.Equal("Failed", e.State);            // ärlig omstartsmarkering
        Assert.Equal("pixel-rush", e.ProjectRel);   // återupptagningsdatan kvar
    }

    [Fact]
    public void Complete_MedProjectRel_SkriverOchSnapshotBevarar()
    {
        var log = new AssignmentLog(LogPath);
        var entry = log.Begin("bygg ett spel", "NOD-A");
        log.Complete(entry, success: false, "grinden underkände", null, projectRel: "spelet");
        Assert.Equal("spelet", Assert.Single(log.Snapshot()).ProjectRel);
    }

    [Theory]
    [InlineData("sub/spel", "sub/spel")]     // undermapp -> rel
    [InlineData(".", ".")]                   // projektet I roten
    [InlineData(null, null)]                 // okänd rot -> null
    public void SafeProjectRel_InomArbetsytan(string? sub, string? expected)
    {
        var root = _dir;
        var project = sub is null ? null : Path.GetFullPath(Path.Combine(root, sub));
        Assert.Equal(expected, AiLocal.Node.Roles.WorkerRole.SafeProjectRel(root, project));
    }

    [Fact]
    public void SafeProjectRel_UtanforArbetsytan_GerNull()
    {
        // En logg-post får aldrig peka utåt - traversal-vakten.
        Assert.Null(AiLocal.Node.Roles.WorkerRole.SafeProjectRel(_dir, Path.GetTempPath()));
    }

    [Fact]
    public void BeginStepComplete_RoundTrips_NewestFirst()
    {
        var log = new AssignmentLog(LogPath);
        var first = log.Begin("bygg ett spel", "NOD-A");
        log.AddStep(first, "thinking", "planerar");
        log.AddStep(first, "tool_call", "write_file index.html");
        log.Complete(first, success: true, "Klart!", "/api/preview/index.html");
        log.Begin("nästa uppdrag", "NOD-A");

        var snapshot = log.Snapshot();
        Assert.Equal(2, snapshot.Count);
        Assert.Equal("nästa uppdrag", snapshot[0].Prompt); // nyast först
        var done = snapshot[1];
        Assert.Equal("Completed", done.State);
        Assert.Equal(2, done.Steps.Count);
        Assert.Equal("tool_call", done.Steps[1].Kind);
        Assert.Equal("/api/preview/index.html", done.PreviewPath);
    }

    [Fact]
    public void Reload_MarksStaleRunningEntriesAsFailed()
    {
        var log = new AssignmentLog(LogPath);
        log.Begin("kraschade mitt i", "NOD-A"); // aldrig Complete - noden "dog"

        var reloaded = new AssignmentLog(LogPath);
        var entry = Assert.Single(reloaded.Snapshot());
        Assert.Equal("Failed", entry.State);
        Assert.Contains("startades om", entry.FinalAnswer);
        Assert.Equal(0, reloaded.RunningCount);
    }

    [Fact]
    public void RunningCount_TracksOpenEntries()
    {
        var log = new AssignmentLog(LogPath);
        var entry = log.Begin("jobb", null);
        Assert.Equal(1, log.RunningCount);
        log.Complete(entry, success: false, "avbruten", null);
        Assert.Equal(0, log.RunningCount);
    }

    [Fact]
    public void Caps_LimitStepsAndDetailLength()
    {
        var log = new AssignmentLog(LogPath);
        var entry = log.Begin("jobb", null);
        for (var i = 0; i < 500; i++)
            log.AddStep(entry, "thinking", new string('x', 10_000));

        var stored = log.Snapshot()[0];
        Assert.True(stored.Steps.Count <= 400, $"steg: {stored.Steps.Count}");
        Assert.True(stored.Steps[0].Detail.Length <= 4001, $"detalj: {stored.Steps[0].Detail.Length}");
    }

    [Fact]
    public void Snapshot_IsADeepEnoughCopy()
    {
        var log = new AssignmentLog(LogPath);
        var entry = log.Begin("jobb", null);
        var snapshot = log.Snapshot();
        log.AddStep(entry, "thinking", "efteråt");
        Assert.Empty(snapshot[0].Steps); // ögonblicksbilden muteras inte i efterhand
    }
}
