using AiLocal.Core.Nodes;

namespace AiLocal.Core.Cluster;

public sealed record WorkerScore(NodeInfo Node, double Capability, string Tier)
{
    public double Effective => Capability / (1 + Math.Max(0, Node.ActiveTasks));
}

public sealed record WorkerMatch(
    WorkerScore Worker,
    double MatchScore,
    bool HasCapacity,
    bool SkillMatched,
    string RequiredSkill,
    string Reason);

public sealed record WorkRequirement(string Skill, int Complexity);

/// <summary>
/// Scores raw Worker capability and matches business-like work requirements to
/// the most suitable node. Explicit skills win, while hardware and provider
/// availability provide useful defaults for unconfigured Workers.
/// </summary>
public static class WorkerScorer
{
    public static WorkerScore Score(NodeInfo node)
    {
        var hw = node.Hardware;
        double vram = hw is { CudaAvailable: true } ? hw.GpuMemoryGb : 0;
        double ram = hw?.SystemMemoryGb ?? 0;
        int cores = hw?.LogicalCores ?? 1;

        double compute = vram * 8 + ram * 1.5 + cores * 2;
        bool hasCloud = node.ProviderPriority.Any(IsCloudProvider);
        double capability = compute + (hasCloud ? 120 : 0);

        string tier = capability switch
        {
            >= 180 => "Strong",
            >= 90 => "Medium",
            _ => "Light"
        };

        return new WorkerScore(node, Math.Round(capability, 1), tier);
    }

    public static WorkerMatch Match(
        NodeInfo node,
        WorkRequirement requirement,
        int reservedTasks = 0)
    {
        var worker = Score(node);
        var skill = NormalizeSkill(requirement.Skill);
        var declaredSkills = DeclaredSkills(node);
        var inferredSkills = InferredSkills(node);
        var exactSkill = declaredSkills.Contains(skill);
        var inferredSkill = inferredSkills.Contains(skill);
        var generalist = declaredSkills.Contains("general");
        var skillMatched = skill == "general" || exactSkill || inferredSkill || generalist;

        var load = Math.Max(0, node.ActiveTasks + reservedTasks);
        var maxTasks = Math.Clamp(node.MaxConcurrentTasks, 1, 32);
        var hasCapacity = load < maxTasks;

        var complexity = Math.Clamp(requirement.Complexity, 1, 5);
        var requiredCapability = complexity switch
        {
            1 => 25,
            2 => 60,
            3 => 100,
            4 => 155,
            _ => 210
        };

        var capabilityGap = worker.Capability - requiredCapability;
        var capabilityFit = capabilityGap >= 0
            ? Math.Min(45, capabilityGap * 0.2)
            : capabilityGap * 1.4;
        var skillScore = exactSkill
            ? 300
            : inferredSkill
                ? 100
                : skill == "general"
                    ? 45
                    : generalist
                        ? 10
                        : -120;
        var loadPenalty = load * 70;
        var capacityPenalty = hasCapacity ? 0 : 500;

        // Reliability: unproven workers (no attempts yet) get a mildly optimistic
        // prior so they still get picked and can build a track record, rather
        // than being starved forever by workers that happen to have history.
        var attempts = node.SuccessCount + node.FailureCount;
        var reliability = attempts == 0 ? 0.9 : (double)node.SuccessCount / attempts;
        var reliabilityScore = (reliability - 0.5) * 200;
        var latencyPenalty = node.AvgLatencyMs > 0 ? Math.Min(40, node.AvgLatencyMs / 500.0) : 0;

        var matchScore = worker.Capability + capabilityFit + skillScore
            - loadPenalty - capacityPenalty + reliabilityScore - latencyPenalty;

        var skillText = exactSkill
            ? $"specialized in {skill}"
            : inferredSkill
                ? $"hardware/provider fit for {skill}"
            : skill == "general"
                ? "general-purpose work"
                : generalist
                    ? $"generalist fallback for {skill}"
                    : $"no declared {skill} skill";
        var capacityText = hasCapacity
            ? $"{load}/{maxTasks} active slots"
            : $"capacity full ({load}/{maxTasks})";
        var reliabilityText = attempts == 0
            ? "no history yet"
            : $"{reliability:P0} success over {attempts} tasks";
        var reason = $"{worker.Tier} worker, {skillText}, {capacityText}, {reliabilityText}, capability {worker.Capability:0.#}";

        return new WorkerMatch(
            worker,
            Math.Round(matchScore, 1),
            hasCapacity,
            skillMatched,
            skill,
            reason);
    }

    public static IReadOnlyList<WorkerScore> Rank(IEnumerable<NodeInfo> workers) =>
        workers.Select(Score).OrderByDescending(s => s.Effective).ToList();

    public static IReadOnlyList<WorkerMatch> RankFor(
        IEnumerable<NodeInfo> workers,
        WorkRequirement requirement,
        IReadOnlyDictionary<string, int>? reservations = null) =>
        workers
            .Select(node => Match(
                node,
                requirement,
                reservations is not null && reservations.TryGetValue(node.Id, out var count) ? count : 0))
            .OrderByDescending(match => match.HasCapacity)
            .ThenByDescending(match => match.MatchScore)
            .ToList();

    private static IReadOnlySet<string> DeclaredSkills(NodeInfo node)
    {
        var skills = new HashSet<string>(
            node.Skills.Select(NormalizeSkill).Where(value => value.Length > 0),
            StringComparer.OrdinalIgnoreCase);

        if (skills.Count == 0)
            skills.Add("general");
        return skills;
    }

    private static IReadOnlySet<string> InferredSkills(NodeInfo node)
    {
        var skills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (node.Hardware is { CudaAvailable: true, GpuMemoryGb: >= 6 })
        {
            skills.Add("vision");
            skills.Add("data");
        }

        if (node.ProviderPriority.Any(IsCloudProvider))
        {
            skills.Add("research");
            skills.Add("writing");
            skills.Add("analysis");
        }

        return skills;
    }

    private static bool IsCloudProvider(string provider) =>
        provider.Equals("anthropic", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("gemini", StringComparison.OrdinalIgnoreCase) ||
        provider.Equals("openrouter", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSkill(string? skill) =>
        string.IsNullOrWhiteSpace(skill) ? "general" : skill.Trim().ToLowerInvariant();
}
