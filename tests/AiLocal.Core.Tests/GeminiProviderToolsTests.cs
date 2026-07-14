using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Same rationale as AnthropicProviderToolsTests: no live Gemini key
/// available, so the HTTP transport is stubbed to verify request/response
/// shape instead of making a real network call.</summary>
public class GeminiProviderToolsTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;
        public string? CapturedRequestBody { get; private set; }
        public Uri? CapturedRequestUri { get; private set; }

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            CapturedRequestUri = request.RequestUri;
            return _respond(request, CapturedRequestBody);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private const string FunctionCallResponseBody = """
        {
          "candidates": [
            {
              "content": {
                "parts": [
                  { "text": "Let me check that file." },
                  { "functionCall": { "name": "read_file", "args": { "path": "notes.txt" } } }
                ]
              }
            }
          ],
          "usageMetadata": { "promptTokenCount": 40, "candidatesTokenCount": 15 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_WithTools_SendsFunctionDeclarationsInRequestPayload()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, FunctionCallResponseBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var request = new ChatRequest
        {
            Messages = { new ChatMessage("user", "read notes.txt") },
            Tools = [new ToolDefinition("read_file", "Read a file.", """{"type":"object","properties":{"path":{"type":"string"}}}""")]
        };

        await provider.CompleteAsync(request);

        Assert.NotNull(handler.CapturedRequestBody);
        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var tools = sentBody.RootElement.GetProperty("tools");
        var declarations = tools[0].GetProperty("functionDeclarations");
        Assert.Equal(1, declarations.GetArrayLength());
        Assert.Equal("read_file", declarations[0].GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ResponseWithFunctionCall_PopulatesToolCalls()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, FunctionCallResponseBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "go") } });

        Assert.True(result.IsSuccess);
        Assert.Equal("Let me check that file.", result.Response!.Content);
        Assert.NotNull(result.Response.ToolCalls);
        var call = Assert.Single(result.Response.ToolCalls);
        Assert.Equal("read_file", call.Name);
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("notes.txt", args.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task CompleteAsync_PlainTextResponse_HasNoToolCalls()
    {
        const string plainBody = """
            {
              "candidates": [ { "content": { "parts": [ { "text": "just an answer" } ] } } ],
              "usageMetadata": { "promptTokenCount": 5, "candidatesTokenCount": 3 }
            }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Response!.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_ToolResultMessage_IsSentAsUserFunctionResponseMatchedByName()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, FunctionCallResponseBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var request = new ChatRequest
        {
            Messages =
            {
                new ChatMessage("user", "read notes.txt"),
                new ChatMessage("assistant", "Let me check.",
                    ToolCalls: [new ToolCall("gemini_call_0", "read_file", """{"path":"notes.txt"}""")]),
                new ChatMessage("tool", "file contents here", ToolCallId: "gemini_call_0", ToolName: "read_file")
            }
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var contents = sentBody.RootElement.GetProperty("contents");
        Assert.Equal(3, contents.GetArrayLength());

        var modelTurn = contents[1];
        Assert.Equal("model", modelTurn.GetProperty("role").GetString());
        var functionCallPart = modelTurn.GetProperty("parts").EnumerateArray()
            .First(p => p.TryGetProperty("functionCall", out _));
        Assert.Equal("read_file", functionCallPart.GetProperty("functionCall").GetProperty("name").GetString());

        var resultTurn = contents[2];
        Assert.Equal("user", resultTurn.GetProperty("role").GetString());
        var functionResponsePart = resultTurn.GetProperty("parts")[0].GetProperty("functionResponse");
        Assert.Equal("read_file", functionResponsePart.GetProperty("name").GetString());
        Assert.Equal("file contents here", functionResponsePart.GetProperty("response").GetProperty("result").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithoutTools_RequestHasNoToolsField()
    {
        const string plainBody = """
            { "candidates": [ { "content": { "parts": [ { "text": "hi" } ] } } ], "usageMetadata": {} }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.False(sentBody.RootElement.TryGetProperty("tools", out _));
    }

    /// <summary>Regression: see the matching test in OllamaProviderToolsTests.
    /// A Claude-shaped ModelHint used to get baked straight into the request
    /// URL ("/models/claude-sonnet-5:generateContent"), 404ing instead of
    /// resolving - breaking Gemini as a fallback the moment Anthropic itself
    /// failed over to it.</summary>
    [Fact]
    public async Task CompleteAsync_WithAnthropicModelHint_IgnoresItAndUsesOwnConfiguredModel()
    {
        const string plainBody = """
            { "candidates": [ { "content": { "parts": [ { "text": "hi" } ] } } ], "usageMetadata": {} }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new GeminiProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest
        {
            Messages = { new ChatMessage("user", "hi") },
            ModelHint = "claude-sonnet-5"
        });

        Assert.True(result.IsSuccess);
        Assert.Contains("gemini-2.5-flash", handler.CapturedRequestUri!.ToString());
        Assert.DoesNotContain("claude", handler.CapturedRequestUri!.ToString());
    }
}
