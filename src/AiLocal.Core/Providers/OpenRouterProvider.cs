using System.Net;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Providers;

/// <summary>
/// OpenRouter (openrouter.ai) - one API key/endpoint that proxies to a huge
/// catalog of models (Claude, GPT, Llama, Mistral, Gemini, and many more)
/// from a single OpenAI-compatible chat completions API. Model id is
/// config-driven (e.g. "anthropic/claude-3.5-sonnet", "openai/gpt-4o") since
/// OpenRouter's catalog changes far too often to hardcode - set
/// Providers:OpenRouterModel.
/// </summary>
public sealed class OpenRouterProvider : IChatProvider
{
    private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";

    private readonly HttpClient _http;
    private readonly Func<string?> _apiKey;
    private readonly ProviderSettings _settings;

    public OpenRouterProvider(HttpClient http, Func<string?> apiKey, ProviderSettings settings)
    {
        _http = http;
        _apiKey = apiKey;
        _settings = settings;
    }

    public string Name => "openrouter";
    public bool IsLocal => false;

    public Task<bool> IsAvailableAsync(CancellationToken ct = default) =>
        Task.FromResult(!string.IsNullOrWhiteSpace(_apiKey()));

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        var apiKey = _apiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
            return ProviderResponse.Fail(ProviderOutcome.AuthFailed, "OPENROUTER_API_KEY not set");

        // ModelHint is never honored here by default - OpenRouter's catalog
        // uses prefixed ids ("anthropic/claude-sonnet-4.5"). But every current
        // source of ModelHint sends a BARE Anthropic id ("claude-sonnet-5"),
        // which OpenRouter 404s on. So when the configured OpenRouterModel is
        // the "openrouter/auto" magic value, fall back to translating a bare
        // Anthropic id into its prefixed form instead of 404ing.
        var model = _settings.OpenRouterModel;
        if ((string.IsNullOrWhiteSpace(model) || model.Equals("openrouter/auto", StringComparison.OrdinalIgnoreCase))
            && !string.IsNullOrWhiteSpace(request.ModelHint))
        {
            model = ToOpenRouterId(request.ModelHint);
        }

