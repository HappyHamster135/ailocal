using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;

namespace AiLocal.Node.Hosting;

/// <summary>
/// C4: den "riktiga studion". Där team-läget delar upp arbetet i PARALLELLA
/// oberoende spår, kör producent-läget en SEKVENTIELL rollpipeline på SAMMA
/// arbetsyta - en producent koordinerar överlämningar: programmeraren bygger
/// kärnspelet, konstnären förädlar ALL grafik till en sammanhållen art-bibel,
/// ljuddesignern lägger på ljudbilden. Varje roll är en egen agenttur som LÄSER
/// och bygger vidare på förra rollens arbete (filerna på disken ÄR överlämningen,
/// plus en kort sammanfattning). Kvalitetsgrinden (QA) kör efteråt som förr.
///
/// Rollerna kan köra på OLIKA modeller (v1.71: konstnären får den starka tiern)
/// - olika modeller mot samma mål på en maskin.
///
/// Pipelinen KÖR inte agenten själv - anroparen skickar in en <c>runRole</c>-
/// delegat som gör en full agenttur (WorkerRole ger den samma kostnadsbokförda,
/// kostnadstakade loop OCH samma iterationstak-/plan-vakts-continuations som en
/// ensam agent får). Så producent-läget ärver kostnadsredovisning, kostnadstak
/// och continuation-skydd gratis i stället för att kringgå dem (granskning
/// v1.83). Pipelinen äger BARA rollordningen, modellvalet per roll,
/// överlämningen och sammanvägningen av resultatet.
/// </summary>
public static class ProducerPipeline
{
    public sealed record Role(string Title, Func<string, string?, string> Prompt, bool UseStrongModel);

    /// <summary>The role handoff order (testbar). Programmerare -> Konstnär ->
    /// Ljuddesigner. QA/grinden är ett separat steg som anroparen kör efteråt.</summary>
    public static readonly IReadOnlyList<Role> Roles =
    [
        new("Programmerare", CodePrompt, false),
        new("Konstnär", ArtPrompt, true),
        new("Ljuddesigner", AudioPrompt, false),
    ];

    /// <param name="runRole">Kör EN full agenttur för en roll: (prompt, modell) ->
    /// resultat. WorkerRole låter denna gå genom den kostnadsbokförda loopen med
    /// iterationstak-/plan-vakts-continuations, precis som en ensam agent.</param>
    public static async Task<AgentRunResult> RunAsync(
        string assignment,
        string? modelHint,
        string? strongModelHint,
        Func<string, string?, Task<AgentRunResult>> runRole,
        Func<AgentStep, Task> emit,
        CancellationToken ct)
    {
        AgentRunResult? codeResult = null;   // steg 1 = kärnbygget; dess utfall styr leverans
        AgentRunResult? last = null;
        string? prevSummary = null;
        var iterations = 0;
        var inTok = 0;
        var outTok = 0;
        var hitIterationCap = false;
        var hitCostCap = false;

        for (var i = 0; i < Roles.Count; i++)
        {
            if (ct.IsCancellationRequested) break;
            var role = Roles[i];
            await emit(new AgentStep("tool_call", $"producent → lämnar över till {role.Title}"));
            var model = role.UseStrongModel && !string.IsNullOrWhiteSpace(strongModelHint) ? strongModelHint : modelHint;
            var r = await runRole(role.Prompt(assignment, prevSummary), model);
            iterations += r.Iterations;
            inTok += r.TotalUsage.InputTokens;
            outTok += r.TotalUsage.OutputTokens;
            hitIterationCap |= r.HitIterationCap;   // bevara kärnbyggets kapsignal även om en senare roll slutar rent
            hitCostCap |= r.HitCostCap;
            if (i == 0) codeResult = r;
            last = r;
            prevSummary = Trunc(r.FinalAnswer, 400);
            await emit(new AgentStep(r.Success ? "tool_result" : "tool_error",
                $"{role.Title} klar" + (r.Success ? "." : " (fortsätter till nästa roll ändå).")));
        }

        var final = last ?? new AgentRunResult(false, "Producent-pipelinen kunde inte köras.", [], 0, [], TokenUsage.Zero);
        // Leveransen (och därmed QA-grinden, exporten, snapshoten) styrs av
        // KÄRNBYGGET (programmeraren), INTE den sista rollen (ljud). Annars kunde
        // ett trasigt/avbrutet ljudsteg dölja ett fullt spelbart spel på disken -
        // och ett trasigt kärnbygge maskeras av att ljudsteget råkade lyckas
        // (granskning v1.83: sista-rollens Success styrde hela leveransen).
        // Messages = kärnbyggets konversation, så grindens fixrundor får
        // gameplay-kontexten och inte bara ljudturens.
        return final with
        {
            Success = codeResult?.Success ?? final.Success,
            HitIterationCap = hitIterationCap,
            HitCostCap = hitCostCap,
            Messages = codeResult?.Messages ?? final.Messages,
            FinalAnswer = "Producent-pipeline (programmerare → konstnär → ljuddesigner):\n" + final.FinalAnswer,
            TotalUsage = new TokenUsage(inTok, outTok),
            Iterations = iterations,
        };
    }

    // ---- Roll-prompter --------------------------------------------------

    internal static string CodePrompt(string assignment, string? _) =>
        $"{assignment}\n\n=== DIN ROLL: PROGRAMMERARE (steg 1 av 3 i studions pipeline) ===\n" +
        "Bygg KÄRNSPELET så det fungerar och är spelbart enligt DESIGN.md/leveranskontraktet: " +
        "riktig gameplay, alla skärmar (titel/spel/paus/vinst/förlust), progression och robusthet. " +
        "Konstnären och ljuddesignern förädlar grafik och ljud EFTER dig - fokusera på att mekaniken " +
        "sitter och att verify passerar. Arbeta genom verktygen (scaffold/write_file/edit_file/verify).";

    internal static string ArtPrompt(string assignment, string? prevSummary) =>
        $"Uppdrag: {assignment}\n\n=== DIN ROLL: KONSTNÄR (steg 2 av 3) ===\n" +
        (prevSummary is null ? "" : $"Programmeraren är klar: {prevSummary}\n\n") +
        "Spelet finns redan i arbetsmappen. LÄS koden och DESIGN.md:s art direction, sedan förädla ALL " +
        "grafik till en SAMMANHÅLLEN art-bibel (samma palett, stil och stämning): sprites, tilesets, " +
        "bakgrunder, UI och animationer - inte bara karaktärer. Använd generate_asset där det finns, annars " +
        "förbättra formerna/färgerna i koden. Bryt ALDRIG gameplayen - kör verify när du är klar. " +
        "Arbeta genom verktygen; text/planer utan verktygsanrop kastas bort.";

    internal static string AudioPrompt(string assignment, string? prevSummary) =>
        $"Uppdrag: {assignment}\n\n=== DIN ROLL: LJUDDESIGNER (steg 3 av 3) ===\n" +
        (prevSummary is null ? "" : $"Konstnären är klar: {prevSummary}\n\n") +
        "Spelet och grafiken finns i arbetsmappen. LÄS koden och säkerställ en komplett ljudbild: " +
        "ljudeffekter för VARJE viktig händelse (hopp/träff/plock/skjut/vinst/förlust) och en kort " +
        "bakgrundsslinga där det passar. Använd generate_asset (sfx/music) eller game_module för ljud. " +
        "Skydda ljuduppspelningen så en blockerad ljudkontext aldrig kraschar spel-loopen. Bryt aldrig " +
        "gameplayen - kör verify när du är klar. Arbeta genom verktygen.";

    private static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
