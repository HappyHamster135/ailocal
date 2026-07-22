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
        CancellationToken ct,
        bool gameExpected = false,
        string? genre = null,
        string? assignment = null)
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

        // v2.2.0: Structured genre contract verification - grep-verifiable
        // constraints that catch structural misses (missing gravity, no dice
        // roll, single enemy type) BEFORE the LLM playtest review.
        if (gameExpected && genre is not null)
        {
            var (met, total, contractFindings) = GenreContracts.Verify(projectRoot, genre, assignment);
            if (contractFindings.Count > 0)
            {
                var mustFindings = contractFindings.Where(f => f.StartsWith("SAKNAS")).ToList();
                var shouldFindings = contractFindings.Where(f => f.StartsWith("REKOMMENDATION")).ToList();
                // Hard fail only if > half of MUST constraints are unmet
                if (mustFindings.Count > total / 2)
                {
                    hard = true;
                    issues.Add($"Genrekontrakt ({genre}): {total - mustFindings.Count}/{total} krav uppfyllda.\n"
                        + string.Join("\n", mustFindings.Take(5)));
                }
                else if (mustFindings.Count > 0)
                {
                    issues.Add($"Genrekontrakt ({genre}): {total - mustFindings.Count}/{total} krav uppfyllda.\n"
                        + string.Join("\n", mustFindings.Take(3)));
                }
                // Should-have findings are always advisory
                foreach (var f in shouldFindings.Take(3))
                    okSummary.AppendLine(f);
            }
            else if (total > 0)
            {
                okSummary.AppendLine($"Genrekontrakt ({genre}): alla {total} krav uppfyllda.");
            }
        }

        // v2.2.0: Anti-pattern scan - design-level issues (repetitive levels,
        // missing screens, format strings) detected by regex, not LLM.
        // (projectRoot är garanterat non-null efter early-return ovan - den
        // ärvda is-not-null-testen fick flödesanalysen att tappa det och gav
        // CS8604 längre ner.)
        if (gameExpected)
        {
            try
            {
                var allSource = "";
                foreach (var srcFile in Directory.EnumerateFiles(projectRoot, "*",
                    SearchOption.AllDirectories)
                    .Where(f => f.EndsWith(".gd") || f.EndsWith(".cs") || f.EndsWith(".js"))
                    .Take(30))
                {
                    try { allSource += File.ReadAllText(srcFile) + "\n"; } catch { }
                }
                if (allSource.Length > 0)
                {
                    var apFindings = AntiPatternDb.Scan(allSource, engine ?? "unknown");
                    var formatted = AntiPatternDb.FormatFindings(apFindings);
                    foreach (var f in formatted.Take(5))
                        okSummary.AppendLine(f);
                }
            }
            catch { /* best-effort */ }
        }


        // A game was requested but the deliverable is not a game-engine project.
        // DetectEngine returns "unknown" for a plain C#/CLI app (no project.godot,
        // no Assets/ProjectSettings, no index.html) - exactly what the agent
        // freelanced for "Football Manager" instead of using the Godot kit. A
        // console/CLI app is not a playable game, so fail the gate and steer the
        // fix-round back to a real engine rather than shipping it as done.
        if (gameExpected && engine is not ("godot" or "unity" or "html5"))
        {
            hard = true;
            issues.Add(
                "Uppdraget var ett SPEL men det byggda projektet är ingen spelmotor-titel (motor: \"" +
                engine + "\"). En textbaserad konsol-/CLI-app räknas inte - användaren ska få en spelbar " +
                "exe eller ett spel som öppnas i Godot/Unity/HTML. Bygg om det som ett Godot-spel " +
                "(scaffold_game med engine=godot) eller ett HTML5-spel i den BEFINTLIGA arbetsmappen, " +
                "inte ett nytt projekt.");
        }

        // Playtesta BÅDE html5 och godot: godot är husmotorn och ska inte bara
        // headless-importeras utan ocksa spelas (fönstersond + vision + design-
        // bedömning). FullTestAsync degraderar snällt utan godot/skärm (try/catch
        // nedan), så en headless ser-nod utan skärm failar aldrig av detta.
        if ((engine is "html5" or "godot") && playtest is not null)
        {
            try
            {
                var pt = await playtest(projectRoot, engine, ct);
                if (!pt.Success)
                {
                    if (engine == "html5")
                    {
                        // HTML5 kör alltid (Jint-smoke) - ett misslyckande är hårt.
                        hard = true;
                        issues.Add("Playtest misslyckades: " + pt.Summary);
                    }
                    else
                    {
                        // Godot-speltest är BEST-EFFORT: utan godot-verktyg/skärm
                        // (pre-export, headless ser-nod) gick spelet inte att köra -
                        // degradera i stället för att falskt underkänna. Verify +
                        // headless-import star for korrektheten; sonden är ett plus.
                        okSummary.AppendLine("Godot-speltest kunde inte köras (godot/skärm saknas) - hoppade över.");
                    }
                }
                else if (pt.Issues.Count > 0)
                {
                    issues.AddRange(pt.Issues.Select(i => "Playtest: " + i));
                    // Ett spel som speldesign-passet bedömer som OSPELBART är ett
                    // HÅRT fynd - det får en designfixrunda, inte bara en rapportrad.
                    if (pt.Issues.Any(i => i.Contains("INTE spelbart")))
                        hard = true;
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

        // Ljuddesigner-rollen: godot/unity-spel anvander .wav-tillgangar. Saknad
        // musik/sfx rapporteras som en ADVISORY (inte ett hart fynd - ett tekniskt
        // gront spel forblir gront) sa agaren/agenten ser ljudavdelningens omdome
        // och kan lyfta ljudet i en uppfoljning. Html5 anvander WebAudio (JS), inte
        // .wav-filer, sa dess ljud gar inte att bedoma pa filer - hoppas over.
        if (engine is "godot" or "unity")
            foreach (var note in StudioAudioReview.Review(projectRoot))
                okSummary.AppendLine(note);

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
        "VIKTIGT: anropa verktygen på riktigt via tool-anrop - JSON eller kommandon skrivna som TEXT i svaret kör ingenting." +
        (findings.Report.Contains("SPELDESIGN")
            ? "\n\nNÅGRA fynd gäller SPELDESIGN, inte buggar: justera balans/svårighet/spelbarhet (fiendehastighet, " +
              "HP, spawn-antal, tidsgränser, målvillkor) så spelet GÅR att spela och känns rimligt svårt - inte bara " +
              "att det kompilerar. Ändra VÄRDEN i den befintliga koden och speltesta igen."
            : "");

    /// <summary>C5 (milstolpe-drivet bygge): ska grinden köra EN fixrunda till?
    /// En ren TEKNISK miss (contractUnmet &lt; 0) har det snäva taket
    /// (maxFixRounds). En KONTRAKTS-/milstolpe-miss (contractUnmet &gt; 0) får
    /// fortsätta upp till maxMilestoneRounds SÅ LÄNGE antalet ouppfyllda punkter
    /// MINSKAR (framsteg mot milstolpen) - stannar av när det står stilla, så
    /// bygget alltid konvergerar och aldrig blir en runaway.</summary>
    public static bool ShouldContinueFixing(int round, int contractUnmet, int prevUnmet, int maxFixRounds, int maxMilestoneRounds)
    {
        if (contractUnmet > 0)
            return round < maxMilestoneRounds && contractUnmet < prevUnmet;
        return round < maxFixRounds;
    }

    private static string FirstLine(string text)
    {
        var i = text.IndexOf('\n');
        return i < 0 ? text : text[..i];
    }
}
