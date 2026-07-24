namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.2.0: Curated visual style presets for game development.
/// Instead of agents picking random RGB values, they can select a named
/// style preset that provides a coherent color palette, UI theme, and
/// animation timing hints. Each style is a complete visual identity.
/// 
/// Usage: var style = [ADDRESS]"(type)";
///        bg_color = style.Background;
///        accent = style.Accent;
/// </summary>
public static class VisualStyleLib
{
    public record Style(string Name, string Description,
        ColorSpec Background, ColorSpec Accent, ColorSpec Text,
        ColorSpec Particle, ColorSpec Danger, ColorSpec Success,
        float AnimSpeed, string Mood);

    public record ColorSpec(float R, float G, float B, string Hex);

    public static readonly Style[] All;

    static VisualStyleLib()
    {
        All = new Style[]
        {
            // ──── Warm / Desert ───────────────────────────────────────────
            new("desert_warmth", "Golden sands, warm sun, ancient stone",
                new(0.12f, 0.08f, 0.04f, "#1E140A"),
                new(0.95f, 0.70f, 0.20f, "#F2B233"),
                new(0.95f, 0.88f, 0.75f, "#F2E0BF"),
                new(0.90f, 0.65f, 0.15f, "#E6A626"),
                new(0.85f, 0.25f, 0.15f, "#D94026"),
                new(0.30f, 0.75f, 0.30f, "#4DC04D"),
                1.0f, "Adventure, exploration, ancient ruins"),

            // ──── Frost / Ice ────────────────────────────────────────────
            new("frost_night", "Deep blue ice, crisp white snow, aurora hints",
                new(0.04f, 0.08f, 0.18f, "#0A142E"),
                new(0.40f, 0.75f, 0.95f, "#66BFF2"),
                new(0.85f, 0.92f, 1.00f, "#D9EBFF"),
                new(0.60f, 0.85f, 0.95f, "#99D9F2"),
                new(0.95f, 0.30f, 0.40f, "#F24D66"),
                new(0.35f, 0.85f, 0.55f, "#59D98C"),
                0.9f, "Cold, mysterious, precision, winter levels"),

            // ──── Forest / Nature ─────────────────────────────────────────
            new("deep_forest", "Rich greens, earthy browns, dappled sunlight",
                new(0.06f, 0.15f, 0.06f, "#0F260F"),
                new(0.35f, 0.80f, 0.30f, "#59CC4D"),
                new(0.85f, 0.95f, 0.80f, "#D9F2CC"),
                new(0.50f, 0.90f, 0.35f, "#80E659"),
                new(0.85f, 0.20f, 0.25f, "#D93340"),
                new(0.45f, 0.70f, 0.30f, "#73B34D"),
                1.1f, "Nature, growth, organic, platformers"),

            // ──── Neon / Cyberpunk ────────────────────────────────────────
            new("neon_underground", "Dark city with vibrant neon signs",
                new(0.04f, 0.02f, 0.10f, "#0A0529"),
                new(0.20f, 0.90f, 0.95f, "#33E6F2"),
                new(0.90f, 0.85f, 1.00f, "#E6D9FF"),
                new(0.80f, 0.30f, 0.95f, "#CC4DF2"),
                new(1.00f, 0.20f, 0.40f, "#FF3366"),
                new(0.25f, 0.95f, 0.55f, "#40F28C"),
                1.2f, "Fast-paced, futuristic, shooters, racing"),

            // ──── Candy / Playful ─────────────────────────────────────────
            new("candy_pop", "Bright candy colors, bouncy and cheerful",
                new(0.15f, 0.10f, 0.25f, "#261A40"),
                new(1.00f, 0.75f, 0.30f, "#FFBF4D"),
                new(1.00f, 0.95f, 0.85f, "#FFF2D9"),
                new(0.95f, 0.55f, 0.75f, "#F28CBF"),
                new(0.95f, 0.35f, 0.45f, "#F25973"),
                new(0.45f, 0.90f, 0.55f, "#73E68C"),
                1.3f, "Casual, party games, puzzle, kids"),

            // ──── Industrial / Metal ──────────────────────────────────────
            new("rusted_machine", "Weathered metal, oil, steam, gears",
                new(0.08f, 0.08f, 0.09f, "#141417"),
                new(0.85f, 0.55f, 0.25f, "#D98C40"),
                new(0.80f, 0.78f, 0.75f, "#CCC7BF"),
                new(0.70f, 0.45f, 0.20f, "#B37333"),
                new(0.90f, 0.25f, 0.15f, "#E64026"),
                new(0.50f, 0.60f, 0.55f, "#80998C"),
                0.85f, "Heavy, mechanical, management, artillery"),

            // ──── Space / Void ────────────────────────────────────────────
            new("deep_space", "Endless void, distant stars, cold technology",
                new(0.02f, 0.02f, 0.08f, "#050514"),
                new(0.30f, 0.55f, 0.90f, "#4D8CE6"),
                new(0.75f, 0.82f, 0.95f, "#BFD1F2"),
                new(0.40f, 0.65f, 0.95f, "#66A6F2"),
                new(0.95f, 0.20f, 0.30f, "#F2334D"),
                new(0.30f, 0.80f, 0.60f, "#4DCC99"),
                0.95f, "Isolation, technology, space shooters, sci-fi"),

            // ──── Sunset / Warm Retro ─────────────────────────────────────
            new("sunset_retro", "80s sunset gradient, palm trees, synthwave",
                new(0.10f, 0.04f, 0.15f, "#1A0A26"),
                new(0.95f, 0.45f, 0.55f, "#F2738C"),
                new(0.98f, 0.90f, 0.75f, "#FAE6BF"),
                new(0.95f, 0.55f, 0.35f, "#F28C59"),
                new(0.90f, 0.25f, 0.40f, "#E64066"),
                new(0.40f, 0.85f, 0.70f, "#66D9B3"),
                1.15f, "Retro, nostalgic, racing, arcade"),

            // ──── Minimal / Clean ─────────────────────────────────────────
            new("clean_slate", "Minimal white/gray with single accent color",
                new(0.12f, 0.12f, 0.14f, "#1E1E23"),
                new(0.35f, 0.65f, 0.95f, "#59A6F2"),
                new(0.90f, 0.90f, 0.92f, "#E6E6EB"),
                new(0.45f, 0.70f, 0.95f, "#73B3F2"),
                new(0.90f, 0.35f, 0.35f, "#E65959"),
                new(0.40f, 0.75f, 0.50f, "#66BF80"),
                1.0f, "Clean, professional, puzzle, strategy"),

            // ──── Toxic / Alien ───────────────────────────────────────────
            new("toxic_hive", "Bioluminescent greens, organic purples",
                new(0.04f, 0.10f, 0.04f, "#0A1A0A"),
                new(0.30f, 0.95f, 0.35f, "#4DF259"),
                new(0.80f, 0.95f, 0.80f, "#CCF2CC"),
                new(0.55f, 0.90f, 0.25f, "#8CE640"),
                new(0.75f, 0.20f, 0.70f, "#BF33B3"),
                new(0.50f, 0.85f, 0.40f, "#80D966"),
                1.05f, "Alien, organic, horror, sci-fi, roguelike"),
        };
    }

