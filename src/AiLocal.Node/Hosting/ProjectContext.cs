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

    /// <summary>True when the prompt clearly names a THEME that the existing
    /// project shows no trace of - the signal to start a NEW project instead
    /// of continuing. Root cause of the "växtspel i stället för fotboll"-
    /// report: an old farm project in the workspace + a football prompt, and
    /// the continuity brief said CONTINUE. Distinctive prompt nouns (fotboll,
    /// hockey, rymd...) are matched against DESIGN.md/README/key sources; a
    /// prompt with no distinctive nouns ("bygg ett plattformsspel") never
    /// counts as unrelated - benefit of the doubt goes to continuing.</summary>
    public static bool SeemsUnrelated(string projectRoot, string prompt)
    {
        // Bara prompts som beskriver ETT HELT projekt ("bygg ett fotbolls-
        // spel") kan vara orelaterade - uppföljningar ("gör spelet svårare",
        // "lägg till powerups") beskriver aldrig ett nytt projekt och ska
        // ALLTID fortsätta på det befintliga.
        if (!NamesAWholeProject(prompt)) return false;
        var topics = TopicTokens(prompt);
        if (topics.Count == 0) return false;
        var haystack = Fold(ProjectText(projectRoot).ToLowerInvariant());
        if (haystack.Length == 0) return false;
        return !topics.Any(t => haystack.Contains(Fold(t), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Diakritikvikning: kit-dokumenten skriver ASCII-svenska
    /// ("Bondgard", "grodor"), så "bondgård" i prompten måste matcha ändå.</summary>
    private static string Fold(string text) => text
        .Replace('å', 'a').Replace('ä', 'a').Replace('ö', 'o')
        .Replace('é', 'e').Replace('Å', 'A').Replace('Ä', 'A').Replace('Ö', 'O');

    /// <summary>True när prompten beskriver ett helt projekt: obestämd artikel
    /// följt (inom samma andetag) av ett spel/app-ord - "bygg ETT ... SPEL".
    /// "spelet"/"appen" (bestämd form = bakreferens) matchar medvetet inte.</summary>
    internal static bool NamesAWholeProject(string prompt) =>
        System.Text.RegularExpressions.Regex.IsMatch(
            prompt ?? "",
            @"\b(ett|en|a|an)\b[^.!?\n]{0,60}?\w*(spel|game|app)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>Distinctive theme nouns from the prompt: ≥5 chars, not verbs/
    /// genre mechanics/filler. "fotbollsspel" contributes "fotboll".</summary>
    internal static List<string> TopicTokens(string prompt)
    {
        var stop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bygga", "bygger", "skapa", "skapar", "spela", "spelet", "spelare",
            "enkel", "enkelt", "avancerad", "avancerat", "liten", "litet", "riktigt",
            "manager", "management", "managementspel", "simulator", "simulatorspel",
            "tycoon", "roguelike", "plattform", "plattformsspel", "plattformare",
            "pussel", "pusselspel", "arkad", "arkadspel", "webbspel", "browser",
            "godot", "unity", "html5", "svårighetsgrader", "svårighetsgrad",
            "nivåer", "banor", "poäng", "highscore", "väljer", "vill", "finns",
            "sedan", "också", "gärna", "kunna", "skall", "skulle", "mellan",
            "game", "games", "build", "create", "simple", "advanced", "levels",
            "difficulty", "would", "should", "where", "which", "there",
            // Uppföljningsvokabulär - komparativ, polishord och byggverb får
            // aldrig räknas som TEMA ("gör spelet svårare/snyggare").
            "svårare", "svarare", "lättare", "lattare", "enklare", "snabbare",
            "bättre", "battre", "sämre", "samre", "roligare", "snyggare",
            "större", "storre", "mindre", "längre", "langre", "kortare",
            "högre", "hogre", "starkare", "flera", "fortsätt", "fortsatt",
            "fortsätta", "fortsatta", "uppdatera", "förbättra", "forbattra",
            "utöka", "utoka", "ändra", "andra", "justera", "polera", "fixa",
            "menyer", "grafik", "ljudeffekter", "musik", "sprites", "fiender",
            "fiendetyper", "spelbar", "spelbart", "harder", "easier", "faster",
            "better", "improve", "update", "continue", "extend", "polish",
        };
        var tokens = new List<string>();
        foreach (var raw in (prompt ?? "").ToLowerInvariant()
                     .Split(' ', ',', '.', '!', '?', ':', ';', '(', ')', '\n', '\r', '\t'))
        {
            var word = raw.Trim('-', '"', '\'');
            // Svenska sammansättningar: "fotbollsspel" -> "fotbolls" -> "fotboll".
            if (word.EndsWith("spel", StringComparison.Ordinal) && word.Length > 6)
                word = word[..^4].TrimEnd('s');
            if (word.Length < 5 || stop.Contains(word)) continue;
            if (!tokens.Contains(word)) tokens.Add(word);
        }
        return tokens;
    }

    private static string ProjectText(string projectRoot)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            // Mappnamnet räknas: "fotboll-management-simulator" ska matcha en
            // fotbollsprompt även om dokumenten skriver "football".
            sb.AppendLine(Path.GetFileName(projectRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
            foreach (var name in new[] { "DESIGN.md", "README.md", "index.html", "project.godot" })
            {
                var path = Path.Combine(projectRoot, name);
                if (File.Exists(path))
                    sb.AppendLine(File.ReadAllText(path));
            }
            foreach (var gd in Directory.EnumerateFiles(projectRoot, "*.gd", SearchOption.TopDirectoryOnly).Take(5))
                sb.AppendLine(File.ReadAllText(gd));
        }
        catch { /* delunderlag räcker */ }
        return sb.ToString();
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
