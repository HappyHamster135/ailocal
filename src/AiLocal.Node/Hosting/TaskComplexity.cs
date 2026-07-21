using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Deterministic 1-5 complexity estimate for an assignment - the missing
/// link between the model-tier config and the assignment engine. ModelTiers
/// had cost-aware routes all along ("coding cheap up to complexity 3, strong
/// from 4") but the assignment path never asked, so EVERY task ran on the
/// chain's first model regardless of difficulty (user report: "endast
/// DeepSeek ... väljer inte modell efter uppgiften"). Deliberately heuristic
/// and free - a model call to pick a model would defeat the cost purpose.
/// </summary>
public static class TaskComplexity
{
    private static readonly string[] SimpleMarkers =
        ["enkel", "enkelt", "simple", "liten", "litet", "basic", "minimal", "snabb", "quick", "small"];

    private static readonly string[] HeavyMarkers =
        ["avancerad", "advanced", "djup", "deep", "komplex", "complex", "stor", "big ",
         "manager", "management", "simulator", "tycoon", "rpg", "roguelike", "multiplayer",
         "3d", "procedur", "produktionsnivå", "production", "ekonomi", "kampanj", "campaign"];

    public static (int Complexity, string Reason) Estimate(string prompt, int? teamSize = null)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        var score = 3;
        var reasons = new List<string> { "standardbygge" };

        if (SimpleMarkers.Any(p.Contains))
        {
            score -= 1;
            reasons.Add("markerad enkel");
        }
        if (HeavyMarkers.Any(p.Contains))
        {
            score += 1;
            reasons.Add("avancerade nyckelord");
        }
        if (p.Length > 300)
        {
            score += 1;
            reasons.Add("lång kravtext");
        }
        if (HostRole.LooksMultiPart(prompt ?? ""))
        {
            score += 1;
            reasons.Add("flera delar");
        }
        if (teamSize is >= 2)
        {
            score += 1;
            reasons.Add("team-läge");
        }

        return (Math.Clamp(score, 1, 5), string.Join(", ", reasons));
    }
}
