using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

public class GoalPlannerTests
{
    [Fact]
    public async Task PlanAsync_ValidJson_ParsesSubtasksInOrder()
    {
        var provider = FakeChatProvider.Success("test", """
            {"subtasks":[
              {"title":"Scaffold project","description":"Set up the project skeleton.","independent":false},
              {"title":"Research fonts","description":"Find three good fonts.","independent":true}
            ]}
            """);

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("build a landing page", 5, default);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Count);
        Assert.Equal("Scaffold project", plan[0].Title);
        Assert.False(plan[0].Independent);
        Assert.Equal("Research fonts", plan[1].Title);
        Assert.True(plan[1].Independent);
    }

    [Fact]
    public async Task PlanAsync_MissingIndependentField_DefaultsToFalse()
    {
        var provider = FakeChatProvider.Success("test", """{"subtasks":[{"title":"Step 1","description":"Do it."}]}""");

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 5, default);

        Assert.NotNull(plan);
        Assert.False(Assert.Single(plan!).Independent);
    }

    [Fact]
    public async Task PlanAsync_MarkdownFencedResponse_StillParses()
    {
        var provider = FakeChatProvider.Success("test", """
            ```json
            {"subtasks":[{"title":"Only step","description":"Do the whole thing.","independent":false}]}
            ```
            """);

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 5, default);

        Assert.NotNull(plan);
        Assert.Equal("Only step", Assert.Single(plan!).Title);
    }

    [Fact]
    public async Task PlanAsync_ProviderFails_ReturnsNull()
    {
        var provider = FakeChatProvider.Failing("test", ProviderOutcome.AuthFailed);

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 5, default);

        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_UnparsableResponse_ReturnsNull()
    {
        var provider = FakeChatProvider.Success("test", "Sure, here's a plan for you: first, ...");

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 5, default);

        Assert.Null(plan);
    }

    [Fact]
    public async Task PlanAsync_MoreSubtasksThanMaxParts_TruncatesToMaxParts()
    {
        var provider = FakeChatProvider.Success("test", """
            {"subtasks":[
              {"title":"A","description":"a","independent":false},
              {"title":"B","description":"b","independent":false},
              {"title":"C","description":"c","independent":false}
            ]}
            """);

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 2, default);

        Assert.NotNull(plan);
        Assert.Equal(2, plan!.Count);
        Assert.Equal(["A", "B"], plan.Select(p => p.Title));
    }

    [Fact]
    public async Task PlanAsync_SubtaskMissingDescription_IsSkipped()
    {
        var provider = FakeChatProvider.Success("test", """
            {"subtasks":[
              {"title":"No description"},
              {"title":"Has one","description":"do this","independent":false}
            ]}
            """);

        var plan = await new GoalPlanner(provider.CompleteAsync).PlanAsync("goal", 5, default);

        Assert.NotNull(plan);
        Assert.Equal("Has one", Assert.Single(plan!).Title);
    }
}
