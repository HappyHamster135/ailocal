using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
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

    /// <summary>Whether (and how much) this Worker will accept an
    /// "assignment" (agent-mode) task - set from its own AgentAccess
    /// setting, off by default. The Host uses this only to decide who's
    /// eligible for that kind of dispatch; it can't change it remotely.</summary>
    public AgentAccessLevel AgentAccess { get; set; } = AgentAccessLevel.Off;
    public List<string> ProviderPriority { get; set; } = [];
    public string? LocalModel { get; set; }
    public string? RecommendedModel { get; set; }
    public string? Version { get; set; }
    /// <summary>The registering node's own cluster token, shared with the
    /// Overseer so it can proxy node-to-node calls back using the Host's
    /// token (each node mints its own; presenting the Overseer's token to a
    /// remote Host would be rejected with 401). Empty for Workers, which are
    /// reached through their Host and never proxied to directly.</summary>
    public string? ClusterToken { get; set; }

    /// <summary>Per-complexity model tiers this Worker wants the Host to
    /// use when dispatching an assignment with no explicit model hint.
    /// Carried in the heartbeat so the Host does the selection locally
    /// (no extra round-trip per task).</summary>
    public ModelTiers ModelTiers { get; set; } = new();

    /// <summary>Folder this Worker's agent runs inside (Sandboxed: the
    /// access root; Full: run_command's default dir). Null => its own
    /// data dir / agent-workspace.</summary>
    public string? WorkspacePath { get; set; }
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
