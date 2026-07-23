namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.25: procedurell pixelart-BAKGRUND, deterministisk per prompt.
/// Nyckellosa noder far en riktig scen i stallet for ingenting: posteriserad
/// himmel i band med dither-kanter, himlakropp, vaderdetaljer och 2-3
/// parallaxlager silhuetter. Temat valjs pa promptens nyckelord (rymd/grotta/
/// hav/oken/sno/stad/solnedgang/natt/skog - annars ang i dagsljus).
/// Lag upplosning (240x135 default) - NEAREST-uppskalning i motorn haller
/// pixlarna skarpa. Samma prompt ger alltid exakt samma bild.
/// </summary>
public static class PixelBackdrop
{
    public static byte[] Build(string prompt, int width = 240, int height = 135)
    {
        width = Math.Clamp(width, 64, 480);
        height = Math.Clamp(height, 36, 270);
        var p = (prompt ?? "").ToLowerInvariant();
        var rnd = new Lcg(StableSeed(p));
        var px = new byte[width * height * 4];

        // Tema pa nyckelord (svenska + engelska). Ordningen avgor vid flera
        // traffar; korta ord som ger falska traffar ("is", "tree" i "street")
        // ar medvetet utelamnade.
        var theme =
            Has(p, "rymd", "space", "galax", "planet", "asteroid") ? "space" :
            Has(p, "grotta", "cave", "dungeon", "gruva") ? "cave" :
            Has(p, "underwater", "undervatten", "havet", "havs", "ocean", "fisk", "fish", "korall", "coral") ? "ocean" :
            Has(p, "öken", "oken", "desert", "sahara") ? "desert" :
            Has(p, "snö", "snow", "vinter", "winter", "frost") ? "snow" :
            Has(p, "stad", "city", "town", "neon", "cyber", "skyline") ? "city" :
            Has(p, "solnedgång", "solnedgang", "sunset", "skymning", "dusk") ? "sunset" :
            Has(p, "natt", "night", "måne", "moon", "stjärn", "stjarn") ? "night" :
            Has(p, "skog", "forest", "djungel", "jungle", "träd", "woods") ? "forest" :
            "meadow";

        // Palett per tema: 4 himmelsband uppifran och ner, himlakropp
        // (0=ingen 1=sol 2=mane 3=planet), detaljflaggor och lagerfarger.
        (int R, int G, int B)[] sky;
        int celestial;
        bool stars = false, clouds = false;
        (int R, int G, int B) far, mid, near, ground, groundTop;
        var extra = "";
        switch (theme)
        {
            case "space":
                sky = [(12, 10, 28), (18, 14, 40), (26, 18, 54), (36, 24, 68)];
                celestial = 3; stars = true;
                far = (30, 22, 58); mid = (44, 30, 76); near = (58, 38, 94);
                ground = (24, 18, 44); groundTop = (70, 48, 110);
                break;
            case "cave":
                sky = [(16, 13, 18), (22, 18, 24), (30, 24, 31), (38, 30, 38)];
                celestial = 0;
                far = (44, 36, 46); mid = (56, 45, 56); near = (34, 27, 36);
                ground = (48, 38, 46); groundTop = (74, 60, 68);
                extra = "spikes";
                break;
            case "ocean":
                sky = [(20, 60, 96), (16, 76, 112), (14, 92, 124), (12, 108, 132)];
                celestial = 0;
                far = (10, 66, 96); mid = (8, 52, 78); near = (6, 40, 62);
                ground = (110, 96, 60); groundTop = (140, 122, 76);
                extra = "rays";
                break;
            case "desert":
                sky = [(120, 180, 214), (150, 196, 214), (196, 204, 196), (232, 202, 150)];
                celestial = 1;
                far = (216, 168, 104); mid = (198, 146, 84); near = (176, 124, 66);
                ground = (188, 140, 78); groundTop = (222, 178, 108);
                break;
            case "snow":
                sky = [(120, 156, 200), (146, 178, 214), (176, 200, 226), (206, 222, 238)];
                celestial = 1; clouds = true;
                far = (188, 204, 224); mid = (216, 228, 240); near = (238, 244, 250);
                ground = (226, 234, 246); groundTop = (250, 252, 255);
                break;
            case "city":
                sky = [(34, 22, 56), (66, 32, 76), (120, 48, 84), (196, 92, 80)];
                celestial = 1; stars = true;
                far = (28, 20, 44); mid = (20, 15, 34); near = (13, 10, 24);
                ground = (16, 12, 26); groundTop = (36, 28, 52);
                extra = "buildings";
                break;
            case "sunset":
                sky = [(64, 32, 84), (140, 58, 96), (216, 108, 84), (244, 172, 96)];
                celestial = 1;
                far = (72, 40, 84); mid = (52, 30, 66); near = (34, 20, 48);
                ground = (28, 18, 40); groundTop = (58, 36, 70);
                break;
            case "night":
                sky = [(10, 12, 30), (14, 18, 42), (20, 26, 56), (28, 36, 70)];
                celestial = 2; stars = true;
                far = (24, 30, 56); mid = (18, 24, 46); near = (12, 17, 36);
                ground = (10, 14, 30); groundTop = (26, 34, 58);
                break;
            case "forest":
                sky = [(96, 168, 200), (130, 190, 206), (168, 210, 206), (206, 226, 198)];
                celestial = 1; clouds = true;
                far = (74, 124, 96); mid = (52, 100, 74); near = (34, 76, 54);
                ground = (40, 84, 52); groundTop = (66, 116, 68);
                break;
            default: // meadow - ang i dagsljus
                sky = [(92, 160, 220), (120, 180, 228), (150, 198, 232), (184, 216, 236)];
                celestial = 1; clouds = true;
                far = (110, 160, 120); mid = (84, 140, 96); near = (60, 118, 74);
                ground = (72, 130, 80); groundTop = (104, 160, 96);
                break;
        }

        // 0) Grundfyllnad med understa himmelsbandet: kullagrens toppar kan
        // hamna UNDER horisonten (sinusvagorna ar slumpade) och gapet fick
        // annars alfa 0 - nu blir det horisontdis i stallet for hal.
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                Set(px, width, x, y, sky[3]);

        // 1) Himmel: fyra lika hoga band ner till horisonten, dither-rad pa
        // varje bandgrans (varannan pixel tar ovre bandets farg).
        var horizon = extra == "rays" ? height : height * 62 / 100;
        var bandH = Math.Max(1, horizon / 4);
        for (var y = 0; y < horizon; y++)
        {
            var band = Math.Min(3, y / bandH);
            var c = sky[band];
            for (var x = 0; x < width; x++)
            {
                var use = c;
                if (band > 0 && y % bandH < 2 && (x + y) % 2 == 0)
                    use = sky[band - 1];
                Set(px, width, x, y, use);
            }
        }

        // 2) Stjarnor: prickar i ovre halvan, var sjatte som litet kors.
        if (stars)
            for (var i = 0; i < 46; i++)
            {
                var sx = rnd.Next(width);
                var sy = rnd.Next(horizon * 6 / 10);
                var c = i % 3 == 0 ? (226, 232, 255) : (168, 178, 220);
                Set(px, width, sx, sy, c);
                if (i % 6 == 0)
                {
                    Set(px, width, sx - 1, sy, c); Set(px, width, sx + 1, sy, c);
                    Set(px, width, sx, sy - 1, c); Set(px, width, sx, sy + 1, c);
                }
            }

        // 3) Himlakropp uppe till hoger.
        var cx = width * 78 / 100 + rnd.Next(width / 12) - width / 24;
        var cy = horizon * 30 / 100;
        var cr = Math.Max(5, height / 9);
        if (celestial == 1)
        {
            FillCircle(px, width, height, cx, cy, cr + 2, theme == "sunset" || theme == "city" ? (250, 200, 120) : (250, 232, 150));
            FillCircle(px, width, height, cx, cy, cr - 1, theme == "sunset" || theme == "city" ? (255, 224, 150) : (255, 246, 190));
        }
        else if (celestial == 2)
        {
            FillCircle(px, width, height, cx, cy, cr, (222, 226, 238));
            FillCircle(px, width, height, cx + cr / 2, cy - cr / 3, cr * 3 / 4, sky[0]); // skara
        }
        else if (celestial == 3)
        {
            FillCircle(px, width, height, cx, cy, cr, (150, 96, 170));
            FillCircle(px, width, height, cx - cr / 3, cy - cr / 3, cr / 2, (182, 128, 198));
            for (var dx = -cr - 5; dx <= cr + 5; dx++) // ring
            {
                var ry = cy + dx / 4;
                Set(px, width, cx + dx, ry, (208, 186, 226));
                if (Math.Abs(dx) > cr / 2) Set(px, width, cx + dx, ry + 1, (168, 146, 196));
            }
        }

        // 4) Moln: tre blobbar av staplade rader (kort-lang-kort).
        if (clouds)
            for (var i = 0; i < 3; i++)
            {
                var mx = rnd.Next(width - 40) + 8;
                var my = rnd.Next(Math.Max(1, horizon / 2 - 14)) + 6;
                var mw = 18 + rnd.Next(16);
                var c = (244, 248, 252);
                FillRect(px, width, height, mx + 4, my, mw - 8, 2, c);
                FillRect(px, width, height, mx, my + 2, mw, 3, c);
                FillRect(px, width, height, mx + 3, my + 5, mw - 6, 2, (222, 232, 244));
            }

        // 5) Parallaxlager: tva-tre siluettlinjer av staplade sinusvagor
        // (posteriserade heltal - inga mjuka kanter), narmast ar morkast
        // i skymningsteman och ljusast i sno.
        if (extra == "spikes")
        {
            // Grotta: stalaktiter fran taket + stalagmiter vid golvet.
            SpikeRow(px, width, height, rnd, far, fromTop: true, maxLen: height / 3);
            HillLayer(px, width, height, rnd, height * 80 / 100, height / 10, mid);
            SpikeRow(px, width, height, rnd, near, fromTop: false, maxLen: height / 4);
        }
        else if (extra == "buildings")
        {
            BuildingRow(px, width, height, rnd, far, height * 46 / 100, 12);
            BuildingRow(px, width, height, rnd, mid, height * 58 / 100, 16);
            BuildingRow(px, width, height, rnd, near, height * 72 / 100, 22);
        }
        else if (extra == "rays")
        {
            // Undervatten: diagonala ljusstralar + bottenkullar.
            for (var i = 0; i < 3; i++)
            {
                var rx = width * (i * 2 + 1) / 7 + rnd.Next(10);
                for (var y = 0; y < height * 3 / 4; y++)
                    for (var dx = 0; dx < 5; dx++)
                    {
                        var x = rx + y / 3 + dx;
                        if ((x + y) % 2 == 0) Lighten(px, width, height, x, y, 22);
                    }
            }
            HillLayer(px, width, height, rnd, height * 82 / 100, height / 12, mid);
            HillLayer(px, width, height, rnd, height * 90 / 100, height / 14, near);
        }
        else
        {
            HillLayer(px, width, height, rnd, horizon + height / 24, height / 7, far);
            HillLayer(px, width, height, rnd, horizon + height / 9, height / 9, mid);
            if (theme == "forest")
                TreeRow(px, width, height, rnd, near, height * 74 / 100);
            HillLayer(px, width, height, rnd, height * 84 / 100, height / 12, near);
        }

        // 6) Mark: ljus topprad + korn av morkare prickar.
        var groundY = height * 91 / 100;
        for (var y = groundY; y < height; y++)
            for (var x = 0; x < width; x++)
                Set(px, width, x, y, y == groundY ? groundTop : ground);
        for (var i = 0; i < width / 3; i++)
        {
            var gx = rnd.Next(width);
            var gy = groundY + 1 + rnd.Next(Math.Max(1, height - groundY - 1));
            Set(px, width, gx, gy, Dark(ground, 24));
        }

        return AssetGenerator.EncodePng(width, height, px);
    }

