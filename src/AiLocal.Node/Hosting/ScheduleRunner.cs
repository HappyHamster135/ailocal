using AiLocal.Core.Configuration;
using AiLocal.Core.Providers;
using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Ticks every 30s and submits any due scheduled goal through the same
/// entry point a manually-submitted goal uses. Only registered for the Host
/// role (see HostRole.ConfigureServices), so all its dependencies are
/// guaranteed to already be registered as singletons alongside it.
/// </summary>
public sealed class ScheduleRunner : BackgroundService
{
    private readonly ScheduleStore _store;
    private readonly WorkerRegistry _registry;
    private readonly TaskBoard _board;
    private readonly IHttpClientFactory _httpFactory;
    private readonly FallbackChatProvider _providers;
    private readonly WorkerSlotBroker _broker;
    private readonly TaskStreamHub _streamHub;
    private readonly TaskCancellationRegistry _cancellationRegistry;
    private readonly NodeSettings _settings;
    private readonly ILogger<ScheduleRunner> _log;

    public ScheduleRunner(
        ScheduleStore store,
        WorkerRegistry registry,
        TaskBoard board,
        IHttpClientFactory httpFactory,
        FallbackChatProvider providers,
        WorkerSlotBroker broker,
        TaskStreamHub streamHub,
        TaskCancellationRegistry cancellationRegistry,
        NodeSettings settings,
        ILogger<ScheduleRunner> log)
    {
        _store = store;
        _registry = registry;
        _board = board;
        _httpFactory = httpFactory;
        _providers = providers;
        _broker = broker;
        _streamHub = streamHub;
        _cancellationRegistry = cancellationRegistry;
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                _log.LogWarning("schedule tick failed: {Message}", ex.Message);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    private void Tick()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var schedule in _store.All().Where(s => IsDue(s, now)))
        {
            _store.Update(schedule.Id, s => s.LastRunAt = now);
            var request = new SubmitTaskRequest(schedule.Prompt, schedule.System, schedule.Parallelism);
            _log.LogInformation("running scheduled goal {Name}", schedule.Name);
            HostRole.SubmitGoal(request, null, _board, _registry, _httpFactory, _providers, _broker,
                _streamHub, _cancellationRegistry, _settings, _log);
        }
    }

    private static bool IsDue(ScheduledGoal schedule, DateTimeOffset now)
    {
        if (!schedule.Enabled) return false;
        var last = schedule.LastRunAt;

        if (!string.IsNullOrWhiteSpace(schedule.AtTimeOfDay) &&
            TimeSpan.TryParse(schedule.AtTimeOfDay, out var timeOfDay))
        {
            var todayRunUtc = new DateTimeOffset(now.UtcDateTime.Date.Add(timeOfDay), TimeSpan.Zero);
            if (now < todayRunUtc) return false;
            return last is null || last.Value.UtcDateTime.Date < now.UtcDateTime.Date;
        }

        if (last is null) return true;
        return now - last.Value >= TimeSpan.FromMinutes(Math.Max(1, schedule.IntervalMinutes));
    }
}
