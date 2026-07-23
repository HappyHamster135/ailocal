namespace AiLocal.Node.Hosting;

/// <summary>One named animation inside a spritesheet: which frames it uses,
/// how fast, and whether it loops. Godot's SpriteFrames and an HTML5 CSS
/// steps() animation are both generated from this.</summary>
public sealed record SpriteAnim(string Name, int StartFrame, int FrameCount, double Fps, bool Loop);

/// <summary>A horizontal spritesheet (all frames in one row) plus the animations
/// that index into it.</summary>
public sealed record AnimatedSpriteSheet(byte[] Png, int FrameWidth, int FrameHeight, int FrameCount, IReadOnlyList<SpriteAnim> Anims);

/// <summary>
/// Procedural multi-FRAME pixel-art characters - the frame-by-frame animation
/// the old single-sprite pipeline lacked. Draws a small humanoid
/// parametrically with a real walk cycle (the legs actually alternate) and an
/// idle bob, then composes the frames into one horizontal spritesheet.
/// Deterministic per prompt so a scaffold or a regen is reproducible. A "gubbe"
/// now walks instead of a static image just bobbing. Pairs with
/// <see cref="GodotSpriteFrames"/> to wire the sheet into an AnimatedSprite2D.
/// </summary>
public static class PixelAnimator
{
    /// <summary>Build idle (2 frames) + walk (4 frames) for a character. The
    /// prompt drives a deterministic colour palette so every regen matches.
    /// v2.17: 24 px default - riktig pixelart behover utrymme for ramper,
    /// har, skor och kontur (16 px racker inte for shading-kluster).</summary>
    public static AnimatedSpriteSheet Build(string prompt, int frame = 24)
    {
        frame = Math.Clamp(frame, 12, 64);
        var frames = Frames(prompt, frame);
        var png = Compose(frames, frame);
        var anims = new List<SpriteAnim>
        {
            new("idle", 0, 2, 3, true),
            new("walk", 2, 4, 10, true),
        };
        return new AnimatedSpriteSheet(png, frame, frame, frames.Count, anims);
    }

    /// <summary>The raw RGBA frame buffers (idle 0/1, walk 0-3) before they are
    /// composed into the sheet - the seam the animation test asserts on (the
    /// walk frames must actually differ, i.e. the legs move).</summary>
    internal static List<byte[]> Frames(string prompt, int frame = 24)
    {
        frame = Math.Clamp(frame, 12, 64);
        var pal = Palette(prompt);
        return new List<byte[]>
        {
            Draw(frame, pal, walkPhase: -1, bob: 0), // idle 0
            Draw(frame, pal, walkPhase: -1, bob: 1), // idle 1 (andnings-bob)
            Draw(frame, pal, walkPhase: 0, bob: 0),  // walk: vanster ben fram
            Draw(frame, pal, walkPhase: 1, bob: 1),  // walk: passering, bob
            Draw(frame, pal, walkPhase: 2, bob: 0),  // walk: hoger ben fram
            Draw(frame, pal, walkPhase: 3, bob: 1),  // walk: passering, bob
        };
    }

    // ---- palett: RAMPER, inte enstaka farger (v2.17 - riktig pixelart har
    // 2-3 nyanser per material i stora kluster: skugga/bas/ljus) -------------
    private readonly record struct Pal(
        (byte, byte, byte) Outline,
        (byte, byte, byte) SkinD, (byte, byte, byte) Skin, (byte, byte, byte) SkinL,
        (byte, byte, byte) ShirtD, (byte, byte, byte) Shirt, (byte, byte, byte) ShirtL,
        (byte, byte, byte) PantsD, (byte, byte, byte) Pants,
        (byte, byte, byte) HairD, (byte, byte, byte) Hair,
        (byte, byte, byte) Shoe, (byte, byte, byte) Eye, (byte, byte, byte) Shine);

