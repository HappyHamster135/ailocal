using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Hardware;

namespace AiLocal.Core.Providers;

/// <summary>
/// Local inference via Ollama (http://localhost:11434). This is the always-on
/// fallback: it never gets a cooldown, so when every paid provider is out of
/// credits the cluster keeps running on-device.
/// </summary>
public sealed class OllamaProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly ProviderSettings _settings;
    private readonly LocalModelRecommendation _recommendation;

    public OllamaProvider(
        HttpClient http,
        ProviderSettings settings,
        LocalModelRecommendation recommendation)
    {
        _http = http;
        _settings = settings;
        _recommendation = recommendation;
    }

    public string Name => "ollama";
    public bool IsLocal => true;

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            using var resp = await _http.GetAsync($"{BaseUrl}/api/tags", ct);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<ProviderResponse> CompleteAsync(ChatRequest request, CancellationToken ct = default)
    {
        // ModelHint is never honored here - see the matching note in
        // GeminiProvider. Ollama's own model catalog has nothing in common
        // with the Anthropic ids the dashboard can produce, so a hint from
        // there ("claude-sonnet-5") 404s instead of resolving to anything.
        // Guard against a misconfigured OllamaModel too: if someone sets it
        // to an Anthropic/OpenAI id (e.g. "claude-haiku-4-5") it will 404 on
        // Ollama just the same - fall back to the hardware-recommended tag
        // instead of blindly 404ing.
        var model = !string.IsNullOrWhiteSpace(_settings.OllamaModel) && LooksLikeOllamaTag(_settings.OllamaModel)
            ? _settings.OllamaModel
            : _recommendation.OllamaTag;

        // (LooksLikeOllamaTag is a private static helper at the bottom of the class.)
        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["stream"] = false,
            ["messages"] = BuildMessages(request),
            ["options"] = new { num_predict = request.MaxTokens ?? _settings.MaxTokens }
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

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
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
            return ProviderResponse.Fail(ProviderOutcome.TransientError, $"ollama unreachable: {ex.Message}");
        }

        var body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            // Sjalvlakning vid "model not found": fraga Ollama vilka modeller
            // som FAKTISKT ar installerade och gor om anropet med den forsta.
            // Tacker felkonfigurerad OllamaModel, en rekommendation som inte
            // ar pullad, och aldre noder som skickat vidare ett moln-modellnamn
            // ("claude-haiku-4-5" -> 404) - anvandaren fick annars totalstopp
            // trots en fullt fungerande lokal modell.
            if ((int)response.StatusCode == 404 &&
                body.Contains("not found", StringComparison.OrdinalIgnoreCase) &&
                await FirstInstalledTagAsync(ct) is { } installed && installed != model)
            {
                payload["model"] = installed;
                using var retryRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
                {
                    Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
                };
                try
                {
                    response.Dispose();
                    response = await _http.SendAsync(retryRequest, ct);
                    body = await response.Content.ReadAsStringAsync(ct);
                    if (response.IsSuccessStatusCode)
                        model = installed;
                }
                catch (Exception ex)
                {
                    return ProviderResponse.Fail(ProviderOutcome.TransientError, $"ollama unreachable: {ex.Message}");
                }
            }

            if (!response.IsSuccessStatusCode)
            {
                var outcome = (int)response.StatusCode == 404
                    ? ProviderOutcome.FatalError   // model not pulled
                    : ProviderOutcome.TransientError;
                return ProviderResponse.Fail(outcome, $"ollama {(int)response.StatusCode}: {Truncate(body)}");
            }
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var content = root.TryGetProperty("message", out var m)
                && m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

            List<ToolCall>? toolCalls = null;
            if (root.TryGetProperty("message", out var msg) &&
                msg.TryGetProperty("tool_calls", out var calls) && calls.ValueKind == JsonValueKind.Array)
            {
                var callIndex = 0;
                foreach (var call in calls.EnumerateArray())
                {
                    if (!call.TryGetProperty("function", out var fn) || !fn.TryGetProperty("name", out var nameEl))
                        continue;
                    // Not every model/Ollama version issues a call id - not
                    // strictly needed on the way back either, since a "tool"
                    // role message here is matched to its call positionally
                    // (see BuildMessages), not by id - but ToolCall itself
                    // needs a non-null value.
                    var id = call.TryGetProperty("id", out var idEl) && idEl.ValueKind == JsonValueKind.String
                        ? idEl.GetString()!
                        : $"ollama_call_{callIndex}";
                    var argsJson = fn.TryGetProperty("arguments", out var argsEl) ? argsEl.GetRawText() : "{}";
                    (toolCalls ??= []).Add(new ToolCall(id, nameEl.GetString()!, argsJson));
                    callIndex++;
                }
            }

            int inTok = root.TryGetProperty("prompt_eval_count", out var it) ? it.GetInt32() : 0;
            int outTok = root.TryGetProperty("eval_count", out var ot) ? ot.GetInt32() : 0;

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = content,
                Model = model,
                Provider = "ollama",
                Usage = new TokenUsage(inTok, outTok),
                IsLocal = true,
                ToolCalls = toolCalls
            });
        }
        catch (Exception ex)
        {
            return ProviderResponse.Fail(ProviderOutcome.FatalError, $"parse error: {ex.Message}");
        }
    }

    /// <summary>
    /// Ollama's /api/chat is OpenAI-compatible: an assistant turn that called
    /// tools carries a tool_calls array alongside its (often empty) content,
    /// and each result comes back as its own role:"tool" message - no
    /// batching needed the way Anthropic/Gemini's block-array formats
    /// require, and matching is positional (by call order) rather than by id
    /// for models/versions that don't echo one back.
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
                    content = m.Content,
                    tool_calls = calls.Select(call => new
                    {
                        id = call.Id,
                        type = "function",
                        function = new
                        {
                            name = call.Name,
                            arguments = JsonSerializer.Deserialize<JsonElement>(
                                string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson)
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

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // See the matching note in CompleteAsync - ModelHint is never
        // honored here, only this Worker's own configured/recommended model.
        // Apply the same misconfiguration guard so a cloud id in OllamaModel
        // doesn't 404 the streaming path either.
        var model = !string.IsNullOrWhiteSpace(_settings.OllamaModel) && LooksLikeOllamaTag(_settings.OllamaModel)
            ? _settings.OllamaModel
            : _recommendation.OllamaTag;

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System))
            messages.Add(new { role = "system", content = request.System });
        foreach (var m in request.Messages)
            messages.Add(new { role = m.Role, content = m.Content });

        var payload = new
        {
            model,
            stream = true,
            messages,
            options = new { num_predict = request.MaxTokens ?? _settings.MaxTokens }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        HttpResponseMessage? response = null;
        StreamChunk? earlyFailure = null;
        try
        {
            response = await _http.SendAsync(httpRequest, HttpCompletionOption.ResponseHeadersRead, ct);
        }
        catch (Exception ex)
        {
            earlyFailure = new StreamChunk(null, ProviderResponse.Fail(ProviderOutcome.TransientError, $"ollama unreachable: {ex.Message}"));
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
            var outcome = (int)response.StatusCode == 404 ? ProviderOutcome.FatalError : ProviderOutcome.TransientError;
            yield return new StreamChunk(null, ProviderResponse.Fail(outcome, $"ollama {(int)response.StatusCode}: {Truncate(errorBody)}"));
            yield break;
        }

        var text = new StringBuilder();
        int inTok = 0, outTok = 0;

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(line); }
            catch { /* skip malformed NDJSON line */ }
            if (doc is null) continue;

            using (doc)
            {
                var root = doc.RootElement;
                var piece = root.TryGetProperty("message", out var m) && m.TryGetProperty("content", out var c)
                    ? c.GetString() ?? ""
                    : "";
                if (piece.Length > 0)
                {
                    text.Append(piece);
                    yield return new StreamChunk(piece, null);
                }

                if (root.TryGetProperty("done", out var done) && done.GetBoolean())
                {
                    if (root.TryGetProperty("prompt_eval_count", out var it)) inTok = it.GetInt32();
                    if (root.TryGetProperty("eval_count", out var ot)) outTok = ot.GetInt32();
                }
            }
        }

        yield return new StreamChunk(null, ProviderResponse.Ok(new ChatResponse
        {
            Content = text.ToString(),
            Model = model,
            Provider = "ollama",
            Usage = new TokenUsage(inTok, outTok),
            IsLocal = true
        }));
    }

    private string BaseUrl => _settings.OllamaEndpoint.TrimEnd('/');

    // A real Ollama tag looks like "llama3.1:8b" or "qwen2.5-coder:7b"
    // (name[:version] or namespace/name[:version]). Cloud ids
    // ("claude-*", "gpt-*", "anthropic/...", "openai/...", "google/...")
    // are NOT valid Ollama tags, so we refuse to use them and let the
    // caller fall back to the hardware-recommended tag instead of 404ing.
    /// <summary>First installed model tag from /api/tags, or null when none
    /// (or Ollama is unreachable). Used by the 404-heal above.</summary>
    private async Task<string?> FirstInstalledTagAsync(CancellationToken ct)
    {
        try
        {
            using var response = await _http.GetAsync($"{BaseUrl}/api/tags", ct);
            if (!response.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
                return null;
            foreach (var m in models.EnumerateArray())
                if (m.TryGetProperty("name", out var n) && n.GetString() is { Length: > 0 } tag)
                    return tag;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeOllamaTag(string name)
    {
        if (name.Contains('/')) return !name.StartsWith("anthropic/", StringComparison.OrdinalIgnoreCase)
                                       && !name.StartsWith("openai/", StringComparison.OrdinalIgnoreCase)
                                       && !name.StartsWith("google/", StringComparison.OrdinalIgnoreCase);
        return name.StartsWith("claude-", StringComparison.OrdinalIgnoreCase)
            ? false
            : !name.StartsWith("gpt-", StringComparison.OrdinalIgnoreCase)
              && !name.StartsWith("gemini-", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}
