using AiLocal.Core.Cluster;
using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Turns "assign this work to a worker" into a real queue: a caller blocks
/// (task state becomes Queued) until a worker with an actual free capacity
/// slot exists, then atomically claims that slot. Replaces the old behavior
/// where a full cluster just force-assigned to an already-overloaded worker.
/// </summary>
public sealed class WorkerSlotBroker
{
    private readonly WorkerRegistry _registry;
    private readonly object _gate = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);

    public WorkerSlotBroker(WorkerRegistry registry) => _registry = registry;

    /// <summary>
    /// Waits until a worker matching <paramref name="requirement"/> has a free
    /// slot, then claims it (increments that worker's live ActiveTasks count).
    /// The caller must eventually call <see cref="Release"/> once the claimed
    /// work finishes so waiters wake up.
    /// </summary>
    public async Task<WorkerMatch> ClaimAsync(
        WorkRequirement requirement,
        Action? onQueued,
        CancellationToken ct)
    {
        var announced = false;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            WorkerMatch? claimed = null;
            lock (_gate)
            {
                var workers = _registry.AvailableWorkers();
                if (workers.Count > 0)
                {
                    var ranked = WorkerScorer.RankFor(workers, requirement);
                    var candidate = ranked.FirstOrDefault(m => m.HasCapacity);
                    if (candidate is not null)
                    {
                        candidate.Worker.Node.ActiveTasks++;
                        candidate.Worker.Node.Status = Core.Nodes.NodeStatus.Busy;
                        claimed = candidate;
                    }
                }
            }

            if (claimed is not null)
                return claimed;

            if (!announced)
            {
                onQueued?.Invoke();
                announced = true;
            }

            try
            {
                // Wake immediately when a slot frees; a short safety-net
                // interval also covers e.g. a fresh worker registering with
                // capacity while nothing was actively being released.
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));
                await _signal.WaitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // Safety-net interval elapsed - loop and re-check capacity.
            }
        }
    }

    /// <summary>Signals that a previously-claimed slot is free again.</summary>
    public void Release()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* nothing waiting - fine */ }
    }
}
