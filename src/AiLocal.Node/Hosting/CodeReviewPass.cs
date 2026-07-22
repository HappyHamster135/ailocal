using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v1.88: cross-modell KODgranskare i kvalitetsgrinden. Regissörens kontrakts-
/// granskning läser en EVIDENSSAMMANFATTNING (max 4 filer × 3000 tecken) - den
/// ser aldrig merparten av spellogiken. Det här passet läser HELA huvudkod-
/// filerna (upp till ~24 kB, störst först) med ETT uppdrag: hitta RIKTIGA
/// buggar (krascher, döda knappar, trasiga loopar/tillståndsmaskiner,
/// logikfel), inte stil. Körs EN gång (runda 0) på den starka tiern = en
/// ANNAN modell än byggaren i normalfallet - två olika modeller fångar fler
/// felmoder än en modell som granskar sitt eget arbete (samma princip som
/// hittade riktiga buggar i v1.73/v1.74/v1.83-granskningarna).
/// Fail-open: varje fel/parse-miss ger tom lista - granskaren får aldrig
/// sänka ett bygge på sin egen krasch.
/// </summary>
public static class CodeReviewPass
{
    private const int MaxTotalChars = 24_000;
    private const int MaxFiles = 6;

    /// <summary>Huvudkodfilerna, störst först (spellogiken bor där), cap:ad
    /// till en total budget så prompten aldrig sväller okontrollerat.</summary>
    internal static string BuildCodeSample(string projectRoot)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var skip = new[] { ".git", ".worktrees", "node_modules", "build", "dist", "screenshots", "__pycache__" };
            var files = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".gd" or ".js" or ".html" or ".cs" or ".py")
                .Where(f => !Path.GetRelativePath(projectRoot, f)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(s => skip.Contains(s, StringComparer.OrdinalIgnoreCase)))
                .Select(f => new FileInfo(f))
                .OrderByDescending(f => f.Length)
                .Take(MaxFiles)
                .ToList();
            var budget = MaxTotalChars;
            foreach (var file in files)
            {
                if (budget <= 500) break;
                var content = File.ReadAllText(file.FullName);
                if (content.Length > budget) content = content[..budget] + "…";
                budget -= content.Length;
                sb.AppendLine($"--- {Path.GetRelativePath(projectRoot, file.FullName)} ---");
                sb.AppendLine(content);
            }
        }
        catch { /* delbevis räcker - granskaren jobbar med det som gick att läsa */ }
        return sb.ToString();
    }

    public static async Task<IReadOnlyList<string>> ReviewAsync(
        string projectRoot,
        string assignment,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        CancellationToken ct,
        string? reviewModelHint = null)
    {
        try
        {
            var code = BuildCodeSample(projectRoot);
            if (string.IsNullOrWhiteSpace(code)) return [];
            var prompt = "Assignment: " + Trunc(assignment, 400) +
                "\n\nFULL SOURCE of the main code files:\n" + code +
                "\n\nYou are an INDEPENDENT code reviewer - a DIFFERENT model than the one that wrote this. " +
                "Find REAL bugs only: crashes/exceptions on reachable paths, dead or unwired buttons/inputs, " +
                "broken loops or state machines (states you can never leave or reach), logic errors that break " +
                "gameplay, and resources that are used but never created. Do NOT report style, naming, " +
                "performance guesses or hypotheticals - every finding must cite the concrete place and why it " +
                "breaks. Respond ONLY with JSON: {\"bugs\":[\"file/function: concrete bug and why\"]} " +
                "(max 4 items; {\"bugs\":[]} when the code is sound).";
            var response = await complete(new ChatRequest
            {
                System = "You are a senior code reviewer hunting for real, demonstrable bugs in a game codebase. You are strict about evidence and never invent nitpicks.",
                Messages = [new ChatMessage("user", prompt)],
                ModelHint = reviewModelHint,
                MaxTokens = 500
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content)) return [];

            var text = response.Response.Content;
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return [];
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("bugs", out var bugs) || bugs.ValueKind != JsonValueKind.Array)
                return [];
            return bugs.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Take(4)
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