    // ---- ritprimitiver -----------------------------------------------------

    static void Set(byte[] px, int w, int x, int y, (int R, int G, int B) c)
    {
        if (x < 0 || y < 0 || x >= w) return;
        var i = (y * w + x) * 4;
        if (i < 0 || i + 3 >= px.Length) return;
        px[i] = (byte)c.R; px[i + 1] = (byte)c.G; px[i + 2] = (byte)c.B; px[i + 3] = 255;
    }

    static void FillRect(byte[] px, int w, int h, int x, int y, int rw, int rh, (int R, int G, int B) c)
    {
        for (var yy = y; yy < y + rh && yy < h; yy++)
            for (var xx = x; xx < x + rw && xx < w; xx++)
                Set(px, w, xx, yy, c);
    }

    static void FillCircle(byte[] px, int w, int h, int cx, int cy, int r, (int R, int G, int B) c)
    {
        for (var y = cy - r; y <= cy + r; y++)
            for (var x = cx - r; x <= cx + r; x++)
                if ((x - cx) * (x - cx) + (y - cy) * (y - cy) <= r * r)
                    Set(px, w, x, y, c);
    }

    static void Lighten(byte[] px, int w, int h, int x, int y, int amt)
    {
        if (x < 0 || y < 0 || x >= w || y >= h) return;
        var i = (y * w + x) * 4;
        px[i] = (byte)Math.Min(255, px[i] + amt);
        px[i + 1] = (byte)Math.Min(255, px[i + 1] + amt);
        px[i + 2] = (byte)Math.Min(255, px[i + 2] + amt);
    }

