namespace AiLocal.Core.Contracts;

/// <summary>A single turn. Role is "user" or "assistant".</summary>
public sealed record ChatMessage(string Role, string Content);
