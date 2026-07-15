using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

public sealed record ProviderOrderUpdate(List<string> Priority);

public static class ProviderOrderApi
{
    private static readonly string[] KnownProviders = ["anthropic", "openai", "gemini", "openrouter", "ollama"];

    public static object Read(NodeSettings settings) => new
    {
        priority = settings.Providers.Priority,
        options = new[]
        {
            new { id = "anthropic", label = "Claude", local = false },
            new { id = "openai", label = "ChatGPT", local = false },
            new { id = "gemini", label = "Gemini", local = false },
            new { id = "openrouter", label = "OpenRouter", local = false },
            new { id = "ollama", label = "Local", local = true }
        }
    };

    public static object Update(ProviderOrderUpdate request, NodeSettings settings)
    {
        var priority = Normalize(request.Priority);
        settings.Providers.Priority = priority;
        return Read(settings);
    }

    public static List<string> Normalize(IEnumerable<string>? priority)
    {
        var normalized = (priority ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p.Trim())
            .Where(p => KnownProviders.Contains(p, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (normalized.Count == 0)
            normalized.Add("ollama");

        return normalized;
    }
}
