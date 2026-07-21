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
        if (type?.Trim().ToLowerInvariant() is not ("image" or "texture" or "sprite" or "ui"))
            return prompt;

        var projectRoot = ProjectRootDetector.Detect(workspaceRoot) ?? workspaceRoot;
        var style = ExtractStyle(Path.Combine(projectRoot, "DESIGN.md"))
            ?? "ren 2d-spelgrafik med enhetlig palett och tydliga konturer";
        if (prompt.Contains(style, StringComparison.OrdinalIgnoreCase))
            return prompt;
        return $"{prompt}. Konsekvent stil för HELA spelet: {style}. Spelasset med enkel/transparent bakgrund.";
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
}