        // Translate a bare Anthropic id (claude-sonnet-5) into OpenRouter's
        // prefixed form (anthropic/claude-sonnet-5) so it resolves instead of
        // 404ing. Other providers' ids (openai/..., google/...) pass through.
        static string ToOpenRouterId(string hint)
        {
            if (hint.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase))
                return hint;
            if (hint.StartsWith("claude-"))
                return "anthropic/" + hint;
            return hint;
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = request.MaxTokens ?? _settings.MaxTokens,
            ["messages"] = BuildMessages(request)
        };
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, Endpoint)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        // Optional per OpenRouter's docs (used only for their own leaderboards/rankings) -
        // harmless to omit, but a real value avoids being lumped in as "unknown app".
        httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/HappyHamster135/ailocal");
        httpRequest.Headers.TryAddWithoutValidation("X-Title", "AiLocal");

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

    /// <summary>
    /// Real OpenAI-format chat completions (which OpenRouter mirrors, unlike
    /// Ollama's looser "OpenAI-ish" /api/chat): an assistant turn that called
    /// tools carries a tool_calls array, and - the one place this genuinely
    /// differs from OllamaProvider's BuildMessages - each call's `arguments`
    /// is a JSON-encoded STRING, not a nested object, both in what's sent
    /// back here and in what ParseSuccess below reads out of the response.
    /// Each tool result is its own role:"tool" message referencing the
    /// original call's id, matched positionally same as Ollama.
    /// </summary>
    private static List<object> BuildMessages(ChatRequest request)
    {
        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System))
            messages.Add(new { role = "system", content = request.System });

        foreach (var m in request.Messages)
        {
            if (m.ToolCalls is { Count: > 0 } calls)
            {
                messages.Add(new
                {
                    role = m.Role,
                    content = string.IsNullOrEmpty(m.Content) ? null : m.Content,
                    tool_calls = calls.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new
                        {
                            name = call.Name,
                            arguments = string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson
                        }
                    }).ToArray()
                });
            }
            else if (string.Equals(m.Role, "tool", StringComparison.OrdinalIgnoreCase))
            {
                messages.Add(new { role = "tool", content = m.Content, tool_call_id = m.ToolCallId });
            }
            else
            {
                messages.Add(new { role = m.Role, content = m.Content });
            }
        }

        return messages;
    }

    private static ProviderResponse ParseSuccess(string body, string requestedModel)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                // v1.95: TRANSIENT, inte fatalt. OpenRouter svarar ibland 200
                // med tomt choices (upstream-hicka/ratelimit) - som FatalError
                // dödade det HELA kedjan varje gång det inträffade mitt i ett
                // bygge ("crashar alltid vid något tillfälle", live). Transient
                // -> cooldown + nästa provider/försök i stället för totalstopp.
                return ProviderResponse.Fail(ProviderOutcome.TransientError, "no choices in response (tomt svar - transient)");

            var message = choices[0].GetProperty("message");
            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";

            List<ToolCall>? toolCalls = null;
            if (message.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                foreach (var call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("id", out var idEl) || !call.TryGetProperty("function", out var fn) ||
                        !fn.TryGetProperty("name", out var nameEl))
                        continue;

                    // Real OpenAI-format APIs give `arguments` as a JSON-encoded
                    // string (a well-known quirk of that API), not a nested
                    // object like Anthropic's `input` or Gemini's `args` -
                    // GetString() here, not GetRawText() like the other providers.
                    var argsJson = fn.TryGetProperty("arguments", out var argsEl) && argsEl.ValueKind == JsonValueKind.String
                        ? argsEl.GetString()!
                        : "{}";
                    (toolCalls ??= []).Add(new ToolCall(idEl.GetString()!, nameEl.GetString()!, argsJson));
                }
            }

            int inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage))
            {
                if (usage.TryGetProperty("prompt_tokens", out var it)) inTok = it.GetInt32();
                if (usage.TryGetProperty("completion_tokens", out var ot)) outTok = ot.GetInt32();
            }

            var model = root.TryGetProperty("model", out var m) ? m.GetString() ?? requestedModel : requestedModel;

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = content,
                Model = model,
                Provider = "openrouter",
                Usage = new TokenUsage(inTok, outTok),
                IsLocal = false,
                ToolCalls = toolCalls
            });
        }
        catch (Exception ex)
        {
            // v2.6: TRANSIENT, inte fatalt (live-sett: OpenRouter svarade
            // 200 med icke-JSON/trunkerat skrap -> "parse error ... does not
            // contain any JSON tokens" dodade HELA kedjan). Ett svar som inte
            // gar att tolka ar en kanal-/upstream-hicka av samma klass som
            // "no choices" (v1.95) - cooldown + nasta forsok, aldrig totalstopp.
            return ProviderResponse.Fail(ProviderOutcome.TransientError, $"parse error (transient): {ex.Message}");
        }
    }

    private static ProviderOutcome Classify(HttpStatusCode code, string body) => (int)code switch
    {
        429 => LooksLikeQuota(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.RateLimited,
        401 or 403 => ProviderOutcome.AuthFailed,
        402 => ProviderOutcome.QuotaExhausted, // OpenRouter uses 402 specifically for "out of credits"
        400 => LooksLikeQuota(body) ? ProviderOutcome.QuotaExhausted : ProviderOutcome.FatalError,
        >= 500 => ProviderOutcome.TransientError,
        _ => ProviderOutcome.FatalError
    };

    private static bool LooksLikeQuota(string body)
    {
        var b = body.ToLowerInvariant();
        return b.Contains("credit") || b.Contains("billing") || b.Contains("quota") || b.Contains("insufficient");
    }

    private static string Summarize(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var e) &&
                e.TryGetProperty("message", out var msg))
                return msg.GetString() ?? "unknown error";
        }
        catch { /* not JSON */ }
        return body.Length > 200 ? body[..200] : body;
    }
}
