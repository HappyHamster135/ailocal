using AiLocal.Core.Agent;
using AiLocal.Core.Hardware;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Display-only rows for the cluster view: each cloud provider with an API
/// key configured appears as a pseudo-worker, so the operator sees the cloud
/// capacity the Host can route work through NEXT TO the physical machines.
/// The rows are minted fresh per request and are NEVER inserted into
/// WorkerRegistry - dispatch, slot brokering, heartbeats, and persistence
/// cannot be affected. AgentAccess=Off keeps them out of the composer's
/// worker picker (it filters Off), ClusterToken stays null, and the
/// "cloud:"-prefixed id makes them unambiguous for the dashboard.
/// </summary>
public static class CloudPseudoWorkers
{
    private static readonly (string Provider, string Name)[] Providers =
    [
        ("anthropic", "Anthropic API"),
        ("openai", "OpenAI API"),
        ("gemini", "Google Gemini API"),
        ("openrouter", "OpenRouter API"),
    ];

    public static IEnumerable<NodeInfo> For(Func<string, string?> apiKeyLookup)
    {
        foreach (var (provider, name) in Providers)
        {
            if (string.IsNullOrWhiteSpace(apiKeyLookup(provider))) continue;
            yield return new NodeInfo
            {
                Id = $"cloud:{provider}",
                Name = name,
                Endpoint = "",
                Role = NodeRole.Worker,
                Status = NodeStatus.Idle,
                Hardware = new HardwareProfile("Moln-API - nyckel konfigurerad", 0, 0, null, 0, false),
                Skills = ["cloud"],
                ProviderPriority = [provider],
                AgentAccess = AgentAccessLevel.Off,
                MaxConcurrentTasks = 0,
                LastSeen = DateTimeOffset.UtcNow,
            };
        }
    }
}
