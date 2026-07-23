namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.16: EN stillbild -> ANIMERAD karaktar. Bildmodeller kan inte halla
/// samma figur konsekvent over flera frames (agarens transkript: fyra
/// "walk-frames" blev fyra olika figurer) - sa animationen byggs i stallet
/// DETERMINISTISKT fran en enda sprite med klassiska puppet-transformer:
///   idle: 2 frames (andnings-bob + latt squash)
///   walk: 4 frames (bob + lutning framat + squash/stretch-cykel)
/// Resultatet ar samma AnimatedSpriteSheet som PixelAnimator producerar,
/// sa GodotSpriteFrames skriver .tres:en och spelet far en AnimatedSprite2D.
/// </summary>
public static class SpriteAnimator
{
    /// <summary>Bygger idle(2)+walk(4)-sheet ur en RGBA-sprite. Frames blir
    /// kvadratiska med liten marginal så rotation/squash aldrig klipps.</summary>
    public static AnimatedSpriteSheet BuildSheet(byte[] rgba, int w, int h)
    {
        var size = Math.Max(w, h) + Math.Max(4, Math.Max(w, h) / 6);
        var frames = new List<byte[]>
        {
            // idle: vilofas + mjuk inandning (spriten 3% bredare, 3% kortare)
            Transform(rgba, w, h, size, 0.0, 1.00, 1.00, 0.0),
            Transform(rgba, w, h, size, 0.0, 1.03, 0.97, 0.5),
            // walk: framatlutning + bob + squash/stretch-cykel
            Transform(rgba, w, h, size, 0.10, 1.02, 0.98, 1.0),
            Transform(rgba, w, h, size, 0.00, 0.96, 1.05, -1.5),
            Transform(rgba, w, h, size, -0.10, 1.02, 0.98, 1.0),
            Transform(rgba, w, h, size, 0.00, 0.96, 1.05, -1.5),
        };
        var png = Compose(frames, size);
        var anims = new List<SpriteAnim>
        {
            new("idle", 0, 2, 3, true),
            new("walk", 2, 4, 8, true),
        };
        return new AnimatedSpriteSheet(png, size, size, frames.Count, anims);
    }

    /// <summary>Samplar källspriten in i en kvadratisk frame med rotation
    /// (radianer) kring botten-mitt, separat x/y-skala och y-offset (bob).
    /// Invers mappning + nearest = pixelartens hårda kanter bevaras.</summary>
    static byte[] Transform(byte[] rgba, int w, int h, int size, double rot, double sx, double sy, double bobY)
    {
        var frame = new byte[size * size * 4];
        // Ankare: botten-mitt (fötterna står stilla, kroppen rör sig).
        var ax = w / 2.0;
        var ay = (double)h;
        var dx = size / 2.0;
        var dy = size - 1.0 - bobY;
        var cos = Math.Cos(-rot);
        var sin = Math.Sin(-rot);
        for (var y = 0; y < size; y++)
            for (var x = 0; x < size; x++)
            {
                // dest -> källkoordinat: flytta till ankare, rotera baklänges,
                // skala baklänges.
                var rx = x - dx;
                var ry = y - dy;
                var ux = (rx * cos - ry * sin) / sx + ax;
                var uy = (rx * sin + ry * cos) / sy + ay;
                var sxI = (int)Math.Round(ux);
                var syI = (int)Math.Round(uy);
                if (sxI < 0 || syI < 0 || sxI >= w || syI >= h) continue;
                var s = (syI * w + sxI) * 4;
                if (rgba[s + 3] == 0) continue;
                var d = (y * size + x) * 4;
                frame[d] = rgba[s];
                frame[d + 1] = rgba[s + 1];
                frame[d + 2] = rgba[s + 2];
                frame[d + 3] = rgba[s + 3];
            }
        return frame;
    }

    static byte[] Compose(List<byte[]> frames, int size)
    {
        var sheetW = size * frames.Count;
        var sheet = new byte[sheetW * size * 4];
        for (var f = 0; f < frames.Count; f++)
            for (var y = 0; y < size; y++)
                for (var x = 0; x < size; x++)
                {
                    var s = (y * size + x) * 4;
                    var d = (y * sheetW + f * size + x) * 4;
                    sheet[d] = frames[f][s];
                    sheet[d + 1] = frames[f][s + 1];
                    sheet[d + 2] = frames[f][s + 2];
                    sheet[d + 3] = frames[f][s + 3];
                }
        return AssetGenerator.EncodePng(sheetW, size, sheet);
    }
}
