namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.16: AI-bild -> AKTA pixelart. Bildmodeller ar bra pa form/karaktar men
/// levererar 1024x1024 RGB UTAN alfa, med pseudo-pixlar utanfor grid och
/// skrapbakgrund (bevisat i agarens Candy Party-transkript: begard 192x48-
/// spritesheet kom som 1024x1024 med rutmonster). Pipen gor det modellen
/// inte kan, deterministiskt:
///   1. bakgrund bort (flood-fill fran hornen med tolerans) -> riktig alfa
///   2. beskarning till innehallets bounding box
///   3. nearest-downsample till exakt malstorlek -> riktigt pixelgrid
///   4. palettkvantisering (N vanligaste farger, alla pixlar snapps)
///   5. valfri 1px mork kontur - pixelart-signaturen
/// Ren berakning pa RGBA-buffertar = enhetstestbar utan AI.
/// </summary>
public static class PixelArtPipeline
{
    /// <summary>PNG-bytes in -> pixelart-PNG-bytes ut (null vid dekodfel).</summary>
    public static byte[]? ToPixelArt(byte[] pngBytes, int targetSize = 48, int paletteSize = 16,
        bool transparentBackground = true, bool outline = true)
    {
        var decoded = DecodePng(pngBytes);
        if (decoded is null) return null;
        var (rgba, w, h) = decoded.Value;
        var (outRgba, ow, oh) = Process(rgba, w, h, targetSize, paletteSize, transparentBackground, outline);
        return AssetGenerator.EncodePng(ow, oh, outRgba);
    }

    /// <summary>Kärnan på råa RGBA-buffertar (testbar). Returnerar ny buffert.</summary>
    public static (byte[] Rgba, int W, int H) Process(byte[] rgba, int w, int h,
        int targetSize = 48, int paletteSize = 16, bool transparentBackground = true, bool outline = true)
    {
        targetSize = Math.Clamp(targetSize, 8, 256);
        paletteSize = Math.Clamp(paletteSize, 2, 64);
        var work = (byte[])rgba.Clone();

        if (transparentBackground)
            RemoveBackground(work, w, h);

        var (cx, cy, cw, ch) = ContentBounds(work, w, h);
        var (small, sw, sh) = Downsample(work, w, h, cx, cy, cw, ch, targetSize);
        Quantize(small, paletteSize);
        Despeckle(small, sw, sh);
        if (outline)
            AddOutline(small, sw, sh);
        return (small, sw, sh);
    }

