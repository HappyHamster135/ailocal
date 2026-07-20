using System.Text;
using AiLocal.Core.Agent;

namespace AiLocal.Node.Hosting;

/// <summary>What the node's own inspection of a finished assignment found.
/// HardFail flips the assignment to failed (build errors, a runtime crash in
/// playtest, or a build assignment that wrote no files at all); soft findings
/// (polish issues) are reported but don't change the outcome.</summary>
public sealed record QualityFindings(bool Clean, bool HardFail, string Report, string? ProjectRoot, string? Engine);

/// <summary>
/// The node-enforced quality gate for agent assignments. The model's own
/// "done" is never trusted: weak local models routinely declare success after
/// writing nothing, or leave a syntax error that ships as a black screen
/// (both observed verbatim in user transcripts - a 35-step assignment marked
/// "Klar" produced zero files). After the agent loop finishes, the node runs
/// verify + playtest ITSELF and feeds concrete findings back to the model as
/// a new turn, so the production bar is enforced rather than requested.
/// </summary>
public static class AssignmentQualityGate
{
    public static async Task<QualityFindings> InspectAsync(
        string workspaceRoot,
        bool buildIntent,
        DateTime runStartUtc,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        Func<string, string, CancellationToken, Task<(bool Success, string Summary, IReadOnlyList<string> Issues)>>? playtest,
        CancellationToken ct)
    {
        var projectRoot = ProjectRootDetector.Detect(workspaceRoot);
        if (projectRoot is null)
        {
            return buildIntent
                ? new(false, true,
                    "Uppdraget skulle bygga något, men arbetsytan innehåller inget igenkännbart projekt " +
                    "(ingen index.html, package.json, .csproj, requirements.txt eller liknande). " +
                    "Skapa projektet på riktigt med scaffold_game/scaffold_app eller write_file - beskriv det inte bara i text.",
                    null, null)
                : new(true, false, "Ingen projektmapp att kontrollera.", null, null);
        }

        if (buildIntent && ProjectRootDetector.NewestWriteUtc(workspaceRoot) < runStartUtc)
        {
            return new(false, true,
                $"Uppdraget skulle bygga något, men inga filer skapades eller ändrades under körningen " +
                $"(senast byggda projektet är {projectRoot}). Gör själva arbetet med write_file/edit_file - " +
                "svara inte bara med en beskrivning.",
                projectRoot, null);
        }

        var issues = new List<string>();
        var hard = false;
        var okSummary = new StringBuilder();

        var verify = await new ProjectVerifier().VerifyAsync(projectRoot, runCommand, ct);
        if (!verify.Success)
        {
            hard = true;
            issues.Add(verify.Report);
        }
        else
        {
            okSummary.AppendLine(FirstLine(verify.Report));
        }

        var engine = GameBuilder.DetectEngine(projectRoot);
        if (engine == "html5" && playtest is not null)
        {
            try
            {
                var pt = await playtest(projectRoot, engine, ct);
                if (!pt.Success)
                {
                    hard = true;
                    issues.Add("Playtest misslyckades: " + pt.Summary);
                }
                else if (pt.Issues.Count > 0)
                {
                    issues.AddRange(pt.Issues.Select(i => "Playtest: " + i));
                }
                else
                {
                    okSummary.AppendLine("Playtest: inga anmärkningar.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The gate must never crash the assignment because the tester
                // itself hiccuped - verify already ran; report and move on.
                okSummary.AppendLine($"Playtest kunde inte köras ({ex.GetType().Name}).");
            }
        }

        return issues.Count == 0
            ? new(true, false, okSummary.ToString().Trim(), projectRoot, engine)
            : new(false, hard, string.Join("\n", issues.Take(20)), projectRoot, engine);
    }

    /// <summary>The corrective turn sent back into the agent loop when the
    /// gate found problems - concrete, in Swedish (assignments run in the
    /// user's language), and explicit that a NEW project is not the fix.</summary>
    public static string FixPrompt(QualityFindings findings) =>
        "Nodens kvalitetskontroll körde verify + playtest på projektet" +
        (findings.ProjectRoot is null ? "" : $" i {findings.ProjectRoot}") +
        " och hittade följande problem:\n\n" + findings.Report +
        "\n\nÅtgärda problemen i de BEFINTLIGA filerna (edit_file/write_file), kör verify igen, " +
        "och avsluta först när allt är grönt. Skapa inte ett nytt projekt. " +
        "Saknas ett verktyg (python, node, godot, ...) - installera det med provision och försök igen. " +
        "VIKTIGT: anropa verktygen på riktigt via tool-anrop - JSON eller kommandon skrivna som TEXT i svaret kör ingenting.";

    private static string FirstLine(string text)
    {
        var i = text.IndexOf('\n');
        return i < 0 ? text : text[..i];
    }
}
