using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>One isolated task: its own git worktree + branch, forked from a
/// base branch of the Worker's repo. The agent works ONLY inside the
/// worktree, so two tasks on the same repo can never touch each other's
/// files. After the agent finishes, the diff is reviewable as a PR and either
/// merged (fast-forward into the base) or discarded (worktree + branch
/// deleted) - the discard is the free "undo" button for a bad agent run.</summary>
public sealed record IsolatedTask(
    string TaskId,
    string BranchName,
    string BaseBranch,
    string WorktreePath,
    string RepoPath,
    string Title,
    DateTime CreatedAt);

/// <summary>
/// Per-task git isolation for the multi-agent ("many employees") model. Lives
/// on the Worker node, because the repo/workspace physically lives there.
///
/// Flow:
///  1. CreateAsync  - git worktree add -b &lt;task-branch&gt; &lt;worktree&gt; &lt;base&gt;
///  2. Agent runs its assignment inside WorktreePath (WorkerRole points the
///     executor at it via AssignmentRequest.WorkspaceOverride).
///  3. CommitAsync  - stage+commit whatever the agent produced in the worktree.
///  4. DiffAsync    - the resulting change, surfaced for AI/human PR review.
///  5. MergeAsync   - merge the task branch into its base, then discard.
///     DiscardAsync - delete worktree + branch, no merge (the undo button).
///
/// Only applies when the Worker's workspace is a git repo; otherwise the
/// Worker silently runs the old way (no isolation, single shared folder) so a
/// non-repo workspace is never broken by this feature.
/// </summary>
public class GitIsolationService
{
    private readonly GitService _git;
    private readonly ConcurrentDictionary<string, IsolatedTask> _active = new();

    public GitIsolationService(GitService git) => _git = git;

    /// <summary>True when <paramref name="repoPath"/> is a git repo that can be
    /// isolated. Non-repos simply opt out of isolation everywhere it's used.</summary>
    public async Task<bool> CanIsolateAsync(string repoPath, CancellationToken ct = default) =>
        await _git.IsRepoAsync(repoPath, ct);

    public async Task<IsolatedTask?> CreateAsync(
        string repoPath, string title, string? baseBranch = null, CancellationToken ct = default)
    {
        if (!await _git.IsRepoAsync(repoPath, ct))
            return null;

        baseBranch ??= await _git.GetCurrentBranchAsync(repoPath, ct) ?? "main";
        var taskId = Guid.NewGuid().ToString("n")[..8];
        var branchName = $"ailocal/task-{taskId}";
        // Worktrees live in a sibling ".worktrees/" folder of the repo so they
        // never collide with the repo's own tracked files or submodules.
        var worktreesDir = Path.Combine(repoPath, ".worktrees");
        Directory.CreateDirectory(worktreesDir);
        var worktreePath = Path.Combine(worktreesDir, taskId);

        var ok = await _git.CreateWorktreeAsync(repoPath, worktreePath, branchName, baseBranch, ct);
        if (!ok)
            return null;

        var task = new IsolatedTask(taskId, branchName, baseBranch, worktreePath, repoPath, title, DateTime.UtcNow);
        _active[taskId] = task;
        return task;
    }

    public IReadOnlyCollection<IsolatedTask> ListActive() => _active.Values.ToArray();

    public IsolatedTask? Get(string taskId) => _active.GetValueOrDefault(taskId);

    /// <summary>Commits everything the agent staged in the worktree. Returns
    /// false (and the task stays active) when there's nothing to commit.</summary>
    public async Task<GitCommitResult> CommitAsync(string taskId, string message, CancellationToken ct = default)
    {
        var task = Get(taskId);
        if (task is null)
            return new GitCommitResult(false, "unknown isolated task");
        if (!await _git.HasUncommittedChangesAsync(task.WorktreePath, ct))
            return new GitCommitResult(false, "no changes to commit");
        return await _git.CommitAsync(task.WorktreePath, message, ct);
    }

    public async Task<string> DiffAsync(string taskId, CancellationToken ct = default)
    {
        var task = Get(taskId);
        if (task is null) return "(unknown isolated task)";
        return await _git.GetDiffAsync(task.WorktreePath, staged: false, ct);
    }

