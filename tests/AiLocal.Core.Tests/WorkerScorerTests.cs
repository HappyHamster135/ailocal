using AiLocal.Core.Cluster;
using AiLocal.Core.Hardware;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

public class WorkerScorerTests
{
    private static NodeInfo Worker(
        string id,
        List<string>? skills = null,
        int activeTasks = 0,
        int maxConcurrentTasks = 4,
        HardwareProfile? hardware = null,
        List<string>? providerPriority = null,
        int successCount = 0,
        int failureCount = 0,
        double avgLatencyMs = 0) => new()
    {
        Id = id,
        Name = id,
        Role = NodeRole.Worker,
        Endpoint = $"http://worker-{id}:5081",
        Skills = skills ?? ["general"],
        ActiveTasks = activeTasks,
        MaxConcurrentTasks = maxConcurrentTasks,
        Hardware = hardware,
        ProviderPriority = providerPriority ?? [],
        SuccessCount = successCount,
        FailureCount = failureCount,
        AvgLatencyMs = avgLatencyMs
    };

    [Fact]
    public void Match_PrefersExactSkillOverGeneralist()
    {
        var specialist = Worker("specialist", skills: ["coding"]);
        var generalist = Worker("generalist", skills: ["general"]);
        var requirement = new WorkRequirement("coding", Complexity: 3);

        var specialistMatch = WorkerScorer.Match(specialist, requirement);
        var generalistMatch = WorkerScorer.Match(generalist, requirement);

        Assert.True(specialistMatch.SkillMatched);
        Assert.True(generalistMatch.SkillMatched); // generalists can still take any work
        Assert.True(specialistMatch.MatchScore > generalistMatch.MatchScore);
    }

    [Fact]
    public void Match_NoDeclaredSkillAndNotGeneralist_IsPenalizedButStillMatchable()
    {
        var writerOnly = Worker("writer", skills: ["writing"]);
        var requirement = new WorkRequirement("coding", Complexity: 3);

        var match = WorkerScorer.Match(writerOnly, requirement);

        Assert.False(match.SkillMatched);
        Assert.True(match.MatchScore < 0, "an unrelated specialist should score worse than an unscored baseline");
    }

    [Fact]
    public void Match_AtCapacity_HasCapacityIsFalseAndScoreIsPenalized()
    {
        var full = Worker("full", activeTasks: 4, maxConcurrentTasks: 4);
        var free = Worker("free", activeTasks: 0, maxConcurrentTasks: 4);
        var requirement = new WorkRequirement("general", Complexity: 2);

        var fullMatch = WorkerScorer.Match(full, requirement);
        var freeMatch = WorkerScorer.Match(free, requirement);

        Assert.False(fullMatch.HasCapacity);
        Assert.True(freeMatch.HasCapacity);
        Assert.True(freeMatch.MatchScore > fullMatch.MatchScore);
    }

    [Fact]
    public void Match_ReservedTasksCountTowardLoad_EvenBeforeTheyAppearAsActiveTasks()
    {
        var worker = Worker("w", activeTasks: 0, maxConcurrentTasks: 1);
        var requirement = new WorkRequirement("general", Complexity: 2);

        // One task already claimed via WorkerSlotBroker this round, not yet
        // reflected in ActiveTasks (which only updates once dispatch starts).
        var reserved = WorkerScorer.Match(worker, requirement, reservedTasks: 1);

        Assert.False(reserved.HasCapacity);
    }

    [Fact]
    public void Match_StrongerHardwareScoresHigherForHardTasks()
    {
        var beefy = Worker("beefy", hardware: new HardwareProfile("cpu", 16, 64, "RTX 4090", 24, true));
        var light = Worker("light", hardware: new HardwareProfile("cpu", 4, 8, null, 0, false));
        var requirement = new WorkRequirement("general", Complexity: 5);

        var beefyMatch = WorkerScorer.Match(beefy, requirement);
        var lightMatch = WorkerScorer.Match(light, requirement);

        Assert.True(beefyMatch.MatchScore > lightMatch.MatchScore);
    }

    [Fact]
    public void Match_HigherSuccessRateScoresHigherThanFrequentFailures()
    {
        var reliable = Worker("reliable", successCount: 20, failureCount: 0);
        var flaky = Worker("flaky", successCount: 2, failureCount: 18);
        var requirement = new WorkRequirement("general", Complexity: 2);

        var reliableMatch = WorkerScorer.Match(reliable, requirement);
        var flakyMatch = WorkerScorer.Match(flaky, requirement);

        Assert.True(reliableMatch.MatchScore > flakyMatch.MatchScore);
    }

    [Fact]
    public void Match_UnprovenWorker_ScoresBetweenReliableAndFlaky()
    {
        // No attempts yet should get an optimistic prior (see WorkerScorer),
        // not be starved forever behind workers that already built a track record.
        var unproven = Worker("unproven");
        var reliable = Worker("reliable", successCount: 20, failureCount: 0);
        var flaky = Worker("flaky", successCount: 2, failureCount: 18);
        var requirement = new WorkRequirement("general", Complexity: 2);

        var unprovenMatch = WorkerScorer.Match(unproven, requirement);
        var reliableMatch = WorkerScorer.Match(reliable, requirement);
        var flakyMatch = WorkerScorer.Match(flaky, requirement);

        Assert.True(unprovenMatch.MatchScore > flakyMatch.MatchScore);
        Assert.True(unprovenMatch.MatchScore < reliableMatch.MatchScore);
    }

    [Fact]
    public void Match_HigherLatencyScoresLower()
    {
        var fast = Worker("fast", avgLatencyMs: 200);
        var slow = Worker("slow", avgLatencyMs: 15000);
        var requirement = new WorkRequirement("general", Complexity: 2);

        var fastMatch = WorkerScorer.Match(fast, requirement);
        var slowMatch = WorkerScorer.Match(slow, requirement);

        Assert.True(fastMatch.MatchScore > slowMatch.MatchScore);
    }

    [Fact]
    public void RankFor_OrdersByCapacityFirstThenScore()
    {
        var busy = Worker("busy", skills: ["coding"], activeTasks: 4, maxConcurrentTasks: 4);
        var freeGeneralist = Worker("free", skills: ["general"], activeTasks: 0, maxConcurrentTasks: 4);
        var requirement = new WorkRequirement("coding", Complexity: 3);

        var ranked = WorkerScorer.RankFor([busy, freeGeneralist], requirement);

        // Capacity beats raw skill fit: a full specialist can't take the work at all.
        Assert.Equal("free", ranked[0].Worker.Node.Id);
        Assert.Equal("busy", ranked[1].Worker.Node.Id);
    }

    [Fact]
    public void RankFor_ReservationsFromConcurrentClaimsAreRespected()
    {
        var a = Worker("a", maxConcurrentTasks: 1);
        var b = Worker("b", maxConcurrentTasks: 1);
        var requirement = new WorkRequirement("general", Complexity: 1);
        var reservations = new Dictionary<string, int> { ["a"] = 1 };

        var ranked = WorkerScorer.RankFor([a, b], requirement, reservations);

        var aMatch = ranked.Single(m => m.Worker.Node.Id == "a");
        var bMatch = ranked.Single(m => m.Worker.Node.Id == "b");
        Assert.False(aMatch.HasCapacity);
        Assert.True(bMatch.HasCapacity);
    }
}
