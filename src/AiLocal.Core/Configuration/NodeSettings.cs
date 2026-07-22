using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
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

    /// <summary>How much of this machine an "assignment" (agent-mode) task may
    /// touch. Off by default - only this Worker's own operator can raise it,
    /// never the Host.</summary>
    public AgentAccessLevel AgentAccess { get; set; } = AgentAccessLevel.Off;

    /// <summary>Folder the agent works inside when agent mode is on (Sandboxed:
    /// the access root; Full: the default working directory for run_command).
    /// Null => the Worker's own data dir / agent-workspace. Only this
    /// Worker's own operator can set it - a Host cannot point a Worker's
    /// agent at an arbitrary folder on that machine.</summary>
    public string? WorkspacePath { get; set; }

    /// <summary>Per-complexity model selection the Host uses when it dispatches
    /// an assignment with no explicit model hint, so a trivial task doesn't
    /// burn the most expensive model. Editable per Worker.</summary>
    public ModelTiers ModelTiers { get; set; } = new();

    /// <summary>When on, every file write during a Host-dispatched assignment
    /// is first reviewed by the Host's strongest model (POST
    /// /cluster/review-change); a rejection is fed back to this Worker's
    /// (often small, local) model as a tool error with the reviewer's reason
    /// so it can correct itself. Quality gate for weak models - review
    /// failures fail open, this is not a security boundary.</summary>
    public bool AiReviewWrites { get; set; }

    /// <summary>Pause local builds after the director's delivery contract
    /// until the operator approves or redirects with a note - the studio
    /// checkpoint. Off by default; cluster runs never pause regardless.</summary>
    public bool MilestoneApproval { get; set; }

    /// <summary>v1.92 (opt-in, AV som default): vid nodstart återupptas det
    /// SENASTE omstartsdödade bygget automatiskt (ett enda, max 48h gammalt) -
    /// samma resume-väg som Återuppta-knappen. Default av är en ärlighets-
    /// princip: en omstartad nod ska aldrig börja spendera tokens tyst.</summary>
    public bool AutoResume { get; set; }

    /// <summary>When on, each agent assignment the Host dispatches runs in its
    /// own git worktree+branch (forked from the workspace's current branch), so
    /// multiple "employees" working the same repo never overwrite each other.
    /// After the agent finishes, the Host/operator reviews the diff as a PR and
    /// merges or discards it - discard is the free undo button. Requires the
    /// workspace to be a git repo; a non-repo silently runs un-isolated.</summary>
    public bool UseGitIsolation { get; set; }

    /// <summary>When on, isolated tasks whose CI gate passes are merged
    /// automatically (and discarded if CI fails) instead of waiting for a
    /// manual merge in the Studio "Branches" tab. Requires UseGitIsolation.</summary>
    public bool AutoMergeIsolatedTasks { get; set; }

    /// <summary>Daily USD spend cap (A4). When today's accumulated task cost
    /// reaches this, the dispatcher routes new work to the local Ollama model
    /// instead of paid providers. 0 = no cap.</summary>
    public decimal BudgetLimitUsd { get; set; }

    /// <summary>When on, the agent gets a fetch_url tool (http/https, text
    /// extraction) so it can look things up on the internet. Independent of
    /// AgentAccess - network reach, not filesystem reach.</summary>
    public bool AllowInternet { get; set; }

    /// <summary>Shell-command safety net for agent mode (run_command). Small/local
    /// models can be talked into "rm -rf" by prompt injection, so the guard is
    /// on by default even in Full access mode. Block refuses destructive
    /// commands; Warn runs them but flags it; Off removes the screen entirely.</summary>
    public CommandGuardLevel CommandGuard { get; set; } = CommandGuardLevel.Block;

    /// <summary>Extra case-insensitive regex patterns (besides the built-in
    /// destructive defaults) the command guard should refuse. Operator-specific
    /// bans, e.g. a project's own risky script.</summary>
    public List<string> BlockedCommands { get; set; } = [];

    /// <summary>When on, the agent gets <c>recall</c>/<c>remember</c> tools backed
    /// by a per-project code index + memory file in the workspace, so the
    /// "employees" build up and reuse project knowledge across sessions.</summary>
    public bool ProjectMemoryEnabled { get; set; }

    /// <summary>Opt-in desktop control: when on, the agent (via the Studio
    /// "Skärm" tab or the /api/desktop endpoints) may capture the screen and
    /// inject mouse/keyboard input on THIS machine. Off by default - this is
    /// powerful, so only the operator turns it on deliberately.</summary>
    public bool AllowDesktopControl { get; set; }
}

