using AiLocal.Core.Roles;

namespace AiLocal.Core.Configuration;

/// <summary>Root config, bound from the "Node" section of appsettings.json.</summary>
public sealed class NodeSettings
{
    public NodeRole Role { get; set; } = NodeRole.Launcher;
    public string NodeName { get; set; } = Environment.MachineName;

    /// <summary>0 means "use the role default port".</summary>
    public int Port { get; set; }

    /// <summary>Explicit host URL to bypass LAN discovery (worker/overseer).</summary>
    public string? HostEndpoint { get; set; }

    /// <summary>Optional parent process id. If the parent exits, this node stops.</summary>
    public int? ParentProcessId { get; set; }

    /// <summary>
    /// Cluster token to adopt on this node's very first run (e.g. passed by the
    /// Launcher when it spawns a co-located Worker during Quickstart). Ignored
    /// once the node already has a token of its own.
    /// </summary>
    public string? SeedClusterToken { get; set; }

    public ProviderSettings Providers { get; set; } = new();
    public WorkerProfileSettings Worker { get; set; } = new();
    public InstallerSettings Installer { get; set; } = new();
    public DiscoverySettings Discovery { get; set; } = new();
    public UiSettings Ui { get; set; } = new();
    public HostSettings Host { get; set; } = new();
    public TlsSettings Tls { get; set; } = new();
}

public sealed class WorkerProfileSettings
{
    /// <summary>Free-form areas this Worker should be preferred for.</summary>
    public List<string> Skills { get; set; } = ["general"];

    /// <summary>Maximum tasks the Host may assign to this Worker at once.</summary>
    public int MaxConcurrentTasks { get; set; } = 1;
}

public sealed class ProviderSettings
{
    /// <summary>Order in which providers are tried before falling back.</summary>
    public List<string> Priority { get; set; } = new() { "anthropic", "gemini", "ollama" };

    /// <summary>Default remote model when a task gives no hint.</summary>
    public string DefaultModel { get; set; } = "claude-opus-4-8";

    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    public int MaxTokens { get; set; } = 4096;

    public string OllamaEndpoint { get; set; } = "http://localhost:11434";

    /// <summary>Null => use the hardware-recommended local model.</summary>
    public string? OllamaModel { get; set; }

    /// <summary>
    /// Hard timeout for a single provider HTTP call (Worker -> Anthropic/Gemini/
    /// Ollama). Prevents a hung local model from occupying a capacity slot
    /// forever; also the backstop when a Worker is queried directly.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 300;
}

public sealed class InstallerSettings
{
    /// <summary>
    /// When true, Workers try to run "ollama pull {recommendedModel}" at startup.
    /// Keep false for the first skeleton so the exe never downloads large models
    /// without an explicit operator choice.
    /// </summary>
    public bool AutoPullOllamaModel { get; set; }
}

public sealed class DiscoverySettings
{
    public string MulticastAddress { get; set; } = "239.7.7.7";
    public int Port { get; set; } = 47777;
    public bool Enabled { get; set; } = true;
}

public sealed class UiSettings
{
    public bool OpenBrowser { get; set; } = true;
}

/// <summary>Host-only orchestration knobs (dispatch timeout, retry, history retention).</summary>
public sealed class HostSettings
{
    /// <summary>Hard ceiling on a single dispatch (Host -> Worker -> provider), including retries.</summary>
    public int DispatchTimeoutSeconds { get; set; } = 600;

    /// <summary>Automatic retries per (sub)task after a non-cancelled failure.</summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>Keep at most this many completed/failed/cancelled top-level goals
    /// (plus their children) in memory and in host-state.json.</summary>
    public int MaxCompletedTasks { get; set; } = 500;

    /// <summary>Keep at most this many chat messages.</summary>
    public int MaxChatMessages { get; set; } = 1000;

    /// <summary>How many prior chat turns to include as context on a new chat message.</summary>
    public int ChatHistoryWindow { get; set; } = 12;

    /// <summary>Optional URL to a JSON update manifest ({"version": "...", "url": "..."}). Empty = disabled.</summary>
    public string? UpdateManifestUrl { get; set; }
}

public sealed class TlsSettings
{
    /// <summary>
    /// Adds an HTTPS Kestrel listener with a self-signed, auto-generated
    /// certificate for node-to-node traffic, alongside the existing plain-HTTP
    /// listener (which stays for the loopback dashboard, so this never causes a
    /// browser certificate warning locally). The cluster token remains the real
    /// authentication boundary - the "cluster" HttpClient trusts any server
    /// certificate, so this buys transport encryption, not certificate-based
    /// server identity.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>HTTPS port = Port + Offset.</summary>
    public int PortOffset { get; set; } = 10000;

    /// <summary>
    /// The HTTPS port for <paramref name="port"/>, or null if TLS is disabled
    /// or the sum would fall outside the valid TCP port range (0-65535) - e.g.
    /// the desktop app's Launcher role binds an OS-assigned ephemeral port,
    /// which can be high enough that adding PortOffset overflows. Callers must
    /// treat null as "no HTTPS for this node" rather than binding/advertising
    /// an invalid port.
    /// </summary>
    public int? HttpsPortFor(int port) =>
        Enabled && port > 0 && port + PortOffset is > 0 and <= 65535
            ? port + PortOffset
            : null;
}
