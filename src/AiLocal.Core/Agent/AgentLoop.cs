using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Core.Agent;

/// <summary>One observable step of an agent run, for progress reporting -
/// "thinking" is any text the model produced alongside or instead of a tool
/// call, "tool_call"/"tool_result"/"tool_error" bracket one tool execution,
/// "done" is the final answer, "error"/"cancelled" are terminal failures.</summary>
public sealed record AgentStep(string Kind, string Detail);

/// <summary>Messages carries the full updated conversation (including every
/// tool call/result turn) so a caller can persist it and pass it back in as
/// the next call's `history` to resume - Steps is a lossy, display-only
/// projection of the same run and is not sufficient for that by itself.
/// TotalUsage sums every iteration's TokenUsage (each provider response
/// reports its own turn's usage only).</summary>
public sealed record AgentRunResult(
    bool Success,
    string FinalAnswer,
    IReadOnlyList<AgentStep> Steps,
    int Iterations,
    IReadOnlyList<ChatMessage> Messages,
    TokenUsage TotalUsage,
    bool HitIterationCap = false);

/// <summary>
/// Runs an assignment to completion: send the conversation (with tools) to
/// the provider, and either it answers directly (done) or it calls one or
/// more tools, whose results get appended as the next turn before asking
/// again - repeating until the model stops calling tools, something fails,
/// the caller cancels, or a safety iteration cap is hit (a runaway agent
/// should not be able to loop, and spend tokens, forever).
/// </summary>
public sealed class AgentLoop
{
    // 25 räckte inte för stora byggen: ett riktigt spel med flera filer och
    // fix-rundor dog på taket efter 18 minuter (observerat). 50 ger utrymme
    // utan att släppa en äkta runaway fri.
    private const int MaxIterations = 50;

    // Utan explicit tak föll providers tillbaka på snåla defaults (~4k
    // tokens) som kapade stora write_file mitt i filen - den enskilt största
    // orsaken till misslyckade byggen. Providers klampar själva neråt om
    // deras modell har lägre tak.
    private const int AgentMaxTokens = 8192;

    /// <summary>Just "complete this chat request" - takes a plain delegate
    /// rather than IChatProvider so callers can pass either a single
    /// provider's CompleteAsync directly, or FallbackChatProvider's (which
    /// has the identical signature but doesn't formally implement
    /// IChatProvider, since chaining providers doesn't have a single
    /// Name/IsLocal of its own) to get automatic fallback across every
    /// configured provider for free, with zero special-casing here.</summary>
    private readonly Func<ChatRequest, CancellationToken, Task<ProviderResponse>> _complete;
    private readonly AgentToolExecutor _tools;

    public AgentLoop(Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete, AgentToolExecutor tools)
    {
        _complete = complete;
        _tools = tools;
    }

