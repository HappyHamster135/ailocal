namespace AiLocal.Core.Contracts;

/// <summary>Provider-agnostic completion request passed down the fallback chain.</summary>
public sealed class ChatRequest
{
    public string? System { get; set; }
    public List<ChatMessage> Messages { get; set; } = new();

    /// <summary>Optional model override, honored by AnthropicProvider only -
    /// every current source of this (the dashboard's model dropdown,
    /// ModelTiers) produces an Anthropic model id, which means nothing to any
    /// other provider. A Gemini/Ollama/OpenRouter model 404s on a Claude id
    /// instead of resolving to anything, which used to break the fallback
    /// chain the moment Anthropic itself failed over to one of them.</summary>
    public string? ModelHint { get; set; }

    /// <summary>Optional per-request provider order, e.g. ["anthropic", "ollama"].</summary>
    public List<string>? ProviderOrder { get; set; }

    public int? MaxTokens { get; set; }

    /// <summary>Tools the model may call this turn. Null/empty means a plain
    /// completion, identical to before this existed.</summary>
    public List<ToolDefinition>? Tools { get; set; }
}
