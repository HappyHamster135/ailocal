using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>Body of POST /cluster/review-change. Old/new content travels raw
/// rather than as a precomputed diff - LineDiff lives in AiLocal.Node, so the
/// HOST builds the diff for its review prompt; the Worker's executor (Core)
/// never needs a diff dependency.</summary>
public sealed record ReviewChangeRequest(string Path, string? OldContent, string NewContent, string? Goal);

public sealed record ReviewChangeResponse(bool Approve, string? Reason);

/// <summary>
/// AI review of an agent's file write: the Host's strongest configured model
/// (ModelTiers.Complex) reads the diff and approves or rejects it BEFORE the
/// Worker writes to disk. Built for the "small local model does confident
/// nonsense" problem - the rejection reason is returned to the Worker, whose
/// executor surfaces it to the small model as a tool error, so the model gets
/// corrective feedback and can retry instead of just being blocked.
///
/// This is a quality gate, not a security boundary (the Worker already holds
/// whatever filesystem access its operator granted) - which is why every
/// failure mode of the REVIEW itself (host unreachable, provider error,
/// unparseable verdict) fails OPEN: a broken reviewer must degrade to
/// "no review", never to "no work gets done".
/// </summary>
public sealed class ChangeReviewer
{
    private const int MaxDiffCharsInPrompt = 12_000;
    private const int MaxReviewTokens = 400;

    private const string ReviewerSystem =
        "You are a strict but fair code reviewer inside an AI agent cluster. " +
        "An agent (often a small local model) wants to write a file. You see the task it was given and the diff of the change. " +
        "The project is usually an EXISTING scaffolded game/app that the task EXTENDS - adding new files, new features, " +
        "autoloads, helper scripts or modifying existing code IS the job. NEVER reject because the change 'adds something new', " +
        "'modifies existing code' or 'was not asked for' - extension is the default expectation. " +
        "Reject only real problems: the change contradicts the task, destroys existing content that clearly should be kept, is syntactically broken for its file type, or is dangerous. " +
        "Style, naming, file organization, missing error handling, or 'could be better' are NEVER reasons to reject. " +
        "If you are unsure, approve. " +
        "Reply with EXACTLY one of:\n" +
        "GODKÄNN\n" +
        "AVVISA: <one short concrete sentence: what is wrong AND what to do instead>\n" +
        "No other text.";

    public static (bool Approved, string? Reason) ParseVerdict(string? reply)
    {
        var text = (reply ?? "").Trim();
        if (text.StartsWith("GODKÄNN", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("GODKANN", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("APPROVE", StringComparison.OrdinalIgnoreCase))
            return (true, null);

        if (text.StartsWith("AVVISA", StringComparison.OrdinalIgnoreCase) ||
            text.StartsWith("REJECT", StringComparison.OrdinalIgnoreCase))
        {
            var colon = text.IndexOf(':');
            var reason = colon >= 0 ? text[(colon + 1)..].Trim() : "";

            // Motsagelseskydd: svaga granskarmodeller har setts skriva
            // "AVVISA: ... ar tekniskt korrekt, sa godkand." - ett avslag vars
            // MOTIVERING ar ett godkannande. En sjalvmotsagelse ar ett trasigt
            // granskarsvar, och trasiga granskarsvar failar OPEN per klassens
            // kontrakt - annars blockeras korrekta andringar pa nonsens.
            var lower = reason.ToLowerInvariant();
            if (lower.Contains("godkän") || lower.Contains("godkann") || lower.Contains("godkand")
                || lower.Contains("approved") || lower.Contains("looks good") || lower.Contains("is correct"))
                return (true, null);

            // Maskningsartefakt (v1.99, sett live i teamlägget): leverantörens
            // PII-filter maskar giltiga kodmönster (PackedVector2Array(),
            // particle_burst-anrop, efternamnslistor) till "[ADDRESS]" i den
            // diff GRANSKAREN ser - varpå den avvisar korrekt kod som
            // "ogiltig GDScript-syntax". Skrivvakterna garanterar redan att
            // markörer aldrig når disk, så ETT avslag som citerar en markör
            // är per definition ett kanalartefakt, inte ett kodfel -> fail
            // open. (Kostade rundor i tre spår och fick ett spår att skriva
            // en medvetet enklare version för att undvika filtret.)
            if (AiLocal.Core.Agent.AgentToolExecutor.RedactionArtifactIn(reason) is not null)
                return (true, null);

            // Nitpick-filter: granskarprompten FÖRBJUDER stil-, namn- och
            // felhanterings-avslag, men svaga granskarmodeller avvisar ändå
            // på exakt de grunderna (transkript: "lacks proper error
            // handling", "should be a constant ... readonly", "Missing null
            // check"). Promptregler räcker inte - håll dem deterministiskt:
            // ett avslag vars motivering är en nitpick failar OPEN.
            string[] nitpicks = ["error handling", "felhantering", "null check", "null-check",
                "readonly", "should be named", "borde heta", "naming", "namngivning",
                "convention", "konvention", "file organization", "consider adding",
                "consider using", "could be better"];
            if (nitpicks.Any(n => lower.Contains(n)))
                return (true, null);

            // Scope-nonsens (v1.95, sett live i team-bygget): granskaren avvisade
            // LEGITIM vidareutveckling med "the task was not to add any new
            // features", "appears to be modifying an existing..." och "no
            // indication that this change..." - att UTÖKA ett befintligt kit ÄR
            // uppdraget. Sådana motiveringar är trasiga granskarsvar -> fail open.
            string[] scopeNonsense = ["not to add any new", "was not to add",
                "modifying an existing", "modifies an existing",
                "no indication that", "not related to building", "unrelated to the task"];
            if (scopeNonsense.Any(n => lower.Contains(n)))
                return (true, null);

            return (false, reason.Length > 0 ? reason : "Ändringen avvisades av granskaren utan angiven orsak.");
        }

        // Unparseable reviewer output = the reviewer failed, not the change -
        // fail open per the class doc.
        return (true, null);
    }

    public static async Task<ReviewChangeResponse> ReviewAsync(
        FallbackChatProvider provider, NodeSettings settings, ReviewChangeRequest change, CancellationToken ct)
    {
        try
        {
            var diff = LineDiff.Compute(change.OldContent ?? "", change.NewContent);
            if (diff.Length > MaxDiffCharsInPrompt)
                diff = diff[..MaxDiffCharsInPrompt] + "\n...(diff truncated)";

            var request = new ChatRequest
            {
                System = ReviewerSystem,
                MaxTokens = MaxReviewTokens,
                // The whole point is that the HOST reviews with its strongest
                // configured model - the Worker's own (often small, local)
                // model is exactly what we're double-checking.
                ModelHint = settings.Worker.ModelTiers.Complex,
                Messages =
                {
                    new ChatMessage("user",
                        $"Task the agent was given:\n{change.Goal ?? "(unknown)"}\n\n" +
                        $"File: {change.Path}\n" +
                        $"{(change.OldContent is null ? "This is a NEW file." : "This modifies an existing file.")}\n\n" +
                        $"Diff:\n{diff}")
                }
            };

            var response = await provider.CompleteAsync(request, ct);
            if (!response.IsSuccess)
                return new ReviewChangeResponse(true, null);

            var (approved, reason) = ParseVerdict(response.Response!.Content);
            return new ReviewChangeResponse(approved, reason);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CrashLog.Write("ChangeReviewFailed", ex);
            return new ReviewChangeResponse(true, null);
        }
    }
}
