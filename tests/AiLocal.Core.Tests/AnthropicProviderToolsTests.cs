using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// AnthropicProvider talks to a real external API this test suite has no
/// credentials for, so these tests stub the HTTP transport instead - real
/// enough to catch request/response shape bugs (the tool-calling payload,
/// tool_use block extraction) without needing a live key or making a real
/// network call.
/// </summary>
public class AnthropicProviderToolsTests
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

    private const string ToolUseResponseBody = """
        {
          "model": "claude-opus-4-8",
          "stop_reason": "tool_use",
          "content": [
            { "type": "text", "text": "I'll check that file first." },
            { "type": "tool_use", "id": "call_1", "name": "read_file", "input": { "path": "notes.txt" } }
          ],
          "usage": { "input_tokens": 50, "output_tokens": 20 }
        }
        """;

    [Fact]
    public async Task CompleteAsync_WithTools_SendsToolsInRequestPayload()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolUseResponseBody));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

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
        Assert.Equal("read_file", tools[0].GetProperty("name").GetString());
        Assert.Equal(JsonValueKind.Object, tools[0].GetProperty("input_schema").ValueKind);
    }

    [Fact]
    public async Task CompleteAsync_ResponseWithToolUse_PopulatesToolCalls()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolUseResponseBody));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "go") } });

        Assert.True(result.IsSuccess);
        Assert.Equal("I'll check that file first.", result.Response!.Content);
        Assert.NotNull(result.Response.ToolCalls);
        var call = Assert.Single(result.Response.ToolCalls);
        Assert.Equal("call_1", call.Id);
        Assert.Equal("read_file", call.Name);
        using var args = JsonDocument.Parse(call.ArgumentsJson);
        Assert.Equal("notes.txt", args.RootElement.GetProperty("path").GetString());
    }

    [Fact]
    public async Task CompleteAsync_PlainTextResponse_HasNoToolCalls()
    {
        const string plainBody = """
            {
              "model": "claude-opus-4-8",
              "stop_reason": "end_turn",
              "content": [ { "type": "text", "text": "just a normal answer" } ],
              "usage": { "input_tokens": 10, "output_tokens": 5 }
            }
            """;
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, plainBody));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var result = await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        Assert.True(result.IsSuccess);
        Assert.Null(result.Response!.ToolCalls);
    }

    [Fact]
    public async Task CompleteAsync_ToolResultMessage_IsSentAsUserRoleToolResultBlock()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolUseResponseBody));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        // Simulates the agent loop's second call: the assistant called
        // read_file, the tool executed, and now the result is being fed back.
        var request = new ChatRequest
        {
            Messages =
            {
                new ChatMessage("user", "read notes.txt"),
                new ChatMessage("assistant", "I'll check that file.",
                    ToolCalls: [new ToolCall("call_1", "read_file", """{"path":"notes.txt"}""")]),
                new ChatMessage("tool", "file contents here", ToolCallId: "call_1")
            }
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var messages = sentBody.RootElement.GetProperty("messages");
        Assert.Equal(3, messages.GetArrayLength());

        // The assistant turn's content must be a block array containing both
        // the text and the tool_use block - not a plain string.
        var assistantTurn = messages[1];
        Assert.Equal("assistant", assistantTurn.GetProperty("role").GetString());
        Assert.Equal(JsonValueKind.Array, assistantTurn.GetProperty("content").ValueKind);
        var toolUseBlock = assistantTurn.GetProperty("content").EnumerateArray()
            .First(b => b.GetProperty("type").GetString() == "tool_use");
        Assert.Equal("call_1", toolUseBlock.GetProperty("id").GetString());

        // Anthropic has no "tool" role - the result must arrive as a user
        // message with a tool_result block referencing the same call id.
        var toolResultTurn = messages[2];
        Assert.Equal("user", toolResultTurn.GetProperty("role").GetString());
        var toolResultBlock = toolResultTurn.GetProperty("content").EnumerateArray().First();
        Assert.Equal("tool_result", toolResultBlock.GetProperty("type").GetString());
        Assert.Equal("call_1", toolResultBlock.GetProperty("tool_use_id").GetString());
        Assert.Equal("file contents here", toolResultBlock.GetProperty("content").GetString());
    }

    [Fact]
    public async Task CompleteAsync_MultipleToolResultsInARow_AreBatchedIntoOneUserMessage()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, ToolUseResponseBody));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        var request = new ChatRequest
        {
            Messages =
            {
                new ChatMessage("user", "list then read two files"),
                new ChatMessage("assistant", "",
                    ToolCalls: [
                        new ToolCall("call_1", "read_file", """{"path":"a.txt"}"""),
                        new ToolCall("call_2", "read_file", """{"path":"b.txt"}""")
                    ]),
                new ChatMessage("tool", "contents of a", ToolCallId: "call_1"),
                new ChatMessage("tool", "contents of b", ToolCallId: "call_2")
            }
        };

        await provider.CompleteAsync(request);

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        var messages = sentBody.RootElement.GetProperty("messages");
        // user, assistant (2 tool_use blocks), ONE batched user message with 2 tool_result blocks
        Assert.Equal(3, messages.GetArrayLength());
        var batchedResults = messages[2];
        Assert.Equal("user", batchedResults.GetProperty("role").GetString());
        Assert.Equal(2, batchedResults.GetProperty("content").GetArrayLength());
    }

    [Fact]
    public async Task CompleteAsync_WithoutTools_RequestHasNoToolsField()
    {
        var handler = new StubHandler((_, _) => JsonResponse(HttpStatusCode.OK, """
            { "model": "claude-opus-4-8", "content": [{"type":"text","text":"hi"}], "usage": {} }
            """));
        var provider = new AnthropicProvider(new HttpClient(handler), () => "fake-key", new ProviderSettings());

        await provider.CompleteAsync(new ChatRequest { Messages = { new ChatMessage("user", "hi") } });

        using var sentBody = JsonDocument.Parse(handler.CapturedRequestBody!);
        Assert.False(sentBody.RootElement.TryGetProperty("tools", out _));
    }
}
