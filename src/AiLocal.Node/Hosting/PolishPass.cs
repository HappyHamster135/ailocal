using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.0.0: utvecklingsrundorna - ägarens fundamentala krav: "det som var
/// finalprodukten ska vara PROTOTYPEN; sedan ska studion titta på den och
/// utveckla den till ett riktigt spel - större, snyggare, bättre ljud,
/// mindre buggigt - och felsöka vad den hade kunnat göra bättre."
///
/// Grinden garanterar FUNGERANDE och kontraktet INTRESSANT - det här passet
/// driver BÄTTRE: efter godkänd prototyp granskar en kritikermodell spelet
/// (kod + grindens speltestbevis) över fyra axlar och producerar en konkret
/// byggbar förbättringslista, som körs som en ny utvecklingsrunda ovanpå
/// historiken. Snapshot före varje runda = en försämrande runda återställs.
///
/// Kostnadsdisciplin (stående regel): kritiken kör på MEDIUM-tiern (billig),
/// byggrundorna på samma modell som huvudbygget, och hela loopen respekterar
/// Max$-taket. Fail-open: en kraschad kritik ger tom lista - prototypen
/// levereras hellre som den är än att rundan gissar.
/// </summary>
public static class PolishPass
{
    /// <summary>Max antal förbättringspunkter per runda - fler blir en
    /// önskelista ingen runda hinner bygga klart.</summary>
    public const int MaxImprovements = 6;

    internal static string CritiquePrompt(string assignment, string gateReport, string codeSample) =>
        "Original assignment: " + Trunc(assignment, 400) +
        "\n\nQuality-gate evidence (the node's own verify + playtest + vision review of the running game):\n" +
        Trunc(gateReport, 3000) +
        "\n\nMain source files:\n" + codeSample +
        "\n\nYou are the CREATIVE DIRECTOR of a game studio doing a post-prototype review. " +
        "The prototype above WORKS and passed the quality gate - treat it as a working prototype, NOT a finished game. " +
        "Your job: find what would take it to a REAL, sellable game. Think across exactly these four axes:\n" +
        "1. BIGGER - more content and depth: more levels/systems/enemies/choices, a longer arc, more to discover.\n" +
        "2. BETTER LOOKING - visual identity: color palette, readable layout that fills the screen, animation, backgrounds, UI polish.\n" +
        "3. BETTER SOUND - more audio events, variation, a background loop, audio feedback on every meaningful action.\n" +
        "4. LESS BUGGY - anything in the evidence or code that suggests rough edges, dead ends, unclear states or missing feedback.\n" +
        "Pick the " + MaxImprovements + " HIGHEST-IMPACT concrete improvements a developer can build in one session. " +
        "Each item must be specific and buildable (say WHAT to add/change and WHERE), never vague advice like 'improve graphics'. " +
        "Respond ONLY with JSON: {\"done\": false, \"improvements\": [\"...\"]}. " +
        "Use {\"done\": true, \"improvements\": []} ONLY if the game already looks and plays like a finished product.";