    private static Pal Palette(string prompt)
    {
        var seed = 2166136261u;
        foreach (var c in prompt ?? "") seed = (seed ^ c) * 16777619u;
        var hue = (seed % 360) / 360.0;
        var hairHue = ((seed >> 7) % 360) / 360.0;
        return new Pal(
            Outline: ((byte)27, (byte)22, (byte)36),
            SkinD: Hsv(0.07, 0.42, 0.76), Skin: Hsv(0.07, 0.33, 0.94), SkinL: Hsv(0.09, 0.20, 1.0),
            ShirtD: Hsv(hue, 0.68, 0.58), Shirt: Hsv(hue, 0.62, 0.86), ShirtL: Hsv(hue, 0.48, 1.0),
            PantsD: Hsv((hue + 0.55) % 1.0, 0.38, 0.30), Pants: Hsv((hue + 0.55) % 1.0, 0.34, 0.46),
            HairD: Hsv(hairHue, 0.55, 0.30), Hair: Hsv(hairHue, 0.50, 0.48),
            Shoe: ((byte)46, (byte)38, (byte)52), Eye: ((byte)24, (byte)22, (byte)32),
            Shine: ((byte)255, (byte)255, (byte)255));
    }

    private static (byte, byte, byte) Hsv(double h, double s, double v)
    {
        var i = (int)(h * 6);
        var f = h * 6 - i;
        double p = v * (1 - s), q = v * (1 - f * s), t = v * (1 - (1 - f) * s);
        var (r, g, b) = (i % 6) switch
        {
            0 => (v, t, p), 1 => (q, v, p), 2 => (p, v, t),
            3 => (p, q, v), 4 => (t, p, v), _ => (v, p, q),
        };
        return ((byte)(r * 255), (byte)(g * 255), (byte)(b * 255));
    }

    // ---- drawing (v2.17: riktig sprite-teknik) -----------------------------
    // Ljus fran vanster-topp: vansterkolumn/topprad = ljus ton, hogerkolumn/
    // bottenrad = skuggton. Benen star pa marken (bob flyttar bara over-
    // kroppen - figuren "andas" i stallet for att svava). Sist ett INNERLINE-
    // pass: varje solid pixel som grannar transparens blir konturfarg =
    // sluten mork silhuettlinje, pixelartens signatur.
    private static byte[] Draw(int frame, Pal pal, int walkPhase, int bob)
    {
        var raw = new byte[frame * frame * 4];
        int S(int v) => v * frame / 24; // regions authored for a 24px frame
        void Px(int x, int y, (byte, byte, byte) c)
        {
            if (x < 0 || y < 0 || x >= frame || y >= frame) return;
            var i = (y * frame + x) * 4;
            raw[i] = c.Item1; raw[i + 1] = c.Item2; raw[i + 2] = c.Item3; raw[i + 3] = 255;
        }
        void Clear(int x, int y)
        {
            if (x < 0 || y < 0 || x >= frame || y >= frame) return;
            raw[(y * frame + x) * 4 + 3] = 0;
        }
        void Rect(int x0, int y0, int x1, int y1, (byte, byte, byte) fill)
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    Px(x, y, fill);
        }
        // Skuggad yta: bas + ljus vanster/topp + skugga hoger/botten.
        void Shaded(int x0, int y0, int x1, int y1,
            (byte, byte, byte) dark, (byte, byte, byte) baseCol, (byte, byte, byte) light)
        {
            Rect(x0, y0, x1, y1, baseCol);
            for (var y = y0; y <= y1; y++) Px(x0, y, light);
            for (var x = x0; x <= x1; x++) Px(x, y0, light);
            for (var y = y0 + 1; y <= y1; y++) Px(x1, y, dark);
            for (var x = x0 + 1; x <= x1; x++) Px(x, y1, dark);
        }

        // ---- ben + skor (star alltid pa marken, paverkas ej av bob) --------
        int lx = 0, rx = 0, lLift = 0, rLift = 0;
        switch (walkPhase)
        {
            case 0: lx = -S(1); rx = S(1); rLift = S(2); break; // vanster fram
            case 1: rLift = S(1); break;                        // passering
            case 2: lx = S(1); rx = -S(1); lLift = S(2); break; // hoger fram
            case 3: lLift = S(1); break;
        }
        var legTop = S(17);
        var shoeY = S(21);
        Rect(S(9) + lx, legTop, S(10) + lx, shoeY - 1 - lLift, pal.Pants);
        for (var y = legTop; y <= shoeY - 1 - lLift; y++) Px(S(10) + lx, y, pal.PantsD);
        Rect(S(13) + rx, legTop, S(14) + rx, shoeY - 1 - rLift, pal.Pants);
        for (var y = legTop; y <= shoeY - 1 - rLift; y++) Px(S(14) + rx, y, pal.PantsD);
        // skor: en pixel langre fram an benet (lasbar profil)
        Rect(S(8) + lx, shoeY - lLift, S(10) + lx, S(22) - lLift, pal.Shoe);
        Rect(S(12) + rx, shoeY - rLift, S(14) + rx, S(22) - rLift, pal.Shoe);

