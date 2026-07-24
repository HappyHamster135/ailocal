using System.Globalization;
using System.Text.Json;
using AiLocal.Core.Agent;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29: projektets ENDA färgkälla. Tidigare fanns två frånkopplade
/// konstsystem: <see cref="AssetStyle"/> klistrade ett stil-suffix på
/// MOLN-promptar (hämtat ur DESIGN.md), medan <see cref="PixelAnimator"/>
/// ritade procedurellt med egna hash-härledda nyanser och aldrig läste
/// DESIGN.md. Resultatet blev ett collage: gubbarna hörde inte ihop med
/// bakgrunden, och två kit i samma projekt kunde ha helt olika konturfärg.
///
/// Bibeln skrivs EN gång per projekt till <c>art/artbible.json</c> och
/// speglas till DESIGN.md - där <see cref="AssetStyle.ExtractStyle"/> och
/// <see cref="AssetStyle.ExtractPalette"/> redan letar men ingenting hittills
/// har skrivit. Molnvägen och den procedurella vägen delar därmed palett.
/// </summary>
public sealed record ArtBible(
    string StyleName,
    string Mood,
    string OutlineHex,
    string[] SkinRampHex,
    string[] AccentRampHex,
    double AccentHue,
    int Frame,
    int FootY,
    int Schema = ArtBible.CurrentSchema,
    int RendererVersion = ArtBible.CurrentRenderer)
{
    public const int CurrentSchema = 1;
    /// <summary>Höjs bara när ritmotorn medvetet ändrar utseende. Specar med
    /// äldre version ritas ALDRIG om tyst - se CharacterCast.</summary>
    public const int CurrentRenderer = 1;

    public (byte, byte, byte) Outline => Hex(OutlineHex);
    public (byte, byte, byte) SkinDark => Hex(SkinRampHex[0]);
    public (byte, byte, byte) Skin => Hex(SkinRampHex[1]);
    public (byte, byte, byte) SkinLight => Hex(SkinRampHex[2]);

    public static (byte, byte, byte) Hex(string hex)
    {
        var s = (hex ?? "").TrimStart('#');
        if (s.Length != 6) return (255, 0, 255); // magenta = synligt fel
        return (
            byte.Parse(s[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(s.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    public static string ToHex((byte R, byte G, byte B) c) =>
        $"{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>HSV -> RGB. Delas med karaktärshärledningen så ramper i
    /// bibeln och i specarna byggs på exakt samma sätt.</summary>
    public static (byte, byte, byte) Hsv(double h, double s, double v)
    {
        h = ((h % 1.0) + 1.0) % 1.0;
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
}

/// <summary>Läser/skriver art-bibeln i projektet och speglar den till
/// DESIGN.md. Allt är deterministiskt: samma (genre, identitetstext) ger
/// samma bibel i alla processer och över omstarter.</summary>
public static class ArtBibleStore
{
    private const string RelPath = "art/artbible.json";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string PathFor(string projectRoot) =>
        Path.Combine(projectRoot, "art", "artbible.json");

    /// <summary>Läser befintlig bibel, eller härleder och SPARAR en ny.
    /// En redan sparad bibel ändras aldrig - det är hela poängen: en
    /// uppföljning månader senare får samma palett.</summary>
    public static ArtBible LoadOrCreate(string projectRoot, string genre, string identityText)
    {
        var existing = Load(projectRoot);
        if (existing is not null) return existing;
        var bible = Derive(genre, identityText);
        Save(projectRoot, bible);
        return bible;
    }

    public static ArtBible? Load(string projectRoot)
    {
        try
        {
            var p = PathFor(projectRoot);
            if (!File.Exists(p)) return null;
            var b = JsonSerializer.Deserialize<ArtBible>(File.ReadAllText(p));
            // Trasig/ofullständig fil ska inte krascha bygget - härled om.
            if (b is null || b.SkinRampHex is not { Length: 3 } || b.AccentRampHex is not { Length: 3 })
                return null;
            return b;
        }
        catch { return null; }
    }

    public static void Save(string projectRoot, ArtBible bible)
    {
        try
        {
            var p = PathFor(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(bible, Json));
        }
        catch { /* bibeln är en optimering, aldrig en byggstoppare */ }
    }

    /// <summary>Deterministisk härledning. Genren ger stilankaret (via
    /// VisualStyleLib, som sedan v2.29 är en ren funktion), identitetstexten
    /// ger variationen mellan projekt.</summary>
    public static ArtBible Derive(string genre, string identityText)
    {
        var seed = VisualStyleLib.StableHash((genre ?? "") + "|" + (identityText ?? ""));
        var style = VisualStyleLib.PickForGenre(genre ?? "");
        // Accent-nyansen ankras i stilens accentfärg så karaktärerna hör ihop
        // med UI och bakgrund, med en liten projektunik förskjutning.
        var accentHue = HueOf(style.Accent.R, style.Accent.G, style.Accent.B);
        accentHue = ((accentHue + ((seed >> 9) % 40 - 20) / 360.0) + 1.0) % 1.0;
        // Hudtonen varierar mellan projekt men håller sig i hudspannet.
        var skinHue = 0.05 + ((seed >> 3) % 5) / 100.0;
        var skinSat = 0.30 + ((seed >> 17) % 12) / 100.0;
        return new ArtBible(
            StyleName: style.Name,
            Mood: style.Mood,
            OutlineHex: "1B1624",
            SkinRampHex:
            [
                ArtBible.ToHex(ArtBible.Hsv(skinHue, skinSat + 0.10, 0.76)),
                ArtBible.ToHex(ArtBible.Hsv(skinHue, skinSat, 0.94)),
                ArtBible.ToHex(ArtBible.Hsv(skinHue + 0.02, skinSat - 0.13, 1.0)),
            ],
            AccentRampHex:
            [
                ArtBible.ToHex(ArtBible.Hsv(accentHue, 0.68, 0.58)),
                ArtBible.ToHex(ArtBible.Hsv(accentHue, 0.62, 0.86)),
                ArtBible.ToHex(ArtBible.Hsv(accentHue, 0.48, 1.0)),
            ],
            AccentHue: accentHue,
            Frame: 24,
            FootY: 22);
    }

    private static double HueOf(float r, float g, float b)
    {
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var d = max - min;
        if (d < 1e-6) return 0;
        double h;
        if (max == r) h = ((g - b) / d + (g < b ? 6 : 0)) / 6.0;
        else if (max == g) h = ((b - r) / d + 2) / 6.0;
        else h = ((r - g) / d + 4) / 6.0;
        return h;
    }

    /// <summary>Speglar bibeln till DESIGN.md under EXAKT de rubriker
    /// <see cref="AssetStyle"/> redan letar efter ("## Art direction",
    /// "## Palett"). Det är kopplingen som gör att molngenererade bilder
    /// hamnar i samma palett som de procedurella gubbarna. Skrivs TIDIGT i
    /// filen - ProjectContext klipper de första 1500 tecknen.</summary>
    public static void MirrorToDesign(string projectRoot, ArtBible bible)
    {
        try
        {
            var design = Path.Combine(projectRoot, "DESIGN.md");
            if (!File.Exists(design)) return;
            var text = File.ReadAllText(design);
            if (text.Contains("## Art direction", StringComparison.OrdinalIgnoreCase)) return;
            var block =
                "\n## Art direction\n" +
                $"{bible.StyleName} - {bible.Mood}. Pixelart, {bible.Frame}px-ram, sluten mork kontur, " +
                "ljus fran vanster-topp, 2-3 nyanser per material.\n" +
                "\n## Palett\n" +
                $"kontur #{bible.OutlineHex}, hud #{string.Join(" #", bible.SkinRampHex)}, " +
                $"accent #{string.Join(" #", bible.AccentRampHex)}\n";
            // Efter forsta rubriken (titeln) sa den ryms inom ProjectContext-
            // fonstret, i stallet for att appendas sist dar den klipps bort.
            var nl = text.IndexOf('\n');
            var insertAt = nl < 0 ? text.Length : nl + 1;
            File.WriteAllText(design, text[..insertAt] + block + text[insertAt..]);
        }
        catch { /* spegling är en bonus, aldrig en byggstoppare */ }
    }
}
