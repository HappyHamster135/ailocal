namespace AiLocal.Core.Providers;

/// <summary>
/// One item from a streaming completion. Either an incremental text delta
/// (<see cref="Delta"/> set), or the terminal chunk carrying the full
/// <see cref="ProviderResponse"/> (usage/model/success-or-failure). A stream
/// always ends with exactly one <see cref="Final"/> chunk.
/// </summary>
public sealed record StreamChunk(string? Delta, ProviderResponse? Final);
