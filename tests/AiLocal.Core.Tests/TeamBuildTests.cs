using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.31.0's team build: the architect's JSON parsing (with
/// deterministic fallback tracks), and the full git chain a team run rests on
/// (init -> worktree -> commit -> merge back). Git-dependent tests skip
/// silently on machines without git.</summary>
public class TeamBuildTests : IDisposable
{
    private readonly string _dir;

    public TeamBuildTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-team-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        // Worktree-metadata kan ligga kvar med readonly-flaggor - best effort.
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Arkitektens JSON --------------------------------------------------

    [Fact]
    public void ParseTracks_PlainJson_Works()
    {
        var tracks = TeamBuild.ParseTracks(
            """{"tracks":[{"title":"Ljud","description":"Lägg till ljud"},{"title":"Nivåer","description":"Fler nivåer"}]}""");
        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
        Assert.Equal("Ljud", tracks[0].Title);
    }

    [Fact]
    public void ParseTracks_FencedJsonWithProse_Works()
    {
        var tracks = TeamBuild.ParseTracks(
            "Här är uppdelningen:\n```json\n{\"tracks\":[{\"title\":\"A\",\"description\":\"a\"},{\"title\":\"B\",\"description\":\"b\"}]}\n```");
        Assert.NotNull(tracks);
        Assert.Equal(2, tracks.Count);
    }

    [Theory]
    [InlineData("jag kan tyvärr inte svara med json")]
    [InlineData("{\"tracks\":[{\"title\":\"Ensam\",\"description\":\"bara ett spår\"}]}")]
    [InlineData("{\"tracks\":\"fel typ\"}")]
    public void ParseTracks_GarbageOrTooFew_ReturnsNull(string content)
    {
        Assert.Null(TeamBuild.ParseTracks(content));
    }

    [Fact]
    public void FallbackTracks_GamePrompt_GetsGameTracks()
    {
        var tracks = TeamBuild.FallbackTracks("bygg ett plattformsspel", _dir);
        Assert.True(tracks.Count >= 3);
        Assert.Contains(tracks, t => t.Title.Contains("Ljud"));
    }

    [Fact]
    public void FallbackTracks_AppPrompt_GetsAppTracks()
    {
        var tracks = TeamBuild.FallbackTracks("bygg ett budgetverktyg", _dir);
        Assert.Contains(tracks, t => t.Title.Contains("Kärnfunktioner"));
        Assert.DoesNotContain(tracks, t => t.Title.Contains("Ljud"));
    }

    // ---- Git-kedjan --------------------------------------------------------

    [Fact]
    public async Task GitChain_InitWorktreeCommitMerge_LandsInMainRepo()
    {
        var git = new GitService();
        if (!await git.InitAsync(_dir, CancellationToken.None))
            return; // ingen git på maskinen - testet är meningslöst här

        await File.WriteAllTextAsync(Path.Combine(_dir, ".gitignore"), ".worktrees/\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"), "<!DOCTYPE html><html></html>");
        var baseline = await git.CommitAsync(_dir, "baslinje", CancellationToken.None);
        Assert.True(baseline.Success, baseline.Output);

        var isolation = new GitIsolationService(git);
        var task = await isolation.CreateAsync(_dir, "Ljudspår", ct: CancellationToken.None);
        Assert.NotNull(task);

        await File.WriteAllTextAsync(Path.Combine(task.WorktreePath, "sound.js"), "const sfx = 1;");
        var commit = await isolation.CommitAsync(task.TaskId, "team: ljud", CancellationToken.None);
        Assert.True(commit.Success, commit.Output);

        var (merged, output) = await isolation.MergeAsync(task.TaskId, CancellationToken.None);
        Assert.True(merged, output);
        Assert.True(File.Exists(Path.Combine(_dir, "sound.js")), "mergad fil saknas i huvudrepot");
    }

    [Fact]
    public async Task GitChain_ConflictingWorktrees_SecondMergeFails_AndAbortRecovers()
    {
        var git = new GitService();
        if (!await git.InitAsync(_dir, CancellationToken.None))
            return; // ingen git på maskinen

        // Spegla TeamBuild.EnsureGitignore: utan .worktrees/ i .gitignore
        // förorenar worktree-mapparna huvudrepots status för alltid.
        await File.WriteAllTextAsync(Path.Combine(_dir, ".gitignore"), ".worktrees/\n");
        await File.WriteAllTextAsync(Path.Combine(_dir, "app.js"), "const version = 0;");
        await git.CommitAsync(_dir, "baslinje", CancellationToken.None);

        var isolation = new GitIsolationService(git);
        var a = await isolation.CreateAsync(_dir, "spår A", ct: CancellationToken.None);
        var b = await isolation.CreateAsync(_dir, "spår B", ct: CancellationToken.None);
        Assert.NotNull(a);
        Assert.NotNull(b);

        // Båda ändrar SAMMA rad - garanterad konflikt vid andra mergen.
        await File.WriteAllTextAsync(Path.Combine(a.WorktreePath, "app.js"), "const version = 1;");
        await isolation.CommitAsync(a.TaskId, "A", CancellationToken.None);
        await File.WriteAllTextAsync(Path.Combine(b.WorktreePath, "app.js"), "const version = 2;");
        await isolation.CommitAsync(b.TaskId, "B", CancellationToken.None);

        var (mergedA, outA) = await isolation.MergeAsync(a.TaskId, CancellationToken.None);
        Assert.True(mergedA, outA);
        var (mergedB, outB) = await isolation.MergeAsync(b.TaskId, CancellationToken.None);
        Assert.False(mergedB, $"merge B borde ha konfliktat men gav: {outB} | app.js: {await File.ReadAllTextAsync(Path.Combine(_dir, "app.js"))}");

        // Abort ska återställa repot så att det går att committa vidare -
        // exakt vad redo-på-toppen-flödet kräver.
        await git.AbortMergeAsync(_dir, CancellationToken.None);
        Assert.False(await git.HasUncommittedChangesAsync(_dir, CancellationToken.None));
        Assert.Contains("version = 1", await File.ReadAllTextAsync(Path.Combine(_dir, "app.js")));
    }
}
