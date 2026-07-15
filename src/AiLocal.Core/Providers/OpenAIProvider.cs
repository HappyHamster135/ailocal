using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>
/// Calls the OpenAI Chat Completions API directly (no SDK) - i.e. ChatGPT -
/// and maps HTTP status + error body onto a <see cref="ProviderOutcome"/> so
/// the fallback chain can react to rate limits and quota exhaustion. Mirrors
/// <see cref="AnthropicProvider"/>'s shape (non-streaming CompleteAsync is
/// fully implemented; StreamAsync inherits the interface default, which
/// buffers the whole answer - fine for chat use).
/// </summary>
public sealed class OpenAIProvider : IChatProvider
{
    private const string Endpoint = "https://api.openai.com/v1/chat/completions";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly ProviderSettings _settings;

    public OpenAIProvider(HttpClient http, Func<string?> apiKey, ProviderSettings settings)
    {
        _http = http;
        _apiKey = apiKey;
        _settings = settings;
    }

    public string Name => "openai";
    public bool IsLocal => false;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey()));

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _apiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResponse.Fail(ProviderOutcome.AuthFailed, "OPENAI_API_KEY not set");

        var model = request.ModelHint ?? _settings.OpenAIModel ?? "gpt-4o";

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens ?? _settings.MaxTokens,
            ["messages"] = BuildMessages(request.Messages)
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["messages"] = PrependSystem(request.System, request.Messages);
        if (request.Tools is { Count: > 0 } tools)
            payload["tools"] = tools.Select(t => new
            {
                type = "function",
                function = new
                {
                    name = t.Name,
                    description = t.Description,
                    parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
                }
            }).ToArray();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await _http.SendAsync(httpRequest, ct);
        }
        catch (Exception ex)
        {
            return ProviderResponse.Fail(ProviderOutcome.TransientError, $"network: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (response.IsSuccessStatusCode)
            return ParseSuccess(body, model);

        return ProviderResponse.Fail(Classify(response.StatusCode, body), Summarize(body));
    }

    private static ProviderResponse ParseSuccess(string body, string requestedModel)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var content = new StringBuilder();
            List<ToolCall>? toolCalls = null;
            string? finish = root.TryGetProperty("choices", out var choices) &&
                             choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0
                ? (choices[0].TryGetProperty("finish_reason", out var fr) ? fr.GetString() : null)
                : null;

            if (choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0)
            {
                var msg = choices[0].TryGetProperty("message", out var m) ? m : default;
                if (msg.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String)
                    content.Append(c.GetString());
                if (msg.TryGetProperty("tool_calls", out var tcs) && tcs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var tc in tcs.EnumerateArray())
                    {
                        var id = tc.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                        var fn = tc.TryGetProperty("function", out var fnEl) ? fnEl : default;
                        var name = fn.TryGetProperty("name", out var nEl) ? nEl.GetString() : null;
                        var args = fn.TryGetProperty("arguments", out var aEl) && aEl.ValueKind == JsonValueKind.String
                            ? aEl.GetString()! : "{}";
                        if (id is not null && name is not null)
                            (toolCalls ??= []).Add(new ToolCall(id, name, args));
                    }
                }
            }

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var it)) inTok = it.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ot)) outTok = ot.GetInt32();
            }

            var model = root.TryGetProperty("model", out var mdl) ? mdl.GetString() ?? requestedModel : requestedModel;

            if (finish == "content_filter")
                return ProviderResponse.Fail(ProviderOutcome.FatalError, "response blocked by OpenAI content filter");

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = content.ToString(),
                Model = model,
                Provider = "openai",
                Usage = new TokenUsage(inTok, outTok),
                IsLocal = false,
                ToolCalls = toolCalls
            });
        }
        catch (Exception ex)
        {
            return ProviderResponse.Fail(ProviderOutcome.FatalError, $"parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// OpenAI wants the system prompt as its own message at the front of the
    /// array (unlike Anthropic's separate top-level field). Rebuilds the
    /// message list with a synthetic system message prepended.
    /// </summary>
    private static object[] PrependSystem(string system, List<ChatMessage> messages)
    {
        var list = new List<object> { new { role = "system", content = system } };
        list.AddRange(BuildMessages(messages));
        return list.ToArray();
    }

    private static object[] BuildMessages(List<ChatMessage> messages)
    {
        var result = new List<object>();
        foreach (var m in messages)
        {
            // OpenAI has no tool role - a tool result becomes a user message
            // whose content is the tool output (matches our single-result shape).
            var role = string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase)
                ? "user"
                : m.Role;
            result.Add(new { role, content = m.Content });
        }
        return result.ToArray();
    }

    private static ProviderOutcome Classify(HttpStatusCode code, string body) => (int)code switch
    {
        429 => ProviderOutcome.RateLimited,
        401 => ProviderOutcome.AuthFailed,
        403 => LooksLikeBilling(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.AuthFailed,
        400 => LooksLikeBilling(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.FatalError,
        >= 500 => ProviderOutcome.TransientError,
        _ => ProviderOutcome.FatalError
    };

    private static bool LooksLikeBilling(string body)
    {
        var b = body.ToLowerInvariant();
        return b.Contains("quota") || b.Contains("billing") || b.Contains("exceeded")
            || b.Contains("insufficient") || b.Contains("balance") || b.Contains("rate limit");
    }

    private static string Summarize(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e)
                && e.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "unknown error";
        }
        catch { /* not JSON */ }
        return body.Length > 200 ? body[..200] : body;
    }
}
