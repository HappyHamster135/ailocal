namespace AiLocal.Core.Contracts;

/// <summary>Provider-agnostic completion request passed down the fallback chain.</summary>
public sealed class ChatRequest
{
    public string? System { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Optional model override, honored by the matched provider. Sources
    /// now produce provider-specific model ids via ModelTiers.ForTask, not just
    /// Anthropic ids.</summary>
    public string? ModelHint { get; set; }

    /// <summary>Optional preferred provider for this request (e.g. "openai" for a
    /// writing task). When set, the fallback chain tries it first, then the rest
    /// of ProviderOrder as backup - so the router can steer a task to a specific
    /// model while still degrading gracefully if that provider is down.</summary>
    public string? PreferredProvider { get; set; }

    /// <summary>Optional per-request provider order, e.g. ["anthropic", "ollama"].</summary>
    public List<string>? ProviderOrder { get; set; }

    public int? MaxTokens { get; set; }

    /// <summary>Tools the model may call this turn. Null/empty means a plain
    /// completion, identical to before this existed.</summary>
    public List<ToolDefinition>? Tools { get; set; }
}
