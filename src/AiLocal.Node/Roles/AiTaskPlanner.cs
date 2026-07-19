using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Roles;

/// <summary>
/// Uses the Host's own provider chain to break a goal into subtasks the way a
/// manager would - each item gets a difficulty score so the Host can match the
/// hardest work to the strongest workers. Returns null (caller falls back to the
/// heuristic planner) whenever no model is available or the reply is unusable.
/// </summary>
public sealed class AiTaskPlanner
{
    private const string PlannerSystem =
        "You are the coordinator of a compute cluster of AI workers. " +
        "Break the user's goal into independent subtasks that different workers can run in parallel. " +
        "Each subtask must be self-contained. Rate each subtask's difficulty from 1 (trivial) to 5 (hard reasoning). " +
        "Assign one primary skill to each subtask: general, coding, research, writing, analysis, data, or vision. " +
        "For GAME goals, prefer the game-team skills so the right specialist role owns the work: " +
        "game-design (mechanics/balance/progression), level-design (levels/pacing), art (sprites/UI/visuals), " +
        "sound-design (SFX/music), game-review (playtesting/critique), plus coding for the implementation itself. " +
        "Reply with STRICT JSON only - no prose, no code fences - in exactly this shape: " +
        "{\"subtasks\":[{\"title\":\"short label\",\"prompt\":\"full instruction for one worker\",\"complexity\":3,\"skill\":\"coding\"}]}. " +
        "If the goal is small enough for a single worker, return one subtask.";

    private readonly FallbackChatProvider _provider;
    private readonly int _maxTokens;

    public AiTaskPlanner(FallbackChatProvider provider, int maxTokens)
    {
        _provider = provider;
        _maxTokens = Math.Clamp(maxTokens, 512, 4096);
    }

    public async Task<IReadOnlyList<PlannedWorkItem>?> PlanAsync(
        string goal, int maxParts, CancellationToken ct)
    {
        var request = new ChatRequest
        {
            System = PlannerSystem,
            MaxTokens = _maxTokens,
            Messages =
            {
                new ChatMessage("user", $"Split this goal into at most {maxParts} subtasks:\n\n{goal}")
            }
        };

        ProviderResponse response;
        try
        {
            response = await _provider.CompleteAsync(request, ct);
        }
        catch
        {
            return null;
        }

        if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content))
            return null;

        return Parse(response.Response.Content, goal, maxParts);
    }

    private static IReadOnlyList<PlannedWorkItem>? Parse(string content, string goal, int maxParts)
    {
        var json = ExtractJson(content);
        if (json is null) return null;

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("subtasks", out var subtasks) ||
                subtasks.ValueKind != JsonValueKind.Array)
                return null;

            var items = new List<PlannedWorkItem>();
            foreach (var element in subtasks.EnumerateArray())
            {
                var prompt = element.TryGetProperty("prompt", out var p) ? p.GetString() : null;
                if (string.IsNullOrWhiteSpace(prompt)) continue;

                var title = element.TryGetProperty("title", out var t) && t.GetString() is { Length: > 0 } label
                    ? label
                    : $"Part {items.Count + 1}";

                var complexity = element.TryGetProperty("complexity", out var c) && c.TryGetInt32(out var value)
                    ? Math.Clamp(value, 1, 5)
                    : 3;

                var skill = element.TryGetProperty("skill", out var s) && s.GetString() is { Length: > 0 } area
                    ? area.Trim().ToLowerInvariant()
                    : "general";

                items.Add(new PlannedWorkItem(title, GroundPrompt(goal, prompt!), complexity, skill));
                if (items.Count >= maxParts) break;
            }

            return items.Count > 0 ? items : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GroundPrompt(string goal, string subtask) =>
        $"""
        Overall goal (for context):
        {goal}

        Your assigned subtask:
        {subtask}

        Complete only this subtask and return a result that can be merged with the other workers' output.
        """;

    private static string? ExtractJson(string content)
    {
        var text = content.Trim();

        // Strip ```json ... ``` fences if the model added them.
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
