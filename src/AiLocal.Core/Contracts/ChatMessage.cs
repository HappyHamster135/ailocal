namespace AiLocal.Core.Contracts;

/// <summary>A single turn. Role is "user", "assistant", or "tool".
/// <paramref name="ToolCalls"/> is set on an assistant turn that invoked one
/// or more tools instead of (or alongside) answering directly. A "tool" role
/// message is the result of exactly one of those calls, echoing the same
/// <paramref name="ToolCallId"/> so the provider can match it back up - every
/// tool call in one assistant turn needs its own following tool-role message
/// before the conversation can continue. Both are null for every plain
/// single-shot chat turn, so this stays a no-op extension for the existing
/// (non-agent) call sites.</summary>
public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null);
