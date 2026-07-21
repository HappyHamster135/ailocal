using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>OpenRouterCatalog.Parse: turns the /api/v1/models payload into the
/// fields the model picker needs (id, price per million, coding_index,
/// multimodal), defensively defaulting on missing fields.</summary>
public class ModelCatalogTests
{
    private const string Sample = """
        {"data":[
          {"id":"z-ai/glm-5.2","name":"GLM 5.2","context_length":1048576,
           "pricing":{"prompt":"0.00000079","completion":"0.0000025"},
           "benchmarks":{"artificial_analysis":{"coding_index":68.8}},
           "architecture":{"input_modalities":["text"]}},
          {"id":"qwen/qwen3.7-plus","name":"Qwen 3.7 Plus","context_length":131072,
           "pricing":{"prompt":"0.00000032","completion":"0.0000012"},
           "benchmarks":{"artificial_analysis":{"coding_index":55.9}},
           "architecture":{"input_modalities":["text","image"]}},
          {"id":"bare/model","name":"Bare"}
        ]}
        """;

    [Fact]
    public void Parse_ExtractsPriceCodingAndModality()
    {
        var models = OpenRouterCatalog.Parse(Sample);
        Assert.Equal(3, models.Count);

        var glm = models.Single(m => m.Id == "z-ai/glm-5.2");
        Assert.Equal(0.79, glm.InputPerMillion, 3);      // 0.00000079 * 1e6
        Assert.Equal(2.5, glm.OutputPerMillion, 3);
        Assert.Equal(68.8, glm.CodingIndex);
        Assert.False(glm.Multimodal);                    // text-only
        Assert.Equal(1048576, glm.ContextLength);

        var qwen = models.Single(m => m.Id == "qwen/qwen3.7-plus");
        Assert.True(qwen.Multimodal);                    // accepterar bild
        Assert.Equal(55.9, qwen.CodingIndex);

        // Ett magert objekt utan pris/benchmark/arkitektur ska inte krascha.
        var bare = models.Single(m => m.Id == "bare/model");
        Assert.Equal(0, bare.InputPerMillion);
        Assert.Null(bare.CodingIndex);
        Assert.False(bare.Multimodal);
    }

    [Fact]
    public void Parse_EmptyOrJunk_ReturnsEmpty()
    {
        Assert.Empty(OpenRouterCatalog.Parse("{}"));
        Assert.Empty(OpenRouterCatalog.Parse("""{"data":"nope"}"""));
    }
}