    /// <summary>Merges the task branch into its base (no-fast-forward so the
    /// work shows up as a real merge commit), then discards the worktree.
    /// The branch itself is kept after merge (it's now part of history); call
    /// DeleteBranchAsync if you want it gone too.</summary>
    public async Task<(bool Success, string Output)> MergeAsync(string taskId, CancellationToken ct = default)
    {
        var task = Get(taskId);
        if (task is null)
            return (false, "unknown isolated task");

        // Switch the main repo onto the task's base branch, then merge the
        // task branch into it. This lands the change on the base branch's real
        // history (not inside the worktree), which is the whole point of
        // isolation: the main repo is now updated, the worktree can go.
        var (checkoutOk, checkoutOut) = await _git.CheckoutAsync(task.RepoPath, task.BaseBranch, ct);
        if (!checkoutOk)
            return (false, $"could not checkout base branch {task.BaseBranch}: {checkoutOut}");
        var (success, output) = await _git.MergeAsync(task.RepoPath, task.BranchName, ct);
        if (success)
        {
            // Base branch now carries the change. Drop the worktree, keep the
            // task's branch ref in history.
            await DiscardWorktreeOnlyAsync(task, ct);
            _active.TryRemove(taskId, out _);
        }
        return (success, output);
    }

    /// <summary>Builds (and tests, if detected) the worktree of an isolated task.
    /// Discovers the build system by looking for well-known project files in the
    /// worktree root, then runs the appropriate build command. Returns
    /// (Success=true, output) on a clean build, or (Success=false, output) on
    /// failure. If no known build system is found, passes through without
    /// blocking — the gate is only meaningful for projects that actually build.
    /// Timeout: 5 minutes.</summary>
    public async Task<(bool Success, string Output)> RunCiGateAsync(string taskId, CancellationToken ct = default)
    {
        var task = Get(taskId);
        if (task is null)
            return (false, "unknown isolated task");

        var cmd = DetectBuildCommand(task.WorktreePath);
        if (cmd is null)
            return (true, "no build system detected - skipping gate");

        var (exitCode, output) = await RunBuildProcessAsync(cmd, task.WorktreePath, ct);
        return (exitCode == 0, output);
    }

    /// <summary>Describes a build executable and its arguments.</summary>
    public sealed record BuildCommand(string FileName, IReadOnlyList<string> Arguments);

    /// <summary>Detects which build system to use based on files present in the
    /// worktree root. Returns null if no known build system is found or the
    /// directory doesn't exist.</summary>
    private static BuildCommand? DetectBuildCommand(string worktreePath)
    {
        if (!Directory.Exists(worktreePath))
            return null;
        if (Directory.GetFiles(worktreePath, "*.sln").Length > 0 ||
            Directory.GetFiles(worktreePath, "*.csproj").Length > 0)
        {
            // .NET project — build is the minimum gate; if tests exist, run
            // them too. A single dotnet test command covers both implicitly
            // (it builds first), so we use that when test projects are present.
            var hasTests = Directory.GetFiles(worktreePath, "*Tests.csproj").Length > 0
                || Directory.GetFiles(worktreePath, "*Test.csproj").Length > 0
                || Directory.Exists(Path.Combine(worktreePath, "tests"));
            return new BuildCommand("dotnet", hasTests ? ["test"] : ["build"]);
        }
        if (File.Exists(Path.Combine(worktreePath, "package.json")))
        {
            // npm project — prefer the build script; fall back to install+build.
            return new BuildCommand("npm", ["run", "build"]);
        }
        if (File.Exists(Path.Combine(worktreePath, "Cargo.toml")))
        {
            return new BuildCommand("cargo", ["build"]);
        }
        if (File.Exists(Path.Combine(worktreePath, "go.mod")))
        {
            return new BuildCommand("go", ["build", "./..."]);
        }
        return null;
    }

    /// <summary>Runs a build command in the worktree directory with a 5-minute
    /// timeout. Protected virtual so tests can override it without needing a
    /// real build toolchain.</summary>
    protected virtual async Task<(int ExitCode, string Output)> RunBuildProcessAsync(
        BuildCommand cmd, string workingDirectory, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = cmd.FileName,
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in cmd.Arguments)
            psi.ArgumentList.Add(arg);

        using var process = new Process { StartInfo = psi };
        var output = new StringBuilder();

        process.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            try { process.Kill(true); } catch { /* best effort */ }
            return (137, "CI timed out after 5 min");
        }

        return (process.ExitCode, output.ToString().TrimEnd());
    }

    /// <summary>The undo button: delete worktree + branch, throwing away the
    /// agent's changes entirely. Safe to call after a bad run.</summary>
    public async Task DiscardAsync(string taskId, CancellationToken ct = default)
    {
        var task = Get(taskId);
        if (task is null) return;
        await DiscardWorktreeOnlyAsync(task, ct);
        await _git.DeleteBranchAsync(task.RepoPath, task.BranchName, ct);
        _active.TryRemove(taskId, out _);
    }

    private async Task DiscardWorktreeOnlyAsync(IsolatedTask task, CancellationToken ct)
    {
        await _git.RemoveWorktreeAsync(task.RepoPath, task.WorktreePath, task.BranchName, ct);
        try { if (Directory.Exists(task.WorktreePath)) Directory.Delete(task.WorktreePath, recursive: true); }
        catch { /* best effort - git worktree remove already cleared it */ }
    }
}
