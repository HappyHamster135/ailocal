using System.Security.Cryptography;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Roles;
using Microsoft.AspNetCore.DataProtection;

namespace AiLocal.Node.Hosting;

public sealed record SettingsUpdate(
    string? NodeName = null,
    string? HostEndpoint = null,
    bool? DiscoveryEnabled = null,
    List<string>? Skills = null,
    int? MaxConcurrentTasks = null,
    AgentAccessLevel? AgentAccess = null,
    string? ClusterToken = null,
    bool ClearClusterToken = false,
    bool RegenerateClusterToken = false,
    string? OperatorToken = null,
    bool ClearOperatorToken = false,
    bool RegenerateOperatorToken = false,
    bool? StartWithWindows = null,
    List<string>? ProviderPriority = null,
    string? AnthropicModel = null,
    string? GeminiModel = null,
    string? OllamaModel = null,
    string? OllamaEndpoint = null,
    string? OpenRouterModel = null,
    string? OpenAIModel = null,
    int? MaxTokens = null,
    bool? AutoPullOllamaModel = null,
    string? WorkspacePath = null,
    bool? AiReviewWrites = null,
    bool? AllowInternet = null,
    bool? UseGitIsolation = null,
    ModelTiers? ModelTiers = null,
    List<AgentRole>? Roles = null,
    CommandGuardLevel? CommandGuard = null,
    List<string>? BlockedCommands = null,
    bool? ProjectMemoryEnabled = null,
    string? AnthropicApiKey = null,
    string? GeminiApiKey = null,
    string? OpenRouterApiKey = null,
    string? OpenAIApiKey = null,
    bool ClearAnthropicApiKey = false,
    bool ClearGeminiApiKey = false,
    bool ClearOpenRouterApiKey = false,
    bool ClearOpenAIApiKey = false);

internal sealed class StoredNodeSettings
{
    public string? NodeId { get; set; }
    public string? NodeName { get; set; }
    public string? HostEndpoint { get; set; }
    public bool DiscoveryEnabled { get; set; } = true;
    public List<string> Skills { get; set; } = ["general"];
    public int MaxConcurrentTasks { get; set; } = 1;
    public AgentAccessLevel AgentAccess { get; set; } = AgentAccessLevel.Off;
    public string? WorkspacePath { get; set; }
    public bool AiReviewWrites { get; set; }
    public bool AllowInternet { get; set; }
    public bool UseGitIsolation { get; set; }
    public ModelTiers ModelTiers { get; set; } = new();
    public string? ProtectedClusterToken { get; set; }
    public string? ProtectedOperatorToken { get; set; }
    public bool StartWithWindows { get; set; }
    public List<string> ProviderPriority { get; set; } = ["anthropic", "gemini", "openrouter", "ollama"];
    public string AnthropicModel { get; set; } = "claude-opus-4-8";
    public string GeminiModel { get; set; } = "gemini-2.5-flash";
    public string? OllamaModel { get; set; }
    public string OllamaEndpoint { get; set; } = "http://localhost:11434";
    public string OpenRouterModel { get; set; } = "anthropic/claude-sonnet-4.5";
    public string OpenAIModel { get; set; } = "gpt-4o";
    public int MaxTokens { get; set; } = 4096;
    public bool AutoPullOllamaModel { get; set; }
    public string? ProtectedAnthropicApiKey { get; set; }
    public string? ProtectedGeminiApiKey { get; set; }
    public string? ProtectedOpenRouterApiKey { get; set; }
    public string? ProtectedOpenAIApiKey { get; set; }
}

public static class SettingsPaths
{
    public static string DataDirectory =>
        Environment.GetEnvironmentVariable("AILOCAL_DATA_DIR")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiLocal");

    public static string SettingsFile(NodeRole role) =>
        Path.Combine(DataDirectory, $"{role.ToString().ToLowerInvariant()}.settings.json");
}

