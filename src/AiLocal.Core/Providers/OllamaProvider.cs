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
        var model = request.ModelHint ?? _settings.OllamaModel ?? _recommendation.OllamaTag;

        var messages = new List<object>();
        if (!string.IsNullOrWhiteSpace(request.System))
            messages.Add(new { role = "system", content = request.System });
        foreach (var m in request.Messages)
            messages.Add(new { role = m.Role, content = m.Content });

        var payload = new
        {
            model,
            stream = false,
            messages,
            options = new { num_predict = request.MaxTokens ?? _settings.MaxTokens }
        };

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
            var outcome = (int)response.StatusCode == 404
                ? ProviderOutcome.FatalError   // model not pulled
                : ProviderOutcome.TransientError;
            return ProviderResponse.Fail(outcome, $"ollama {(int)response.StatusCode}: {Truncate(body)}");
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var content = root.TryGetProperty("message", out var m)
                && m.TryGetProperty("content", out var c) ? c.GetString() ?? "" : "";

            int inTok = root.TryGetProperty("prompt_eval_count", out var it) ? it.GetInt32() : 0;
            int outTok = root.TryGetProperty("eval_count", out var ot) ? ot.GetInt32() : 0;

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = content,
                Model = model,
                Provider = "ollama",
                Usage = new TokenUsage(inTok, outTok),
                IsLocal = true
            });
        }
        catch (Exception ex)
        {
            return ProviderResponse.Fail(ProviderOutcome.FatalError, $"parse error: {ex.Message}");
        }
    }

    public async IAsyncEnumerable<StreamChunk> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        var model = request.ModelHint ?? _settings.OllamaModel ?? _recommendation.OllamaTag;

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

    private static string Truncate(string s) => s.Length > 200 ? s[..200] : s;
}
