using System.Collections.Concurrent;

namespace AiLocal.Node.Hosting;

/// <summary>
/// One CancellationTokenSource per task the Host is currently tracking
/// (queued or in flight), so an operator-triggered cancel can stop exactly
/// that task's wait-for-a-slot loop or in-progress HTTP call.
/// </summary>
public sealed class TaskCancellationRegistry
{
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _sources = new();

    public CancellationTokenSource GetOrCreate(string taskId) =>
        _sources.GetOrAdd(taskId, _ => new CancellationTokenSource());

    public bool Cancel(string taskId)
    {
        if (!_sources.TryGetValue(taskId, out var cts))
            return false;

        try { cts.Cancel(); }
        catch (ObjectDisposedException) { return false; }
        return true;
    }

    public void Complete(string taskId)
    {
        if (_sources.TryRemove(taskId, out var cts))
            cts.Dispose();
    }
}
