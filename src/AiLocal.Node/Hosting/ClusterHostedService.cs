using System.Net.Http.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Discovery;
using AiLocal.Core.Hardware;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Handles cluster membership per role: the Host announces itself on the LAN,
/// Workers discover the Host and register (heartbeat), the Overseer just learns
/// the Host endpoint. Explicit <c>HostEndpoint</c> config bypasses discovery.
/// </summary>
public sealed class ClusterHostedService : BackgroundService
{
    private readonly NodeSettings _settings;
    private readonly HardwareProfile _hardware;
    private readonly LocalModelRecommendation _recommendation;
    private readonly HostLocator _hostLocator;
    private readonly HostRegistry _hostRegistry;
    private readonly RegistrationStatus _registrationStatus;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ClusterHostedService> _log;
    private readonly string _nodeId;

    public ClusterHostedService(
        NodeSettings settings,
        HardwareProfile hardware,
        LocalModelRecommendation recommendation,
        PersistentSettingsStore settingsStore,
        HostLocator hostLocator,
        HostRegistry hostRegistry,
        RegistrationStatus registrationStatus,
        IHttpClientFactory httpFactory,
        ILogger<ClusterHostedService> log)
    {
        _settings = settings;
        _hardware = hardware;
        _recommendation = recommendation;
        _nodeId = settingsStore.NodeId;
        _hostLocator = hostLocator;
        _hostRegistry = hostRegistry;
        _registrationStatus = registrationStatus;
        _httpFactory = httpFactory;
        _log = log;
    }

    private string SelfEndpoint => $"http://{NetworkUtil.LocalIPv4()}:{_settings.Port}";

    private string? SelfTlsEndpoint => _settings.Tls.HttpsPortFor(_settings.Port) is { } port
        ? $"https://{NetworkUtil.LocalIPv4()}:{port}"
        : null;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(_settings.HostEndpoint))
        {
            _hostLocator.HostEndpoint = _settings.HostEndpoint;
            if (_settings.Role == NodeRole.Overseer)
                _hostRegistry.UpsertExplicit(_settings.HostEndpoint);
        }

        var discovery = new LanDiscovery(_settings.Discovery.MulticastAddress, _settings.Discovery.Port);
        var beacon = new DiscoveryBeacon(_nodeId, _settings.NodeName, _settings.Role, SelfEndpoint);

        switch (_settings.Role)
        {
            case NodeRole.Host:
                _log.LogInformation("Host announcing at {Endpoint}", SelfEndpoint);
                if (_settings.Discovery.Enabled)
                    await discovery.AnnounceAsync(beacon, TimeSpan.FromSeconds(5), ct);
                break;

            case NodeRole.Worker:
                if (_settings.Discovery.Enabled)
                    _ = discovery.ListenAsync(OnBeacon, ct);
                await RegisterLoopAsync(ct);
                break;

            case NodeRole.Overseer:
                if (_settings.Discovery.Enabled)
                    await discovery.ListenAsync(OnBeacon, ct);
                break;
        }
    }

    private void OnBeacon(DiscoveryBeacon beacon)
    {
        if (beacon.Role == NodeRole.Host && _hostLocator.HostEndpoint is null)
        {
            _hostLocator.HostEndpoint = beacon.Endpoint;
            _log.LogInformation("Discovered host at {Endpoint}", beacon.Endpoint);
        }

        if (beacon.Role == NodeRole.Host && _settings.Role == NodeRole.Overseer)
            _hostRegistry.Upsert(beacon);
    }

    private async Task RegisterLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (_hostLocator.HostEndpoint is { } host)
            {
                try
                {
                    var info = new NodeInfo
                    {
                        Id = _nodeId,
                        Name = _settings.NodeName,
                        Role = NodeRole.Worker,
                        Endpoint = SelfEndpoint,
                        TlsEndpoint = SelfTlsEndpoint,
                        Hardware = _hardware,
                        Skills = [.. _settings.Worker.Skills],
                        MaxConcurrentTasks = _settings.Worker.MaxConcurrentTasks,
                        ProviderPriority = [.. _settings.Providers.Priority],
                        LocalModel = _settings.Providers.OllamaModel ?? _recommendation.OllamaTag,
                        RecommendedModel = _recommendation.OllamaTag,
                        Version = typeof(ClusterHostedService).Assembly.GetName().Version?.ToString(3)
                    };
                    var client = _httpFactory.CreateClient("cluster");
                    using var response = await client.PostAsJsonAsync($"{host}/cluster/register", info, ct);
                    if (response.IsSuccessStatusCode)
                    {
                        SetStatus(RegistrationState.Connected, null);
                    }
                    else if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized
                        or System.Net.HttpStatusCode.Forbidden)
                    {
                        SetStatus(
                            RegistrationState.Unauthorized,
                            $"Host {host} avvisade denna nod ({(int)response.StatusCode}). " +
                            "Kontrollera att klusternyckeln matchar Hostens (Instaellningar -> Klustersaekerhet).");
                    }
                    else
                    {
                        SetStatus(RegistrationState.Unreachable, $"Host {host} svarade {(int)response.StatusCode}.");
                    }
                }
                catch (Exception ex)
                {
                    SetStatus(RegistrationState.Unreachable, ex.Message);
                }
            }

            try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
            catch (TaskCanceledException) { break; }
        }
    }

    /// <summary>Logs at Warning/Information (visible by default) only on state
    /// transitions, so a persistent pairing failure doesn't spam the console
    /// every 15 seconds but is never silently swallowed either.</summary>
    private void SetStatus(RegistrationState state, string? detail)
    {
        var (previous, _) = _registrationStatus.Read();
        _registrationStatus.Set(state, detail);
        if (state == previous)
            return;

        if (state == RegistrationState.Connected)
            _log.LogInformation("connected to host {Host}", _hostLocator.HostEndpoint);
        else if (state == RegistrationState.Unauthorized)
            _log.LogWarning("{Detail}", detail);
        else
            _log.LogDebug("register failed: {Detail}", detail);
    }
}
