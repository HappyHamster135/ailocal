using AiLocal.Core.Roles;

namespace AiLocal.Core.Discovery;

/// <summary>The payload multicast on the LAN so nodes can find each other.</summary>
public sealed record DiscoveryBeacon(
    string NodeId,
    string Name,
    NodeRole Role,
    string Endpoint);
