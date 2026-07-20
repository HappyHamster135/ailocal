using System.Diagnostics;

namespace AiLocal.Node.Hosting;

/// <summary>One entry from `git status --porcelain -b`. IndexState/WorkTreeState
/// are git's own single-character codes (M/A/D/R/C/U/space) - surfaced as-is
/// rather than translated, since the operator-facing UI already labels them.</summary>
public sealed record GitFileStatus(string Path, string IndexState, string WorkTreeState);

/// <summary>IsRepo is false (with every other field empty/default) for a
/// folder that isn't a git repository at all - not an error, just a normal,
/// common state for a session's folder.</summary>
public sealed record GitStatus(
    bool IsRepo,
    string? Branch,
    int Ahead,
    int Behind,
    IReadOnlyList<GitFileStatus> Staged,
    IReadOnlyList<GitFileStatus> Unstaged,
    IReadOnlyList<string> Untracked);

public sealed record GitCommitResult(bool Success, string Output);

/// <summary>
/// Shells out to the `git` CLI, scoped to a single folder (a session's
/// FolderPath) - no persisted state, no library dependency (no LibGit2Sharp
/// etc.), same "start simple" spirit as write_file being whole-file rather
/// than patch-based. Process-shelling conventions mirror
/// AgentToolExecutor.RunCommandAsync: fixed argv via ArgumentList (never a
/// raw shell string - there's no user-supplied command text here, unlike
/// run_command), redirected stdio, a linked timeout CancellationTokenSource,
/// exit code always checked. Never touches a remote (no push/pull/fetch) -
/// see GitApi's doc comment for why that's a deliberate v1 boundary, not an
/// oversight.
/// </summary>
public class GitService
{
    private static readonly TimeSpan GitTimeout = TimeSpan.FromSeconds(30);
    private const int MaxDiffChars = 100_000;

    public virtual async Task<bool> IsRepoAsync(string folderPath, CancellationToken ct = default)
    {
        var (exitCode, _, _) = await RunGitAsync(folderPath, ["rev-parse", "--is-inside-work-tree"], ct);
        return exitCode == 0;
    }

    public virtual async Task<GitStatus> GetStatusAsync(string folderPath, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(folderPath, ct))
            return new GitStatus(false, null, 0, 0, [], [], []);

