namespace AiLocal.Core.Contracts;

/// <summary>What kind of event a <see cref="HostNotice"/> is about. Drives the
/// Dashboard icon/section and the (future) sound preference.</summary>
public enum NoticeType
{
    TaskDone,
    TaskFailed,
    NeedsYou,
    WorkerDown
}

/// <summary>A server-side event the operator should see: a goal finished, a
/// goal failed, an agent is waiting on operator input, or a worker dropped
/// offline. Persisted in host-state so it survives a Host restart.</summary>
public sealed record HostNotice(
    NoticeType Type,
    string Message,
    string? RefId = null,
    DateTimeOffset At = default)
{
    public DateTimeOffset At { get; init; } = At == default ? DateTimeOffset.UtcNow : At;
}
