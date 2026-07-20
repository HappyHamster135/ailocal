namespace AiLocal.Node.Hosting;

/// <summary>
/// Picks where a scaffold should land when the requested root is already in
/// use. Scaffolding used to hard-refuse a non-empty root ("root-mappen ar
/// inte tom") - but agent workspaces are long-lived, so the SECOND build in a
/// workspace always hit that wall and the agent had to hand-write every file
/// (observed in a user transcript: scaffold_app refused, 88 steps of manual
/// file writing followed). Now a fresh subfolder is derived from the prompt
/// instead: "bygg ett fotbolls managerspel" -> root/fotbolls-managerspel/.
/// </summary>
internal static class ScaffoldPaths
{
    /// <summary>Returns <paramref name="root"/> itself when it's empty or
    /// missing, otherwise a fresh (empty or new) subfolder named after the
    /// prompt, with -2/-3 suffixes when earlier builds took the name.</summary>
    internal static string ForProject(string root, string prompt, string fallbackName)
    {
        if (!Directory.Exists(root) || !Directory.EnumerateFileSystemEntries(root).Any())
            return root;

        var slug = Slug(prompt, fallbackName);
        var candidate = Path.Combine(root, slug);
        for (var i = 2; Directory.Exists(candidate) && Directory.EnumerateFileSystemEntries(candidate).Any(); i++)
            candidate = Path.Combine(root, $"{slug}-{i}");
        return candidate;
    }

    /// <summary>Folder-name slug from a prompt: up to three meaningful words,
    /// lowercased, joined by dashes. Verbs/fillers are dropped so "bygg ett
    /// 2d plattformsspel" becomes "plattformsspel", not "bygg-ett-2d".</summary>
    internal static string Slug(string prompt, string fallbackName)
    {
        var cleaned = new string((prompt ?? "")
            .ToLowerInvariant()
            .Select(c => char.IsLetterOrDigit(c) ? c : ' ')
            .ToArray());
        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length > 2 && !StopWords.Contains(w))
            .Take(3)
            .ToArray();
        return words.Length == 0 ? fallbackName : string.Join("-", words);
    }

    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "bygg", "bygga", "skapa", "gör", "gor", "göra", "gora", "ett", "en", "med", "och",
        "som", "för", "for", "kan", "vill", "ska", "har", "inte", "till", "där", "dar",
        "det", "den", "man", "the", "and", "with", "build", "create", "make", "want",
        "please", "snälla", "snalla", "hade", "velat", "gärna", "garna"
    };
}
