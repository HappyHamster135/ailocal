namespace AiLocal.Node.Hosting;

/// <summary>
/// One build at a time per node, the rest wait in line. Before this there was
/// NO guard at all: a second assignment ran BESIDE the first in the same
/// workspace (shared files, racing writes). Now "ställ tre spel på kö över
/// natten" works: each caller's SSE stream stays open while queued, the
/// position is reported through the onQueued callback (which the caller
/// writes into the stream and the persistent log), and FIFO order comes from
/// SemaphoreSlim's internal wait queue.
/// </summary>
public sealed class AssignmentQueue
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _waiting;

    /// <summary>Number of runs currently waiting (not counting the active one).</summary>
    public int WaitingCount => Volatile.Read(ref _waiting);

    /// <summary>True while a run holds the slot.</summary>
    public bool Busy => _gate.CurrentCount == 0;

    /// <summary>Acquires the node's single build slot. If the slot is taken,
    /// onQueued(position) is invoked once and the call waits its turn.
    /// Dispose the returned handle to release the slot.</summary>
    public async Task<IDisposable> EnterAsync(Func<int, Task>? onQueued, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(TimeSpan.Zero, ct))
        {
            var position = Interlocked.Increment(ref _waiting);
            try
            {
                if (onQueued is not null)
                    await onQueued(position);
                await _gate.WaitAsync(ct);
            }
            finally
            {
                Interlocked.Decrement(ref _waiting);
            }
        }
        return new Releaser(_gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                gate.Release();
        }
    }
}