    static (int R, int G, int B) Dark((int R, int G, int B) c, int amt) =>
        (Math.Max(0, c.R - amt), Math.Max(0, c.G - amt), Math.Max(0, c.B - amt));

    /// <summary>Kullinje: summan av tva sinusvagor, fylld ner till botten.</summary>
    static void HillLayer(byte[] px, int w, int h, Lcg rnd, int baseY, int amp, (int R, int G, int B) c)
    {
        var f1 = 0.018 + rnd.NextD() * 0.02;
        var f2 = 0.05 + rnd.NextD() * 0.05;
        var p1 = rnd.NextD() * Math.Tau;
        var p2 = rnd.NextD() * Math.Tau;
        for (var x = 0; x < w; x++)
        {
            var yTop = baseY - (int)(amp * (0.55 + 0.45 * Math.Sin(x * f1 + p1)) + amp * 0.35 * Math.Sin(x * f2 + p2));
            for (var y = Math.Max(0, yTop); y < h; y++)
                Set(px, w, x, y, c);
        }
    }

    /// <summary>Granrad: trianglar med stam pa en gemensam kullinje.</summary>
    static void TreeRow(byte[] px, int w, int h, Lcg rnd, (int R, int G, int B) c, int baseY)
    {
        var x = 2 + rnd.Next(6);
        while (x < w - 4)
        {
            var tw = 7 + rnd.Next(6);        // bredd (udda ser bast ut)
            if (tw % 2 == 0) tw++;
            var th = tw + 3 + rnd.Next(5);   // hojd
            var top = baseY - th;
            for (var row = 0; row < th; row++)
            {
                var half = Math.Max(1, (tw / 2) * row / th);
                for (var dx = -half; dx <= half; dx++)
                    Set(px, w, x + tw / 2 + dx, top + row, c);
            }
            FillRect(px, w, h, x + tw / 2 - 1, baseY, 2, 3, Dark(c, 18));
            x += tw + 2 + rnd.Next(5);
        }
    }

