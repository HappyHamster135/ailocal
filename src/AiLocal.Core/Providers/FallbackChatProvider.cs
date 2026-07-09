using System.Collections.Concurrent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>
/// Tries providers in priority order. When one reports credit exhaustion, auth
/// failure, or a rate limit, it is put in a cooldown and the next provider is
/// used. Local providers (Ollama) never cool down, so the cluster degrades
/// gracefully to on-device inference instead of failing.
/// </summary>
public sealed class FallbackChatProvider
{
    private readonly IReadOnlyDictionary<string, IChatProvider> _providers;
    private readonly ProviderSettings _settings;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _cooldownUntil = new();
    private readonly Action<string>? _log;

    public FallbackChatProvider(IEnumerable<IChatProvider> providers, ProviderSettings settings, Action<string>? log = null)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _settings = settings;
        _log = log;
    }

    public IReadOnlyList<string> ProviderNames => BuildChain(null).Select(p => p.Name).ToList();

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var errors = new List<string>();

        foreach (var provider in BuildChain(request.ProviderOrder))
        {
            if (IsCoolingDown(provider.Name))
            {
                errors.Add($"{provider.Name}: cooling down");
                continue;
            }

            ProviderResponse result;
            try
            {
                result = await provider.CompleteAsync(request, ct);
            }
            catch (Exception ex)
            {
                result = ProviderResponse.Fail(ProviderOutcome.TransientError, ex.Message);
            }

            if (result.IsSuccess)
            {
                _log?.Invoke($"served by {provider.Name} ({result.Response!.Model})");
                return result;
            }

            errors.Add($"{provider.Name}: {result.Outcome} - {result.Error}");
            ApplyCooldown(provider, result);
        }

        return ProviderResponse.Fail(
            ProviderOutcome.FatalError,
            "all providers failed: " + string.Join(" | ", errors));
    }

    /// <summary>
    /// Same provider chain and cooldown behavior as <see cref="CompleteAsync"/>,
    /// but streams incremental text as it arrives. Falls back to the next
    /// provider only if a provider fails before emitting any text (a mid-stream
    /// failure, after the caller has already seen partial output, surfaces as
    /// the terminal error instead of silently switching providers).
    /// </summary>
    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        var errors = new List<string>();

        foreach (var provider in BuildChain(request.ProviderOrder))
        {
            if (IsCoolingDown(provider.Name))
            {
                errors.Add($"{provider.Name}: cooling down");
                continue;
            }

            bool available;
            try { available = await provider.IsAvailableAsync(ct); }
            catch { available = false; }
            if (!available)
            {
                errors.Add($"{provider.Name}: not available");
                continue;
            }

            var emittedAny = false;
            var fellBack = false;
            await foreach (var chunk in SafeStream(provider, request, ct))
            {
                if (chunk.Delta is not null)
                {
                    emittedAny = true;
                    yield return chunk;
                    continue;
                }

                if (chunk.Final is not { } final) continue;

                if (final.IsSuccess)
                {
                    _log?.Invoke($"served by {provider.Name} ({final.Response!.Model}) [stream]");
                    yield return chunk;
                    yield break;
                }

                if (!emittedAny)
                {
                    errors.Add($"{provider.Name}: {final.Outcome} - {final.Error}");
                    ApplyCooldown(provider, final);
                    fellBack = true;
                    break;
                }

                // Already streamed partial content to the caller - commit to
                // surfacing this as the end of the stream rather than jumping
                // to a different provider mid-answer.
                yield return chunk;
                yield break;
            }

            if (!fellBack)
                yield break;
        }

        yield return new StreamChunk(null, ProviderResponse.Fail(
            ProviderOutcome.FatalError,
            "all providers failed: " + string.Join(" | ", errors)));
    }

    private static async IAsyncEnumerable<StreamChunk> SafeStream(
        IChatProvider provider,
        ChatRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var enumerator = provider.StreamAsync(request, ct).GetAsyncEnumerator(ct);
        try
        {
            while (true)
            {
                StreamChunk current;
                var hasNextResult = await TryMoveNextAsync(enumerator);
                if (hasNextResult.Failure is { } failure)
                {
                    yield return new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.TransientError, failure.Message));
                    yield break;
                }
                if (!hasNextResult.HasNext) yield break;
                current = enumerator.Current;
                yield return current;
            }
        }
        finally
        {
            await enumerator.DisposeAsync();
        }
    }

    private static async Task<(bool HasNext, Exception? Failure)> TryMoveNextAsync(IAsyncEnumerator<StreamChunk> enumerator)
    {
        try
        {
            return (await enumerator.MoveNextAsync(), null);
        }
        catch (Exception ex)
        {
            return (false, ex);
        }
    }

    private IReadOnlyList<IChatProvider> BuildChain(IReadOnlyCollection<string>? overrideOrder)
    {
        var names = overrideOrder is { Count: > 0 }
            ? overrideOrder
            : _settings.Priority;

        var chain = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(n => _providers.TryGetValue(n, out var provider) ? provider : null)
            .OfType<IChatProvider>()
            .ToList();

        if (chain.Count == 0 && _providers.TryGetValue("ollama", out var local))
            chain.Add(local);

        return chain;
    }

    private bool IsCoolingDown(string name) =>
        _cooldownUntil.TryGetValue(name, out var until) && until > DateTimeOffset.UtcNow;

    private void ApplyCooldown(IChatProvider provider, ProviderResponse result)
    {
        if (provider.IsLocal) return; // local runtime is always the safety net

        var cooldown = result.Outcome switch
        {
            ProviderOutcome.QuotaExhausted => TimeSpan.FromHours(1),
            ProviderOutcome.AuthFailed => TimeSpan.FromMinutes(15),
            ProviderOutcome.RateLimited => result.RetryAfter ?? TimeSpan.FromSeconds(30),
            ProviderOutcome.Overloaded => result.RetryAfter ?? TimeSpan.FromSeconds(15),
            ProviderOutcome.TransientError => TimeSpan.FromSeconds(5),
            _ => TimeSpan.Zero
        };

        if (cooldown > TimeSpan.Zero)
        {
            _cooldownUntil[provider.Name] = DateTimeOffset.UtcNow + cooldown;
            _log?.Invoke($"{provider.Name} cooling down {cooldown.TotalSeconds:0}s ({result.Outcome})");
        }
    }
}