    /// <summary>Get a style by name (case-insensitive).</summary>
    public static Style? Get(string name) =>
        All.FirstOrDefault(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>List all available style names with descriptions.</summary>
    public static IReadOnlyList<string> List() =>
        All.Select(s => $"{s.Name}: {s.Description}").ToList();

    /// <summary>Pick a random style suitable for a genre.
    /// Some styles map better to certain genres.</summary>
    public static Style PickForGenre(string genre) => genre switch
    {
        "party" => All.First(s => s.Name == "candy_pop"),
        "platformer" => All.First(s => s.Name == "deep_forest"),
        "rpg" or "roguelike" => All.First(s => s.Name == "desert_warmth"),
        "shooter" => All.First(s => s.Name == "neon_underground"),
        "racing" => All.First(s => s.Name == "sunset_retro"),
        "management" or "simulator" => All.First(s => s.Name == "clean_slate"),
        "artillery" => All.First(s => s.Name == "rusted_machine"),
        "puzzle" => All.First(s => s.Name == "clean_slate"),
        // v2.29: STABIL fallback. Tidigare Random.Shared.Next -> varje anrop
        // gav olika stil for samma genre (snake, breakout, memory, quiz,
        // towerdefense, iso, fps ... allt som inte listas ovan). Art-bibeln
        // och karaktarspaletten bygger pa det har valet, sa det MASTE vara
        // en ren funktion av genren.
        _ => All[(int)(StableHash(genre ?? "") % (uint)All.Length)],
    };

    /// <summary>FNV-1a: samma genre ger samma stil i alla processer och
    /// over omstarter (string.GetHashCode ar randomiserad per process).</summary>
    internal static uint StableHash(string s)
    {
        var h = 2166136261u;
        foreach (var c in s) h = (h ^ c) * 16777619u;
        return h;
    }

    /// <summary>Generate GDScript color constants from a style.
    /// Returns a string that can be pasted into a GDScript file.</summary>
    public static string ToGDScript(Style style) =>
        $"# Visual style: {style.Name} — {style.Mood}\n" +
        $"const BG_COLOR := Color({style.Background.R}, {style.Background.G}, {style.Background.B})\n" +
        $"const ACCENT_COLOR := Color({style.Accent.R}, {style.Accent.G}, {style.Accent.B})\n" +
        $"const TEXT_COLOR := Color({style.Text.R}, {style.Text.G}, {style.Text.B})\n" +
        $"const PARTICLE_COLOR := Color({style.Particle.R}, {style.Particle.G}, {style.Particle.B})\n" +
        $"const DANGER_COLOR := Color({style.Danger.R}, {style.Danger.G}, {style.Danger.B})\n" +
        $"const SUCCESS_COLOR := Color({style.Success.R}, {style.Success.G}, {style.Success.B})";
}
