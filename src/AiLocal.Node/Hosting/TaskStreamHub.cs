using System.Collections.Concurrent;
using System.Threading.Channels;

namespace AiLocal.Node.Hosting;

/// <summary>
/// In-memory relay from an in-flight single-worker dispatch to any browser
/// tab watching that task's SSE endpoint. Unbounded per-task channel so a
/// late-connecting subscriber still gets everything published so far;
/// removed (and any late subscriber gets nothing further) once the task
/// completes, at which point the caller should read the persisted
/// AgentTask.Result instead.
/// </summary>
public sealed class TaskStreamHub
{
    private readonly ConcurrentDictionary<string, Channel<string>> _channels = new();

    public void Publish(string taskId, string delta)
    {
        if (delta.Length == 0) return;
        var channel = _channels.GetOrAdd(taskId, _ => Channel.CreateUnbounded<string>());
        channel.Writer.TryWrite(delta);
    }

    public void Complete(string taskId)
    {
        if (_channels.TryRemove(taskId, out var channel))
            channel.Writer.TryComplete();
    }

    /// <summary>Null if the task never streamed or already finished.</summary>
    public IAsyncEnumerable<string>? Subscribe(string taskId) =>
        _channels.TryGetValue(taskId, out var channel) ? channel.Reader.ReadAllAsync() : null;
}