    public static async Task<IReadOnlyList<string>> CritiqueAsync(
        string projectRoot,
        string assignment,
        string gateReport,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        CancellationToken ct,
        string? modelHint = null,
        Func<string, string, CancellationToken, Task<(bool Ok, string Text)>>? visionReview = null,
        IReadOnlyList<string>? screenshots = null)
    {
        try
        {
            var code = CodeReviewPass.BuildCodeSample(projectRoot);
            if (string.IsNullOrWhiteSpace(code)) return [];

            // v2.4: BILDBEVIS i kritiken - kritikern LÄSER sondens skärmdumpar
            // via visionsmodellen i stället för att gissa utseendet ur koden
            // (ägarens skärmdumpar visade exakt det text-kritiken missar:
            // halvtomma layouter, oläslig kontrast). Fail-open per dump.
            var evidence = gateReport;
            if (visionReview is not null && screenshots is { Count: > 0 })
            {
                foreach (var shot in screenshots.Take(2))
                {
                    try
                    {
                        if (!File.Exists(shot)) continue;
                        var (ok, text) = await visionReview(shot,
                            "You are a game studio's ART DIRECTOR reviewing a real screenshot of the game. " +
                            "In 3-5 short bullet points, name concretely what looks empty, unbalanced, unreadable " +
                            "or unpolished (layout, palette, contrast, empty screen areas, missing visual identity) " +
                            "and what to add/change. Be specific and buildable - never 'improve the graphics'.", ct);
                        if (ok && !string.IsNullOrWhiteSpace(text))
                            evidence += "\n\nART DIRECTOR review of screenshot (" + Path.GetFileName(shot) + "):\n" + text.Trim();
                    }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch { /* en trasig dump stoppar aldrig kritiken */ }
                }
            }
            gateReport = evidence;
            var response = await complete(new ChatRequest
            {
                System = "You are a demanding but constructive creative director reviewing a game prototype. " +
                         "You always ground feedback in what you actually see in the code and evidence, and you only propose buildable work.",
                Messages = [new ChatMessage("user", CritiquePrompt(assignment, gateReport, code))],
                ModelHint = modelHint,
                MaxTokens = 700
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content)) return [];
            return ParseImprovements(response.Response.Content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    /// <summary>JSON-tolkning med radlista som fallback (svaga modeller
    /// svarar ibland med punktlista trots JSON-instruktionen). Public for test.</summary>
    public static IReadOnlyList<string> ParseImprovements(string reply)
    {
        try
        {
            var start = reply.IndexOf('{');
            var end = reply.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
                if (doc.RootElement.TryGetProperty("done", out var done) && done.ValueKind == JsonValueKind.True)
                    return [];
                if (doc.RootElement.TryGetProperty("improvements", out var arr) && arr.ValueKind == JsonValueKind.Array)
                    return arr.EnumerateArray()
                        .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                        .Where(s => !string.IsNullOrWhiteSpace(s))
                        .Select(s => s!.Trim())
                        .Take(MaxImprovements)
                        .ToList();
            }
        }
        catch { /* fall igenom till radlistan */ }

        // Fallback: rader som ser ut som punkter ("- x", "1. x", "* x").
        var lines = reply.Split('\n')
            .Select(l => l.Trim().TrimStart('-', '*', '•', ' '))
            .Select(l => System.Text.RegularExpressions.Regex.Replace(l, @"^\d+[.)]\s*", ""))
            .Where(l => l.Length > 15 && !l.StartsWith("{") && !l.StartsWith("\""))
            .Take(MaxImprovements)
            .ToList();
        return lines;
    }

    /// <summary>Byggturens uppgiftstext: prototypen är godkänd - utveckla den
    /// OVANPÅ det som finns, riv aldrig fungerande delar.</summary>
    public static string BuildPrompt(int round, int total, IReadOnlyList<string> improvements) =>
        $"UTVECKLINGSRUNDA {round}/{total}: Prototypen är GODKÄND och fungerar - nu ska den utvecklas mot ett riktigt, säljbart spel.\n\n" +
        "Studiokritikens viktigaste förbättringar (bygg ALLA du hinner, i denna ordning):\n" +
        string.Join("\n", improvements.Select((s, i) => $"{i + 1}. {s}")) +
        "\n\nRegler för rundan:\n" +
        "- Bygg OVANPÅ det som finns: utöka och förbättra, riv aldrig fungerande system.\n" +
        "- All spelartext på engelska; inga råa formatsträngar/BBCode/datadumpar i UI.\n" +
        "- Nya banor/system får aldrig göra befintliga oåtkomliga - spelet ska förbli klarbart från start till slut.\n" +
        "- Kör verify när du är klar och åtgärda det den rapporterar.";

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
