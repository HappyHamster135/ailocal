using System.Collections.Concurrent;

namespace AiLocal.Node.Hosting;

/// <summary>
/// The pause point for milestone approval: the assignment engine parks here
/// after the director's contract until the operator clicks Godkänn/Justera
/// in the dashboard (POST /api/assignment/milestone). Timeout auto-approves -
/// an unattended build must never hang forever on a checkpoint.
/// </summary>
public static class MilestoneRegistry
{
    private static readonly ConcurrentDictionary<string, TaskCompletionSource<(bool Approved, string? Note)>> Pending = new();

    public static async Task<(bool Approved, string? Note)> WaitAsync(string id, TimeSpan timeout, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<(bool, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        Pending[id] = tcs;
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);
            try
            {
                return await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                return (true, null); // timeout = auto-godkänn
            }
        }
        finally
        {
            Pending.TryRemove(id, out _);
        }
    }

    /// <summary>False when the id is unknown (already resolved/timed out).</summary>
    public static bool Resolve(string id, bool approve, string? note) =>
        Pending.TryRemove(id, out var tcs) && tcs.TrySetResult((approve, note));
}
