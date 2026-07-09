namespace AiLocal.Core.Providers;

/// <summary>Static metadata used for cost display and model selection.</summary>
public sealed record ModelInfo(
    string Id,
    string Provider,
    decimal InputPerMTok,
    decimal OutputPerMTok,
    int ContextWindow);

public static class ModelCatalog
{
    // USD per 1M tokens. Anthropic pricing snapshot checked 2026-07-08; verify against
    // https://platform.claude.com/docs/en/about-claude/models/overview before billing.
    public static readonly IReadOnlyList<ModelInfo> Anthropic = new List<ModelInfo>
    {
        new("claude-fable-5",   "anthropic", 10.00m, 50.00m, 1_000_000),
        new("claude-opus-4-8",  "anthropic",  5.00m, 25.00m, 1_000_000),
        new("claude-sonnet-5",  "anthropic",  2.00m, 10.00m, 1_000_000),
        new("claude-haiku-4-5", "anthropic",  1.00m,  5.00m,   200_000),
        new("claude-haiku-4-5-20251001", "anthropic", 1.00m, 5.00m, 200_000),
    };

    public static ModelInfo? Find(string id) =>
        Anthropic.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>Estimated USD cost of a completion, or null for unknown/local models.</summary>
    public static decimal? EstimateCost(string modelId, int inputTokens, int outputTokens)
    {
        var m = Find(modelId);
        if (m is null) return null;
        return inputTokens / 1_000_000m * m.InputPerMTok
             + outputTokens / 1_000_000m * m.OutputPerMTok;
    }
}
