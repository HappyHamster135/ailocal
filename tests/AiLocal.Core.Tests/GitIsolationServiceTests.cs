using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>A GitService whose git-CLI calls are stubbed, so GitIsolationService
/// can be tested without a real repo on disk.</summary>
internal sealed class FakeGitService : GitService
{
    public bool IsRepo { get; set; } = true;
    public string CurrentBranch { get; set; } = "main";
    public bool WorktreeCreated { get; set; }
    public bool HasChanges { get; set; } = true;
    public string DiffText { get; set; } = "diff --git a/x b/x\n+added";
    public bool MergeSucceeds { get; set; } = true;
    public bool WorktreeRemoved { get; set; }
    public bool BranchDeleted { get; set; }

    public override Task<bool> IsRepoAsync(string folderPath, CancellationToken ct = default)
        => Task.FromResult(IsRepo);

    public override Task<string?> GetCurrentBranchAsync(string folderPath, CancellationToken ct = default)
        => Task.FromResult<string?>(CurrentBranch);

    public override Task<bool> CreateWorktreeAsync(
        string repoPath, string worktreePath, string branchName, string? baseRef = null, CancellationToken ct = default)
    {
        WorktreeCreated = true;
        return Task.FromResult(true);
    }

    public override Task<bool> HasUncommittedChangesAsync(string folderPath, CancellationToken ct = default)
        => Task.FromResult(HasChanges);

    public override Task<GitCommitResult> CommitAsync(string folderPath, string message, CancellationToken ct = default)
        => Task.FromResult(new GitCommitResult(true, "committed"));

    public override Task<string> GetDiffAsync(string folderPath, bool staged, CancellationToken ct = default)
        => Task.FromResult(DiffText);

    public override Task<(bool Success, string Output)> MergeAsync(string repoPath, string branchName, CancellationToken ct = default)
        => Task.FromResult((MergeSucceeds, MergeSucceeds ? "merged" : "conflict"));

    public override Task<(bool Success, string Output)> CheckoutAsync(string repoPath, string branch, CancellationToken ct = default)
        => Task.FromResult((true, "checked out"));

    public override Task<bool> RemoveWorktreeAsync(string repoPath, string worktreePath, string branchName, CancellationToken ct = default)
    {
        WorktreeRemoved = true;
        return Task.FromResult(true);
    }

    public override Task<bool> DeleteBranchAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        BranchDeleted = true;
        return Task.FromResult(true);
    }
}

public class GitIsolationServiceTests
{
    [Fact]
    public async Task Create_RequiresRepo()
    {
        var git = new FakeGitService { IsRepo = false };
        var svc = new GitIsolationService(git);
        Assert.Null(await svc.CreateAsync("C:/repo", "task", null, CancellationToken.None));
    }

    [Fact]
    public async Task Create_MakesWorktreeOnBranch()
    {
        var git = new FakeGitService();
        var svc = new GitIsolationService(git);
        var task = await svc.CreateAsync("C:/repo", "my task", null, CancellationToken.None);
        Assert.NotNull(task);
        Assert.True(git.WorktreeCreated);
        Assert.StartsWith("ailocal/task-", task!.BranchName);
        Assert.Contains(task, svc.ListActive());
    }

    [Fact]
    public async Task Commit_NoChanges_ReturnsFailure()
    {
        var git = new FakeGitService { HasChanges = false };
        var svc = new GitIsolationService(git);
        var task = await svc.CreateAsync("C:/repo", "t", null, CancellationToken.None);
        var result = await svc.CommitAsync(task!.TaskId, "msg", CancellationToken.None);
        Assert.False(result.Success);
    }

    [Fact]
    public async Task Merge_Success_RemovesWorktreeAndKeepsTaskGone()
    {
        var git = new FakeGitService { MergeSucceeds = true };
        var svc = new GitIsolationService(git);
        var task = await svc.CreateAsync("C:/repo", "t", null, CancellationToken.None);
        var (success, _) = await svc.MergeAsync(task!.TaskId, CancellationToken.None);
        Assert.True(success);
        Assert.True(git.WorktreeRemoved);
        Assert.DoesNotContain(task, svc.ListActive());
    }

    [Fact]
    public async Task Discard_RemovesWorktreeAndBranch()
    {
        var git = new FakeGitService();
        var svc = new GitIsolationService(git);
        var task = await svc.CreateAsync("C:/repo", "t", null, CancellationToken.None);
        await svc.DiscardAsync(task!.TaskId, CancellationToken.None);
        Assert.True(git.WorktreeRemoved);
        Assert.True(git.BranchDeleted);
        Assert.DoesNotContain(task, svc.ListActive());
    }

    [Fact]
    public async Task Merge_Failure_KeepsTaskActive()
    {
        var git = new FakeGitService { MergeSucceeds = false };
        var svc = new GitIsolationService(git);
        var task = await svc.CreateAsync("C:/repo", "t", null, CancellationToken.None);
        var (success, _) = await svc.MergeAsync(task!.TaskId, CancellationToken.None);
        Assert.False(success);
        Assert.Contains(task, svc.ListActive());
    }
}
