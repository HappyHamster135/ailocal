namespace AiLocal.Core.Agent;

/// <summary>
/// Finds THE project directory inside an agent workspace. A workspace is
/// long-lived and accumulates projects over time (an old HTML5 game at the
/// root, a new Python build in a subfolder, ...), and every consumer that
/// naively assumed "workspace root == the project" picked the wrong one:
/// verify parsed a stale game's JS while the agent worked on a Python app,
/// and the quality gate would have graded last week's build. The rule here:
/// among the root itself and its first-level subdirectories that contain a
/// recognizable project (ProjectVerifier.Detect), the one with the NEWEST
/// file write wins - that is where the agent is working right now.
/// </summary>
public static class ProjectRootDetector
{
    private static readonly string[] SkipDirs =
        ["node_modules", ".git", "bin", "obj", "dist", "build", "packages", "__pycache__"];

    public static string? Detect(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot) || !Directory.Exists(workspaceRoot))
            return null;

        var verifier = new ProjectVerifier();
        var candidates = new List<(string Dir, bool IsRoot)>();
        if (verifier.Detect(workspaceRoot) != ProjectVerifier.ProjectKind.Unknown)
            candidates.Add((Path.GetFullPath(workspaceRoot), true));

        foreach (var dir in SafeEnumerateDirectories(workspaceRoot))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith('.') || SkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                continue;
            if (verifier.Detect(dir) != ProjectVerifier.ProjectKind.Unknown)
                candidates.Add((Path.GetFullPath(dir), false));
        }

        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0].Dir;

        // The root candidate must not count files that live inside another
        // candidate's subtree, or a stale root project would tie with (and
        // often beat, via directory enumeration order) the newer subproject.
        var subDirs = candidates.Where(c => !c.IsRoot).Select(c => c.Dir).ToArray();
        return candidates
            .OrderByDescending(c => NewestWriteUtc(c.Dir, c.IsRoot ? subDirs : []))
            .First().Dir;
    }

    /// <summary>Newest file write under <paramref name="dir"/>, skipping noise
    /// directories (node_modules, .git, ...) and dotfiles. Used both for
    /// candidate ranking and for the quality gate's "did the agent actually
    /// write anything during this run?" check.</summary>
    public static DateTime NewestWriteUtc(string dir) => NewestWriteUtc(dir, []);

    private static DateTime NewestWriteUtc(string dir, string[] excludeSubtrees)
    {
        var newest = DateTime.MinValue;
        try
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                var full = Path.GetFullPath(file);
                var rel = Path.GetRelativePath(dir, full);
                var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (segments.Any(s => s.StartsWith('.') || SkipDirs.Contains(s, StringComparer.OrdinalIgnoreCase)))
                    continue;
                if (excludeSubtrees.Any(ex => full.StartsWith(ex + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var t = File.GetLastWriteTimeUtc(full);
                if (t > newest) newest = t;
            }
        }
        catch
        {
            // Ranking heuristic only - an unreadable file/dir must never crash
            // the gate or verify; the candidate just ranks by what WAS readable.
        }
        return newest;
    }

    private static IEnumerable<string> SafeEnumerateDirectories(string root)
    {
        try { return Directory.EnumerateDirectories(root).ToArray(); }
        catch { return []; }
    }
}
