using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using Xunit;

namespace AiLocal.Core.Tests;

public class AgentRolesTests
{
    [Theory]
    [InlineData("coding", "developer")]
    [InlineData("testing", "tester")]
    [InlineData("review", "reviewer")]
    [InlineData("architecture", "architect")]
    [InlineData("writing", "architect")]
    [InlineData("research", "architect")]
    [InlineData("general", "architect")]
    [InlineData(null, "architect")]
    public void RoleForSkill_MapsPlannerSkillToRole(string? skill, string expectedRole)
    {
        Assert.Equal(expectedRole, AgentRoles.RoleForSkill(skill));
    }

    [Fact]
    public void Defaults_ContainTheFourCompanyRoles()
    {
        var roles = AgentRoles.Defaults();
        Assert.Equal(9, roles.Count);
        Assert.Contains(roles, r => r.Id == "architect");
        Assert.Contains(roles, r => r.Id == "developer");
        Assert.Contains(roles, r => r.Id == "tester");
        Assert.Contains(roles, r => r.Id == "reviewer");
    }

    [Fact]
    public void Reviewer_IsMarkedAsReviewer()
    {
        var reviewer = AgentRoles.Defaults().First(r => r.Id == "reviewer");
        Assert.True(reviewer.IsReviewer);
        var developer = AgentRoles.Defaults().First(r => r.Id == "developer");
        Assert.False(developer.IsReviewer);
    }

    [Fact]
    public void Architect_HasComplexityBias_SoItLandsOnAStrongerModel()
    {
        var architect = AgentRoles.Defaults().First(r => r.Id == "architect");
        Assert.Equal(1, architect.ComplexityBias);
        Assert.Equal("architecture", architect.RequiredSkill);
    }

    [Fact]
    public void ResolveRole_FallsBackToDefaultsWhenConfigEmpty()
    {
        var settings = new NodeSettings();
        settings.Host.Roles = new List<AgentRole>(); // operator cleared them
        var role = settings.Host.ResolveRole("developer");
        Assert.NotNull(role);
        Assert.Equal("developer", role!.Id);
    }

    [Fact]
    public void ResolveRole_PrefersConfiguredRole()
    {
        var settings = new NodeSettings();
        settings.Host.Roles = new List<AgentRole>
        {
            new("developer", "Min Utvecklare", "koda!", "coding")
        };
        var role = settings.Host.ResolveRole("developer");
        Assert.Equal("Min Utvecklare", role!.Name);
    }

    [Fact]
    public void AgentRole_RoundTripsThroughJson()
    {
        var role = new AgentRole("developer", "Utvecklare", "system", "coding", ComplexityBias: 1, IsReviewer: false);
        var json = System.Text.Json.JsonSerializer.Serialize(role);
        var back = System.Text.Json.JsonSerializer.Deserialize<AgentRole>(json);
        Assert.Equal("developer", back!.Id);
        Assert.Equal("coding", back.RequiredSkill);
        Assert.Equal(1, back.ComplexityBias);
    }
}
