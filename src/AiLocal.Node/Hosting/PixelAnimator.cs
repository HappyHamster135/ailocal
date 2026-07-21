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
    /// prompt drives a deterministic colour palette so every regen matches.</summary>
    public static AnimatedSpriteSheet Build(string prompt, int frame = 16)
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
    internal static List<byte[]> Frames(string prompt, int frame = 16)
    {
        frame = Math.Clamp(frame, 12, 64);
        var pal = Palette(prompt);
        return new List<byte[]>
        {
            Draw(frame, pal, walkPhase: -1, bob: 0), // idle 0
            Draw(frame, pal, walkPhase: -1, bob: 1), // idle 1 (bob down 1px)
            Draw(frame, pal, walkPhase: 0, bob: 0),  // walk: left leg forward
            Draw(frame, pal, walkPhase: 1, bob: 1),  // walk: pass, bob
            Draw(frame, pal, walkPhase: 2, bob: 0),  // walk: right leg forward
            Draw(frame, pal, walkPhase: 3, bob: 1),  // walk: pass, bob
        };
    }

    // ---- palette -----------------------------------------------------------
    private readonly record struct Pal(
        (byte, byte, byte) Outline, (byte, byte, byte) Body,
        (byte, byte, byte) Skin, (byte, byte, byte) Legs, (byte, byte, byte) Eye);

    private static Pal Palette(string prompt)
    {
        var seed = 2166136261u;
        foreach (var c in prompt ?? "") seed = (seed ^ c) * 16777619u;
        var hue = (seed % 360) / 360.0;
        var body = Hsv(hue, 0.62, 0.85);
        var legs = Hsv(hue, 0.55, 0.5);
        var skin = Hsv((hue + 0.08) % 1.0, 0.35, 0.95);
        var eye = ((byte)24, (byte)22, (byte)32);
        return new Pal(((byte)26, (byte)24, (byte)34), body, skin, legs, eye);
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

    // ---- drawing -----------------------------------------------------------
    private static byte[] Draw(int frame, Pal pal, int walkPhase, int bob)
    {
        var raw = new byte[frame * frame * 4];
        int S(int v) => v * frame / 16; // regions authored for a 16px frame
        void Px(int x, int y, (byte, byte, byte) c)
        {
            if (x < 0 || y < 0 || x >= frame || y >= frame) return;
            var i = (y * frame + x) * 4;
            raw[i] = c.Item1; raw[i + 1] = c.Item2; raw[i + 2] = c.Item3; raw[i + 3] = 255;
        }
        void Rect(int x0, int y0, int x1, int y1, (byte, byte, byte) fill, bool border)
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    Px(x, y + bob, (border && (x == x0 || x == x1 || y == y0 || y == y1)) ? pal.Outline : fill);
        }

        // legs first (behind body), animated
        int lx = 0, rx = 0, ll = S(4), rl = S(4);
        switch (walkPhase)
        {
            case 0: lx = -S(1); rl -= S(1); break; // left steps forward, right lifts
            case 2: rx = S(1); ll -= S(1); break;  // right steps forward, left lifts
            // 1, 3, idle (-1): both legs neutral
        }
        Rect(S(5) + lx, S(11), S(6) + lx, S(11) + ll, pal.Legs, false);
        Rect(S(9) + rx, S(11), S(10) + rx, S(11) + rl, pal.Legs, false);

        // body + head with a 1px outline
        Rect(S(4), S(7), S(11), S(11), pal.Body, true);
        Rect(S(5), S(2), S(10), S(6), pal.Skin, true);

        // arms swing opposite the legs
        var armL = walkPhase == 0 ? S(1) : walkPhase == 2 ? -S(1) : 0;
        Rect(S(3), S(7) + armL, S(3), S(9) + armL, pal.Body, false);
        Rect(S(12), S(7) - armL, S(12), S(9) - armL, pal.Body, false);

        // eyes
        Px(S(6), S(4) + bob, pal.Eye);
        Px(S(9), S(4) + bob, pal.Eye);
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
