using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>
/// B5: uppskattar ett uppdrags USD-kostnad över BÅDA prislistorna - ModelCatalog
/// (Anthropic, statisk) och OpenRouter-katalogen (cachad live-pris). Tokens
/// summeras per modell under körningen (WorkerRole), och prissätts EN gång när
/// uppdraget är klart så bygget inte får extra nätlatens. Lokala modeller
/// (Ollama) och okända modeller kostar 0 - gratis compute ska inte redovisas
/// som en utgift, och en okänd prislapp ska inte gissas.
/// </summary>
public static class AssignmentCost
{
    /// <summary>Ren prissättning (testbar utan nät): Anthropic via ModelCatalog,
    /// övriga via den medskickade OpenRouter-katalogen. Returnerar (total,
    /// anyPriced) så anroparen kan skilja "kostade 0" från "okänt pris".</summary>
    public static (decimal Total, bool AnyPriced) Price(
        IReadOnlyDictionary<string, (long In, long Out)> usageByModel,
        IReadOnlyList<CatalogModel> openRouterCatalog)
    {
        decimal total = 0m;
        var any = false;
        foreach (var (model, u) in usageByModel)
        {
            var anthropic = ModelCatalog.EstimateCost(model, Clamp(u.In), Clamp(u.Out));
            if (anthropic is { } a) { total += a; any = true; continue; }

            var m = openRouterCatalog.FirstOrDefault(x => string.Equals(x.Id, model, StringComparison.OrdinalIgnoreCase));
            if (m is not null && (m.InputPerMillion > 0 || m.OutputPerMillion > 0))
            {
                total += (decimal)(u.In / 1_000_000.0 * m.InputPerMillion
                                 + u.Out / 1_000_000.0 * m.OutputPerMillion);
                any = true;
            }
        }
        return (total, any);
    }

    /// <summary>Hämtar OpenRouter-katalogen vid behov (bara om någon icke-
    /// Anthropic-modell användes) och prissätter. Null när ingenting kunde
    /// prissättas (allt lokalt/okänt) - då redovisas ingen siffra i stället för
    /// en missvisande nolla.</summary>
    public static async Task<decimal?> EstimateAsync(
        IReadOnlyDictionary<string, (long In, long Out)> usageByModel,
        IHttpClientFactory httpFactory, CancellationToken ct)
    {
        if (usageByModel.Count == 0) return null;

        IReadOnlyList<CatalogModel> catalog = [];
        if (usageByModel.Keys.Any(m => ModelCatalog.Find(m) is null))
        {
            try { catalog = await new OpenRouterCatalog(httpFactory).GetAsync(ct); }
            catch { catalog = []; }
        }

        var (total, any) = Price(usageByModel, catalog);
        return any ? total : null;
    }

    private static int Clamp(long v) => (int)Math.Min(v, int.MaxValue);
}