/// <summary>Which (provider, model) to use for a task of a given skill+complexity.
/// Lets the router send e.g. writing to ChatGPT, coding to Claude, private/data
/// to a local Ollama model - so several models collaborate on one goal instead
/// of everything funnelling through a single provider.</summary>
public sealed record ModelRoute(string Skill, string Provider, string Model, int MinComplexity = 1)
{
    // Live, current-best-value OpenRouter ids (verified mot katalogen). De tre
    // gamla var trasiga: deepseek-coder och tencent/hy3:free 404:ar (retirerade),
    // och kimi-k2 pa vision-routen var TEXT-ONLY - den kunde inte lasa skarm-
    // dumparna den routades for. GLM 5.2 (coding_index 68.8) tar hard kod, DeepSeek
    // V4 Flash (billig + coding_index 56.2) tar latt kod, en multimodal for vision.
    public static List<ModelRoute> Defaults() => new()
    {
        // Coding: billig kapabel coder for latt jobb, GLM 5.2 for de tunga
        // byggena (Godot/Unity-spel hamnar har sedan v1.51).
        new("coding", "openrouter", "deepseek/deepseek-v4-flash", 1),
        new("coding", "openrouter", "z-ai/glm-5.2", 4),
        // Writing/creative: billig oppen modell.
        new("writing", "openrouter", "deepseek/deepseek-v4-flash", 1),
        // Research: billig for latt, GLM 5.2 for det djupa.
        new("research", "openrouter", "deepseek/deepseek-v4-flash", 1),
        new("research", "openrouter", "z-ai/glm-5.2", 3),
        // Data: billig kapabel modell.
        new("data", "openrouter", "deepseek/deepseek-v4-flash", 1),
        // Vision: MASTE vara multimodal (laser skarmdumpar i vision-granskningen).
        new("vision", "openrouter", "qwen/qwen3.7-plus", 1),
        // General: lokal Ollama nar den finns (billig, privat), annars billig OR.
        new("general", "ollama", "", 1),
        new("general", "openrouter", "deepseek/deepseek-v4-flash", 1),
    };

    /// <summary>Modell-id som OpenRouter retirerat (404) eller som hade fel form
    /// (kimi-k2 pa vision ar text-only). Routes gar INTE att andra i UI:t, sa en
    /// nods sparade Routes ar bara en gammal kopia av en aldre fabriksdefault -
    /// darfor byts varje route som fortfarande ligger pa ett retirerat id ut mot
    /// den aktuella defaulten for samma (skill, komplexitet). Aktade custom-routes
    /// (levande id vi inte retirerat) ror vi aldrig.</summary>
    private static readonly HashSet<string> Retired = new(StringComparer.OrdinalIgnoreCase)
    {
        "deepseek/deepseek-coder",   // 404
        "tencent/hy3:free",          // 404
        "moonshotai/kimi-k2",        // gammal K2 0711: ingen kod-benchmark, text-only (dalig for vision)
        "deepseek/deepseek-chat",    // ersatt av deepseek-v4-flash (billigare + benchmarkad)
    };

    public static List<ModelRoute> HealRetired(List<ModelRoute> stored)
    {
        if (stored is null || stored.Count == 0) return Defaults();
        var defaults = Defaults();
        return stored.Select(r =>
            !Retired.Contains(r.Model) ? r
            : defaults.FirstOrDefault(d =>
                d.Skill.Equals(r.Skill, StringComparison.OrdinalIgnoreCase) && d.MinComplexity == r.MinComplexity) ?? r
        ).ToList();
    }
}

/// <summary>Which model the Host picks for a task, keyed off the task's
/// computed complexity (1-5) and skill. Values used to be Anthropic model ids
/// only (see the historical ModelHint note in ChatRequest); now the router
/// picks a (provider, model) pair per task so multiple models collaborate.</summary>
public sealed class ModelTiers
{
    /// <summary>complexity 1-2 (trivial: summaries, short edits, lookups).
    /// Cheap by default - a free/low-cost OpenRouter model, not a paid Claude.</summary>
    public string Simple { get; set; } = "deepseek/deepseek-v4-flash";

    /// <summary>complexity 3-4 (most real work: coding, drafting, analysis).
    /// A strong-but-cheap model (DeepSeek V3 / Kimi) instead of Claude.</summary>
    public string Medium { get; set; } = "deepseek/deepseek-v4-pro";

    /// <summary>complexity 5 (hard: architecture, deep research, debugging).
    /// Falls back to a capable cheap model; Claude only if you opt in.</summary>
    public string Complex { get; set; } = "z-ai/glm-5.2";

    /// <summary>Provider-aware routes. The router picks the first route whose
    /// skill matches the task and whose MinComplexity is met; falls back to the
    /// complexity tiers above (Anthropic) when nothing matches.</summary>
    public List<ModelRoute> Routes { get; set; } = ModelRoute.Defaults();

    /// <summary>Model ids the auto-selector must NEVER pick (the user's ban list
    /// from the model picker). ForTask skips banned routes and steps to a
    /// non-banned tier, so a banned model can't sneak back in via a fallback.</summary>
    public List<string> BannedModels { get; set; } = [];

    public bool IsBanned(string? model) =>
        !string.IsNullOrWhiteSpace(model) && BannedModels.Contains(model, StringComparer.OrdinalIgnoreCase);

    /// <summary>The model for a 1-5 complexity score (Anthropic-only legacy path).</summary>
    public string ForComplexity(int complexity) =>
        complexity <= 2 ? Simple : complexity <= 4 ? Medium : Complex;

