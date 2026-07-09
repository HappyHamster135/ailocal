using System.Diagnostics;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

public sealed class ParentProcessMonitorHostedService : BackgroundService
{
    private readonly NodeSettings _settings;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<ParentProcessMonitorHostedService> _log;

    public ParentProcessMonitorHostedService(
        NodeSettings settings,
        IHostApplicationLifetime lifetime,
        ILogger<ParentProcessMonitorHostedService> log)
    {
        _settings = settings;
        _lifetime = lifetime;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.ParentProcessId is not { } parentProcessId)
            return;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!IsProcessAlive(parentProcessId))
            {
                _log.LogInformation("parent process {ParentProcessId} exited; stopping node", parentProcessId);
                _lifetime.StopApplication();
                return;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken); }
            catch (TaskCanceledException) { return; }
        }
    }

    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }
}