        // ---- overkropp (bob = andning/steg) --------------------------------
        var dy = bob;
        // armar bakom kroppen: pendlar motsatt benen
        var armSwing = walkPhase == 0 ? S(1) : walkPhase == 2 ? -S(1) : 0;
        Rect(S(6), S(11) + dy + armSwing, S(7), S(15) + dy + armSwing, pal.ShirtD);
        Px(S(6), S(16) + dy + armSwing, pal.Skin); Px(S(7), S(16) + dy + armSwing, pal.Skin); // hand
        Rect(S(16), S(11) + dy - armSwing, S(17), S(15) + dy - armSwing, pal.ShirtD);
        Px(S(16), S(16) + dy - armSwing, pal.Skin); Px(S(17), S(16) + dy - armSwing, pal.Skin);

        // trojan med ramp-shading + balte
        Shaded(S(8), S(10) + dy, S(15), S(16) + dy, pal.ShirtD, pal.Shirt, pal.ShirtL);
        Rect(S(8), S(16) + dy, S(15), S(16) + dy, pal.PantsD); // balte

        // huvud (hud-ramp) med rundade horn
        Shaded(S(8), S(2) + dy, S(15), S(9) + dy, pal.SkinD, pal.Skin, pal.SkinL);
        Clear(S(8), S(2) + dy); Clear(S(15), S(2) + dy);
        Clear(S(8), S(9) + dy); Clear(S(15), S(9) + dy);

        // har: topp + sidoflikar (egen ramp)
        Rect(S(8), S(2) + dy, S(15), S(4) + dy, pal.Hair);
        Rect(S(8), S(4) + dy, S(9), S(6) + dy, pal.Hair);
        Rect(S(14), S(4) + dy, S(15), S(6) + dy, pal.HairD);
        for (var x = S(8); x <= S(15); x++) Px(x, S(4) + dy, pal.HairD); // harkant
        Clear(S(8), S(2) + dy); Clear(S(15), S(2) + dy);

        // ogon med glans + mun
        Px(S(10), S(7) + dy, pal.Eye); Px(S(13), S(7) + dy, pal.Eye);
        Px(S(10), S(6) + dy, pal.Shine); Px(S(13), S(6) + dy, pal.Shine);
        Px(S(11), S(8) + dy, pal.SkinD); Px(S(12), S(8) + dy, pal.SkinD);

        // ---- innerline: sluten mork kontur runt hela silhuetten ------------
        var snap = (byte[])raw.Clone();
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < frame && y < frame && snap[(y * frame + x) * 4 + 3] > 0;
        for (var y = 0; y < frame; y++)
            for (var x = 0; x < frame; x++)
            {
                if (!Solid(x, y)) continue;
                if (!Solid(x - 1, y) || !Solid(x + 1, y) || !Solid(x, y - 1) || !Solid(x, y + 1))
                    Px(x, y, pal.Outline);
            }
        return raw;
    }

    private static byte[] Compose(List<byte[]> frames, int frame)
    {
        var cols = frames.Count;
        var sheetW = cols * frame;
        var raw = new byte[frame * sheetW * 4];
        for (var f = 0; f < cols; f++)
        {
            var src = frames[f];
            for (var y = 0; y < frame; y++)
                for (var x = 0; x < frame; x++)
                {
                    var si = (y * frame + x) * 4;
                    var di = (y * sheetW + f * frame + x) * 4;
                    raw[di] = src[si]; raw[di + 1] = src[si + 1]; raw[di + 2] = src[si + 2]; raw[di + 3] = src[si + 3];
                }
        }
        return AssetGenerator.EncodePng(sheetW, frame, raw);
    }
}
