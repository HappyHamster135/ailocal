using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>
/// Calls the Anthropic Messages API directly over HTTP (no SDK) and maps
/// HTTP status + error body onto a <see cref="ProviderOutcome"/> so the
/// fallback chain can react to credit exhaustion and rate limits.
/// </summary>
public sealed class AnthropicProvider : IChatProvider
{
    private const string Endpoint = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly ProviderSettings _settings;

    public AnthropicProvider(HttpClient http, Func<string?> apiKey, ProviderSettings settings)
    {
        _http = http;
        _apiKey = apiKey;
        _settings = settings;
    }

    public string Name => "anthropic";
    public bool IsLocal => false;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey()));

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _apiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResponse.Fail(ProviderOutcome.AuthFailed, "ANTHROPIC_API_KEY not set");

        var model = request.ModelHint ?? _settings.DefaultModel;

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens ?? _settings.MaxTokens,
            ["messages"] = BuildMessages(request.Messages)
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["system"] = request.System;
        if (request.Tools is { Count: > 0 } tools)
            payload["tools"] = tools.Select(t => new
            {
                name = t.Name,
                description = t.Description,
                input_schema = JsonSerializer.Deserialize<JsonElement>(t.ParametersJsonSchema)
            }).ToArray();

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
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

        var retryAfter = response.Headers.RetryAfter?.Delta
            ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null);

        return ProviderResponse.Fail(Classify(response.StatusCode, body), Summarize(body), retryAfter);
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var apiKey = _apiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            yield return new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.AuthFailed, "ANTHROPIC_API_KEY not set"));
            yield break;
        }

        var model = request.ModelHint ?? _settings.DefaultModel;
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens ?? _settings.MaxTokens,
            ["stream"] = true,
            ["messages"] = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray()
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["system"] = request.System;

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", ApiVersion);
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        HttpResponseMessage? response = null;
        StreamChunk? earlyFailure = null;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            earlyFailure = new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.TransientError, $"network: {ex.Message}"));
        }

        if (earlyFailure is not null)
        {
            yield return earlyFailure;
            yield break;
        }

        using var _ = response;
        if (!response!.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            yield return new StreamChunk(null, ProviderResponse.Fail(Classify(response.StatusCode, errorBody), Summarize(errorBody)));
            yield break;
        }

        var text = new StringBuilder();
        int inTok = 0, outTok = 0;
        var responseModel = model;
        string? stopReason = null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line[5..].Trim();
            if (json.Length == 0) continue;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(json); }
            catch { /* skip malformed SSE frame */ }
            if (doc is null) continue;

            using (doc)
            {
                var root = doc.RootElement;
                var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;

                if (type == "message_start" && root.TryGetProperty("message", out var msg))
                {
                    if (msg.TryGetProperty("model", out var m)) responseModel = m.GetString() ?? model;
                    if (msg.TryGetProperty("usage", out var u) && u.TryGetProperty("input_tokens", out var it))
                        inTok = it.GetInt32();
                }
                else if (type == "content_block_delta" &&
                    root.TryGetProperty("delta", out var delta) &&
                    delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta" &&
                    delta.TryGetProperty("text", out var dtext))
                {
                    var piece = dtext.GetString() ?? "";
                    if (piece.Length > 0)
                    {
                        text.Append(piece);
                        yield return new StreamChunk(piece, null);
                    }
                }
                else if (type == "message_delta")
                {
                    if (root.TryGetProperty("usage", out var du) && du.TryGetProperty("output_tokens", out var ot))
                        outTok = ot.GetInt32();
                    if (root.TryGetProperty("delta", out var md) && md.TryGetProperty("stop_reason", out var sr))
                        stopReason = sr.GetString();
                }
            }
        }

        if (stopReason == "refusal")
        {
            yield return new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.FatalError, "model declined the request (refusal)"));
            yield break;
        }

        yield return new StreamChunk(null, ProviderResponse.Ok(new ChatResponse
        {
            Content = text.ToString(),
            Model = responseModel,
            Provider = "anthropic",
            Usage = new TokenUsage(inTok, outTok),
            IsLocal = false
        }));
    }

    private static ProviderResponse ParseSuccess(string body, string requestedModel)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var stopReason = root.TryGetProperty("stop_reason", out var sr) ? sr.GetString() : null;
            if (stopReason == "refusal")
                return ProviderResponse.Fail(ProviderOutcome.FatalError, "model declined the request (refusal)");

            var text = new StringBuilder();
            List<ToolCall>? toolCalls = null;
            if (root.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in content.EnumerateArray())
                {
                    var type = block.TryGetProperty("type", out var t) ? t.GetString() : null;
                    if (type == "text" && block.TryGetProperty("text", out var textEl))
                        text.Append(textEl.GetString());
                    else if (type == "tool_use" &&
                        block.TryGetProperty("id", out var idEl) &&
                        block.TryGetProperty("name", out var nameEl))
                    {
                        var argsJson = block.TryGetProperty("input", out var inputEl)
                            ? inputEl.GetRawText()
                            : "{}";
                        (toolCalls ??= []).Add(new ToolCall(idEl.GetString()!, nameEl.GetString()!, argsJson));
                    }
                }
            }

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("input_tokens", out var it)) inTok = it.GetInt32();
                if (usage.TryGetProperty("output_tokens", out var ot)) outTok = ot.GetInt32();
            }

            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? requestedModel : requestedModel;

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = text.ToString(),
                Model = model,
                Provider = "anthropic",
                Usage = new TokenUsage(inTok, outTok),
                IsLocal = false,
                ToolCalls = toolCalls
            });
        }
        catch (Exception ex)
        {
            // v2.6: transient - ett otolkbart leverantorssvar ar en kanalhicka,
            // inte ett permanent fel (samma klass som v1.95:s "no choices").
            return ProviderResponse.Fail(ProviderOutcome.TransientError, $"parse error (transient): {ex.Message}");
        }
    }

    /// <summary>
    /// Anthropic has no separate "tool" role - a tool's result travels back as
    /// a user-role message containing a tool_result content block. Consecutive
    /// ChatMessage(Role: "tool", ...) entries (one per call the previous
    /// assistant turn made) are batched into a single user message with one
    /// tool_result block each, since that's one logical turn, not several.
    /// A plain user/assistant message with no tool calls keeps today's simple
    /// string-content shape - only a message that actually touches tools pays
    /// for the richer content-block array form.
    /// </summary>
    private static object[] BuildMessages(List<ChatMessage> messages)
    {
        var result = new List<object>();
        var i = 0;
        while (i < messages.Count)
        {
            var m = messages[i];
            if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                var toolResults = new List<object>();
                while (i < messages.Count && string.Equals(messages[i].Role, "tool", StringComparison.OrdinalIgnoreCase))
                {
                    toolResults.Add(new
                    {
                        type = "tool_result",
                        tool_use_id = messages[i].ToolCallId,
                        content = messages[i].Content
                    });
                    i++;
                }
                result.Add(new { role = "user", content = toolResults.ToArray() });
                continue;
            }

            if (m.ToolCalls is { Count: > 0 } calls)
            {
                var blocks = new List<object>();
                if (!string.IsNullOrEmpty(m.Content))
                    blocks.Add(new { type = "text", text = m.Content });
                foreach (var call in calls)
                    blocks.Add(new
                    {
                        type = "tool_use",
                        id = call.Id,
                        name = call.Name,
                        input = JsonSerializer.Deserialize<JsonElement>(
                            string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson)
                    });
                result.Add(new { role = m.Role, content = blocks.ToArray() });
                i++;
                continue;
            }

            result.Add(new { role = m.Role, content = m.Content });
            i++;
        }
        return result.ToArray();
    }

    private static ProviderOutcome Classify(HttpStatusCode code, string body) => (int)code switch
    {
        429 => ProviderOutcome.RateLimited,
        529 => ProviderOutcome.Overloaded,
        401 => ProviderOutcome.AuthFailed,
        403 => LooksLikeBilling(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.AuthFailed,
        400 => LooksLikeBilling(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.FatalError,
        >= 500 => ProviderOutcome.TransientError,
        _ => ProviderOutcome.FatalError
    };

    private static bool LooksLikeBilling(string body)
    {
        var b = body.ToLowerInvariant();
        return b.Contains("credit") || b.Contains("billing") || b.Contains("quota")
            || b.Contains("insufficient") || b.Contains("balance");
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
