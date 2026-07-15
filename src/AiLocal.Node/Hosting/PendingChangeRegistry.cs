using System.Collections.Concurrent;

namespace AiLocal.Node.Hosting;

/// <summary>A single file-write the agent wants to perform, surfaced to the
/// operator for review before it touches disk. OldContent is null for a
/// brand-new file. Both paths are absolute (already resolved by the
/// executor).</summary>
public sealed record PendingChange(
    string SessionId,
    string Path,
    string? OldContent,
    string NewContent)
{
    public string Diff => LineDiff.Compute(OldContent ?? "", NewContent);
}

/// <summary>Operator's decision on a pending change. Approve=false rejects it
/// (the executor returns a tool error so the agent can adapt).</summary>
public sealed record ChangeDecision(bool Approve, string? Reason = null);

/// <summary>
/// In-memory gate that lets the operator preview-and-approve every file the
/// agent wants to write, instead of the agent writing blindly. Mirrors
/// SessionRunRegistry's "transient run state lives outside the durable
/// SessionStore" rule: a TaskCompletionSource can't be serialized, so this
/// stays here, not in the persisted session model.
///
/// Flow: <see cref="AgentToolExecutor"/> calls <see cref="RequestAsync"/>,
/// which stores the proposal and blocks on a TaskCompletionSource. The
/// dashboard polls <see cref="TryTake"/> (or GET /api/sessions/{id}/pending-change)
/// to show the diff, then POSTs to /approve-change, which resolves the source
/// and unblocks the executor. A cancelled run auto-rejects via
/// <see cref="RejectAllForSession"/> so a paused write can't hang forever.
/// </summary>
public sealed class PendingChangeRegistry
{
    private sealed record Entry(string SessionId, PendingChange Change, TaskCompletionSource<ChangeDecision> Source);

    private readonly ConcurrentDictionary<string, Entry> _pending = new();

    /// <summary>Blocks until the operator decides (or the session's run is
    /// cancelled, which trips ct). Returns the decision.</summary>
    public async Task<ChangeDecision> RequestAsync(string sessionId, PendingChange change, CancellationToken ct)
    {
        var source = new TaskCompletionSource<ChangeDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new Entry(sessionId, change, source);
        _pending[sessionId] = entry;

        using var reg = ct.Register(() => source.TrySetResult(new ChangeDecision(false, "Run cancelled.")));
        try
        {
            return await source.Task;
        }
        finally
        {
            _pending.TryRemove(sessionId, out _);
        }
    }

    /// <summary>Returns the pending change for a session if one is waiting,
    /// without consuming it (the dashboard reads the diff, then calls
    /// <see cref="Resolve"/>). Null if none pending.</summary>
    public PendingChange? Peek(string sessionId) =>
        _pending.TryGetValue(sessionId, out var entry) ? entry.Change : null;

    /// <summary>Resolves a waiting change with the operator's decision.
    /// Returns false if there was nothing pending (already answered /
    /// timed out / cancelled).</summary>
    public bool Resolve(string sessionId, ChangeDecision decision)
    {
        if (!_pending.TryGetValue(sessionId, out var entry))
            return false;
        // Only the exact entry may resolve it - a late Remove shouldn't let a
        // stale decision leak into a newer pending change for the same session.
        if (_pending.TryRemove(sessionId, out _))
            return entry.Source.TrySetResult(decision);
        return false;
    }

    /// <summary>Auto-rejects any pending change for a session whose run was
    /// cancelled, so WriteFileAsync unblocks instead of hanging.</summary>
    public void RejectAllForSession(string sessionId)
    {
        if (_pending.TryRemove(sessionId, out var entry))
            entry.Source.TrySetResult(new ChangeDecision(false, "Run cancelled."));
    }
}
