using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

public sealed class ProviderResponse
{
    public ProviderOutcome Outcome { get; init; }
    public ChatResponse? Response { get; init; }
    public string? Error { get; init; }
    public TimeSpan? RetryAfter { get; init; }

    public bool IsSuccess => Outcome == ProviderOutcome.Success && Response is not null;

    public static ProviderResponse Ok(ChatResponse response) =>
        new() { Outcome = ProviderOutcome.Success, Response = response };

    public static ProviderResponse Fail(ProviderOutcome outcome, string? error, TimeSpan? retryAfter = null) =>
        new() { Outcome = outcome, Error = error, RetryAfter = retryAfter };
}
