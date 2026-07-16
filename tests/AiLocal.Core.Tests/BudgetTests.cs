using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>A4: the daily budget guard must trip routing to local Ollama only
/// once today's accumulated task spend crosses the configured cap.</summary>
public class BudgetTests
{
    private static BudgetService Build(decimal limit, params (decimal cost, int daysAgo)[] tasks)
    {
        var store = new HostStateStore();
        var list = tasks.Select((t, i) => new AgentTask
        {
            Id = "t" + i,
            Prompt = "p" + i,
            CreatedAt = DateTimeOffset.UtcNow.AddDays(t.daysAgo),
            EstimatedCostUsd = t.cost,
        }).ToList();
        store.SaveTasks(list);

        var settings = new NodeSettings();
        settings.Worker.BudgetLimitUsd = limit;

        var sc = new ServiceCollection();
        sc.AddSingleton(store);
        sc.AddSingleton(settings);
        return new BudgetService(sc.BuildServiceProvider());
    }

    [Fact]
    public void IsOverBudget_true_when_today_exceeds_limit()
    {
        var svc = Build(7.00m, (5.00m, 0), (3.00m, 0));
        Assert.True(svc.IsOverBudget()); // 8 > 7
    }

    [Fact]
    public void IsOverBudget_false_under_limit()
    {
        var svc = Build(10.00m, (5.00m, 0), (3.00m, 0));
        Assert.False(svc.IsOverBudget()); // 8 < 10
    }

    [Fact]
    public void IsOverBudget_false_when_guard_disabled_zero()
    {
        var svc = Build(0m, (5.00m, 0), (3.00m, 0));
        Assert.False(svc.IsOverBudget()); // 0 = no cap
    }

    [Fact]
    public void TodaysCost_excludes_prior_days()
    {
        var svc = Build(100m, (5.00m, 0), (9.00m, 1));
        Assert.Equal(5.00m, svc.TodaysCostUsd()); // only today counts
        Assert.False(svc.IsOverBudget());
    }
}
