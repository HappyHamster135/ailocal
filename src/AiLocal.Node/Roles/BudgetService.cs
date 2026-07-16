using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

/// <summary>
/// A4 cost-guard: keeps a running tally of today's USD spend (summed from the
/// Host's task ledger) and reports when it has crossed the operator-configured
/// daily budget. When over budget, the dispatcher routes new work to the local
/// Ollama model instead of paid providers - the "team" downshifts to free
/// compute rather than silently racking up a bill.
/// </summary>
public sealed class BudgetService
{
    private static BudgetService? _current;
    internal static void Bind(BudgetService svc) => _current = svc;
    /// <summary>The active instance, set once the host wires DI up.</summary>
    internal static BudgetService? Current => _current;

    private readonly IServiceProvider _services;

    public BudgetService(IServiceProvider services) => _services = services;

    /// <summary>Today's accumulated USD cost from completed/active tasks.</summary>
    public decimal TodaysCostUsd()
    {
        var store = _services.GetService<HostStateStore>();
        if (store is null) return 0m;
        var today = DateTimeOffset.UtcNow.Date;
        return store.ReadTasks()
            .Where(t => t.CreatedAt.Date == today && t.EstimatedCostUsd is { } c)
            .Sum(t => t.EstimatedCostUsd ?? 0m);
    }

    /// <summary>True once today's spend has reached the configured limit.</summary>
    public bool IsOverBudget()
    {
        var settings = _services.GetService<NodeSettings>();
        if (settings is null || settings.Worker.BudgetLimitUsd <= 0m)
            return false; // budget guard disabled (0 = no cap)
        return TodaysCostUsd() >= settings.Worker.BudgetLimitUsd;
    }

    /// <summary>The local Ollama model to fall back to when over budget, or "" to
    /// let the provider pick its default. Null when none is configured.</summary>
    public string? FallbackOllamaModel()
        => _services.GetService<NodeSettings>()?.Providers.OllamaModel;
}
