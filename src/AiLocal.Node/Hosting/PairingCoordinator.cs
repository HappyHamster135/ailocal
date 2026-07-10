using System.Collections.Concurrent;
using System.Security.Cryptography;
using AiLocal.Core.Discovery;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

public sealed record DiscoveredPeer(string Id, string Name, NodeRole Role, string Endpoint, DateTimeOffset LastSeen);

/// <summary>A connect request this node sent to a peer, awaiting the peer's approval.</summary>
public sealed record OutboundPairingRequest(
    string PeerId, string PeerName, string PeerEndpoint, string Nonce, DateTimeOffset RequestedAt);

/// <summary>A connect request a peer sent to this node, awaiting this node's approval.</summary>
public sealed record InboundPairingRequest(
    string RequesterId, string RequesterName, string RequesterEndpoint, string Nonce, DateTimeOffset ReceivedAt);

/// <summary>Body for both /pairing/request (Host -> Worker) and /pairing/approved
/// (Worker -> Host) - structurally identical, always "who I am and my nonce".</summary>
public sealed record PairingHandshakePayload(string PeerId, string PeerName, string PeerEndpoint, string Nonce);

/// <summary>Response body for /pairing/approved: the credential handed over
/// only now that both sides have explicitly consented.</summary>
public sealed record PairingApprovalResponse(string ClusterToken);

/// <summary>
/// Click-to-pair, no typing: a node "sees" peers on the LAN via their discovery
/// beacon (see ClusterHostedService), and pairing is a two-step handshake -
/// this node sends a connect request (with a random nonce) to the peer, and
/// only once the peer's own operator explicitly approves does the peer call
/// back with that same nonce to complete it. The nonce (not the cluster token)
/// is what travels over the wire before either side has agreed to anything;
/// the real secret (cluster token) is only ever handed over after both sides
/// have clicked. This is LAN-trust security, not cryptographic pairing - see
/// ClusterSecurity for the broader trust model this composes with.
/// </summary>
public sealed class PairingCoordinator
{
    private static readonly TimeSpan RequestTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DiscoveredTtl = TimeSpan.FromSeconds(60);

    // Defense in depth against a flood of distinct fake peer/requester ids
    // from a sender nobody has authenticated yet - NoteDiscovered comes
    // straight off a UDP beacon, AddInbound straight off the deliberately-
    // public POST /pairing/request. Prune() alone only bounds growth to
    // "however long until TTL expiry", which is still unbounded *within*
    // that window under a flood. A real cluster is a handful of machines;
    // this is nowhere close to that in normal use.
    private const int MaxTrackedEntries = 500;

    private readonly ConcurrentDictionary<string, DiscoveredPeer> _discovered = new();
    private readonly ConcurrentDictionary<string, OutboundPairingRequest> _outbound = new();
    private readonly ConcurrentDictionary<string, InboundPairingRequest> _inbound = new();

    public void NoteDiscovered(DiscoveryBeacon beacon)
    {
        Prune();
        // Re-announcing an already-known peer (the normal case - a Worker
        // re-beacons every few seconds) must never be blocked by the cap,
        // only genuinely new entries should be.
        if (!_discovered.ContainsKey(beacon.NodeId) && _discovered.Count >= MaxTrackedEntries)
            return;

        _discovered[beacon.NodeId] = new DiscoveredPeer(
            beacon.NodeId, beacon.Name, beacon.Role, beacon.Endpoint, DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<DiscoveredPeer> Discovered(NodeRole role)
    {
        Prune();
        return _discovered.Values
            .Where(p => p.Role == role)
            .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public DiscoveredPeer? Get(string id)
    {
        Prune();
        return _discovered.TryGetValue(id, out var peer) ? peer : null;
    }

    /// <summary>Starts an outbound request to a peer and returns the nonce to send it.</summary>
    public string BeginOutbound(string peerId, string peerName, string peerEndpoint)
    {
        // Not cap-checked like the two below - this is only reachable via an
        // already-authenticated Host action (/api/discovered-workers/{id}/connect
        // sits behind the normal token gate), not directly by an unauthenticated
        // network sender, so it isn't part of the same flood surface.
        Prune();
        var nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
        _outbound[peerId] = new OutboundPairingRequest(peerId, peerName, peerEndpoint, nonce, DateTimeOffset.UtcNow);
        return nonce;
    }

    public IReadOnlyList<OutboundPairingRequest> PendingOutbound()
    {
        Prune();
        return _outbound.Values.OrderByDescending(r => r.RequestedAt).ToList();
    }

    /// <summary>Completes an outbound request if the peer echoed back the matching nonce.</summary>
    public bool TryCompleteOutbound(string peerId, string nonce, out OutboundPairingRequest request)
    {
        Prune();
        if (_outbound.TryGetValue(peerId, out var found) && SecureEquals(found.Nonce, nonce))
        {
            _outbound.TryRemove(peerId, out _);
            request = found;
            return true;
        }

        request = null!;
        return false;
    }

    public void AddInbound(string requesterId, string requesterName, string requesterEndpoint, string nonce)
    {
        Prune();
        if (!_inbound.ContainsKey(requesterId) && _inbound.Count >= MaxTrackedEntries)
            return;

        _inbound[requesterId] = new InboundPairingRequest(
            requesterId, requesterName, requesterEndpoint, nonce, DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<InboundPairingRequest> PendingInbound()
    {
        Prune();
        return _inbound.Values.OrderByDescending(r => r.ReceivedAt).ToList();
    }

    /// <summary>Reads a pending inbound request without consuming it - the
    /// caller must call <see cref="RemoveInboundIfMatches"/> only once the
    /// approval callback to the peer has actually succeeded, so a transient
    /// network failure leaves the request available to retry instead of
    /// silently discarding it.</summary>
    public InboundPairingRequest? GetInbound(string requesterId)
    {
        Prune();
        return _inbound.TryGetValue(requesterId, out var request) ? request : null;
    }

    /// <summary>Removes a completed inbound request, but only if it still
    /// matches the given nonce - guards against removing a newer request
    /// that arrived (with a fresh nonce) while an earlier attempt was in flight.</summary>
    public void RemoveInboundIfMatches(string requesterId, string nonce)
    {
        if (_inbound.TryGetValue(requesterId, out var existing) && existing.Nonce == nonce)
            _inbound.TryRemove(requesterId, out _);
    }

    public void RejectInbound(string requesterId) => _inbound.TryRemove(requesterId, out _);

    private static bool SecureEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(a), System.Text.Encoding.UTF8.GetBytes(b));

    private void Prune()
    {
        var requestCutoff = DateTimeOffset.UtcNow - RequestTtl;
        foreach (var pair in _outbound)
            if (pair.Value.RequestedAt < requestCutoff) _outbound.TryRemove(pair.Key, out _);
        foreach (var pair in _inbound)
            if (pair.Value.ReceivedAt < requestCutoff) _inbound.TryRemove(pair.Key, out _);

        var discoveredCutoff = DateTimeOffset.UtcNow - DiscoveredTtl;
        foreach (var pair in _discovered)
            if (pair.Value.LastSeen < discoveredCutoff) _discovered.TryRemove(pair.Key, out _);
    }
}
