using AiLocal.Core.Configuration;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.38.0: assignments finally use the cost-aware model
/// tiers. The complexity estimate is deterministic, and the default routes
/// send easy builds to the cheap coder and hard ones to the strong tier.</summary>
public class ModelRoutingTests
{
    [Theory]
    [InlineData("bygg ett enkelt snake-spel som webbspel", 2)]
    [InlineData("bygg ett 2d plattformsspel", 3)]
    [InlineData("bygg ett quiz-spel", 3)]
    public void Estimate_SimpleAndStandardBuilds(string prompt, int expected)
    {
        var (complexity, _) = TaskComplexity.Estimate(prompt);
        Assert.Equal(expected, complexity);
    }

    [Fact]
    public void Estimate_ManagerSimulator_ClimbsTheLadder()
    {
        var (complexity, reason) = TaskComplexity.Estimate(
            "bygg ett 2d fotbolls managementsimulatorspel där man väljer ett lag och bygger det från det värsta " +
            "till det bästa laget, med tre svårighetsgrader, transfermarknad, träningssystem och en säsongsstruktur " +
            "med upp- och nedflyttning mellan divisioner samt ekonomi");
        Assert.True(complexity >= 4, $"fick {complexity} ({reason})");
    }

    [Fact]
    public void Estimate_TeamMode_AddsOne()
    {
        var (solo, _) = TaskComplexity.Estimate("bygg ett 2d plattformsspel");
        var (team, reason) = TaskComplexity.Estimate("bygg ett 2d plattformsspel", teamSize: 3);
        Assert.Equal(solo + 1, team);
        Assert.Contains("team", reason);
    }

    [Fact]
    public void DefaultRoutes_CheapForEasy_StrongForHard()
    {
        var tiers = new ModelTiers();
        var (_, easyModel) = tiers.ForTask("coding", 2);
        var (_, hardModel) = tiers.ForTask("coding", 5);
        Assert.Contains("deepseek", easyModel);
        Assert.NotEqual(easyModel, hardModel); // trappan finns på riktigt
    }

    [Fact]
    public void EndToEnd_SimplePromptRoutesCheap_HeavyRoutesStrong()
    {
        var tiers = new ModelTiers();
        var (easyC, _) = TaskComplexity.Estimate("bygg ett enkelt snake-spel som webbspel");
        var (hardC, _) = TaskComplexity.Estimate(
            "bygg ett avancerat management-simulatorspel med ekonomi, kampanj och multiplayer, " +
            "inklusive transfermarknad och säsonger, och gör det på produktionsnivå");
        var (_, easyModel) = tiers.ForTask("coding", easyC);
        var (_, hardModel) = tiers.ForTask("coding", hardC);
        Assert.Contains("deepseek", easyModel);
        Assert.Contains("kimi", hardModel);
    }

    [Fact]
    public void Estimate_GodotUnityGame_ClimbsToStrongTier_Html5StaysCheap()
    {
        // Samma plattformsspel: en webbleksak klarar den billiga tiern, men ett
        // riktigt Godot/Unity-spel måste starta på den kapabla modellen - annars
        // failar den svaga modellen bygget (rapporterat).
        var (html, _) = TaskComplexity.Estimate("bygg ett 2d plattformsspel", engine: "html5");
        var (godot, godotReason) = TaskComplexity.Estimate("bygg ett 2d plattformsspel", engine: "godot");
        var (unity, _) = TaskComplexity.Estimate("bygg ett 2d plattformsspel", engine: "unity");

        Assert.Equal(3, html);                 // html5 orört => billig tier
        Assert.True(godot >= 5, $"godot fick {godot} ({godotReason})");
        Assert.True(unity >= 5);
        Assert.Contains("motorspel", godotReason);

        var tiers = new ModelTiers();
        Assert.Contains("kimi", tiers.ForTask("coding", godot).Model);  // stark modell
        Assert.NotEqual(tiers.ForTask("coding", godot).Model, tiers.ForTask("coding", html).Model);
    }
}
