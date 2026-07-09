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

        var model = request.ModelHint ?? _settings.GeminiModel;

        var payload = new Dictionary<string, object?>
        {
            ["contents"] = request.Messages.Select(m => new
            {
                role = m.Role == "assistant" ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            }).ToArray(),
            ["generationConfig"] = new { maxOutputTokens = request.MaxTokens ?? _settings.MaxTokens }
        };
        if (!string.IsNullOrWhiteSpace(request.System))
            payload["systemInstruction"] = new { parts = new[] { new { text = request.System } } };

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

    private static ProviderResponse ParseSuccess(string body, string requestedModel)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var text = new StringBuilder();
            if (root.TryGetProperty("candidates", out var candidates)
                && candidates.ValueKind == JsonValueKind.Array
                && candidates.GetArrayLength() > 0)
            {
                var first = candidates[0];
                if (first.TryGetProperty("content", out var content)
                    && content.TryGetProperty("parts", out var parts)
                    && parts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var part in parts.EnumerateArray())
                        if (part.TryGetProperty("text", out var t)) text.Append(t.GetString());
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
                IsLocal = false
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
