namespace AiLocal.Core.Contracts;

/// <summary>Normalized completion result, independent of which provider served it.</summary>
public sealed class ChatResponse
{
    public required string Content { get; init; }
    public required string Model { get; init; }
    public required string Provider { get; init; }
    public TokenUsage Usage { get; init; } = TokenUsage.Zero;
    public bool IsLocal { get; init; }
}
