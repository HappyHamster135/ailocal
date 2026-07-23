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
    public void Despeckle_EnsammaPixlarStadas_KlusterBehalls()
    {
        // 6x6: rod platta med EN gron brusig pixel i mitten + en ensam
        // o-pixel i ett transparent horn. Bruset ska ta grannfargen,
        // on ska forsvinna, plattan ska besta.
        const int W = 6;
        var rgba = new byte[W * W * 4];
        void Set(int x, int y, byte r, byte g, byte b, byte a = 255)
        {
            var i = (y * W + x) * 4;
            rgba[i] = r; rgba[i + 1] = g; rgba[i + 2] = b; rgba[i + 3] = a;
        }
        for (var y = 1; y < 5; y++)
            for (var x = 1; x < 5; x++)
                Set(x, y, 200, 40, 40);
        Set(3, 3, 40, 200, 40);   // brus mitt i plattan
        Set(0, 5, 40, 40, 200);   // ensam o i transparens (hornet)

        PixelArtPipeline.Despeckle(rgba, W, W);

        var mid = (3 * W + 3) * 4;
        Assert.Equal(200, rgba[mid]);          // bruset tog plattans farg
        Assert.Equal(0, rgba[(5 * W + 0) * 4 + 3]); // on rensades
        Assert.Equal(255, rgba[(1 * W + 1) * 4 + 3]); // plattan bestar
    }

    [Fact]
    public void PixelAnimator_V217_HarKonturOchRamper()
    {
        // Riktig pixelart = sluten mork kontur + ramper (2-3 nyanser per
        // material), inte fyra platta farger (agarens "2004-webbspel"-dom).
        var frames = PixelAnimator.Frames("hjalte");
        var frame = frames[0];
        var colors = new HashSet<int>();
        var outline = false;
        for (var i = 0; i < frame.Length; i += 4)
        {
            if (frame[i + 3] == 0) continue;
            colors.Add((frame[i] << 16) | (frame[i + 1] << 8) | frame[i + 2]);
            if (frame[i] == 27 && frame[i + 1] == 22 && frame[i + 2] == 36) outline = true;
        }
        Assert.True(outline, "innerline-konturen ska finnas");
        Assert.True(colors.Count >= 9, $"ramperna ska ge minst 9 farger (fick {colors.Count})");
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
    public void ToPixelArtBackground_V225_HelbildOpakMedPalett()
    {
        // Bakgrundsvagen ar sprite-vagens motsats: HELA bilden behalls,
        // ingen transparens (hornen ar kvar), ingen kontur - bara grid+palett.
        var (rgba, w, h) = FakeCloudImage(256);
        var png = AssetGenerator.EncodePng(w, h, rgba);
        var plate = PixelArtPipeline.ToPixelArtBackground(png, targetWidth: 240, paletteSize: 24);
        Assert.NotNull(plate);
        var decoded = PixelArtPipeline.DecodePng(plate!);
        Assert.NotNull(decoded);
        var (outRgba, ow, oh) = decoded!.Value;
        Assert.True(ow is >= 230 and <= 244, $"bredden ska landa vid target ({ow})");
        Assert.True(oh is >= 230 and <= 244, $"kvadratisk kalla ger kvadratiskt resultat ({oh})");
        var colors = new HashSet<int>();
        for (var i = 0; i < outRgba.Length; i += 4)
        {
            Assert.Equal(255, outRgba[i + 3]); // OPAK overallt - inga hal i plattan
            colors.Add((outRgba[i] << 16) | (outRgba[i + 1] << 8) | outRgba[i + 2]);
        }
        Assert.True(colors.Count is >= 2 and <= 24, $"paletten ska vara begransad (fick {colors.Count})");
    }

    [Fact]
    public void PixelBackdrop_V225_DeterministiskOchTemastyrd()
    {
        // Samma prompt = exakt samma bytes (LCG, inte System.Random) -
        // bakgrunden ska vara reproducerbar pa alla noder.
        var a = PixelBackdrop.Build("a spooky forest clearing", 240, 135);
        var b = PixelBackdrop.Build("a spooky forest clearing", 240, 135);
        Assert.Equal(a, b);

        // Olika teman ger olika bilder.
        var space = PixelBackdrop.Build("space nebula with planets", 240, 135);
        Assert.NotEqual(a, space);

        // Ratt dimensioner och helt opak.
        var decoded = PixelArtPipeline.DecodePng(space);
        Assert.NotNull(decoded);
        Assert.Equal(240, decoded!.Value.W);
        Assert.Equal(135, decoded.Value.H);
        for (var i = 3; i < decoded.Value.Rgba.Length; i += 4)
            Assert.Equal(255, decoded.Value.Rgba[i]);
    }

    [Fact]
    public async Task GenerateAsset_PixelartBackground_UtanNycklar_GerProcedurellScen()
    {
        // Utan molnnycklar ska background:pixelart ge PixelBackdrop-scenen -
        // aldrig identicon-plattan, och ingen _frames.tres (bakgrund animeras inte).
        var dir = Directory.CreateTempSubdirectory("ailocal-bg-").FullName;
        try
        {
            var gen = new AssetGenerator();
            var result = await gen.GenerateAsync("background:pixelart", "forest at dusk",
                null, null, Path.Combine(dir, "assets", "bg.png"), CancellationToken.None);
            Assert.True(result.Success, result.Output);
            Assert.True(File.Exists(Path.Combine(dir, "assets", "bg.png")));
            Assert.False(File.Exists(Path.Combine(dir, "assets", "bg_frames.tres")));
            Assert.Contains("bakgrund", result.Output, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("TextureRect", result.Output);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
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