    public async Task<AgentRunResult> RunAsync(
        string assignment,
        AgentAccessLevel accessLevel,
        string? modelHint = null,
        Func<AgentStep, Task>? onStep = null,
        CancellationToken ct = default,
        IReadOnlyList<ChatMessage>? history = null,
        string? system = null)
    {
        var steps = new List<AgentStep>();
        async Task Emit(AgentStep step)
        {
            steps.Add(step);
            if (onStep is not null) await onStep(step);
        }

        // Every provider response reports only that turn's own usage - sum
        // as we go so a caller (SessionStore) can track a session's running
        // total across many separate RunAsync calls, not just one.
        var inputTokens = 0;
        var outputTokens = 0;
        void AddUsage(TokenUsage usage)
        {
            inputTokens += usage.InputTokens;
            outputTokens += usage.OutputTokens;
        }
        TokenUsage Usage() => new(inputTokens, outputTokens);

        // Ask the executor for ITS tool list rather than recomputing it from
        // the access level here - the executor knows about flags this loop
        // doesn't (internet access), and two call sites deriving the list
        // independently is exactly how they'd drift apart.
        var toolDefs = _tools.Tools;
        // history is seeded from a prior run's own returned Messages (see
        // AgentRunResult) - clone it so repeated resumes never share/mutate
        // a list some earlier caller still holds a reference to.
        var messages = history is { Count: > 0 } ? new List<ChatMessage>(history) : new List<ChatMessage>();
        messages.Add(new ChatMessage("user", assignment));

        if (toolDefs.Count == 0)
        {
            const string message = "Agent mode is not enabled on this Worker (access level is Off) - nothing to run this assignment with.";
            await Emit(new AgentStep("error", message));
            return new AgentRunResult(false, message, steps, 0, messages, Usage());
        }

        for (var iteration = 1; iteration <= MaxIterations; iteration++)
        {
            if (ct.IsCancellationRequested)
            {
                await Emit(new AgentStep("cancelled", "Cancelled by operator."));
                return new AgentRunResult(false, "Cancelled by operator.", steps, iteration, messages, Usage());
            }

            var response = await _complete(new ChatRequest
            {
                System = BuildSystemPrompt(system, toolDefs),
                Messages = messages,
                ModelHint = modelHint,
                Tools = toolDefs.ToList(),
                MaxTokens = AgentMaxTokens
            }, ct);

            if (!response.IsSuccess)
            {
                var error = response.Error ?? "unknown provider error";
                await Emit(new AgentStep("error", error));
                return new AgentRunResult(false, error, steps, iteration, messages, Usage());
            }

            var chat = response.Response!;
            AddUsage(chat.Usage);
            if (!string.IsNullOrWhiteSpace(chat.Content))
                await Emit(new AgentStep("thinking", chat.Content));

            if (chat.ToolCalls is not { Count: > 0 } calls)
            {
                // No tool calls this turn - the model considers the
                // assignment complete (or is stuck without knowing it needs
                // a tool it wasn't given, which reads the same from here).
                await Emit(new AgentStep("done", chat.Content));
                messages.Add(new ChatMessage("assistant", chat.Content));
                return new AgentRunResult(true, chat.Content, steps, iteration, messages, Usage());
            }

            messages.Add(new ChatMessage("assistant", chat.Content, ToolCalls: calls));

            foreach (var call in calls)
            {
                if (ct.IsCancellationRequested)
                {
                    await Emit(new AgentStep("cancelled", "Cancelled by operator."));
                    return new AgentRunResult(false, "Cancelled by operator.", steps, iteration, messages, Usage());
                }

                await Emit(new AgentStep("tool_call", $"{call.Name}({call.ArgumentsJson})"));
                var result = await _tools.ExecuteAsync(call, ct);
                await Emit(new AgentStep(result.IsError ? "tool_error" : "tool_result", result.Output));
                messages.Add(new ChatMessage("tool", result.Output, ToolCallId: result.ToolCallId, ToolName: result.ToolName));
            }
        }

        // HitIterationCap lets the caller distinguish "ran out of budget mid-
        // work" from a genuine failure - the assignment engine continues a
        // capped run that is still making file progress (see WorkerRole),
        // instead of reporting a half-built project as dead (user report:
        // "den slutar alltid vid 50 ... så den blir aldrig klar").
        var timeoutMessage = $"Assignment did not complete within {MaxIterations} iterations - stopped to avoid a runaway loop.";
        await Emit(new AgentStep("error", timeoutMessage));
        return new AgentRunResult(false, timeoutMessage, steps, MaxIterations, messages, Usage(), HitIterationCap: true);
    }

    /// <summary>Augments the caller's system prompt with the verify-loop
    /// contract when the executor exposes the verify tool: the model must not
    /// declare a coding task done until the project actually builds/tests.
    /// Kept minimal and appended only when relevant, so it never overrides a
    /// caller-supplied system prompt that already covers the workflow.</summary>
    private static string? BuildSystemPrompt(string? system, IReadOnlyList<ToolDefinition> tools)
    {
        if (!tools.Any(t => t.Name == "verify"))
            return system;

        const string verifyNote = """

        WORKFLOW: When editing or creating code, a task is only DONE once the
        project verifies. After each file change, run verify and read its
        output. If it reports failures, fix them and run verify again - do not
        stop or report success while verify still fails. Treat a passing
        verify as your definition of "finished", not "the model feels done".
        """;

        return system is { Length: > 0 } ? system + verifyNote : verifyNote.Trim();
    }
}
