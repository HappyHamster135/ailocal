namespace AiLocal.Core.Contracts;

/// <summary>Provider-agnostic completion request passed down the fallback chain.</summary>
public sealed class ChatRequest
{
    public string? System { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Optional model override; null uses the provider default.</summary>
    public string? ModelHint { get; set; }

    /// <summary>Optional per-request provider order, e.g. ["anthropic", "ollama"].</summary>
    public List<string>? ProviderOrder { get; set; }

    public int? MaxTokens { get; set; }
}
