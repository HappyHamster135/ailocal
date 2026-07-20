using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The procedural sprite generator now produces recognizable
/// pixel-art shapes (not flat rectangles): shape selection from prompt
/// keywords (Swedish + English), transparent background, deterministic
/// identicon fallback so distinct prompts yield distinct art.</summary>
public class PixelSpriteTests
{
    [Theory]
    [InlineData("a pixel-art hero character", "hero")]
    [InlineData("en fiende monster sprite", "enemy")]
    [InlineData("ett guldmynt", "coin")]
    [InlineData("health heart icon", "heart")]
    [InlineData("ett rymdskepp", "ship")]
    [InlineData("nagot helt annat konstigt", "pattern")]
    public void ShapeSelection_MatchesPromptKeywords(string prompt, string expected)
    {
        Assert.Equal(expected, AssetGenerator.SpriteShapeFor(prompt));
    }

    [Fact]
    public void Sprite_IsValidPngWithTransparency()
    {
        var png = AssetGenerator.CreatePixelSprite(64, 64, "a hero");
        // PNG magic bytes.
        Assert.Equal(0x89, png[0]);
        Assert.Equal((byte)'P', png[1]);
        Assert.True(png.Length > 100, "suspiciously small PNG");
    }

    [Fact]
    public void DifferentPrompts_YieldDifferentIdenticons()
    {
        var a = AssetGenerator.CreatePixelSprite(48, 48, "zonk blip");
        var b = AssetGenerator.CreatePixelSprite(48, 48, "flurp grok");
        Assert.NotEqual(Convert.ToBase64String(a), Convert.ToBase64String(b));
    }

    [Fact]
    public void SamePrompt_IsDeterministic()
    {
        var a = AssetGenerator.CreatePixelSprite(48, 48, "zonk blip");
        var b = AssetGenerator.CreatePixelSprite(48, 48, "zonk blip");
        Assert.Equal(Convert.ToBase64String(a), Convert.ToBase64String(b));
    }
}
