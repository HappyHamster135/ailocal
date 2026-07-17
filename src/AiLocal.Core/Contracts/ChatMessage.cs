namespace AiLocal.Core.Contracts;

/// <summary>A single turn. Role is "user", "assistant", or "tool".
/// <paramref name="ToolCalls"/> is set on an assistant turn that invoked one
/// or more tools instead of (or alongside) answering directly. A "tool" role
/// message is the result of exactly one of those calls, carrying both
/// <paramref name="ToolCallId"/> (Anthropic/OpenAI-style APIs match a result
/// back to its call by id) and <paramref name="ToolName"/> (Gemini has no
/// call id at all - it matches a functionResponse back to the preceding
/// functionCall by name) so every provider has what it needs regardless of
/// which convention it uses. Every tool call in one assistant turn needs its
/// own following tool-role message before the conversation can continue. All
/// three are null for every plain single-shot chat turn, so this stays a
/// no-op extension for the existing (non-agent) call sites.</summary>
public sealed record ChatMessage(
    string Role,
    string Content,
    IReadOnlyList<ToolCall>? ToolCalls = null,
    string? ToolCallId = null,
    string? ToolName = null,
    DateTimeOffset? CreatedAt = null);
