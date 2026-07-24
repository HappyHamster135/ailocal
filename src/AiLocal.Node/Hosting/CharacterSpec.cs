namespace AiLocal.Node.Hosting;

/// <summary>Färgramper för en karaktär. Hex-strängar (inte tupler) så specen
/// kan serialiseras och läsas tillbaka oförändrad månader senare.</summary>
public sealed record CharacterPalette(
    string Outline,
    string[] SkinRamp,
    string[] ShirtRamp,
    string[] PantsRamp,
    string[] HairRamp,
    string Shoe,
    string Eye,
    bool EyeGlint);

/// <summary>Siluettdrag. Fem enum-lika fält (inte fria flaggor) så
/// kombinatoriken är uttömbar i test: alla kombinationer × poser kan
/// klippningstestas helt.</summary>
public sealed record CharacterTraits(
    string Body,   // slim | normal | broad
    string Hair,   // bald | short | long | spiky | ponytail
    string Face,   // plain | beard | visor
    string Mark);  // none | horns | ears

/// <summary>
/// v2.29: en karaktärs IDENTITET som lagrad data. Detta är kärnan i fixen på
/// ägarens "gubben ser annorlunda ut hela tiden": tidigare var identiteten
/// <c>hash(promptsträngen)</c> (PixelAnimator.Palette), så minsta ändring i
/// uppdragstexten - eller suffixen scaffolden lägger på (" (3d)",
/// " fiende monster") - gav en ny figur. Nu är nyckeln ett NAMN, och de
/// upplösta färgerna/dragen skrivs till disk. En uppföljning läser tillbaka
/// exakt samma värden i stället för att härleda om.
/// </summary>
public sealed record CharacterSpec(
    string Slug,
    string DisplayName,
    string Role,
    uint Seed,
    int Frame,
    CharacterPalette Palette,
    CharacterTraits Traits,
    string Origin,
    int RendererVersion = ArtBible.CurrentRenderer,
    int Schema = CharacterSpec.CurrentSchema)
{
    public const int CurrentSchema = 1;
}

/// <summary>Deterministisk härledning av en spec ur (projektfrö, slug, roll)
/// plus projektets art-bibel. Samma indata ger alltid samma spec - i alla
/// processer, på alla noder, över omstarter.</summary>
public static class CharacterSpecFactory
{
    /// <summary>Roller som ska läsas som fiender: egen dragtabell (horn,
    /// röda ögon utan glans, mörkare ramper, bredare kropp) så fienden inte
    /// blir en omfärgad kopia av spelaren - den vanligaste anmärkningen på
    /// hur kiten ser ut i dag.</summary>
    private static bool IsFoe(string role) =>
        role is "enemy" or "foe" or "monster" or "boss";

    public static CharacterSpec Derive(string slug, string displayName, string role, ArtBible bible, uint projectSeed)
    {
        var rng = new SpecRng(projectSeed ^ VisualStyleLib.StableHash(slug ?? ""));
        var foe = IsFoe(role ?? "");
        var traits = DeriveTraits(rng, foe);
        var palette = DerivePalette(rng, bible, foe, traits);
        return new CharacterSpec(
            Slug: slug ?? "player",
            DisplayName: string.IsNullOrWhiteSpace(displayName) ? Title(slug ?? "player") : displayName,
            Role: role ?? "player",
            Seed: projectSeed ^ VisualStyleLib.StableHash(slug ?? ""),
            Frame: bible.Frame,
            Palette: palette,
            Traits: traits,
            Origin: "procedural");
    }

    private static CharacterTraits DeriveTraits(SpecRng rng, bool foe)
    {
        var body = foe
            ? Pick(rng, "broad", "normal", "broad")
            : Pick(rng, "slim", "normal", "normal", "broad");
        var hair = foe
            ? Pick(rng, "bald", "spiky", "spiky")
            : Pick(rng, "short", "long", "spiky", "ponytail", "bald");
        var face = foe
            ? Pick(rng, "plain", "plain", "visor")
            : Pick(rng, "plain", "plain", "beard", "visor");
        var mark = foe
            ? Pick(rng, "horns", "horns", "ears")
            : Pick(rng, "none", "none", "none", "ears");
        return new CharacterTraits(body, hair, face, mark);
    }

