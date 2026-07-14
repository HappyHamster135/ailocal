using System.Net.Http.Json;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

/// <summary>
/// The outbound half of click-to-pair's connect step - starts a nonce-backed
/// outbound request and POSTs it to a discovered peer's /pairing/request.
/// Shared between the manual "Anslut" button (HostRole's
/// POST /api/discovered-workers/{id}/connect) and HostAutoConnectService's
/// background sweep, so both take the exact same path rather than two
/// implementations that could drift.
/// </summary>
public static class PairingConnect
{
    public static async Task<(bool Success, string? Error)> SendRequestAsync(
        DiscoveredPeer peer,
        PairingCoordinator pairing,
        PersistentSettingsStore store,
        NodeSettings settings,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var nonce = pairing.BeginOutbound(peer.Id, peer.Name, peer.Endpoint);
        try
        {
            var selfEndpoint = $"http://{NetworkUtil.LocalIPv4()}:{settings.Port}";
            var payload = new PairingHandshakePayload(store.NodeId, settings.NodeName, selfEndpoint, nonce);
            var client = httpFactory.CreateClient("cluster");
            using var response = await client.PostAsJsonAsync($"{peer.Endpoint}/pairing/request", payload, ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"Worker {peer.Endpoint} svarade {(int)response.StatusCode}.");

            return (true, null);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }
}
