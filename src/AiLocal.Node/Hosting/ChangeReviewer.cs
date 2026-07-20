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