    private static CharacterPalette DerivePalette(SpecRng rng, ArtBible bible, bool foe, CharacterTraits traits)
    {
        // Skjortnyansen valjs ur en HARMONITABELL runt bibelns accent-hue
        // (analog / komplement / triad) i stallet for en fri 0-360-hash.
        // Det ar skillnaden mellan "alla karaktarer hor ihop" och "ett
        // collage av slumpfarger".
        double[] harmonies = foe
            ? [0.50, 0.45, 0.55]                    // fiender: komplementsidan
            : [0.0, 0.083, -0.083, 0.33, -0.33];    // spelare: analogt + triad
        var shirtHue = bible.AccentHue + harmonies[(int)(rng.Next() % (uint)harmonies.Length)];
        var pantsHue = shirtHue + 0.55;
        // HAR ar inte en accentfarg. Att ankra harnyansen i projektets accent
        // gav varenda figur gront har i ett skogsprojekt - de sag ut att bara
        // mossa. Haret valjs ur en naturlig tabell (svart/brun/kastanj/blond/
        // gra), fiender far dessutom en kall onaturlig ton.
        (double H, double S, double V)[] hairs = foe
            ? [(0.02, 0.55, 0.26), (0.72, 0.30, 0.28), (0.05, 0.10, 0.20)]
            : [(0.06, 0.20, 0.16), (0.07, 0.48, 0.30), (0.05, 0.55, 0.38),
               (0.10, 0.52, 0.66), (0.60, 0.06, 0.52)];
        var hair = hairs[(int)(rng.Next() % (uint)hairs.Length)];

        var sat = foe ? 0.72 : 0.62;
        var val = foe ? 0.70 : 0.86;

        string[] Ramp(double h, double s, double v) =>
        [
            ArtBible.ToHex(ArtBible.Hsv(h, Math.Min(1, s + 0.06), v * 0.68)),
            ArtBible.ToHex(ArtBible.Hsv(h, s, v)),
            ArtBible.ToHex(ArtBible.Hsv(h, Math.Max(0, s - 0.14), Math.Min(1, v * 1.16))),
        ];

        // Hudrampen kommer ur BIBELN (delas av alla karaktarer i projektet);
        // fiender far en kallare/morkare variant men samma grundton.
        var skin = foe
            ? new[]
            {
                ArtBible.ToHex(Shade(bible.SkinDark, 0.62)),
                ArtBible.ToHex(Shade(bible.Skin, 0.70)),
                ArtBible.ToHex(Shade(bible.SkinLight, 0.78)),
            }
            : bible.SkinRampHex;

        return new CharacterPalette(
            Outline: bible.OutlineHex,
            SkinRamp: skin,
            ShirtRamp: Ramp(shirtHue, sat, val),
            PantsRamp: Ramp(pantsHue, 0.36, foe ? 0.34 : 0.46),
            HairRamp: Ramp(hair.H, hair.S, hair.V),
            Shoe: ArtBible.ToHex(ArtBible.Hsv(pantsHue, 0.30, 0.22)),
            Eye: foe ? "D22B2B" : "18161F",
            EyeGlint: !foe);
    }

    private static (byte, byte, byte) Shade((byte R, byte G, byte B) c, double f) =>
        ((byte)(c.R * f), (byte)(c.G * f), (byte)(c.B * f));

    private static string Pick(SpecRng rng, params string[] options) =>
        options[(int)(rng.Next() % (uint)options.Length)];

    private static string Title(string slug) =>
        slug.Length == 0 ? slug : char.ToUpperInvariant(slug[0]) + slug[1..];
}

/// <summary>splitmix32 - en liten deterministisk strom. System.Random ger
/// ingen garanti over .NET-versioner och far darfor inte anvandas till
/// nagot som maste vara bit-identiskt over tid.</summary>
public sealed class SpecRng(uint seed)
{
    private uint _s = seed == 0 ? 0x9E3779B9u : seed;

    public uint Next()
    {
        _s += 0x9E3779B9u;
        var z = _s;
        z = (z ^ (z >> 16)) * 0x21F0AAADu;
        z = (z ^ (z >> 15)) * 0x735A2D97u;
        return z ^ (z >> 15);
    }
}
