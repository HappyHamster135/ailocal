using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>
/// Google Gemini via the Generative Language API. Minimal but real: enough to
/// prove the multi-provider abstraction. Model id is config-driven because
/// Gemini's ids move faster than this snapshot - set Providers:GeminiModel.
/// </summary>
public sealed class GeminiProvider : IChatProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly ProviderSettings _settings;

    public GeminiProvider(HttpClient http, Func<string?> apiKey, ProviderSettings settings)
    {
        _http = http;
        _apiKey = apiKey;
        _settings = settings;
    }

    public string Name => "gemini";
    public bool IsLocal => false;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey()));

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _apiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResponse.Fail(ProviderOutcome.AuthFailed, "GEMINI_API_KEY not set");

        // ModelHint is never honored here - every current source of it (the
        // dashboard's model dropdown, the per-complexity ModelTiers default)
        // only ever produces Anthropic model ids. Blindly forwarding one of
        // those to Gemini instead of this Worker's own configured model used
        // to break the very fallback chain it's supposed to be part of: the
        // moment Anthropic failed over to Gemini, Gemini would get asked for
        // a nonexistent "claude-..." model and fail too.
        var model = _settings.GeminiModel;

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = BuildContents(request.Messages),
            ["generationConfig"] = new { maxOutputTokens = request.MaxTokens ?? _settings.MaxTokens }
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["systemInstruction"] = new { parts = new[] { new { text = request.System } } };
        if (request.Tools is { Count: > 0 } tools)
            payload["tools"] = new[]
            {
                new
                {
                    functionDeclarations = tools.Select(t => new
                    {
                        name = t.Name,
                        description = t.Description,
                        parameters = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
                    }).ToArray()
                }
            };

        var url = $"{BaseUrl}/{model}:generateContent?key={apiKey}";
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

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

    /// <summary>
    /// Gemini has no dedicated "tool" role - a function's result travels back
    /// as a "user" turn containing a functionResponse part, matched to the
    /// preceding functionCall by NAME (Gemini has no call id the way
    /// Anthropic/OpenAI-style APIs do - see ParseSuccess's synthetic ids,
    /// which exist only to satisfy this app's own ToolCall shape and are
    /// never sent back here). Consecutive ChatMessage(Role: "tool", ...)
    /// entries from one round are batched into a single user turn, one
    /// functionResponse part each - same reasoning as AnthropicProvider's
    /// BuildMessages.
    /// </summary>
    private static object[] BuildContents(List<ChatMessage> messages)
    {
        var result = new List<object>();
        var i = 0;
        while (i < messages.Count)
        {
            var m = messages[i];
            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var responses = new List<object>();
                while (i < messages.Count && string.Equals(messages[i].Role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    responses.Add(new
                    {
                        functionResponse = new
                        {
                            name = messages[i].ToolName ?? "unknown_tool",
                            response = new { result = messages[i].Content }
                        }
                    });
                    i++;
                }
                result.Add(new { role = "user", parts = responses.ToArray() });
                continue;
            }

            if (m.ToolCalls is { Count: > 0 } calls)
            {
                var parts = new List<object>();
                if (!string.IsNullOrEmpty(m.Content))
                    parts.Add(new { text = m.Content });
                foreach (var call in calls)
                    parts.Add(new
                    {
                        functionCall = new
                        {
                            name = call.Name,
                            args = JsonSerializer.Deserialize<JsonElement>(
                                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson)
                        }
                    });
                result.Add(new { role = "model", parts = parts.ToArray() });
                i++;
                continue;
            }

            result.Add(new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            });
            i++;
        }
        return result.ToArray();
    }

    private static ProviderResponse ParseSuccess(string body, string requestedModel)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var text = new StringBuilder();
            List<ToolCall>? toolCalls = null;
            if (root.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array)
                {
                    var callIndex = 0;
                    foreach (var part in parts.EnumerateArray())
                    {
                        if (part.TryGetProperty("text", out var t))
                            text.Append(t.GetString());
                        else if (part.TryGetProperty("functionCall", out var fc) &&
                            fc.TryGetProperty("name", out var nameEl))
                        {
                            var argsJson = fc.TryGetProperty("args", out var argsEl) ? argsEl.GetRawText() : "{}";
                            // Gemini doesn't issue a call id the way Anthropic/OpenAI-style
                            // APIs do - functionResponse is matched back to a call by name
                            // alone, so a synthetic id is only needed to satisfy this app's
                            // provider-agnostic ToolCall shape; it's never sent back to Gemini.
                            (toolCalls ??= []).Add(new ToolCall($"gemini_call_{callIndex++}", nameEl.GetString()!, argsJson));
                        }
                    }
                }
            }

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usageMetadata", out var usage))
            {
                if (usage.TryGetProperty("promptTokenCount", out var it)) inTok = it.GetInt32();
                if (usage.TryGetProperty("candidatesTokenCount", out var ot)) outTok = ot.GetInt32();
            }

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = text.ToString(),
                Model = requestedModel,
                Provider = "gemini",
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

    private static ProviderOutcome Classify(HttpStatusCode code, string body) => (int)code switch
    {
        429 => LooksLikeQuota(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.RateLimited,
        401 or 403 => ProviderOutcome.AuthFailed,
        400 => LooksLikeQuota(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.FatalError,
        >= 500 => ProviderOutcome.TransientError,
        _ => ProviderOutcome.FatalError
    };

    private static bool LooksLikeQuota(string body)
    {
        var b = body.ToLowerInvariant();
        return b.Contains("quota") || b.Contains("billing") || b.Contains("exhausted")
            || b.Contains("resource_exhausted");
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
