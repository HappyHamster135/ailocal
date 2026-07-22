using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>B5 (kostnadsredovisning): prissättningen ska täcka BÅDA prislistorna
/// - Anthropic (ModelCatalog) och OpenRouter (katalogen) - och behandla lokala/
/// okända modeller som 0 utan att gissa ett pris (då redovisas ingen siffra).</summary>
public class AssignmentCostTests
{
    [Fact]
    public void Price_AnthropicOchOpenRouter_SummerasKorrekt()
    {
        // claude-opus-4-8: 5 USD in / 25 USD ut per 1M -> 1M+1M = 30 USD.
        // OpenRouter-modell: 2 in / 6 ut per 1M -> 500k in + 1M ut = 1 + 6 = 7.
        var usage = new Dictionary<string, (long In, long Out)>
        {
            ["claude-opus-4-8"] = (1_000_000, 1_000_000),
            ["z-ai/glm-x"] = (500_000, 1_000_000),
        };
        var catalog = new List<CatalogModel>
        {
            new("z-ai/glm-x", "GLM X", 128_000, 2.0, 6.0, 70, false),
        };

        var (total, any) = AssignmentCost.Price(usage, catalog);

        Assert.True(any);
        Assert.Equal(37.0m, total, 2); // 30 + 7
    }

    [Fact]
    public void Price_OkandEllerLokalModell_RaknasSomNoll()
    {
        var usage = new Dictionary<string, (long In, long Out)>
        {
            ["llama3.1:8b"] = (2_000_000, 1_000_000), // inte i någon prislista
        };
        var (total, any) = AssignmentCost.Price(usage, new List<CatalogModel>());

        Assert.False(any);       // inget kunde prissättas -> ingen siffra redovisas
        Assert.Equal(0m, total);
    }

    [Fact]
    public void Price_OpenRouterMedNollpris_RaknasInte()
    {
        var usage = new Dictionary<string, (long In, long Out)> { ["free/model"] = (1_000_000, 1_000_000) };
        var catalog = new List<CatalogModel> { new("free/model", "Free", 8_000, 0.0, 0.0, null, false) };

        var (total, any) = AssignmentCost.Price(usage, catalog);

        Assert.False(any);  // pris 0 = gratis = ingen redovisad kostnad
        Assert.Equal(0m, total);
    }
}
