using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

public class FallbackChatProviderTests
{
    private static ProviderSettings SettingsFor(params string[] priority) =>
        new() { Priority = [.. priority] };

    private static ChatRequest Request(params string[]? providerOrder) => new()
    {
        Messages = [new ChatMessage("user", "hello")],
        ProviderOrder = providerOrder?.Length > 0 ? [.. providerOrder] : null
    };

    [Fact]
    public async Task CompleteAsync_FirstProviderSucceeds_DoesNotTryOthers()
    {
        var first = FakeChatProvider.Success("anthropic", "hi from claude");
        var second = FakeChatProvider.Success("ollama", "hi from local", isLocal: true);
        var fallback = new FallbackChatProvider([first, second], SettingsFor("anthropic", "ollama"));

        var result = await fallback.CompleteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("anthropic", result.Response!.Provider);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(0, second.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_FirstProviderFails_FallsBackToNextProvider()
    {
        var first = FakeChatProvider.Failing("anthropic", ProviderOutcome.TransientError);
        var second = FakeChatProvider.Success("ollama", "hi from local", isLocal: true);
        var fallback = new FallbackChatProvider([first, second], SettingsFor("anthropic", "ollama"));

        var result = await fallback.CompleteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("ollama", result.Response!.Provider);
        Assert.Equal(1, first.CallCount);
        Assert.Equal(1, second.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_QuotaExhausted_CooldownSkipsProviderOnNextCall()
    {
        var flaky = FakeChatProvider.Failing("anthropic", ProviderOutcome.QuotaExhausted);
        var local = FakeChatProvider.Success("ollama", "local reply", isLocal: true);
        var fallback = new FallbackChatProvider([flaky, local], SettingsFor("anthropic", "ollama"));

        var firstCall = await fallback.CompleteAsync(Request());
        Assert.True(firstCall.IsSuccess);
        Assert.Equal(1, flaky.CallCount);

        // Same instant (well inside the 1h QuotaExhausted cooldown) - the
        // cooling-down provider must be skipped entirely, not called and failed again.
        var secondCall = await fallback.CompleteAsync(Request());

        Assert.True(secondCall.IsSuccess);
        Assert.Equal("ollama", secondCall.Response!.Provider);
        Assert.Equal(1, flaky.CallCount); // still 1 - never retried while cooling down
    }

    [Fact]
    public async Task CompleteAsync_LocalProviderNeverCoolsDown()
    {
        var localFlaky = FakeChatProvider.Failing("ollama", ProviderOutcome.TransientError, isLocal: true);
        var fallback = new FallbackChatProvider([localFlaky], SettingsFor("ollama"));

        await fallback.CompleteAsync(Request());
        await fallback.CompleteAsync(Request());

        // A local provider is the safety net and must always be retried, even
        // after failing, so the cluster never gets stuck with zero usable providers.
        Assert.Equal(2, localFlaky.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_RateLimited_RespectsRetryAfterOverride()
    {
        var limited = new FakeChatProvider("anthropic", false, _ =>
            ProviderResponse.Fail(ProviderOutcome.RateLimited, "429", TimeSpan.FromMinutes(10)));
        var local = FakeChatProvider.Success("ollama", "local reply", isLocal: true);
        var fallback = new FallbackChatProvider([limited, local], SettingsFor("anthropic", "ollama"));

        await fallback.CompleteAsync(Request());
        var secondCall = await fallback.CompleteAsync(Request());

        Assert.True(secondCall.IsSuccess);
        Assert.Equal(1, limited.CallCount); // still cooling down from the 10-minute Retry-After
    }

    [Fact]
    public async Task CompleteAsync_AllProvidersFail_ReturnsFatalErrorWithAggregatedReasons()
    {
        var first = FakeChatProvider.Failing("anthropic", ProviderOutcome.AuthFailed);
        var second = FakeChatProvider.Failing("gemini", ProviderOutcome.FatalError);
        var fallback = new FallbackChatProvider([first, second], SettingsFor("anthropic", "gemini"));

        var result = await fallback.CompleteAsync(Request());

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderOutcome.FatalError, result.Outcome);
        Assert.Contains("anthropic", result.Error);
        Assert.Contains("gemini", result.Error);
    }

    [Fact]
    public async Task CompleteAsync_PerRequestProviderOrder_OverridesSettingsPriority()
    {
        var anthropic = FakeChatProvider.Success("anthropic", "claude reply");
        var ollama = FakeChatProvider.Success("ollama", "local reply", isLocal: true);
        // Settings say anthropic first, but this one request asks for ollama first.
        var fallback = new FallbackChatProvider([anthropic, ollama], SettingsFor("anthropic", "ollama"));

        var result = await fallback.CompleteAsync(Request("ollama", "anthropic"));

        Assert.Equal("ollama", result.Response!.Provider);
        Assert.Equal(0, anthropic.CallCount);
    }

    [Fact]
    public async Task CompleteAsync_UnknownProviderInChain_IsSkippedWithoutError()
    {
        var local = FakeChatProvider.Success("ollama", "local reply", isLocal: true);
        var fallback = new FallbackChatProvider([local], SettingsFor("anthropic", "ollama"));

        var result = await fallback.CompleteAsync(Request());

        Assert.True(result.IsSuccess);
        Assert.Equal("ollama", result.Response!.Provider);
    }
}
