namespace AiLocal.Core.Contracts;

/// <summary>A tool a provider may call: name, human description, and a JSON
/// Schema (as raw text, provider-agnostic) describing its parameters.</summary>
public sealed record ToolDefinition(string Name, string Description, string ParametersJsonSchema);

/// <summary>A model's request to invoke one tool, with a provider-issued id
/// used to correlate the eventual result back to this specific call.</summary>
public sealed record ToolCall(string Id, string Name, string ArgumentsJson);

/// <summary>The outcome of actually running a <see cref="ToolCall"/>, fed back
/// to the model as the next turn in the conversation.</summary>
public sealed record ToolResult(string ToolCallId, string ToolName, string Output, bool IsError = false);
