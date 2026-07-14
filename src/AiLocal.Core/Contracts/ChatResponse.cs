namespace AiLocal.Core.Contracts;

/// <summary>Normalized completion result, independent of which provider served it.</summary>
public sealed class ChatResponse
{
    public required string Content { get; init; }
    public required string Model { get; init; }
    public required string Provider { get; init; }
    public TokenUsage Usage { get; init; } = TokenUsage.Zero;
    public bool IsLocal { get; init; }

    /// <summary>Non-empty when the model wants to call tools instead of (or
    /// before) giving a final answer - the agent loop executes each one and
    /// feeds the results back as the next turn. Null for every plain
    /// completion, exactly as before this existed.</summary>
    public IReadOnlyList<ToolCall>? ToolCalls { get; init; }
}
