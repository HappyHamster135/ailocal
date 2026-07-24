using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29: projektets ROLLISTA - den lagrade identiteten som gör att en
/// karaktär överlever sessioner. <see cref="Resolve"/> returnerar en
/// befintlig post ORDAGRANT och härleder aldrig om, oavsett vilken
/// beskrivning anroparen skickar med. Det är låset mot ägarens
/// "gubben ser annorlunda ut hela tiden": uppdragstexten får driva hur
/// mycket som helst utan att figuren ändras.
///
/// En fil per karaktär under <c>art/cast/</c> - inte en delad cast.json -
/// så parallella TeamBuild-worktrees inte får merge-konflikter.
/// </summary>
public static class CharacterCast
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static string DirFor(string projectRoot) => Path.Combine(projectRoot, "art", "cast");

    public static string PathFor(string projectRoot, string slug) =>
        Path.Combine(DirFor(projectRoot), Sanitize(slug) + ".json");

    /// <summary>Uppåtsondering efter project.godot (max 6 nivåer) - samma
    /// mönster som AssetGenerator redan använder för res://-vägar.
    /// ProjectRootDetector rankar på skrivtid och kan flippa mitt i ett
    /// bygge; rollistan måste ligga bredvid project.godot, inte i "senast
    /// ändrade" mapp.</summary>
    public static string FindProjectRoot(string startPath)
    {
        try
        {
            var dir = Directory.Exists(startPath) ? startPath : Path.GetDirectoryName(startPath);
            for (var i = 0; i < 6 && !string.IsNullOrEmpty(dir); i++)
            {
                if (File.Exists(Path.Combine(dir, "project.godot"))) return dir;
                dir = Path.GetDirectoryName(dir);
            }
        }
        catch { /* faller igenom */ }
        return Directory.Exists(startPath) ? startPath : (Path.GetDirectoryName(startPath) ?? startPath);
    }

    public static CharacterSpec? Load(string projectRoot, string slug)
    {
        try
        {
            var p = PathFor(projectRoot, slug);
            if (!File.Exists(p)) return null;
            var spec = JsonSerializer.Deserialize<CharacterSpec>(File.ReadAllText(p));
            if (spec is null || spec.Palette is null || spec.Traits is null) return null;
            if (spec.Palette.SkinRamp is not { Length: 3 } || spec.Palette.ShirtRamp is not { Length: 3 })
                return null;
            return spec;
        }
        catch { return null; }
    }

    public static void Save(string projectRoot, CharacterSpec spec)
    {
        try
        {
            var p = PathFor(projectRoot, spec.Slug);
            Directory.CreateDirectory(Path.GetDirectoryName(p)!);
            File.WriteAllText(p, JsonSerializer.Serialize(spec, Json));
        }
        catch { /* rollistan får aldrig stoppa ett bygge */ }
    }

    /// <summary>Hämtar en karaktär vid NAMN. Finns den redan returneras den
    /// oförändrad - beskrivningen ignoreras medvetet, annars vore identiteten
    /// tillbaka till att bero på promptens formulering.</summary>
    public static (CharacterSpec Spec, bool Created) Resolve(
        string projectRoot, string slug, string? displayName, string role,
        ArtBible bible, uint projectSeed)
    {
        slug = Sanitize(slug);
        var existing = Load(projectRoot, slug);
        if (existing is not null) return (existing, false);
        var spec = CharacterSpecFactory.Derive(slug, displayName ?? "", role, bible, projectSeed);
        Save(projectRoot, spec);
        return (spec, true);
    }

    public static IReadOnlyList<CharacterSpec> All(string projectRoot)
    {
        try
        {
            var dir = DirFor(projectRoot);
            if (!Directory.Exists(dir)) return [];
            return Directory.GetFiles(dir, "*.json")
                .Select(f => Load(projectRoot, Path.GetFileNameWithoutExtension(f)))
                .Where(s => s is not null)
                .Select(s => s!)
                .OrderBy(s => s.Slug, StringComparer.Ordinal)
                .ToList();
        }
        catch { return []; }
    }

    /// <summary>Filnamnsbas för en karaktärs sheet. De två standardrollerna
    /// behåller kitens historiska namn (player.png / enemy.png) så INGET
    /// kit-GDScript behöver ändras när identitetssystemet tas i bruk.</summary>
    public static string SheetBase(string slug) => Sanitize(slug) switch
    {
        "player" => "player",
        "enemy" => "enemy",
        var s => "char_" + s,
    };

    internal static string Sanitize(string slug)
    {
        var s = (slug ?? "").Trim().ToLowerInvariant();
        var clean = new string(s.Where(c => char.IsLetterOrDigit(c) || c == '_' || c == '-').ToArray());
        return clean.Length == 0 ? "player" : clean;
    }
}
