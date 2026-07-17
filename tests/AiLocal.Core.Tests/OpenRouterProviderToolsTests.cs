using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>No live OpenRouter key available, same rationale as the other
/// three providers' tool-calling tests - stubs the HTTP transport and
/// verifies request/response shape against OpenRouter's real OpenAI-format
/// API (the one place it genuinely differs from OllamaProvider's looser
/// interpretation: `arguments` is a JSON-encoded STRING, not a nested
/// object - see ParseSuccess/BuildMessages).</summary>
public class OpenRouterProviderToolsTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;
        public string? CapturedRequestBody { get; private set; }
        public HttpRequestMessage? CapturedRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequest = request;
            CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return _respond(request, CapturedRequestBody);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string ToolCallResponseBody = """
        {
          "model": "anthropic/claude-sonnet-4.5",
          "choices": [
            {
              "message": {
                "role": "assistant",
                "content": "Let me check that file.",
                "tool_calls": [
                  { "id": "call_1", "type": "function", "function": { "name": "read_file", "arguments": "{\"path\":\"notes.txt\"}" } }
                ]
              }
            }
          ],
          "usage": { "prompt_tokens": 40, "completion_tokens": 15 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_NoApiKey_FailsAuthWithoutNetworkCall()
    {
        var handler = new StubHandler((_, _) => throw new InvalidOperationException("should not be called"));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => null, new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderOutcome.AuthFailed, result.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_SendsBearerAuthHeader()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "sk-or-fake-key", new ProviderSettings());

        await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.NotNull(handler.CapturedRequest);
        Assert.Equal("Bearer", handler.CapturedRequest!.Headers.Authorization?.Scheme);
        Assert.Equal("sk-or-fake-key", handler.CapturedRequest.Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CompleteAsync_WithTools_SendsOpenAiStyleToolsInRequestPayload()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var request = new ChatRequest
        {
            Messages = { new ChatMessage("user", "read notes.txt") },
            Tools = [new ToolDefinition("read_file", "Read a file.", """{"type":"object","properties":{"path":{"type":"string"}}}""")]
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var tools = sentBody.RootElement.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("read_file", tools[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ResponseWithToolCalls_ParsesStringEncodedArguments()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "go") } });

        Assert.True(result.IsSuccess);
        Assert.Equal("Let me check that file.", result.Response!.Content);
        Assert.NotNull(result.Response.ToolCalls);
        var call = Assert.Single(result.Response.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_file", call.Name);
        // The response's function.arguments was a JSON-encoded STRING - confirm
        // it round-trips to valid JSON, not double-encoded or mangled.
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("notes.txt", args.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task CompleteAsync_PlainTextResponse_HasNoToolCalls()
    {
        const string plainBody = """
            {
              "model": "openai/gpt-4o-mini",
              "choices": [ { "message": { "role": "assistant", "content": "just an answer" } } ],
              "usage": { "prompt_tokens": 5, "completion_tokens": 3 }
            }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Response!.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_ToolResultMessage_SendsArgumentsAsStringNotObject()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var request = new ChatRequest
        {
            Messages =
            {
                new ChatMessage("user", "read notes.txt"),
                new ChatMessage("assistant", "Let me check.",
                    ToolCalls: [new ToolCall("call_1", "read_file", """{"path":"notes.txt"}""")]),
                new ChatMessage("tool", "file contents here", ToolCallId: "call_1", ToolName: "read_file")
            }
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var messages = sentBody.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        var assistantTurn = messages[1];
        var sentToolCall = assistantTurn.GetProperty("tool_calls")[0];
        // Unlike Anthropic's `input` (object) or Gemini's `args` (object),
        // real OpenAI-format `arguments` must be sent back as a raw JSON
        // string value, not a nested object.
        Assert.Equal(JsonValueKind.String, sentToolCall.GetProperty("function").GetProperty("arguments").ValueKind);

        var toolResultTurn = messages[2];
        Assert.Equal("tool", toolResultTurn.GetProperty("role").GetString());
        Assert.Equal("file contents here", toolResultTurn.GetProperty("content").GetString());
        Assert.Equal("call_1", toolResultTurn.GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task CompleteAsync_402Status_ClassifiedAsQuotaExhausted()
    {
        // OpenRouter specifically uses 402 Payment Required for "out of credits",
        // unlike the other providers - worth its own regression check.
        var handler = new StubHandler((_, _) => JsonResponse((HttpStatusCode)402, """{"error":{"message":"insufficient credits"}}"""));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.False(result.IsSuccess);
        Assert.Equal(ProviderOutcome.QuotaExhausted, result.Outcome);
    }

    [Fact]
    public async Task CompleteAsync_WithoutTools_RequestHasNoToolsField()
    {
        const string plainBody = """
            { "choices": [ { "message": { "role": "assistant", "content": "hi" } } ], "usage": {} }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.False(sentBody.RootElement.TryGetProperty("tools", out _));
    }

    /// <summary>Regression: see the matching test in OllamaProviderToolsTests.
    /// OpenRouter ids are prefixed ("anthropic/claude-sonnet-4.5"); a bare
    /// Anthropic ModelHint ("claude-sonnet-5") used to get sent as-is and
    /// 404 instead of resolving - breaking OpenRouter as a fallback the
    /// moment Anthropic itself failed over to it.</summary>
    [Fact]
    public async Task CompleteAsync_WithAnthropicModelHint_IgnoresItAndUsesOwnConfiguredModel()
    {
        const string plainBody = """
            { "choices": [ { "message": { "role": "assistant", "content": "hi" } } ], "usage": {} }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new OpenRouterProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest
        {
            Messages = { new ChatMessage("user", "hi") },
            ModelHint = "claude-sonnet-5"
        });

        Assert.True(result.IsSuccess);
        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        // Provider forwards ProviderSettings.OpenRouterModel verbatim (default is
        // now the "openrouter/auto" routing alias - no manual id required).
        Assert.Equal("openrouter/auto", sentBody.RootElement.GetProperty("model").GetString());
    }
}
