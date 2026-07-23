using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.16: konstpipen - AI-bild till AKTA pixelart (grid/palett/alfa/kontur)
/// och EN stillbild till ANIMERAD karaktar (puppet-frames). Later exakt de
/// fel agarens transkript visade (1024x1024 RGB utan alfa, omojliga sprite-
/// sheets) bli deterministiskt fixade.
/// </summary>
public class PixelArtTests
{
    /// <summary>Syntetisk "AI-bild": stor, enfärgad ljus bakgrund, färgad
    /// cirkelblob i mitten - som molnmodellernas råa output.</summary>
    static (byte[] Rgba, int W, int H) FakeCloudImage(int size = 256)
    {
        var rgba = new byte[size * size * 4];
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                var i = (y * size + x) * 4;
                var dx = x - size / 2;
                var dy = y - size / 2;
                var inside = dx * dx + dy * dy < size * size / 16;
                if (inside)
                {
                    rgba[i] = (byte)(180 + (x % 3) * 20); // lite brus = kvantiseringsjobb
                    rgba[i + 1] = 60;
                    rgba[i + 2] = 90;
                }
                else
                {
                    rgba[i] = 245; rgba[i + 1] = 244; rgba[i + 2] = 240; // ljus bakgrund
                }
                rgba[i + 3] = 255; // INGEN alfa - som molnbilderna
            }
        return (rgba, size, size);
    }

    [Fact]
    public void PixelArt_Process_GerGridPalettAlfaOchKontur()
    {
        var (rgba, w, h) = FakeCloudImage();
        var (outRgba, ow, oh) = PixelArtPipeline.Process(rgba, w, h, targetSize: 32, paletteSize: 8);

        Assert.True(ow <= 34 && oh <= 34, $"malstorlek ska hallas ({ow}x{oh})");
        Assert.True(ow >= 8 && oh >= 8, "innehallet ska finnas kvar");
        // Hornen ska vara transparenta (bakgrunden bortfloodad).
        Assert.Equal(0, outRgba[3]);
        Assert.Equal(0, outRgba[(ow - 1) * 4 + 3]);
        // Palett: antal unika synliga farger ska vara begransat (8 + kontur).
        var colors = new HashSet<int>();
        var solid = 0;
        for (var i = 0; i < outRgba.Length; i += 4)
        {
            if (outRgba[i + 3] == 0) continue;
            solid++;
            colors.Add((outRgba[i] << 16) | (outRgba[i + 1] << 8) | outRgba[i + 2]);
        }
        Assert.True(solid > 20, "spriten ska ha synligt innehall");
        Assert.True(colors.Count <= 9, $"palettkvantiseringen ska begransa fargerna (fick {colors.Count})");
        // Konturen: minst en pixel med konturfargen (mork).
        Assert.Contains(true, EnumeratePixels(outRgba).Select(p => p is { R: 24, G: 20, B: 32, A: 255 }));
    }

    static IEnumerable<(byte R, byte G, byte B, byte A)> EnumeratePixels(byte[] rgba)
    {
        for (var i = 0; i < rgba.Length; i += 4)
            yield return (rgba[i], rgba[i + 1], rgba[i + 2], rgba[i + 3]);
    }

    [Fact]
    public void SpriteAnimator_EnBild_BlirIdlePlusWalk()
    {
        var (rgba, w, h) = FakeCloudImage(64);
        // Gor bakgrunden transparent forst (som pipen gor).
        var (sprite, sw, sh) = PixelArtPipeline.Process(rgba, w, h, targetSize: 32, paletteSize: 8);

        var sheet = SpriteAnimator.BuildSheet(sprite, sw, sh);
        Assert.Equal(6, sheet.FrameCount);
        Assert.Contains(sheet.Anims, a => a.Name == "idle");
        Assert.Contains(sheet.Anims, a => a.Name == "walk");
        // Sheet-PNG:n ska vara avkodbar och ha ratta matten.
        var decoded = PixelArtPipeline.DecodePng(sheet.Png);
        Assert.NotNull(decoded);
        Assert.Equal(sheet.FrameWidth * 6, decoded!.Value.W);
        Assert.Equal(sheet.FrameHeight, decoded.Value.H);
    }

    [Fact]
    public async Task GenerateAsset_PixelartSprite_UtanNycklar_GerAnimeradTres()
    {
        // Utan molnnycklar ska pixelart-laget ALDRIG ge en stum platta -
        // den procedurella riggen levererar sheet + .tres direkt.
        var dir = Directory.CreateTempSubdirectory("ailocal-px-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"Px\"\n");
            var gen = new AssetGenerator();
            var result = await gen.GenerateAsync("sprite:pixelart", "a cute round candy hero",
                24, null, Path.Combine(dir, "assets", "hero.png"), CancellationToken.None);
            Assert.True(result.Success, result.Output);
            Assert.True(File.Exists(Path.Combine(dir, "assets", "hero.png")));
            Assert.True(File.Exists(Path.Combine(dir, "assets", "hero_frames.tres")), "animerade frames ska skrivas");
            var tres = File.ReadAllText(Path.Combine(dir, "assets", "hero_frames.tres"));
            Assert.Contains("res://assets/hero_sheet.png", tres);
            Assert.Contains("idle", tres);
            Assert.Contains("walk", tres);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
