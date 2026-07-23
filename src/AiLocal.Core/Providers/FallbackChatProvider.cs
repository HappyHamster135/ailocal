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
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>v2.14: hur lång cooldown som är värd att VÄNTA UT innan kedjan
    /// ger upp. Transient/rate-limit-cooldowns är sekunder; quota (1 h) och
    /// auth (15 min) är längre än taket och ger ärligt fail direkt.</summary>
    internal static readonly TimeSpan MaxWaitPerRound = TimeSpan.FromSeconds(90);
    internal const int MaxWaitRounds = 3;

    public FallbackChatProvider(IEnumerable<IChatProvider> providers, ProviderSettings settings, Action<string>? log = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null, Func<DateTimeOffset>? clock = null)
    {
        _providers = providers.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        _settings = settings;
        _log = log;
        _delay = delay ?? Task.Delay;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    public IReadOnlyList<string> ProviderNames => BuildChain(null).Select(p => p.Name).ToList();

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        for (var round = 0; ; round++)
        {
            var errors = new List<string>();

            foreach (var provider in BuildChain(request))
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

            var summary = "all providers failed: " + string.Join(" | ", errors);

            // v2.14: VÄNTA UT korta cooldowns i stället för att fälla bygget.
            // Live (Candy Party-teamet): transient-cooldownen är 5 s, men
            // redo-rundorna och fixrundan dog OMEDELBART på "all providers
            // failed: ... cooling down" - hela bygget föll för att ingen
            // väntade fem sekunder. Långa cooldowns (quota 1 h, auth 15 min)
            // överstiger taket och ger fortfarande ärligt fail direkt.
            if (round < MaxWaitRounds && !ct.IsCancellationRequested && ShortestRecovery() is { } wait)
            {
                _log?.Invoke($"alla leverantörer i cooldown - väntar {wait.TotalSeconds:0.#}s och försöker igen ({round + 1}/{MaxWaitRounds})");
                try { await _delay(wait, ct); }
                catch (OperationCanceledException)
                {
                    _log?.Invoke(summary);
                    return ProviderResponse.Fail(ProviderOutcome.FatalError, summary);
                }
                continue;
            }

            _log?.Invoke(summary);
            return ProviderResponse.Fail(ProviderOutcome.FatalError, summary);
        }
    }

    /// <summary>Kortaste återstående cooldown bland leverantörerna när den är
    /// kort nog att vänta ut (≤ MaxWaitPerRound), med liten marginal så
    /// cooldownen verkligen hunnit löpa ut. Null = inget värt att vänta på.</summary>
    private TimeSpan? ShortestRecovery()
    {
        TimeSpan? best = null;
        var now = _clock();
        foreach (var kv in _cooldownUntil)
        {
            var remaining = kv.Value - now;
            if (remaining <= TimeSpan.Zero || remaining > MaxWaitPerRound) continue;
            if (best is null || remaining < best) best = remaining;
        }
        return best is { } b ? b + TimeSpan.FromMilliseconds(250) : null;
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
        for (var round = 0; ; round++)
        {
        var errors = new List<string>();

        foreach (var provider in BuildChain(request))
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

        var summary = "all providers failed: " + string.Join(" | ", errors);

        // v2.14: samma cooldown-väntan som CompleteAsync - säker här eftersom
        // vägen hit bara nås när INGET innehåll emitterats ännu.
        if (round < MaxWaitRounds && !ct.IsCancellationRequested && ShortestRecovery() is { } wait)
        {
            _log?.Invoke($"alla leverantörer i cooldown - väntar {wait.TotalSeconds:0.#}s och försöker igen ({round + 1}/{MaxWaitRounds})");
            var cancelled = false;
            try { await _delay(wait, ct); }
            catch (OperationCanceledException) { cancelled = true; }
            if (!cancelled) continue;
        }

        _log?.Invoke(summary);
        yield return new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.FatalError, summary));
        yield break;
        }
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

    // request is null when ProviderNames asks for the default chain (no goal in
    // hand) - fall back to the configured priority order rather than NRE.
    private IReadOnlyList<IChatProvider> BuildChain(ChatRequest? request)
    {
        var names = request?.ProviderOrder is { Count: > 0 }
            ? request.ProviderOrder
            : _settings.Priority;

        var ordered = names
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        // A preferred provider (set by the task router) goes first, then the
        // configured order as backup - so a writing task hits ChatGPT but still
        // degrades to Claude/Ollama if OpenAI is down.
        if (!string.IsNullOrWhiteSpace(request?.PreferredProvider) &&
            !ordered.Contains(request.PreferredProvider, StringComparer.OrdinalIgnoreCase))
        {
            ordered.Insert(0, request.PreferredProvider);
        }

        var chain = ordered
            .Select(n => _providers.TryGetValue(n, out var provider) ? provider : null)
            .OfType<IChatProvider>()
            .ToList();

        if (chain.Count == 0 && _providers.TryGetValue("ollama", out var local))
            chain.Add(local);

        return chain;
    }

    private bool IsCoolingDown(string name) =>
        _cooldownUntil.TryGetValue(name, out var until) && until > _clock();

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
            _cooldownUntil[provider.Name] = _clock() + cooldown;
            _log?.Invoke($"{provider.Name} cooling down {cooldown.TotalSeconds:0}s ({result.Outcome})");
        }
    }
}
