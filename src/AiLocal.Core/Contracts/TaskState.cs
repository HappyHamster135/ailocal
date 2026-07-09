namespace AiLocal.Core.Contracts;

public enum TaskState
{
    Pending,
    Dispatched,
    Running,
    Completed,
    Failed,

    /// <summary>Planned (skill/complexity known) but waiting for a worker capacity slot.</summary>
    Queued,

    /// <summary>Stopped by an operator, either while queued or in flight.</summary>
    Cancelled
}
