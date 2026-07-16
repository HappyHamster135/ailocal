using AiLocal.Core.Configuration;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Host-only: automatically sends the connect ("Anslut") half of click-to-pair
/// to every Worker it discovers that isn't already connected and doesn't
/// already have a pending request, instead of requiring the operator to
/// click it manually for every device that shows up on the LAN.
///
/// The Worker side is UNCHANGED - it still requires an explicit Accept from
/// that Worker's own operator, which is the actual trust boundary in this
/// design (see PairingCoordinator's docs). A Host merely ASKING to connect
/// has nothing to gain by itself; automating only this half doesn't weaken
/// the mutual-consent model click-to-pair was built around, it just removes
/// a click that was never really a security decision.
///
/// A rejected or ignored request isn't re-sent immediately - BeginOutbound's
/// existing 5-minute request TTL (PairingCoordinator) already keeps this
/// sweep from re-spamming the same Worker every few seconds; it naturally
/// tries again only once that record expires.
/// </summary>
public sealed class HostAutoConnectService : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(5);

    private readonly PairingCoordinator _pairing;
    private readonly WorkerRegistry _registry;
    private readonly PersistentSettingsStore _store;
    private readonly NodeSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<HostAutoConnectService> _log;

    public HostAutoConnectService(
        PairingCoordinator pairing,
        WorkerRegistry registry,
        PersistentSettingsStore store,
        NodeSettings settings,
        IHttpClientFactory httpFactory,
        ILogger<HostAutoConnectService> log)
    {
        _pairing = pairing;
        _registry = registry;
        _store = store;
        _settings = settings;
        _httpFactory = httpFactory;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(ct);
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "auto-connect sweep failed");
            }

            try { await Task.Delay(SweepInterval, ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        var pendingPeerIds = _pairing.PendingOutbound()
            .Select(p => p.PeerId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var connectedIds = _registry.All
            .Where(n => n.Status != NodeStatus.Offline)
            .Select(n => n.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var peer in _pairing.Discovered(NodeRole.Worker))
        {
            if (ct.IsCancellationRequested) break;
            // Never auto-connect to our own endpoint - when Host + Worker share
            // a machine they have different NodeIds, so the NodeId filter alone
            // would still let the Host spam its own Worker with pair requests.
            var selfEndpoint = $"http://{NetworkUtil.LocalIPv4()}:{_settings.Port}";
            if (peer.Endpoint == selfEndpoint) continue;
            if (connectedIds.Contains(peer.Id) || pendingPeerIds.Contains(peer.Id))
                continue;

            var (success, error) = await PairingConnect.SendRequestAsync(peer, _pairing, _store, _settings, _httpFactory, ct);
            if (success)
                _log.LogInformation("auto-connect: sent a pairing request to {Worker} ({Endpoint})", peer.Name, peer.Endpoint);
            else
                _log.LogDebug("auto-connect: could not reach {Worker}: {Error}", peer.Name, error);
        }
    }
}
