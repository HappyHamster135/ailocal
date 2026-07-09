namespace AiLocal.Core.Roles;

/// <summary>The three roles a node can take when the executable starts.</summary>
public enum NodeRole
{
    /// <summary>First-run browser launcher with role selection buttons.</summary>
    Launcher,

    /// <summary>Orchestrates the cluster: registers workers and delegates tasks.</summary>
    Host,

    /// <summary>Executes AI work via the local/remote provider chain.</summary>
    Worker,

    /// <summary>Monitors the cluster and is where the operator submits goals.</summary>
    Overseer
}
