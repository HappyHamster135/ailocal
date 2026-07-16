using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AiLocal.Node.Roles;

/// <summary>
/// Background loop that, when <see cref="WorkerProfileSettings.AutoMergeIsolatedTasks"/>
/// is enabled, periodically runs the CI gate on every active isolated task and
/// auto-merges the ones that pass (discarding + notifying on failure). This is
/// the "A3" autonomous merge step: the team's work lands on the base branch
/// without a human clicking "Merge" for every PR.
/// </summary>
public sealed class AutoMergeHostedService : IHostedService, IDisposable
{
    private readonly IServiceProvider _services;
    private Timer? _timer;

    public AutoMergeHostedService(IServiceProvider services) => _services = services;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // First pass after 10s, then every 30s.
        _timer = new Timer(_ => RunOnce(), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30));
        return Task.CompletedTask;
    }

    private void RunOnce()
    {
        try { RunAutoMerge(_services); }
        catch { /* best-effort background loop - never throw onto the timer thread */ }
    }

    /// <summary>Runs the CI gate on every active isolated task: merge on green,
    /// discard + notify on red. Safe to call directly (e.g. from an endpoint).</summary>
    internal static void RunAutoMerge(IServiceProvider services)
    {
        var isolation = services.GetService<GitIsolationService>();
        var settings = services.GetService<NodeSettings>();
        if (isolation is null || settings is null || !settings.Worker.AutoMergeIsolatedTasks)
            return;

        foreach (var task in isolation.ListActive())
        {
            var (ciPassed, ciOutput) = isolation.RunCiGateAsync(task.TaskId).GetAwaiter().GetResult();
            if (ciPassed)
            {
                var (ok, output) = isolation.MergeAsync(task.TaskId).GetAwaiter().GetResult();
                NoticeBoard.Add(
                    ok ? NoticeType.TaskDone : NoticeType.TaskFailed,
                    ok
                        ? $"Auto-mergad (CI grön): {task.Title}"
                        : $"Merge misslyckades efter grön CI: {task.Title} - {output}",
                    task.TaskId);
            }
            else
            {
                isolation.DiscardAsync(task.TaskId).GetAwaiter().GetResult();
                NoticeBoard.Add(
                    NoticeType.TaskFailed,
                    $"CI misslyckades - branch kastad: {task.Title} - {ciOutput}",
                    task.TaskId);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();
}
