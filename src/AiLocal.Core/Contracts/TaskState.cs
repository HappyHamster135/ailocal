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
    Cancelled,

    /// <summary>Interrupted by a Host restart (the task was Running/Queued when
    /// the Host went down). Not a failure - it can be resumed, so the "company"
    /// keeps its in-flight work across reboots instead of losing it.</summary>
    Paused
}
