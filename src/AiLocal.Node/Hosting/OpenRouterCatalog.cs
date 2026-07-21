using System.Globalization;
using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>One model from OpenRouter's catalog, reduced to what the model
/// picker needs: id (what we send), display name, context window, per-million
/// input/output price in USD, the Artificial Analysis coding_index (0-100,
/// null when unbenchmarked), and whether it accepts images (vision routing
/// MUST use a multimodal model - the old kimi-k2 vision route was text-only).</summary>
public sealed record CatalogModel(
    string Id, string Name, int ContextLength,
    double InputPerMillion, double OutputPerMillion,
    double? CodingIndex, bool Multimodal);

/// <summary>
/// Fetches and caches OpenRouter's public model catalog
/// (https://openrouter.ai/api/v1/models) so the dashboard's model picker can
/// show live models sorted by coding quality and cost. No API key needed - the
/// catalog is public. Parsing is split out (static, pure) so it is unit-tested
/// without a network call; the fetch layer caches for a while and degrades to
/// the last snapshot (or empty) when OpenRouter is unreachable.
/// </summary>
public sealed class OpenRouterCatalog
{
    private const string Url = "https://openrouter.ai/api/v1/models";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(30);

    private readonly IHttpClientFactory _httpFactory;
    private readonly object _lock = new();
    private IReadOnlyList<CatalogModel>? _cache;
    private DateTimeOffset _cachedAt;

    public OpenRouterCatalog(IHttpClientFactory httpFactory) => _httpFactory = httpFactory;

    public async Task<IReadOnlyList<CatalogModel>> GetAsync(CancellationToken ct)
    {
        lock (_lock)
            if (_cache is not null && DateTimeOffset.UtcNow - _cachedAt < Ttl)
                return _cache;
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(20);
            var json = await client.GetStringAsync(Url, ct);
            var list = Parse(json);
            lock (_lock) { _cache = list; _cachedAt = DateTimeOffset.UtcNow; }
            return list;
        }
        catch
        {
            // Never fail the settings page because OpenRouter hiccuped - serve
            // the last good snapshot, or empty so the UI shows "kunde inte hamta".
            lock (_lock) return _cache ?? [];
        }
    }

    /// <summary>Parse the /models payload. Defensive: any missing/odd field just
    /// defaults (price 0, coding null, not multimodal) rather than throwing.</summary>
    public static IReadOnlyList<CatalogModel> Parse(string json)
    {
        var models = new List<CatalogModel>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            return models;

        foreach (var m in data.EnumerateArray())
        {
            var id = Str(m, "id");
            if (string.IsNullOrWhiteSpace(id)) continue;

            var name = Str(m, "name") ?? id;
            var ctx = m.TryGetProperty("context_length", out var c) && c.TryGetInt32(out var ci) ? ci : 0;

            double inP = 0, outP = 0;
            if (m.TryGetProperty("pricing", out var pr) && pr.ValueKind == JsonValueKind.Object)
            {
                inP = Price(pr, "prompt") * 1_000_000;
                outP = Price(pr, "completion") * 1_000_000;
            }

            double? coding = null;
            if (m.TryGetProperty("benchmarks", out var b) && b.ValueKind == JsonValueKind.Object
                && b.TryGetProperty("artificial_analysis", out var aa) && aa.ValueKind == JsonValueKind.Object
                && aa.TryGetProperty("coding_index", out var cidx) && cidx.ValueKind == JsonValueKind.Number)
                coding = cidx.GetDouble();

            var multimodal = false;
            if (m.TryGetProperty("architecture", out var arch) && arch.ValueKind == JsonValueKind.Object
                && arch.TryGetProperty("input_modalities", out var im) && im.ValueKind == JsonValueKind.Array)
                multimodal = im.EnumerateArray().Any(x => x.ValueKind == JsonValueKind.String && x.GetString() == "image");

            models.Add(new CatalogModel(id, name, ctx,
                Math.Round(inP, 3), Math.Round(outP, 3), coding, multimodal));
        }
        return models;
    }

    private static string? Str(JsonElement e, string prop) =>
        e.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static double Price(JsonElement pricing, string key) =>
        pricing.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
        && double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var d) ? d : 0;
}
