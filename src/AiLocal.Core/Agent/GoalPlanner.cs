using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Core.Agent;

/// <summary>One step of a plan for an autonomous agent goal (see GoalPlanner).</summary>
public sealed record PlannedSubtask(string Title, string Description, bool Independent);

/// <summary>
/// Breaks a free-text goal into an ordered list of subtasks for AgentLoop to
/// execute one after another - the agent-mode counterpart to AiTaskPlanner
/// (which plans plain-chat goals, always assumed independent/parallel-safe).
///
/// The distinction that matters here and doesn't for AiTaskPlanner: an agent
/// subtask touches real files on one specific computer. A subtask that reads
/// or builds on another subtask's file output MUST run after it on the SAME
/// computer (Independent = false, the default) so it can actually see those
/// files - there's no shared filesystem across Workers. Independent = true is
/// reserved for a subtask that truly stands alone (pure research, a fully
/// separate deliverable), which is the only case safe to hand to a different
/// Worker running at the same time.
/// </summary>
public sealed class GoalPlanner
{
    private const string PlannerSystem = """
        You are the coordinator of a cluster of autonomous coding agents. Each
        agent has file and command access on its own computer, and no agent can
        see another agent's files.

        Break the user's goal into an ordered list of concrete, self-contained
        subtasks. For each one, decide "independent":
        - false (the default - use this unless you are sure otherwise): this
          subtask reads, edits, or builds on a shared codebase/file output, so
          it must run after the previous ones on the SAME computer.
        - true: this subtask truly does not depend on any other subtask's file
          output (e.g. independent research, a fully separate deliverable), so
          it is safe to run at the same time as the others on a different
          computer.

        If the goal is small enough for one subtask, return exactly one.

        Reply with STRICT JSON only - no prose, no markdown code fences - in
        exactly this shape:
        {"subtasks":[{"title":"short label","description":"full self-contained instruction for one agent","independent":false}]}
        """;

    private readonly Func<ChatRequest, CancellationToken, Task<ProviderResponse>> _complete;

    /// <summary>Takes a plain delegate rather than a specific provider type,
    /// same rationale as AgentLoop: the caller decides what "complete a chat
    /// request" means (a single provider, a fallback chain, a proxy to a
    /// specific Worker) with zero special-casing needed here.</summary>
    public GoalPlanner(Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete)
    {
        _complete = complete;
    }

    public async Task<IReadOnlyList<PlannedSubtask>?> PlanAsync(string goal, int maxParts, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            System = PlannerSystem,
            Messages = { new ChatMessage("user", $"Break this goal into at most {maxParts} subtasks:\n\n{goal}") }
        };

        ProviderResponse response;
        try
        {
            response = await _complete(request, ct);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content))
            return null;

        return Parse(response.Response.Content, maxParts);
    }

    private static IReadOnlyList<PlannedSubtask>? Parse(string content, int maxParts)
    {
        var json = ExtractJson(content);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("subtasks", out var subtasks) ||
                subtasks.ValueKind != JsonValueKind.Array)
                return null;

            var items = new List<PlannedSubtask>();
            foreach (var element in subtasks.EnumerateArray())
            {
                var description = element.TryGetProperty("description", out var d) ? d.GetString() : null;
                if (string.IsNullOrWhiteSpace(description)) continue;

                var title = element.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } label
                    ? label
                    : $"Steg {items.Count + 1}";

                var independent = element.TryGetProperty("independent", out var i) &&
                    i.ValueKind == JsonValueKind.True;

                items.Add(new PlannedSubtask(title, description!, independent));
                if (items.Count >= maxParts) break;
            }

            return items.Count > 0 ? items : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Strips ```json ... ``` fences if the model added them, same
    /// defensive parsing as AiTaskPlanner.</summary>
    private static string? ExtractJson(string content)
    {
        var text = content.Trim();

        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            if (firstNewline > 0) text = text[(firstNewline + 1)..];
            if (text.EndsWith("```")) text = text[..^3];
            text = text.Trim();
        }

        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return text[start..(end + 1)];
    }
}
