namespace AiLocal.Core.Providers;

/// <summary>
/// Classification of a provider call result. Drives the fallback chain:
/// QuotaExhausted/AuthFailed put the provider in a long cooldown (credits ran out),
/// RateLimited/Overloaded a short one, and the chain moves to the next provider.
/// </summary>
public enum ProviderOutcome
{
    Success,
    RateLimited,      // 429 - temporary, respect Retry-After
    QuotaExhausted,   // credits/billing/quota exhausted - long cooldown
    AuthFailed,       // missing/invalid key
    Overloaded,       // 529 - provider busy
    TransientError,   // 5xx / network - retryable soon
    FatalError        // 400 bad request, refusal, etc. - do not retry
}
