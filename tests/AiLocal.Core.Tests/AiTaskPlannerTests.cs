using AiLocal.Core.Configuration;
using AiLocal.Core.Providers;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

public class AiTaskPlannerTests
{
    private static AiTaskPlanner PlannerReplying(string content)
    {
        var provider = FakeChatProvider.Success("anthropic", content);
        var fallback = new FallbackChatProvider([provider], new ProviderSettings { Priority = ["anthropic"] });
        return new AiTaskPlanner(fallback, maxTokens: 1024);
    }

    [Fact]
    public async Task PlanAsync_CleanJson_ParsesAllFields()
    {
        var planner = PlannerReplying("""
            {"subtasks":[
              {"title":"Research", "prompt":"find sources", "complexity":4, "skill":"research"},
              {"title":"Write", "prompt":"draft the report", "complexity":2, "skill":"writing"}
            ]}
            """);

        var items = await planner.PlanAsync("Write a market report", maxParts: 5, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Equal(2, items!.Count);
        Assert.Equal("Research", items[0].Title);
        Assert.Contains("find sources", items[0].Prompt);
        Assert.Contains("Write a market report", items[0].Prompt); // grounded with the overall goal
        Assert.Equal(4, items[0].Complexity);
        Assert.Equal("research", items[0].Skill);
    }

    [Fact]
    public async Task PlanAsync_MarkdownFencedJson_IsExtracted()
    {
        var planner = PlannerReplying("""
            Sure, here is the plan:
            ```json
            {"subtasks":[{"title":"Only part","prompt":"do the thing","complexity":3,"skill":"general"}]}
            ```
            """);

        var items = await planner.PlanAsync("Do the thing", maxParts: 3, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Single(items!);
        Assert.Equal("Only part", items![0].Title);
    }

    [Fact]
    public async Task PlanAsync_JsonSurroundedByProse_IsExtracted()
    {
        var planner = PlannerReplying(
            "I'll split this into one subtask. " +
            "{\"subtasks\":[{\"title\":\"Part\",\"prompt\":\"work\",\"complexity\":2,\"skill\":\"coding\"}]} " +
            "Let me know if you want changes.");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Single(items!);
    }

    [Fact]
    public async Task PlanAsync_MalformedJson_ReturnsNullForHeuristicFallback()
    {
        var planner = PlannerReplying("{\"subtasks\": [ this is not valid json ");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.Null(items);
    }

    [Fact]
    public async Task PlanAsync_MissingSubtasksProperty_ReturnsNull()
    {
        var planner = PlannerReplying("{\"plan\":\"do it\"}");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.Null(items);
    }

    [Fact]
    public async Task PlanAsync_EmptySubtasksArray_ReturnsNull()
    {
        var planner = PlannerReplying("{\"subtasks\":[]}");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.Null(items);
    }

    [Fact]
    public async Task PlanAsync_ItemsMissingPrompt_AreSkipped()
    {
        var planner = PlannerReplying("""
            {"subtasks":[
              {"title":"No prompt","complexity":3},
              {"title":"Has prompt","prompt":"do it","complexity":3}
            ]}
            """);

        var items = await planner.PlanAsync("goal", maxParts: 5, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Single(items!);
        Assert.Equal("Has prompt", items![0].Title);
    }

    [Fact]
    public async Task PlanAsync_MoreSubtasksThanMaxParts_IsTruncated()
    {
        var planner = PlannerReplying("""
            {"subtasks":[
              {"title":"A","prompt":"a"},
              {"title":"B","prompt":"b"},
              {"title":"C","prompt":"c"}
            ]}
            """);

        var items = await planner.PlanAsync("goal", maxParts: 2, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Equal(2, items!.Count);
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(99, 5)]
    public async Task PlanAsync_ComplexityOutOfRange_IsClamped(int rawComplexity, int expectedClamped)
    {
        var planner = PlannerReplying(
            $$"""{"subtasks":[{"title":"Part","prompt":"work","complexity":{{rawComplexity}}}]}""");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Equal(expectedClamped, items![0].Complexity);
    }

    [Fact]
    public async Task PlanAsync_MissingSkill_DefaultsToGeneral()
    {
        var planner = PlannerReplying("{\"subtasks\":[{\"title\":\"Part\",\"prompt\":\"work\"}]}");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Equal("general", items![0].Skill);
    }

    [Fact]
    public async Task PlanAsync_SkillIsLowercasedAndTrimmed()
    {
        var planner = PlannerReplying(
            "{\"subtasks\":[{\"title\":\"Part\",\"prompt\":\"work\",\"skill\":\"  CODING \"}]}");

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.NotNull(items);
        Assert.Equal("coding", items![0].Skill);
    }

    [Fact]
    public async Task PlanAsync_ProviderChainFails_ReturnsNull()
    {
        var failing = FakeChatProvider.Failing("anthropic", ProviderOutcome.FatalError);
        var fallback = new FallbackChatProvider([failing], new ProviderSettings { Priority = ["anthropic"] });
        var planner = new AiTaskPlanner(fallback, maxTokens: 1024);

        var items = await planner.PlanAsync("goal", maxParts: 3, CancellationToken.None);

        Assert.Null(items);
    }
}
