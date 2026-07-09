using System.Runtime.CompilerServices;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>One AI backend (a remote API or a local runtime).</summary>
public interface IChatProvider
{
    string Name { get; }

    /// <summary>True for on-device runtimes (Ollama). Local providers never get a cooldown.</summary>
    bool IsLocal { get; }

    Task<bool> IsAvailableAsync(CancellationToken ct = default);

    Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default);

    /// <summary>
    /// Streams incremental text, then yields exactly one terminal chunk with
    /// the full <see cref="ProviderResponse"/> (usage/model/success-or-failure).
    /// Providers without native streaming support inherit this default: call
    /// <see cref="CompleteAsync"/> once and yield the whole answer as a single
    /// delta followed by the terminal chunk.
    /// </summary>
    async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var result = await CompleteAsync(request, ct);
        if (result.IsSuccess && result.Response!.Content.Length > 0)
            yield return new StreamChunk(result.Response.Content, null);
        yield return new StreamChunk(null, result);
    }
}
