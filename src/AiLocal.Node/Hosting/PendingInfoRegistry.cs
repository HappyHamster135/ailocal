using System.Collections.Concurrent;

namespace AiLocal.Node.Hosting;

/// <summary>One question the agent needs answered before it can continue.
/// Questions are concrete (not yes/no) so the operator types a real answer.</summary>
public sealed record InfoQuestion(string Text);

/// <summary>A request from the running agent for more information, surfaced to
/// the operator mid-run. Mirrors <see cref="PendingChangeRegistry"/> but for
/// questions instead of file writes: the agent calls <c>ask_user</c>, which
/// blocks on a TaskCompletionSource until the operator replies (or the run is
/// cancelled). The dashboard polls <see cref="Peek"/> and POSTs to the answer
/// endpoint to unblock.</summary>
public sealed record PendingInfoRequest(
    string SessionId,
    IReadOnlyList<InfoQuestion> Questions)
{
    /// <summary>True when the agent considers the prompt too vague to proceed
    /// without these answers (used to render a stronger "can't continue" note).</summary>
    public bool Blocking { get; init; }
}

/// <summary>In-memory gate for the agent's mid-run questions. Like
/// PendingChangeRegistry it lives outside the durable SessionStore because a
/// TaskCompletionSource can't be serialized.</summary>
public sealed class PendingInfoRegistry
{
    private sealed record Entry(string SessionId, PendingInfoRequest Request, TaskCompletionSource<string> Source);

    private readonly ConcurrentDictionary<string, Entry> _pending = new();

    /// <summary>Blocks until the operator answers (or the run is cancelled,
    /// which trips ct). Returns the operator's free-text answer (one combined
    /// string for all questions).</summary>
    public async Task<string> RequestAsync(string sessionId, PendingInfoRequest request, CancellationToken ct)
    {
        var source = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var entry = new Entry(sessionId, request, source);
        _pending[sessionId] = entry;

        using var reg = ct.Register(() => source.TrySetResult("(avbruten)"));
        try
        {
            return await source.Task;
        }
        finally
        {
            _pending.TryRemove(sessionId, out _);
        }
    }

    /// <summary>Returns the pending info request for a session if one is
    /// waiting, without consuming it. Null if none pending.</summary>
    public PendingInfoRequest? Peek(string sessionId) =>
        _pending.TryGetValue(sessionId, out var entry) ? entry.Request : null;

    /// <summary>Resolves a waiting request with the operator's answer. Returns
    /// false if there was nothing pending.</summary>
    public bool Resolve(string sessionId, string answer)
    {
        if (!_pending.TryGetValue(sessionId, out var entry))
            return false;
        if (_pending.TryRemove(sessionId, out _))
            return entry.Source.TrySetResult(answer);
        return false;
    }

    /// <summary>Auto-fails any pending request for a session whose run was
    /// cancelled, so the blocked agent call unblocks instead of hanging.</summary>
    public void RejectAllForSession(string sessionId)
    {
        if (_pending.TryRemove(sessionId, out var entry))
            entry.Source.TrySetResult("(avbruten)");
    }
}
