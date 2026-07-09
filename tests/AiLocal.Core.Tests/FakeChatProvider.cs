using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Core.Tests;

/// <summary>Scriptable <see cref="IChatProvider"/> test double: returns a fixed
/// response (or throws) and counts how many times it was called.</summary>
internal sealed class FakeChatProvider : IChatProvider
{
    private readonly Func<ChatRequest, ProviderResponse> _respond;

    public FakeChatProvider(string name, bool isLocal, Func<ChatRequest, ProviderResponse> respond)
    {
        Name = name;
        IsLocal = isLocal;
        _respond = respond;
    }

    public static FakeChatProvider Success(string name, string content, bool isLocal = false) =>
        new(name, isLocal, _ => ProviderResponse.Ok(new ChatResponse
        {
            Content = content,
            Model = $"{name}-model",
            Provider = name,
            IsLocal = isLocal
        }));

    public static FakeChatProvider Failing(string name, ProviderOutcome outcome, bool isLocal = false) =>
        new(name, isLocal, _ => ProviderResponse.Fail(outcome, $"{name} failed"));

    public string Name { get; }
    public bool IsLocal { get; }
    public int CallCount { get; private set; }

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) => Task.FromResult(true);

    public Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        CallCount++;
        return Task.FromResult(_respond(request));
    }
}
