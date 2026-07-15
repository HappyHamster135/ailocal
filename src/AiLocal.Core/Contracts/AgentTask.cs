namespace AiLocal.Core.Contracts;

/// <summary>A unit of work the Host tracks and delegates to a Worker.</summary>
public sealed class AgentTask
{
    public required string Id { get; init; }
    public required string Prompt { get; init; }
    public string? System { get; init; }
    public string? Title { get; init; }
    public string? ParentId { get; init; }

    public TaskState State { get; set; } = TaskState.Pending;
    public string? AssignedWorkerId { get; set; }

    /// <summary>Display name of the worker the Host delegated this task to.</summary>
    public string? WorkerName { get; set; }

    /// <summary>Capability tier of the assigned worker (Strong/Medium/Light).</summary>
    public string? WorkerTier { get; set; }

    /// <summary>Estimated difficulty 1-5 used when matching tasks to workers.</summary>
    public int? Complexity { get; set; }

    /// <summary>Skill requested by the planner, such as coding or research.</summary>
    public string? RequiredSkill { get; set; }

    /// <summary>Human-readable explanation of why this Worker was selected.</summary>
    public string? AssignmentReason { get; set; }

    /// <summary>Worker capability score at assignment time.</summary>
    public double? WorkerCapability { get; set; }

    public string? Result { get; set; }
    public string? Error { get; set; }
    public string? Provider { get; set; }
    public string? Model { get; set; }
    public bool? IsLocal { get; set; }
    public TokenUsage Usage { get; set; } = TokenUsage.Zero;

    /// <summary>Estimated USD cost from ModelCatalog, null for unknown/local models.</summary>
    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>How many times this task has been automatically retried after a failure.</summary>
    public int RetryCount { get; set; }

    /// <summary>How many times the Host has escalated this task to a stronger
    /// model after repeated failures on a cheaper one (see DispatchWithRetryAsync).
    /// Each escalation bumps Complexity by one, up to 5.</summary>
    public int EscalationCount { get; set; }

    /// <summary>Complexity this task was planned with. Escalation raises
    /// <see cref="AgentTask.Complexity"/> above this; keeping the original lets
    /// us detect/cap how far we've escalated.</summary>
    public int? OriginalComplexity { get; set; }

    /// <summary>Parallelism requested for this goal (how many workers to fan
    /// out across). Stored so an interrupted goal can be resumed with the same
    /// fan-out it was originally given.</summary>
    public int? Parallelism { get; set; }

    /// <summary>The role that owns this task (architect/developer/tester/reviewer),
    /// if assigned. Drives the system prompt and model bias. Null for
    /// non-role work (e.g. plain chat).</summary>
    public string? RoleId { get; set; }

    /// <summary>Shared notes board for the goal this task belongs to. The Host
    /// appends each worker's hand-off context here so "employees" inherit
    /// sibling context instead of just a short summary. Injected into every
    /// child's system prompt at dispatch time.</summary>
    public string? Notes { get; set; }

    /// <summary>Prior conversation turns to prepend before Prompt when dispatching
    /// (only set for a chat-originated, single-worker goal).</summary>
    public List<ChatMessage>? ContextMessages { get; set; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAt { get; set; }
}