public sealed class PersistentSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly NodeSettings _settings;
    private readonly IDataProtector _protector;
    private StoredNodeSettings _stored;

    public PersistentSettingsStore(NodeSettings settings, IDataProtectionProvider dataProtection)
    {
        _settings = settings;
        _protector = dataProtection.CreateProtector("AiLocal.ProviderCredentials.v1");
        _stored = ReadStored(settings.Role);
        if (string.IsNullOrWhiteSpace(_stored.NodeId))
        {
            _stored.NodeId = Guid.NewGuid().ToString("n")[..8];

            // First run only: adopt a token passed in from a co-located launch
            // (Quickstart), or - for a brand-new Host - mint one automatically so
            // the cluster is paired-by-default instead of open to the whole LAN.
            if (!string.IsNullOrWhiteSpace(settings.SeedClusterToken))
                _stored.ProtectedClusterToken = _protector.Protect(settings.SeedClusterToken.Trim());
            else if (settings.Role == NodeRole.Host)
                _stored.ProtectedClusterToken = _protector.Protect(GenerateToken());

            CopyCurrentIntoStored();
            Save();
        }

        ApplyAutoStart();
    }

    private static string GenerateToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant();

    public static void LoadInto(NodeSettings settings)
    {
        var stored = ReadStored(settings.Role);
        if (!string.IsNullOrWhiteSpace(stored.NodeName))
            settings.NodeName = stored.NodeName;

        settings.HostEndpoint = stored.HostEndpoint;
        settings.Discovery.Enabled = stored.DiscoveryEnabled;
        settings.Worker.Skills = NormalizeSkills(stored.Skills);
        settings.Worker.MaxConcurrentTasks = Math.Clamp(stored.MaxConcurrentTasks, 1, 32);
        settings.Worker.AgentAccess = stored.AgentAccess;
        settings.Worker.WorkspacePath = stored.WorkspacePath;
        settings.Worker.AiReviewWrites = stored.AiReviewWrites;
        settings.Worker.AllowInternet = stored.AllowInternet;
        settings.Worker.UseGitIsolation = stored.UseGitIsolation;
        settings.Worker.ModelTiers = stored.ModelTiers;
        settings.Providers.Priority = ProviderOrderApi.Normalize(stored.ProviderPriority);
        settings.Providers.DefaultModel = stored.AnthropicModel;
        settings.Providers.GeminiModel = stored.GeminiModel;
        settings.Providers.OllamaModel = stored.OllamaModel;
        settings.Providers.OllamaEndpoint = stored.OllamaEndpoint;
        settings.Providers.OpenRouterModel = stored.OpenRouterModel;
        settings.Providers.OpenAIModel = stored.OpenAIModel;
        settings.Providers.MaxTokens = stored.MaxTokens;
        settings.Installer.AutoPullOllamaModel = stored.AutoPullOllamaModel;
    }

    /// <param name="includeSecrets">False redacts clusterToken/operatorToken
    /// (nulled, not omitted, so callers don't need to special-case a missing
    /// field) while keeping the *Configured booleans and everything else -
    /// use for any caller not verified at admin tier (see
    /// ClusterSecurity.IsAdminTier). The actual admin-facing settings UI
    /// needs the real values (e.g. to display/copy the cluster token to
    /// paste into a Worker manually), so this defaults to true for the many
    /// internal call sites (Update() below, etc.) that were already trusted
    /// before this parameter existed.</param>
    public object Read(bool includeSecrets = true)
    {
        lock (_gate)
        {
            return new
            {
                nodeName = _settings.NodeName,
                hostEndpoint = _settings.HostEndpoint,
                discoveryEnabled = _settings.Discovery.Enabled,
                skills = _settings.Worker.Skills,
                maxConcurrentTasks = _settings.Worker.MaxConcurrentTasks,
                agentAccess = _settings.Worker.AgentAccess.ToString(),
                workspacePath = _settings.Worker.WorkspacePath,
                aiReviewWrites = _settings.Worker.AiReviewWrites,
                allowInternet = _settings.Worker.AllowInternet,
                useGitIsolation = _settings.Worker.UseGitIsolation,
                commandGuard = _settings.Worker.CommandGuard.ToString(),
                blockedCommands = _settings.Worker.BlockedCommands,
                projectMemoryEnabled = _settings.Worker.ProjectMemoryEnabled,
                modelTiers = new
                {
                    simple = _settings.Worker.ModelTiers.Simple,
                    medium = _settings.Worker.ModelTiers.Medium,
                    complex = _settings.Worker.ModelTiers.Complex
                },
                clusterTokenConfigured = HasClusterToken(),
                clusterToken = includeSecrets ? GetClusterToken() : null,
                operatorTokenConfigured = HasOperatorToken(),
                operatorToken = includeSecrets ? GetOperatorToken() : null,
                startWithWindows = _stored.StartWithWindows,
                startWithWindowsSupported = AutoStartManager.IsSupported,
                providerPriority = _settings.Providers.Priority,
                anthropicModel = _settings.Providers.DefaultModel,
                geminiModel = _settings.Providers.GeminiModel,
                ollamaModel = _settings.Providers.OllamaModel,
                ollamaEndpoint = _settings.Providers.OllamaEndpoint,
                openRouterModel = _settings.Providers.OpenRouterModel,
                openAIModel = _settings.Providers.OpenAIModel,
                maxTokens = _settings.Providers.MaxTokens,
                autoPullOllamaModel = _settings.Installer.AutoPullOllamaModel,
                anthropicKeyConfigured = HasKey("anthropic"),
                geminiKeyConfigured = HasKey("gemini"),
                openRouterKeyConfigured = HasKey("openrouter"),
                openAIKeyConfigured = HasKey("openai"),
                settingsPath = SettingsPaths.SettingsFile(_settings.Role)
            };
        }
    }

    public string NodeId
    {
        get
        {
            lock (_gate)
                return _stored.NodeId!;
        }
    }

    public object Update(SettingsUpdate update, HostLocator hostLocator)
    {
        lock (_gate)
        {
            if (update.NodeName is not null)
            {
                var value = update.NodeName.Trim();
                if (value.Length is < 1 or > 80)
                    throw new ArgumentException("Node name must contain 1-80 characters.");
                _settings.NodeName = value;
            }

            if (update.HostEndpoint is not null)
            {
                var value = NormalizeEndpoint(update.HostEndpoint);
                _settings.HostEndpoint = value;
                hostLocator.HostEndpoint = value;
            }

            if (update.DiscoveryEnabled.HasValue)
                _settings.Discovery.Enabled = update.DiscoveryEnabled.Value;

            if (update.Skills is not null)
                _settings.Worker.Skills = NormalizeSkills(update.Skills);

            if (update.MaxConcurrentTasks.HasValue)
            {
                if (update.MaxConcurrentTasks.Value is < 1 or > 32)
                    throw new ArgumentException("Max concurrent tasks must be between 1 and 32.");
                _settings.Worker.MaxConcurrentTasks = update.MaxConcurrentTasks.Value;
            }

            // Off by default and only ever set by this Worker's own operator
            // (see ClusterSecurity/RequiresAdminTier - settings writes are
            // already admin-only) - a Host has no way to turn this on for a
            // Worker it doesn't control.
            if (update.AgentAccess.HasValue)
                _settings.Worker.AgentAccess = update.AgentAccess.Value;

            // Only this Worker's own operator can point its agent at a folder
            // (see AgentAccess: Host can't raise that either). The Host
            // later reads this back via the heartbeat and uses it as the
            // sandbox root / run_command working dir - it can't override it.
            if (update.WorkspacePath is not null)
                _settings.Worker.WorkspacePath = NullIfWhiteSpace(update.WorkspacePath);

            if (update.AiReviewWrites.HasValue)
                _settings.Worker.AiReviewWrites = update.AiReviewWrites.Value;

            if (update.AllowInternet.HasValue)
                _settings.Worker.AllowInternet = update.AllowInternet.Value;

            if (update.UseGitIsolation.HasValue)
                _settings.Worker.UseGitIsolation = update.UseGitIsolation.Value;

            if (update.ModelTiers is not null)
                _settings.Worker.ModelTiers = update.ModelTiers;

            if (update.Roles is not null)
                _settings.Host.Roles = update.Roles;

            if (update.CommandGuard.HasValue)
                _settings.Worker.CommandGuard = update.CommandGuard.Value;
            if (update.BlockedCommands is not null)
                _settings.Worker.BlockedCommands = update.BlockedCommands;
            if (update.ProjectMemoryEnabled.HasValue)
                _settings.Worker.ProjectMemoryEnabled = update.ProjectMemoryEnabled.Value;

            if (update.RegenerateClusterToken)
                _stored.ProtectedClusterToken = _protector.Protect(GenerateToken());
            else if (update.ClearClusterToken)
                _stored.ProtectedClusterToken = null;
            else if (!string.IsNullOrWhiteSpace(update.ClusterToken))
            {
                var token = update.ClusterToken.Trim();
                if (token.Length < 16)
                    throw new ArgumentException("Cluster token must contain at least 16 characters.");
                _stored.ProtectedClusterToken = _protector.Protect(token);
            }

            if (update.RegenerateOperatorToken)
                _stored.ProtectedOperatorToken = _protector.Protect(GenerateToken());
            else if (update.ClearOperatorToken)
                _stored.ProtectedOperatorToken = null;
            else if (!string.IsNullOrWhiteSpace(update.OperatorToken))
            {
                var token = update.OperatorToken.Trim();
                if (token.Length < 16)
                    throw new ArgumentException("Operator token must contain at least 16 characters.");
                _stored.ProtectedOperatorToken = _protector.Protect(token);
            }

            if (update.StartWithWindows.HasValue)
                _stored.StartWithWindows = update.StartWithWindows.Value;

            if (update.ProviderPriority is not null)
                _settings.Providers.Priority = ProviderOrderApi.Normalize(update.ProviderPriority);

            if (update.AnthropicModel is not null)
                _settings.Providers.DefaultModel = Required(update.AnthropicModel, "Claude model");
            if (update.GeminiModel is not null)
                _settings.Providers.GeminiModel = Required(update.GeminiModel, "Gemini model");
            if (update.OllamaModel is not null)
                _settings.Providers.OllamaModel = NullIfWhiteSpace(update.OllamaModel);
            if (update.OllamaEndpoint is not null)
                _settings.Providers.OllamaEndpoint = RequiredEndpoint(update.OllamaEndpoint, "Ollama endpoint");
            if (update.OpenRouterModel is not null)
                _settings.Providers.OpenRouterModel = Required(update.OpenRouterModel, "OpenRouter model");
            if (update.OpenAIModel is not null)
                _settings.Providers.OpenAIModel = Required(update.OpenAIModel, "OpenAI model");

            if (update.MaxTokens.HasValue)
            {
                if (update.MaxTokens.Value is < 128 or > 131072)
                    throw new ArgumentException("Max tokens must be between 128 and 131072.");
                _settings.Providers.MaxTokens = update.MaxTokens.Value;
            }

            if (update.AutoPullOllamaModel.HasValue)
                _settings.Installer.AutoPullOllamaModel = update.AutoPullOllamaModel.Value;

            if (update.ClearAnthropicApiKey)
                _stored.ProtectedAnthropicApiKey = null;
            else if (!string.IsNullOrWhiteSpace(update.AnthropicApiKey))
                _stored.ProtectedAnthropicApiKey = _protector.Protect(update.AnthropicApiKey.Trim());

            if (update.ClearGeminiApiKey)
                _stored.ProtectedGeminiApiKey = null;
            else if (!string.IsNullOrWhiteSpace(update.GeminiApiKey))
                _stored.ProtectedGeminiApiKey = _protector.Protect(update.GeminiApiKey.Trim());

            if (update.ClearOpenRouterApiKey)
                _stored.ProtectedOpenRouterApiKey = null;
            else if (!string.IsNullOrWhiteSpace(update.OpenRouterApiKey))
                _stored.ProtectedOpenRouterApiKey = _protector.Protect(update.OpenRouterApiKey.Trim());

            if (update.ClearOpenAIApiKey)
                _stored.ProtectedOpenAIApiKey = null;
            else if (!string.IsNullOrWhiteSpace(update.OpenAIApiKey))
                _stored.ProtectedOpenAIApiKey = _protector.Protect(update.OpenAIApiKey.Trim());

            CopyCurrentIntoStored();
            Save();
            ApplyAutoStart();
            return Read();
        }
    }

    private void ApplyAutoStart()
    {
        if (!AutoStartManager.IsSupported)
            return;

        try
        {
            if (_stored.StartWithWindows)
                AutoStartManager.Enable(_settings);
            else
                AutoStartManager.Disable(_settings.Role);
        }
        catch
        {
            // Best-effort only - a registry failure should never block settings.
        }
    }

    public string? GetApiKey(string provider)
    {
        lock (_gate)
        {
            var environmentName = provider.ToLowerInvariant() switch
            {
                "anthropic" => "ANTHROPIC_API_KEY",
                "gemini" => "GEMINI_API_KEY",
                "openrouter" => "OPENROUTER_API_KEY",
                "openai" => "OPENAI_API_KEY",
                _ => null
            };

            if (environmentName is not null &&
                Environment.GetEnvironmentVariable(environmentName) is { Length: > 0 } environmentValue)
                return environmentValue;

            var protectedValue = provider.ToLowerInvariant() switch
            {
                "anthropic" => _stored.ProtectedAnthropicApiKey,
                "gemini" => _stored.ProtectedGeminiApiKey,
                "openrouter" => _stored.ProtectedOpenRouterApiKey,
                "openai" => _stored.ProtectedOpenAIApiKey,
                _ => null
            };

            if (string.IsNullOrWhiteSpace(protectedValue))
                return null;

            try { return _protector.Unprotect(protectedValue); }
            catch { return null; }
        }
    }

    public string? GetClusterToken()
    {
        lock (_gate)
        {
            if (Environment.GetEnvironmentVariable("AILOCAL_CLUSTER_TOKEN") is { Length: > 0 } environmentValue)
                return environmentValue;

            if (string.IsNullOrWhiteSpace(_stored.ProtectedClusterToken))
                return null;

            try { return _protector.Unprotect(_stored.ProtectedClusterToken); }
            catch { return null; }
        }
    }

    /// <summary>
    /// Lower-privilege token: can submit/view goals, chat, and cancel tasks, but
    /// cannot remove/restore nodes, change settings, run worker setup, or manage
    /// the admin (cluster) token itself. See ClusterSecurity.RequiresAdminTier.
    /// </summary>
    public string? GetOperatorToken()
    {
        lock (_gate)
        {
            if (Environment.GetEnvironmentVariable("AILOCAL_OPERATOR_TOKEN") is { Length: > 0 } environmentValue)
                return environmentValue;

            if (string.IsNullOrWhiteSpace(_stored.ProtectedOperatorToken))
                return null;

            try { return _protector.Unprotect(_stored.ProtectedOperatorToken); }
            catch { return null; }
        }
    }

    private bool HasClusterToken() => !string.IsNullOrWhiteSpace(GetClusterToken());

    private bool HasOperatorToken() => !string.IsNullOrWhiteSpace(GetOperatorToken());

    private bool HasKey(string provider) => !string.IsNullOrWhiteSpace(GetApiKey(provider));

    private void CopyCurrentIntoStored()
    {
        _stored.NodeName = _settings.NodeName;
        _stored.HostEndpoint = _settings.HostEndpoint;
        _stored.DiscoveryEnabled = _settings.Discovery.Enabled;
        _stored.Skills = [.. _settings.Worker.Skills];
        _stored.MaxConcurrentTasks = _settings.Worker.MaxConcurrentTasks;
        _stored.AgentAccess = _settings.Worker.AgentAccess;
        _stored.WorkspacePath = _settings.Worker.WorkspacePath;
        _stored.AiReviewWrites = _settings.Worker.AiReviewWrites;
        _stored.AllowInternet = _settings.Worker.AllowInternet;
        _stored.UseGitIsolation = _settings.Worker.UseGitIsolation;
        _stored.ModelTiers = _settings.Worker.ModelTiers;
        // do not overwrite them from NodeSettings here (NodeSettings has no field
        // for them, so this stays intentionally silent about that pair).
        _stored.ProviderPriority = [.. _settings.Providers.Priority];
        _stored.AnthropicModel = _settings.Providers.DefaultModel;
        _stored.GeminiModel = _settings.Providers.GeminiModel;
        _stored.OllamaModel = _settings.Providers.OllamaModel;
        _stored.OllamaEndpoint = _settings.Providers.OllamaEndpoint;
        _stored.OpenRouterModel = _settings.Providers.OpenRouterModel;
        _stored.OpenAIModel = _settings.Providers.OpenAIModel;
        _stored.MaxTokens = _settings.Providers.MaxTokens;
        _stored.AutoPullOllamaModel = _settings.Installer.AutoPullOllamaModel;
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        var settingsFile = SettingsPaths.SettingsFile(_settings.Role);
        var backupFile = settingsFile + ".bak";
        var temporary = settingsFile + ".tmp";

        // Keep a backup of the last-known-good file before overwriting it -
        // mirrors HostStateStore's already-proven primary+backup pattern, so
        // a settingsFile that somehow ends up corrupted (this app's own write
        // path is already crash-safe via the temp+move below, but the file
        // can still be damaged by something outside this app's control - a
        // bad backup/AV tool, disk corruption, manual editing) has a fallback
        // in ReadStored below instead of silently resetting to blank -
        // losing this node's identity, cluster/operator tokens, and API keys.
        if (File.Exists(settingsFile))
        {
            try { File.Copy(settingsFile, backupFile, overwrite: true); }
            catch { /* best effort - the primary write below still proceeds */ }
        }

        File.WriteAllText(temporary, JsonSerializer.Serialize(_stored, JsonOptions));
        File.Move(temporary, settingsFile, true);
    }

    private static StoredNodeSettings ReadStored(NodeRole role)
    {
        var settingsFile = SettingsPaths.SettingsFile(role);
        try
        {
            if (!File.Exists(settingsFile))
                return new StoredNodeSettings();

            return JsonSerializer.Deserialize<StoredNodeSettings>(
                File.ReadAllText(settingsFile), JsonOptions) ?? new StoredNodeSettings();
        }
        catch (Exception ex)
        {
            // This used to silently return a blank StoredNodeSettings() here,
            // with no trace anywhere - the caller then mints a brand new
            // NodeId, so a corrupted file meant silently losing this node's
            // cluster identity, tokens, and API keys with zero indication why
            // pairing/logins started asking for everything again. Try the
            // backup Save() now maintains before giving up to blank, and log
            // either way via CrashLog (no ILogger available yet this early -
            // this runs from the constructor, before DI's logging pipeline
            // exists for this class).
            CrashLog.Write($"SettingsCorrupted({role})", ex);
            try
            {
                var backupFile = settingsFile + ".bak";
                if (File.Exists(backupFile))
                {
                    var restored = JsonSerializer.Deserialize<StoredNodeSettings>(
                        File.ReadAllText(backupFile), JsonOptions);
                    if (restored is not null)
                        return restored;
                }
            }
            catch (Exception backupEx)
            {
                CrashLog.Write($"SettingsBackupAlsoCorrupted({role})", backupEx);
            }

            return new StoredNodeSettings();
        }
    }

    private static string Required(string value, string label) =>
        !string.IsNullOrWhiteSpace(value) ? value.Trim() : throw new ArgumentException($"{label} is required.");

    private static string? NullIfWhiteSpace(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeEndpoint(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return RequiredEndpoint(value, "Host endpoint");
    }

    private static string RequiredEndpoint(string value, string label)
    {
        var trimmed = value.Trim().TrimEnd('/');
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException($"{label} must be an http or https URL.");
        return trimmed;
    }

    private static List<string> NormalizeSkills(IEnumerable<string>? skills)
    {
        var normalized = (skills ?? [])
            .SelectMany(skill => skill.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(skill => skill.Trim().ToLowerInvariant())
            .Where(skill => skill.Length is > 0 and <= 40)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        if (normalized.Count == 0)
            normalized.Add("general");

        return normalized;
    }
}