    /// <summary>Stalaktiter (fran taket) eller stalagmiter (fran golvet).</summary>
    static void SpikeRow(byte[] px, int w, int h, Lcg rnd, (int R, int G, int B) c, bool fromTop, int maxLen)
    {
        var x = rnd.Next(6);
        while (x < w - 3)
        {
            var sw = 4 + rnd.Next(6);
            var len = maxLen / 2 + rnd.Next(Math.Max(1, maxLen / 2));
            for (var row = 0; row < len; row++)
            {
                var half = Math.Max(0, (sw / 2) * (len - row) / len);
                var y = fromTop ? row : h - 1 - row;
                for (var dx = -half; dx <= half; dx++)
                    Set(px, w, x + sw / 2 + dx, y, c);
            }
            x += sw + 1 + rnd.Next(4);
        }
    }

    /// <summary>Husrad: rektangelsiluetter med glest tanda gula fonster.</summary>
    static void BuildingRow(byte[] px, int w, int h, Lcg rnd, (int R, int G, int B) c, int topY, int maxUp)
    {
        var x = 0;
        while (x < w)
        {
            var bw = 10 + rnd.Next(14);
            var bh = topY + rnd.Next(maxUp) - maxUp / 2;
            for (var yy = Math.Max(0, bh); yy < h; yy++)
                for (var xx = x; xx < Math.Min(w, x + bw); xx++)
                    Set(px, w, xx, yy, c);
            for (var wy = bh + 2; wy < h * 88 / 100; wy += 3)
                for (var wx = x + 2; wx < x + bw - 2; wx += 3)
                    if (rnd.Next(10) < 3)
                        Set(px, w, wx, wy, (240, 208, 110));
            x += bw + 1 + rnd.Next(3);
        }
    }

    // ---- deterministik -----------------------------------------------------

    static bool Has(string p, params string[] keys) => keys.Any(p.Contains);

    /// <summary>Samma stabila hash som ljudvagen - prompt ska ge samma bild
    /// pa alla noder och over processomstarter.</summary>
    static int StableSeed(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s ?? "") hash = hash * 31 + c;
            return hash;
        }
    }

    /// <summary>Egen LCG - System.Random ger ingen garanti over ramverks-
    /// versioner, och bakgrunden SKA vara bitidentisk pa alla noder.</summary>
    sealed class Lcg(int seed)
    {
        ulong _s = (ulong)(uint)seed * 2862933555777941757UL + 3037000493UL;

        public int Next(int maxExclusive)
        {
            _s = _s * 6364136223846793005UL + 1442695040888963407UL;
            return (int)((_s >> 33) % (ulong)Math.Max(1, maxExclusive));
        }

        public double NextD()
        {
            _s = _s * 6364136223846793005UL + 1442695040888963407UL;
            return (_s >> 11) / (double)(1UL << 53);
        }
    }
}