    /// <summary>The complexity tier's model, but never a banned one: steps to
    /// another (non-banned) tier rather than returning something the user banned.</summary>
    private string TierModel(int complexity)
    {
        var chosen = ForComplexity(complexity);
        if (!IsBanned(chosen)) return chosen;
        foreach (var alt in new[] { Complex, Medium, Simple })
            if (!IsBanned(alt)) return alt;
        return chosen; // allt bannat - inget bättre att göra
    }

    /// <summary>Pick a (provider, model) for a task. Skill is matched
    /// case-insensitively; if no skill route applies, falls back to the
    /// complexity tier (Anthropic). Returns ("anthropic", tier) as a safe
    /// default so dispatch never ends up with an empty provider.</summary>
    public (string Provider, string Model) ForTask(string? skill, int complexity)
    {
        var norm = (skill ?? "general").Trim().ToLowerInvariant();
        var c = Math.Clamp(complexity, 1, 5);

        var route = Routes
            .Where(r => r.Skill.Equals(norm, StringComparison.OrdinalIgnoreCase) && c >= r.MinComplexity)
            .Where(r => !IsBanned(r.Model)) // hoppa bannade modeller -> fall vidare till nästa route/tier
            .OrderBy(r => r.MinComplexity == c ? 0 : 1) // exact complexity match first
            .ThenByDescending(r => r.MinComplexity)
            .FirstOrDefault();

        if (route is not null)
        {
            var model = string.IsNullOrWhiteSpace(route.Model)
                ? TierModel(c) // ollama route with no explicit model -> use tier default
                : route.Model;
            return (route.Provider, model);
        }

        return ("anthropic", TierModel(c));
    }
}

public sealed class ProviderSettings
{
    /// <summary>Order in which providers are tried before falling back.
    /// OpenRouter is first by default: it proxies cheap models (DeepSeek,
    /// Kimi, Tencent, etc.) so the cluster runs cost-effectively without
    /// burning Anthropic credits - Anthropic/OpenAI/Gemini are only reached
    /// as fallbacks. Flip the order in settings if you'd rather pay for a
    /// specific vendor.</summary>
    public List<string> Priority { get; set; } = new() { "openrouter", "ollama", "anthropic", "openai", "gemini" };

    /// <summary>Default remote model when a task gives no hint. Kept as a
    /// cheap Anthropic id ONLY as the absolute last-resort fallback (Anthropic
    /// is last in Priority); the normal path runs on OpenRouter's cheap
    /// catalog. Avoid setting this to an expensive model - cost lives in the
    /// OpenRouterModel / ModelTiers config, not here.</summary>
    public string DefaultModel { get; set; } = "claude-haiku-4-5";

    public string OpenAIModel { get; set; } = "gpt-4o-mini";

    public string GeminiModel { get; set; } = "gemini-2.5-flash";

    /// <summary>OpenRouter is the cost-effective default provider. "auto"
    /// lets OpenRouter route to a cheap, capable model (DeepSeek/Kimi/Tencent
    /// etc.) per task - no manual id needed. Set explicitly for a specific
    /// model (e.g. "deepseek/deepseek-chat" or "tencent/hy3:free").</summary>
    public string OpenRouterModel { get; set; } = "openrouter/auto";

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

    /// <summary>After MaxRetries failures on the selected (cheaper) model, the
    /// Host escalates the task to a stronger model by bumping its complexity
    /// one notch (capped at 5). MaxEscalations bounds how many times it will
    /// do this before giving up - so a subtask that keeps failing advances to a
    /// bigger model instead of just being dropped.</summary>
    public int MaxEscalations { get; set; } = 2;

    /// <summary>Keep at most this many completed/failed/cancelled top-level goals
    /// (plus their children) in memory and in host-state.json.</summary>
    public int MaxCompletedTasks { get; set; } = 500;

    /// <summary>Keep at most this many chat messages.</summary>
    public int MaxChatMessages { get; set; } = 1000;

    /// <summary>How many prior chat turns to include as context on a new chat message.</summary>
    public int ChatHistoryWindow { get; set; } = 12;

    /// <summary>The roles the Host uses to turn "workers" into a team of
    /// employees (Architect / Developer / Tester / Reviewer). Defaults to the
    /// four standard roles; an operator can rename, re-prompt, or re-skill
    /// them. If empty, the Host falls back to AgentRoles.Defaults().</summary>
    public List<AgentRole> Roles { get; set; } = AgentRoles.Defaults().ToList();

    /// <summary>Resolve a role by id from the configured set, falling back to
    /// the built-in defaults so a partially-edited config still works.</summary>
    public AgentRole? ResolveRole(string? roleId)
    {
        if (string.IsNullOrWhiteSpace(roleId)) return null;
        return Roles.FirstOrDefault(r =>
                r.Id.Equals(roleId, StringComparison.OrdinalIgnoreCase))
            ?? AgentRoles.Defaults().FirstOrDefault(r =>
                r.Id.Equals(roleId, StringComparison.OrdinalIgnoreCase));
    }
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