    /// <summary>v2.17: klusterstädning - riktig pixelart har STORA samman-
    /// hängande färgkluster, inte brus. En pixel vars 4-grannar alla har
    /// annan färg tar vanligaste grannfärgen; en ensam ö i transparens tas
    /// bort helt.</summary>
    internal static void Despeckle(byte[] rgba, int w, int h)
    {
        var src = (byte[])rgba.Clone();
        int Rgb(int i) => (src[i] << 16) | (src[i + 1] << 8) | src[i + 2];
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                if (src[i + 3] == 0) continue;
                var mine = Rgb(i);
                var neighbours = new List<int>();
                var same = false;
                foreach (var (nx, ny) in new[] { (x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1) })
                {
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    var ni = (ny * w + nx) * 4;
                    if (src[ni + 3] == 0) continue;
                    var c = Rgb(ni);
                    neighbours.Add(c);
                    if (c == mine) same = true;
                }
                if (same) continue;
                if (neighbours.Count == 0)
                {
                    rgba[i + 3] = 0; // ensam ö-pixel
                    continue;
                }
                var winner = neighbours.GroupBy(c => c).OrderByDescending(g => g.Count()).First().Key;
                rgba[i] = (byte)(winner >> 16);
                rgba[i + 1] = (byte)(winner >> 8 & 0xFF);
                rgba[i + 2] = (byte)(winner & 0xFF);
            }
    }

    /// <summary>Flood-fill från hörnen: allt som liknar hörnens färger blir
    /// transparent. Tål gradient-/rutmönsterbakgrunder (alla fyra hörn +
    /// tolerans per kanal).</summary>
    static void RemoveBackground(byte[] rgba, int w, int h)
    {
        if (w < 2 || h < 2) return;
        var seeds = new (int X, int Y)[] { (0, 0), (w - 1, 0), (0, h - 1), (w - 1, h - 1) };
        var bg = new List<(byte R, byte G, byte B)>();
        foreach (var (sx, sy) in seeds)
        {
            var i = (sy * w + sx) * 4;
            bg.Add((rgba[i], rgba[i + 1], rgba[i + 2]));
        }

        bool IsBg(int idx)
        {
            if (rgba[idx + 3] < 16) return true; // redan transparent
            foreach (var (r, g, b) in bg)
            {
                var d = Math.Abs(rgba[idx] - r) + Math.Abs(rgba[idx + 1] - g) + Math.Abs(rgba[idx + 2] - b);
                if (d < 90) return true;
            }
            return false;
        }

        var visited = new bool[w * h];
        var queue = new Queue<(int X, int Y)>();
        foreach (var s in seeds) queue.Enqueue(s);
        while (queue.Count > 0)
        {
            var (x, y) = queue.Dequeue();
            if (x < 0 || y < 0 || x >= w || y >= h) continue;
            var p = y * w + x;
            if (visited[p]) continue;
            visited[p] = true;
            var idx = p * 4;
            if (!IsBg(idx)) continue;
            rgba[idx + 3] = 0;
            queue.Enqueue((x + 1, y));
            queue.Enqueue((x - 1, y));
            queue.Enqueue((x, y + 1));
            queue.Enqueue((x, y - 1));
        }
    }

    static (int X, int Y, int W, int H) ContentBounds(byte[] rgba, int w, int h)
    {
        int minX = w, minY = h, maxX = -1, maxY = -1;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
                if (rgba[(y * w + x) * 4 + 3] > 16)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
        if (maxX < 0) return (0, 0, w, h); // helt tomt - behall allt
        return (minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Boxfilter-downsample (medel över källrutan, alfaviktad) till
    /// målstorlek på längsta sidan - ger renare pixelart än ren nearest.</summary>
    static (byte[] Rgba, int W, int H) Downsample(byte[] rgba, int w, int h, int cx, int cy, int cw, int ch, int target)
    {
        var scale = Math.Max(1.0, Math.Max(cw, ch) / (double)target);
        var ow = Math.Max(1, (int)Math.Round(cw / scale));
        var oh = Math.Max(1, (int)Math.Round(ch / scale));
        var result = new byte[ow * oh * 4];
        for (var y = 0; y < oh; y++)
        {
            var sy0 = cy + (int)(y * scale);
            var sy1 = Math.Min(cy + ch, cy + (int)((y + 1) * scale) + 1);
            for (var x = 0; x < ow; x++)
            {
                var sx0 = cx + (int)(x * scale);
                var sx1 = Math.Min(cx + cw, cx + (int)((x + 1) * scale) + 1);
                long r = 0, g = 0, b = 0, a = 0, n = 0;
                for (var sy = sy0; sy < sy1; sy++)
                    for (var sx = sx0; sx < sx1; sx++)
                    {
                        var i = (sy * w + sx) * 4;
                        var al = rgba[i + 3];
                        r += rgba[i] * al; g += rgba[i + 1] * al; b += rgba[i + 2] * al;
                        a += al; n++;
                    }
                var o = (y * ow + x) * 4;
                if (a > 0)
                {
                    result[o] = (byte)(r / a);
                    result[o + 1] = (byte)(g / a);
                    result[o + 2] = (byte)(b / a);
                    result[o + 3] = (byte)Math.Min(255, a / Math.Max(1, n));
                }
                // Pixelart = binär alfa: antingen med eller inte.
                result[o + 3] = result[o + 3] > 110 ? (byte)255 : (byte)0;
            }
        }
        return (result, ow, oh);
    }

    /// <summary>N vanligaste färgerna (grovkvantiserade nycklar) blir palett;
    /// varje pixel snapps till närmaste palettfärg.</summary>
    static void Quantize(byte[] rgba, int paletteSize)
    {
        var counts = new Dictionary<int, (long R, long G, long B, int N)>();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            var key = (rgba[i] >> 4 << 8) | (rgba[i + 1] >> 4 << 4) | (rgba[i + 2] >> 4);
            var e = counts.TryGetValue(key, out var v) ? v : (0, 0, 0, 0);
            counts[key] = (e.Item1 + rgba[i], e.Item2 + rgba[i + 1], e.Item3 + rgba[i + 2], e.Item4 + 1);
        }
        if (counts.Count == 0) return;
        var palette = counts.Values
            .OrderByDescending(v => v.N)
            .Take(paletteSize)
            .Select(v => ((byte)(v.R / v.N), (byte)(v.G / v.N), (byte)(v.B / v.N)))
            .ToList();
        for (var i = 0; i < rgba.Length; i += 4)
        {
            if (rgba[i + 3] == 0) continue;
            var best = 0;
            var bestD = int.MaxValue;
            for (var p = 0; p < palette.Count; p++)
            {
                var (r, g, b) = palette[p];
                var d = (rgba[i] - r) * (rgba[i] - r) + (rgba[i + 1] - g) * (rgba[i + 1] - g) + (rgba[i + 2] - b) * (rgba[i + 2] - b);
                if (d < bestD) { bestD = d; best = p; }
            }
            (rgba[i], rgba[i + 1], rgba[i + 2]) = palette[best];
        }
    }

    /// <summary>1px mörk kontur: transparenta pixlar som grannar innehåll.</summary>
    static void AddOutline(byte[] rgba, int w, int h)
    {
        var src = (byte[])rgba.Clone();
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && src[(y * w + x) * 4 + 3] > 0;
        for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                var i = (y * w + x) * 4;
                if (src[i + 3] > 0) continue;
                if (Solid(x + 1, y) || Solid(x - 1, y) || Solid(x, y + 1) || Solid(x, y - 1))
                {
                    rgba[i] = 24; rgba[i + 1] = 20; rgba[i + 2] = 32; rgba[i + 3] = 255;
                }
            }
    }

    /// <summary>PNG -> (rgba, w, h) via System.Drawing (appen är win-x64-only).</summary>
    public static (byte[] Rgba, int W, int H)? DecodePng(byte[] pngBytes)
    {
#pragma warning disable CA1416
        try
        {
            using var ms = new MemoryStream(pngBytes);
            using var bmp = new System.Drawing.Bitmap(ms);
            var w = bmp.Width;
            var h = bmp.Height;
            var rgba = new byte[w * h * 4];
            var data = bmp.LockBits(new System.Drawing.Rectangle(0, 0, w, h),
                System.Drawing.Imaging.ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var stride = data.Stride;
                var row = new byte[stride];
                for (var y = 0; y < h; y++)
                {
                    System.Runtime.InteropServices.Marshal.Copy(data.Scan0 + y * stride, row, 0, stride);
                    for (var x = 0; x < w; x++)
                    {
                        var s = x * 4;
                        var d = (y * w + x) * 4;
                        rgba[d] = row[s + 2];     // BGRA -> RGBA
                        rgba[d + 1] = row[s + 1];
                        rgba[d + 2] = row[s];
                        rgba[d + 3] = row[s + 3];
                    }
                }
            }
            finally { bmp.UnlockBits(data); }
            return (rgba, w, h);
        }
        catch { return null; }
#pragma warning restore CA1416
    }
}
