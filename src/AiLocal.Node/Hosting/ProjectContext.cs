using System.Text;
using AiLocal.Core.Agent;
using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Project continuity for follow-up prompts. The pre-scaffold floor covers
/// EMPTY workspaces; this covers the opposite case: "gör spelet svårare" on a
/// workspace that already holds a project used to reach the model with zero
/// context about what exists - weak models then scaffolded a NEW game or
/// hallucinated files. Now a build/back-referencing prompt on a non-empty
/// workspace gets an automatic project brief (folder, engine, file list,
/// DESIGN.md excerpt) appended, so the default is always CONTINUE, and
/// starting something new requires saying so.
/// </summary>
public static class ProjectContext
{
    private const int MaxFilesListed = 25;
    private const int MaxDesignChars = 1500;

    /// <summary>Returns the brief to append to the assignment text, or null
    /// when it doesn't apply (no detected project, or a prompt that neither
    /// builds nor refers back to anything).</summary>
    public static string? Build(string workspaceRoot, string prompt)
    {
        // Trigga på byggintention, bakreferens ELLER artefaktord - den
        // kanoniska uppföljningen "gör spelet svårare" har inget byggverb
        // ("gör ett/en") och ingen bakreferens, men nämner spelet.
        if (!HostRole.IsBuildRequest(prompt) && !HostRole.RefersBack(prompt) && !HostRole.HasArtifactWord(prompt))
            return null;
        var projectRoot = ProjectRootDetector.Detect(workspaceRoot);
        if (projectRoot is null)
            return null;

        var engine = GameBuilder.DetectEngine(projectRoot);
        var kind = new ProjectVerifier().Detect(projectRoot);
        var brief = new StringBuilder();
        brief.AppendLine();
        brief.AppendLine();
        brief.AppendLine("=== BEFINTLIGT PROJEKT (uppdraget gäller detta om inget annat uttryckligen sägs) ===");
        brief.AppendLine($"Projektmapp: {projectRoot}");
        brief.AppendLine($"Typ: {kind}" + (engine != "unknown" ? $" ({engine})" : ""));

        var files = ListFiles(projectRoot);
        if (files.Count > 0)
            brief.AppendLine("Filer: " + string.Join(", ", files));

        var designPath = Path.Combine(projectRoot, "DESIGN.md");
        if (File.Exists(designPath))
        {
            try
            {
                var design = File.ReadAllText(designPath);
                if (design.Length > MaxDesignChars) design = design[..MaxDesignChars] + "…";
                brief.AppendLine();
                brief.AppendLine("--- DESIGN.md (utdrag) ---");
                brief.AppendLine(design.Trim());
            }
            catch
            {
                // Utdrag är en bekvämlighet - läsfel får inte stoppa uppdraget.
            }
        }

        brief.AppendLine();
        brief.Append("FORTSÄTT på det här projektet: läs koden först och gör riktade ändringar (edit_file). " +
            "Skapa INTE ett nytt projekt om inte användaren uttryckligen ber om ett nytt/annat.");
        return brief.ToString();
    }

    private static List<string> ListFiles(string projectRoot)
    {
        var skip = new[] { ".git", ".worktrees", "node_modules", "build", "bin", "obj", "__pycache__", "screenshots" };
        try
        {
            return Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(projectRoot, f).Replace('\\', '/'))
                .Where(rel => !rel.StartsWith('.') && !skip.Any(s => rel.Split('/').Contains(s, StringComparer.OrdinalIgnoreCase)))
                .Take(MaxFilesListed)
                .ToList();
        }
        catch
        {
            return [];
        }
    }
}