        var (exitCode, stdout, _) = await RunGitAsync(folderPath, ["status", "--porcelain=v1", "-b"], ct);
        return exitCode == 0 ? ParseStatus(stdout) : new GitStatus(true, null, 0, 0, [], [], []);
    }

    public virtual async Task<string> GetDiffAsync(string folderPath, bool staged, CancellationToken ct = default)
    {
        string[] args = staged ? ["diff", "--staged"] : ["diff"];
        var (exitCode, stdout, stderr) = await RunGitAsync(folderPath, args, ct);
        if (exitCode != 0)
            return string.IsNullOrWhiteSpace(stderr) ? "(kunde inte läsa diff)" : stderr;
        return stdout.Length > MaxDiffChars
            ? stdout[..MaxDiffChars] + $"\n...(trunkerad, {stdout.Length} tecken totalt)"
            : stdout;
    }

    /// <summary>Stages everything then commits - no per-file selection in v1,
    /// same reasoning as GitStatus not needing one: this mirrors write_file
    /// being whole-file rather than patch-based, not a limitation anyone's
    /// asked to lift yet.</summary>
    public virtual async Task<GitCommitResult> CommitAsync(string folderPath, string message, CancellationToken ct = default)
    {
        var (addExit, _, addErr) = await RunGitAsync(folderPath, ["add", "-A"], ct);
        if (addExit != 0)
            return new GitCommitResult(false, string.IsNullOrWhiteSpace(addErr) ? "git add misslyckades" : addErr);

        var (commitExit, commitOut, commitErr) = await RunGitAsync(folderPath, ["commit", "-m", message], ct);
        // "nothing to commit" and similar informational outcomes land on
        // stdout, real failures usually on stderr - prefer whichever stream
        // actually has content so the message is never silently empty.
        var output = string.IsNullOrWhiteSpace(commitOut) ? commitErr : commitOut;
        return new GitCommitResult(commitExit == 0, output);
    }

    /// <summary>True when the repo has staged/unstaged/untracked changes the
    /// next commit would capture - used to decide whether an isolated task
    /// actually produced anything worth merging.</summary>
    public virtual async Task<bool> HasUncommittedChangesAsync(string folderPath, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(folderPath, ct)) return false;
        var (exitCode, stdout, _) = await RunGitAsync(folderPath, ["status", "--porcelain"], ct);
        return exitCode == 0 && !string.IsNullOrWhiteSpace(stdout);
    }

    public virtual async Task<string?> GetCurrentBranchAsync(string folderPath, CancellationToken ct = default)
    {
        var (exitCode, stdout, _) = await RunGitAsync(folderPath, ["rev-parse", "--abbrev-ref", "HEAD"], ct);
        return exitCode == 0 ? stdout.Trim() : null;
    }

    /// <summary>Creates a linked worktree at <paramref name="worktreePath"/>
    /// on a new branch <paramref name="branchName"/>, branched from the
    /// optional <paramref name="baseRef"/> (defaults to the current HEAD).
    /// Worktrees give true filesystem isolation: two tasks get two real
    /// directories and two real branches, so they can never overwrite each
    /// other's files - the keystone of multi-agent (many "employees") work on
    /// one repo.</summary>
    public virtual async Task<bool> CreateWorktreeAsync(
        string repoPath, string worktreePath, string branchName, string? baseRef = null, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(repoPath, ct)) return false;
        var args = new List<string> { "worktree", "add", "-b", branchName, worktreePath };
        if (!string.IsNullOrWhiteSpace(baseRef)) args.Add(baseRef!);
        var (exitCode, _, stderr) = await RunGitAsync(repoPath, args, ct);
        if (exitCode != 0 && stderr.Contains("already exists"))
            return false;
        return exitCode == 0;
    }

    /// <summary>Merges <paramref name="branchName"/> (from its worktree) into
    /// the worktree's base branch, fast-forward when possible. The merge
    /// happens in the worktree's own directory so the branch ref is resolved
    /// naturally; the caller then discards the worktree.</summary>
    /// <summary>Checks out <paramref name="branch"/> in <paramref name="repoPath"/>
    /// (the main repo, not a worktree). Used before merging a task branch back
    /// into its base - the caller ensures repoPath is left on the base
    /// branch, so the subsequent merge lands in the right place.</summary>
    public virtual async Task<(bool Success, string Output)> CheckoutAsync(
        string repoPath, string branch, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(repoPath, ct))
            return (false, "not a git repo");
        var (exitCode, stdout, stderr) = await RunGitAsync(repoPath, ["checkout", branch], ct);
        var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        return (exitCode == 0, output);
    }

    /// <summary>Merges <paramref name="branchName"/> into the repo's currently
    /// checked-out branch (which the caller has switched to the task's base via
    /// <see cref="CheckoutAsync"/> first). Runs against the main repo, not the
    /// task worktree, so the change lands on the base branch's real history.</summary>
    public virtual async Task<(bool Success, string Output)> MergeAsync(
        string repoPath, string branchName, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(repoPath, ct))
            return (false, "not a git repo");

        var (exitCode, stdout, stderr) = await RunGitAsync(repoPath,
            ["merge", "--no-ff", "-m", $"Merge task branch {branchName}", branchName], ct);
        var output = string.IsNullOrWhiteSpace(stdout) ? stderr : stdout;
        return (exitCode == 0, output);
    }

    /// <summary>Removes a worktree (both its directory and the branch it
    /// created). Force-clears any stray lock files first so a discarded task
    /// never leaves a dangling worktree behind.</summary>
    public virtual async Task<bool> RemoveWorktreeAsync(
        string repoPath, string worktreePath, string branchName, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(repoPath, ct)) return false;
        // Best-effort: prune stale locks then remove the worktree.
        await RunGitAsync(repoPath, ["worktree", "prune"], ct);
        var (exitCode, _, stderr) = await RunGitAsync(repoPath,
            ["worktree", "remove", "--force", worktreePath], ct);
        // The branch may still be needed by the caller post-merge; only the
        // worktree removal is forced. Branch cleanup is the caller's choice.
        return exitCode == 0 || stderr.Contains("not a working tree");
    }

    /// <summary>Deletes a branch by name (after it's been merged or discarded).</summary>
    public virtual async Task<bool> DeleteBranchAsync(string repoPath, string branchName, CancellationToken ct = default)
    {
        if (!await IsRepoAsync(repoPath, ct)) return false;
        var (exitCode, _, _) = await RunGitAsync(repoPath, ["branch", "-D", branchName], ct);
        return exitCode == 0;
    }

    private static GitStatus ParseStatus(string stdout)
    {
        string? branch = null;
        var ahead = 0;
        var behind = 0;
        var staged = new List<GitFileStatus>();
        var unstaged = new List<GitFileStatus>();
        var untracked = new List<string>();

        foreach (var rawLine in stdout.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var header = line[3..];
                var bracketIndex = header.IndexOf('[');
                if (bracketIndex >= 0)
                {
                    var closeIndex = header.IndexOf(']', bracketIndex);
                    var trackingInfo = closeIndex > bracketIndex
                        ? header[(bracketIndex + 1)..closeIndex]
                        : header[(bracketIndex + 1)..];
                    foreach (var part in trackingInfo.Split(',', StringSplitOptions.TrimEntries))
                    {
                        if (part.StartsWith("ahead ", StringComparison.Ordinal))
                            int.TryParse(part["ahead ".Length..], out ahead);
                        else if (part.StartsWith("behind ", StringComparison.Ordinal))
                            int.TryParse(part["behind ".Length..], out behind);
                    }
                    header = header[..bracketIndex].TrimEnd();
                }

                var dotsIndex = header.IndexOf("...", StringComparison.Ordinal);
                branch = dotsIndex >= 0 ? header[..dotsIndex] : header;
                const string noCommitsPrefix = "No commits yet on ";
                if (branch.StartsWith(noCommitsPrefix, StringComparison.Ordinal))
                    branch = branch[noCommitsPrefix.Length..];
                continue;
            }

            if (line.Length < 3) continue;
            var indexState = line[0];
            var workTreeState = line[1];
            var path = line[3..];

            if (indexState == '?' && workTreeState == '?')
            {
                untracked.Add(path);
                continue;
            }
            if (indexState != ' ')
                staged.Add(new GitFileStatus(path, indexState.ToString(), workTreeState.ToString()));
            if (workTreeState != ' ')
                unstaged.Add(new GitFileStatus(path, indexState.ToString(), workTreeState.ToString()));
        }

        return new GitStatus(true, branch, ahead, behind, staged, unstaged, untracked);
    }

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunGitAsync(
        string workingDirectory, IReadOnlyList<string> args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            // ToolLocator: absolut sökväg när git finns provisionerad/
            // installerad men inte på PATH - annars bara "git" som förut.
            FileName = AiLocal.Core.Agent.ToolLocator.Find("git") ?? "git",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        Process? process;
        try { process = Process.Start(psi); }
        catch (Exception ex) { return (-1, "", ex.Message); }
        if (process is null) return (-1, "", "failed to start git");

        using (process)
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);

            using var timeoutCts = new CancellationTokenSource(GitTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return (-1, "", $"git command timed out after {GitTimeout.TotalSeconds:0}s");
            }

            string stdout, stderr;
            try { stdout = await outputTask; } catch { stdout = ""; }
            try { stderr = await errorTask; } catch { stderr = ""; }
            return (process.ExitCode, stdout, stderr);
        }
    }
}
