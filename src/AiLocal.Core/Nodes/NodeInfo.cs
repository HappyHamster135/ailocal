using AiLocal.Core.Hardware;
using AiLocal.Core.Roles;

namespace AiLocal.Core.Nodes;

/// <summary>
/// A cluster member as seen by the Host registry. Serialized across the LAN,
/// so keep it a plain data holder.
/// </summary>
public sealed class NodeInfo
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public NodeRole Role { get; set; }

    /// <summary>Base HTTP endpoint, e.g. http://192.168.1.20:5081</summary>
    public required string Endpoint { get; set; }

    public HardwareProfile? Hardware { get; set; }
    public NodeStatus Status { get; set; } = NodeStatus.Idle;
    public int ActiveTasks { get; set; }
    public List<string> Skills { get; set; } = ["general"];
    public int MaxConcurrentTasks { get; set; } = 1;
    public List<string> ProviderPriority { get; set; } = [];
    public string? LocalModel { get; set; }
    public string? RecommendedModel { get; set; }
    public string? Version { get; set; }
    public DateTimeOffset LastSeen { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Optional HTTPS endpoint for this node's cluster traffic (opportunistic transport encryption).</summary>
    public string? TlsEndpoint { get; set; }

    /// <summary>TLS endpoint when advertised, otherwise the plain HTTP endpoint. Use this for node-to-node calls.</summary>
    public string PreferredEndpoint => string.IsNullOrWhiteSpace(TlsEndpoint) ? Endpoint : TlsEndpoint;

    // Rolling health signals, updated by the Host after every dispatch outcome
    // and carried forward across heartbeat re-registrations (see WorkerRegistry.Upsert).
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public double AvgLatencyMs { get; set; }
}
