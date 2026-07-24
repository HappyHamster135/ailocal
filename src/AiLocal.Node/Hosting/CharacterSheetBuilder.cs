namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29: bygger spritesheet + Godot-.tres ur en <see cref="CharacterSpec"/>,
/// och ett KONTAKTARK över hela rollistan. Kontaktarket är verifieringen som
/// faktiskt besvarar "ser gubbarna bra ut och hör de ihop?" - inget C#-test
/// kan svara på det, och att låtsas annat är precis den falskt-gröna fälla
/// den här kodbasen redan bränt sig på.
/// </summary>
public static class CharacterSheetBuilder
{
    public static AnimatedSpriteSheet Build(CharacterSpec spec, IReadOnlyList<Pose>? poses = null)
    {
        poses ??= PoseLib.Standard;
        var frame = Math.Clamp(spec.Frame, 12, 64);
        var frames = poses.Select(p => CharacterRenderer.Draw(spec, p, frame)).ToList();
        var png = Compose(frames, frame);
        return new AnimatedSpriteSheet(png, frame, frame, frames.Count, PoseLib.AnimsFor(poses));
    }

    /// <summary>Skriver &lt;bas&gt;.png + &lt;bas&gt;_frames.tres i projektroten.
    /// Basnamnet följer kitens historiska namn för player/enemy, så de 21
    /// kiten fortsätter fungera utan en enda GDScript-ändring.</summary>
    public static (string Png, string Tres) WriteInto(string projectRoot, CharacterSpec spec, IReadOnlyList<Pose>? poses = null)
    {
        var sheet = Build(spec, poses);
        var b = CharacterCast.SheetBase(spec.Slug);
        var png = b + ".png";
        var tres = b + "_frames.tres";
        Directory.CreateDirectory(projectRoot);
        File.WriteAllBytes(Path.Combine(projectRoot, png), sheet.Png);
        File.WriteAllText(Path.Combine(projectRoot, tres), GodotSpriteFrames.Build(png, sheet));
        return (png, tres);
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
                    raw[di] = src[si]; raw[di + 1] = src[si + 1];
                    raw[di + 2] = src[si + 2]; raw[di + 3] = src[si + 3];
                }
        }
        return AssetGenerator.EncodePng(sheetW, frame, raw);
    }

    /// <summary>Kontaktark: en rad per karaktär, alla poser i bredd,
    /// nearest-uppskalat så pixlarna går att granska med ögat.</summary>
    public static byte[] ContactSheet(IReadOnlyList<CharacterSpec> specs, int scale = 5, IReadOnlyList<Pose>? poses = null)
    {
        poses ??= PoseLib.Standard;
        if (specs.Count == 0) specs = [CharacterSpecFactory.Derive("player", "Player", "player", ArtBibleStore.Derive("platformer", "tom"), 1)];
        var frame = Math.Clamp(specs[0].Frame, 12, 64);
        const int pad = 2;
        var cellW = (frame + pad) * scale;
        var cellH = (frame + pad) * scale;
        var w = cellW * poses.Count;
        var h = cellH * specs.Count;
        var raw = new byte[w * h * 4];
        // Neutral bakgrund sa bade ljusa och morka figurer gar att bedoma.
        for (var i = 0; i < raw.Length; i += 4)
        {
            raw[i] = 58; raw[i + 1] = 56; raw[i + 2] = 66; raw[i + 3] = 255;
        }
        for (var r = 0; r < specs.Count; r++)
            for (var c = 0; c < poses.Count; c++)
            {
                var src = CharacterRenderer.Draw(specs[r], poses[c], frame);
                for (var y = 0; y < frame; y++)
                    for (var x = 0; x < frame; x++)
                    {
                        var si = (y * frame + x) * 4;
                        if (src[si + 3] == 0) continue;
                        for (var sy = 0; sy < scale; sy++)
                            for (var sx = 0; sx < scale; sx++)
                            {
                                var dx = c * cellW + (x + pad / 2) * scale + sx;
                                var dy = r * cellH + (y + pad / 2) * scale + sy;
                                if (dx < 0 || dy < 0 || dx >= w || dy >= h) continue;
                                var di = (dy * w + dx) * 4;
                                raw[di] = src[si]; raw[di + 1] = src[si + 1];
                                raw[di + 2] = src[si + 2]; raw[di + 3] = 255;
                            }
                    }
            }
        return AssetGenerator.EncodePng(w, h, raw);
    }
}
