using System.Collections.Concurrent;

namespace AiLocal.Node.Hosting;

/// <summary>
/// In-memory-only run coordination for sessions - deliberately NOT part of
/// SessionStore's persisted model (a CancellationTokenSource can't be
/// serialized, and mixing transient run state into the durable JSON risks it
/// leaking into the file and going stale across a restart). Guards against
/// two overlapping runs racing the same session's folder: /execute/assignment
/// has no such guard at all today, which sessions should not inherit.
///
/// Not the same concern as the Host's WorkerSlotBroker/TaskCancellationRegistry
/// - those model cross-machine dispatch capacity across the whole cluster;
/// this is single-node, single-session mutual exclusion only.
/// </summary>
public sealed class SessionRunRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _active = new();

    /// <summary>True and a live token if this session wasn't already running;
    /// false (no token) if a run is already in progress for it.</summary>
    public bool TryBegin(string sessionId, out CancellationTokenSource cts)
    {
        cts = new CancellationTokenSource();
        if (_active.TryAdd(sessionId, cts))
            return true;

        cts.Dispose();
        cts = null!;
        return false;
    }

    public bool IsRunning(string sessionId) => _active.ContainsKey(sessionId);

    public int ActiveCount => _active.Count;

    public bool Cancel(string sessionId)
    {
        if (!_active.TryGetValue(sessionId, out var cts))
            return false;
        try { cts.Cancel(); }
        catch (ObjectDisposedException) { /* already ending - fine */ }
        return true;
    }

    public void End(string sessionId)
    {
        if (_active.TryRemove(sessionId, out var cts))
            cts.Dispose();
    }
}
