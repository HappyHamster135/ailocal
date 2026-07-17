using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Hardware;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>No Ollama instance running in this environment (confirmed:
/// localhost:11434 refuses connections here), so the HTTP transport is
/// stubbed instead - verifies request/response shape against Ollama's
/// documented OpenAI-compatible /api/chat tool-calling format.</summary>
public class OllamaProviderToolsTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, string?, HttpResponseMessage> _respond;
        public string? CapturedRequestBody { get; private set; }

        public StubHandler(Func<HttpRequestMessage, string?, HttpResponseMessage> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            CapturedRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return _respond(request, CapturedRequestBody);
        }
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    private static OllamaProvider MakeProvider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), new ProviderSettings(),
            new LocalModelRecommendation("llama3.1:8b", "Llama 3.1 8B", 8, "test"));

    private const string ToolCallResponseBody = """
        {
          "message": {
            "role": "assistant",
            "content": "",
            "tool_calls": [
              { "function": { "name": "read_file", "arguments": { "path": "notes.txt" } } }
            ]
          },
          "prompt_eval_count": 30,
          "eval_count": 10
        }
        """;

    [Fact]
    public async Task CompleteAsync_WithTools_SendsToolsInRequestPayload()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = MakeProvider(handler);

        var request = new ChatRequest
        {
            Messages = { new ChatMessage("user", "read notes.txt") },
            Tools = [new ToolDefinition("read_file", "Read a file.", """{"type":"object","properties":{"path":{"type":"string"}}}""")]
        };

        await provider.CompleteAsync(request);

        Assert.NotNull(handler.CapturedRequestBody);
        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var tools = sentBody.RootElement.GetProperty("tools");
        Assert.Equal(1, tools.GetArrayLength());
        Assert.Equal("function", tools[0].GetProperty("type").GetString());
        Assert.Equal("read_file", tools[0].GetProperty("function").GetProperty("name").GetString());
    }

    [Fact]
    public async Task CompleteAsync_ResponseWithToolCalls_PopulatesToolCalls()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = MakeProvider(handler);

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "go") } });

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Response!.ToolCalls);
        var call = Assert.Single(result.Response.ToolCalls);
        Assert.Equal("read_file", call.Name);
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("notes.txt", args.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task CompleteAsync_PlainTextResponse_HasNoToolCalls()
    {
        const string plainBody = """
            { "message": { "role": "assistant", "content": "just an answer" }, "prompt_eval_count": 5, "eval_count": 3 }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = MakeProvider(handler);

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Response!.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_ToolResultMessage_IsSentAsToolRoleMessage()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolCallResponseBody));
        var provider = MakeProvider(handler);

        var request = new ChatRequest
        {
            Messages =
            {
                new ChatMessage("user", "read notes.txt"),
                new ChatMessage("assistant", "",
                    ToolCalls: [new ToolCall("call_1", "read_file", """{"path":"notes.txt"}""")]),
                new ChatMessage("tool", "file contents here", ToolCallId: "call_1", ToolName: "read_file")
            }
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var messages = sentBody.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        var assistantTurn = messages[1];
        Assert.Equal("assistant", assistantTurn.GetProperty("role").GetString());
        var toolCallsSent = assistantTurn.GetProperty("tool_calls");
        Assert.Equal(1, toolCallsSent.GetArrayLength());
        Assert.Equal("read_file", toolCallsSent[0].GetProperty("function").GetProperty("name").GetString());

        var toolResultTurn = messages[2];
        Assert.Equal("tool", toolResultTurn.GetProperty("role").GetString());
        Assert.Equal("file contents here", toolResultTurn.GetProperty("content").GetString());
        Assert.Equal("call_1", toolResultTurn.GetProperty("tool_call_id").GetString());
    }

    [Fact]
    public async Task CompleteAsync_WithoutTools_RequestHasNoToolsField()
    {
        const string plainBody = """
            { "message": { "role": "assistant", "content": "hi" }, "prompt_eval_count": 1, "eval_count": 1 }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = MakeProvider(handler);

        await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.False(sentBody.RootElement.TryGetProperty("tools", out _));
    }

    /// <summary>Regression: the dashboard's model dropdown and the Host's
    /// per-complexity ModelTiers both only ever produce Anthropic ids
    /// ("claude-sonnet-5"). Ollama used to take that at face value and send it
    /// straight to /api/chat, which 404'd - breaking the fallback chain the
    /// moment Anthropic itself failed over to Ollama, exactly the scenario
    /// FallbackChatProvider exists to handle gracefully.</summary>
    [Fact]
    public async Task CompleteAsync_WithAnthropicModelHint_IgnoresItAndUsesOwnConfiguredModel()
    {
        const string plainBody = """
            { "message": { "role": "assistant", "content": "hi" }, "prompt_eval_count": 1, "eval_count": 1 }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = MakeProvider(handler);

        var result = await provider.CompleteAsync(new ChatRequest
        {
            Messages = { new ChatMessage("user", "hi") },
            ModelHint = "claude-sonnet-5"
        });

        Assert.True(result.IsSuccess);
        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.Equal("llama3.1:8b", sentBody.RootElement.GetProperty("model").GetString());
    }

    [Fact]
    public async Task StreamAsync_WithAnthropicModelHint_IgnoresItAndUsesOwnConfiguredModel()
    {
        const string ndjsonLine = """{"message":{"role":"assistant","content":"hi"},"done":true,"prompt_eval_count":1,"eval_count":1}""";
        var handler = new StubHandler((_, _) => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(ndjsonLine + "\n", Encoding.UTF8, "application/json")
        });
        var provider = MakeProvider(handler);

        await foreach (var _ in provider.StreamAsync(new ChatRequest
        {
            Messages = { new ChatMessage("user", "hi") },
            ModelHint = "claude-sonnet-5"
        })) { }

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.Equal("llama3.1:8b", sentBody.RootElement.GetProperty("model").GetString());
    }

    /// <summary>Regression for the real-world failure: a misconfigured
    /// OllamaModel set to a cloud id (e.g. "claude-haiku-4-5" - exactly what
    /// showed up as "ollama 404: model 'claude-haiku-4-5' not found" in a
    /// user's run) must NOT be sent to Ollama. It is not a valid Ollama tag,
    /// so the provider falls back to the hardware-recommended tag instead of
    /// 404ing and breaking the whole fallback chain.</summary>
    [Fact]
    public async Task CompleteAsync_MisconfiguredCloudOllamaModel_FallsBackToRecommendedTag()
    {
        const string plainBody = """
            { "message": { "role": "assistant", "content": "hi" }, "prompt_eval_count": 1, "eval_count": 1 }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var settings = new ProviderSettings { OllamaModel = "claude-haiku-4-5" };
        var provider = new OllamaProvider(new HttpClient(handler), settings,
            new LocalModelRecommendation("llama3.1:8b", "Llama 3.1 8B", 8, "test"));

        var result = await provider.CompleteAsync(new ChatRequest
        {
            Messages = { new ChatMessage("user", "hi") }
        });

        Assert.True(result.IsSuccess);
        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.Equal("llama3.1:8b", sentBody.RootElement.GetProperty("model").GetString());
    }
}
