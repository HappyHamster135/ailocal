using System.Text.RegularExpressions;

namespace AiLocal.Core.Agent;

/// <summary>
/// Style consistency for generated art: every image prompt in a project gets
/// the SAME style suffix, sourced from the project's DESIGN.md art-direction
/// section when one exists. Without this each asset came out in its own
/// random style and the game looked like a collage; with it, one hand drew
/// everything. Non-image types pass through untouched.
/// </summary>
public static class AssetStyle
{
    private const int MaxStyleChars = 220;

    public static string Apply(string workspaceRoot, string type, string prompt)
    {
        // C10 (art-bibel): tilesets, bakgrunder och miljögrafik måste följa
        // SAMMA konstriktning som sprites/UI - annars ser spelet ut som ett
        // collage även om karaktärerna är enhetliga.
        if (type?.Trim().ToLowerInvariant() is not
            ("image" or "texture" or "sprite" or "ui" or "tileset" or "tile"
             or "background" or "bg" or "backdrop" or "environment"))
            return prompt;

        var projectRoot = ProjectRootDetector.Detect(workspaceRoot) ?? workspaceRoot;
        var design = Path.Combine(projectRoot, "DESIGN.md");
        var style = ExtractStyle(design)
            ?? "ren 2d-spelgrafik med enhetlig palett och tydliga konturer";
        if (prompt.Contains(style, StringComparison.OrdinalIgnoreCase))
            return prompt;
        // Nagel fast paletten om DESIGN.md har ett palett-avsnitt - da delar
        // varje asset exakt samma farger, inte bara "en enhetlig palett".
        var palette = ExtractPalette(design);
        var bible = palette is null ? style : $"{style}. Samma palett överallt: {palette}";
        return $"{prompt}. ART-BIBEL - samma stil, palett och stämning för HELA spelet (sprites, tiles, bakgrunder, UI): {bible}. Spelasset med enkel/transparent bakgrund.";
    }

    /// <summary>First paragraph under an art-direction-ish heading in
    /// DESIGN.md, or null. Public-ish for tests via Apply.</summary>
    internal static string? ExtractStyle(string designPath)
    {
        try
        {
            if (!File.Exists(designPath)) return null;
            var text = File.ReadAllText(designPath);
            var match = Regex.Match(text,
                @"^#{1,4}\s*(?:art direction|grafisk stil|visuell stil|stil|art style|visual style)\s*$\s+(.+?)(?:\r?\n\r?\n|\r?\n#|\z)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (!match.Success) return null;
            var style = Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim().TrimEnd('.');
            if (style.Length == 0) return null;
            return style.Length > MaxStyleChars ? style[..MaxStyleChars] : style;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>C10: palette/colour section from DESIGN.md so every asset shares
    /// the SAME colours, or null when there is no explicit palette.</summary>
    internal static string? ExtractPalette(string designPath)
    {
        try
        {
            if (!File.Exists(designPath)) return null;
            var text = File.ReadAllText(designPath);
            var match = Regex.Match(text,
                @"^#{1,4}\s*(?:palett|palette|färger|farger|colou?rs?)\s*$\s+(.+?)(?:\r?\n\r?\n|\r?\n#|\z)",
                RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Singleline);
            if (!match.Success) return null;
            var pal = Regex.Replace(match.Groups[1].Value, @"\s+", " ").Trim().TrimEnd('.');
            if (pal.Length == 0) return null;
            return pal.Length > 120 ? pal[..120] : pal;
        }
        catch
        {
            return null;
        }
    }
}
