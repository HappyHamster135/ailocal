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
    private readonly PairingCoordinator _pairing;
    private readonly RegistrationStatus _registrationStatus;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<ClusterHostedService> _log;
    private readonly string _nodeId;
    private readonly PersistentSettingsStore _settingsStore;

    public ClusterHostedService(
        NodeSettings settings,
        HardwareProfile hardware,
        LocalModelRecommendation recommendation,
        PersistentSettingsStore settingsStore,
        HostLocator hostLocator,
        HostRegistry hostRegistry,
        PairingCoordinator pairing,
        RegistrationStatus registrationStatus,
        IHttpClientFactory httpFactory,
        ILogger<ClusterHostedService> log)
    {
        _settings = settings;
        _hardware = hardware;
        _recommendation = recommendation;
        _nodeId = settingsStore.NodeId;
        _settingsStore = settingsStore;
        _hostLocator = hostLocator;
        _hostRegistry = hostRegistry;
        _pairing = pairing;
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
                {
                    // Also listen, not just announce: a Host needs to see
                    // unpaired Workers' own beacons for the click-to-pair flow
                    // (PairingCoordinator/HostRole's "discovered workers" list).
                    _ = RunDiscoveryAsync(() => discovery.ListenAsync(OnBeacon, ct), "listen");
                    await RunDiscoveryAsync(() => discovery.AnnounceAsync(beacon, TimeSpan.FromSeconds(5), ct), "announce");
                }
                break;

            case NodeRole.Worker:
                if (_settings.Discovery.Enabled)
                {
                    _ = RunDiscoveryAsync(() => discovery.ListenAsync(OnBeacon, ct), "listen");
                    // A Worker announces itself too (not just listens for the
                    // Host), so a Host that doesn't have it registered yet can
                    // still discover and offer to pair with it.
                    _ = RunDiscoveryAsync(() => discovery.AnnounceAsync(beacon, TimeSpan.FromSeconds(5), ct), "announce");
                }
                await RegisterLoopAsync(ct);
                break;

            case NodeRole.Overseer:
                if (_settings.Discovery.Enabled)
                {
                    // Listen for Host/Worker beacons so they show up in the
                    // registry (and so this Overseer can proxy to them)...
                    _ = RunDiscoveryAsync(() => discovery.ListenAsync(OnBeacon, ct), "listen");
                    // ...and announce ourselves, so Hosts that are merely
                    // listening (not broadly announcing) still learn about
                    // this Overseer and announce their token back to it via
                    // /cluster/announce. Without this, an Overseer running on
                    // a machine with no local Host never discovers the other
                    // Hosts on the LAN.
                    await RunDiscoveryAsync(() => discovery.AnnounceAsync(beacon, TimeSpan.FromSeconds(5), ct), "announce");
                }
                break;
        }
    }

    /// <summary>
    /// Runs a LanDiscovery call (announce or listen) and degrades gracefully
    /// instead of taking the whole node down with it. UdpClient's initial
    /// bind/JoinMulticastGroup happens before that method's own loop - and
    /// its own try/catch - even starts, so a SocketException there (multicast
    /// blocked by a VPN, some corporate/hotel Wi-Fi, certain virtual adapters)
    /// used to propagate out of ExecuteAsync. For the Host/Overseer cases that
    /// await this directly, BackgroundService's default unhandled-exception
    /// behavior (StopHost) then killed the entire process just because LAN
    /// discovery didn't work - not something click-to-pair strictly needs if
    /// an explicit HostEndpoint is configured instead.
    /// </summary>
    private async Task RunDiscoveryAsync(Func<Task> discover, string what)
    {
        try
        {
            await discover();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex,
                "LAN discovery ({What}) could not start, continuing without it - " +
                "this usually means multicast is blocked on this network (VPN, some " +
                "corporate/hotel Wi-Fi, certain virtual adapters). Configure an explicit " +
                "Host endpoint in Installningar if this persists.", what);
        }
    }

    private void OnBeacon(DiscoveryBeacon beacon)
    {
        // Ignore our own beacon (same NodeId) AND any beacon coming from our
        // own endpoint - when Host + Worker run on the same machine they have
        // different NodeIds but the Host would otherwise "discover" its own
        // Worker and spam it with pairing requests forever.
        if (beacon.NodeId != _nodeId && beacon.Endpoint != SelfEndpoint)
            _pairing.NoteDiscovered(beacon);

        if (beacon.Role == NodeRole.Host && _hostLocator.HostEndpoint is null)
        {
            _hostLocator.HostEndpoint = beacon.Endpoint;
            _log.LogInformation("Discovered host at {Endpoint}", beacon.Endpoint);
        }

        if (beacon.Role == NodeRole.Host && _settings.Role == NodeRole.Overseer)
            _hostRegistry.Upsert(beacon);

        // A Host that sees an Overseer announces itself (carrying its own
        // cluster token) so the Overseer can proxy node-to-node calls back to
        // it. Without this, the Overseer would present its own token, which a
        // remote Host rejects with 401 - so cross-machine control silently
        // fails. This is the LAN-trust opt-in: announcing == "you may control
        // me". A Host that never sees an Overseer beacon simply never announces.
        if (beacon.Role == NodeRole.Overseer && _settings.Role == NodeRole.Host)
            _ = AnnounceToOverseerAsync(beacon.Endpoint);
    }

    private async Task AnnounceToOverseerAsync(string overseerEndpoint)
    {
        try
        {
            var info = new NodeInfo
            {
                Id = _nodeId,
                Name = _settings.NodeName,
                Role = NodeRole.Host,
                Endpoint = SelfEndpoint,
                TlsEndpoint = SelfTlsEndpoint,
                Hardware = _hardware,
                Skills = [.. _settings.Worker.Skills],
                MaxConcurrentTasks = _settings.Worker.MaxConcurrentTasks,
                AgentAccess = _settings.Worker.AgentAccess,
                ProviderPriority = [.. _settings.Providers.Priority],
                LocalModel = _settings.Providers.OllamaModel ?? _recommendation.OllamaTag,
                RecommendedModel = _recommendation.OllamaTag,
                Version = typeof(ClusterHostedService).Assembly.GetName().Version?.ToString(3),
                ModelTiers = _settings.Worker.ModelTiers,
                WorkspacePath = _settings.Worker.WorkspacePath,
                ClusterToken = _settingsStore.GetClusterToken()
            };
            var client = _httpFactory.CreateClient("cluster");
            using var response = await client.PostAsJsonAsync($"{overseerEndpoint.TrimEnd('/')}/cluster/announce", info);
            if (response.IsSuccessStatusCode)
                _log.LogInformation("Announced to Overseer at {Endpoint}", overseerEndpoint);
            else
                _log.LogDebug("Overseer {Endpoint} rejected announce: {Status}", overseerEndpoint, (int)response.StatusCode);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Failed to announce to Overseer at {Endpoint}", overseerEndpoint);
        }
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
                        AgentAccess = _settings.Worker.AgentAccess,
                        ProviderPriority = [.. _settings.Providers.Priority],
                        LocalModel = _settings.Providers.OllamaModel ?? _recommendation.OllamaTag,
                        RecommendedModel = _recommendation.OllamaTag,
                        Version = typeof(ClusterHostedService).Assembly.GetName().Version?.ToString(3),
                        ModelTiers = _settings.Worker.ModelTiers,
                        WorkspacePath = _settings.Worker.WorkspacePath,
                        // Share this Host's own cluster token so the Overseer
                        // can proxy node-to-node calls back to it. Each node
                        // mints its own token; the Overseer presenting its own
                        // would be rejected with 401 by a remote Host.
                        ClusterToken = _settingsStore.GetClusterToken()
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
            // Unreachable (host offline, wrong IP, blocked by firewall/network
            // profile) is the single most common real-world failure this app
            // hits - Debug is below the default Information threshold and
            // never surfaces anywhere by default, which is exactly backwards
            // for the fault an operator is most likely to actually need to see.
            _log.LogWarning("register failed: {Detail}", detail);
    }
}
