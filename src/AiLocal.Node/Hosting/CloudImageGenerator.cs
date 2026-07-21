using System.Text;
using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Real game art via the app's OWN configured keys (OpenRouter first, Gemini
/// second) - the previous cloud path needed a REPLICATE_API_TOKEN environment
/// variable nobody sets, so every sprite fell back to procedural pixel art.
/// Both providers are called through their image-output chat APIs and return
/// base64 PNG. Any failure returns null and the caller falls through to the
/// procedural generator - cloud art is an upgrade, never a requirement.
/// </summary>
public sealed class CloudImageGenerator
{
    // Pinnat bildmodellval: billig, snabb och bra på spelgrafik. Medvetet
    // INTE kopplat till chattmodellsinställningen (openrouter/auto är ingen
    // bildmodell).
    private const string OpenRouterImageModel = "google/gemini-2.5-flash-image";
    private const string GeminiImageModel = "gemini-2.5-flash-image";

    private readonly IHttpClientFactory? _httpFactory;
    private readonly Func<string, string?> _getApiKey;

    public CloudImageGenerator(IHttpClientFactory? httpFactory, Func<string, string?> getApiKey)
    {
        _httpFactory = httpFactory;
        _getApiKey = getApiKey;
    }

    public bool HasAnyKey =>
        !string.IsNullOrWhiteSpace(_getApiKey("openrouter")) || !string.IsNullOrWhiteSpace(_getApiKey("gemini"));

    /// <summary>PNG bytes, or null when no key works - callers fall back.</summary>
    public async Task<byte[]?> TryGenerateAsync(string prompt, CancellationToken ct)
    {
        try
        {
            if (_getApiKey("openrouter") is { Length: > 0 } orKey
                && await TryOpenRouterAsync(orKey, prompt, ct) is { } fromOpenRouter)
                return fromOpenRouter;
        }
        catch { /* fall vidare till nästa leverantör */ }

        try
        {
            if (_getApiKey("gemini") is { Length: > 0 } gKey
                && await TryGeminiAsync(gKey, prompt, ct) is { } fromGemini)
                return fromGemini;
        }
        catch { /* procedurella fallbacken tar över hos anroparen */ }

        return null;
    }

    private HttpClient Client() => _httpFactory?.CreateClient("cloud-images")
        ?? SharedClient;

    private static readonly HttpClient SharedClient = new() { Timeout = TimeSpan.FromSeconds(120) };

    private async Task<byte[]?> TryOpenRouterAsync(string apiKey, string prompt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            model = OpenRouterImageModel,
            modalities = new[] { "image", "text" },
            messages = new[] { new { role = "user", content = prompt } }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://openrouter.ai/api/v1/chat/completions")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
        using var response = await Client().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        // Bildsvar: choices[0].message.images[0].image_url.url = data:image/png;base64,...
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
            && choices[0].TryGetProperty("message", out var message)
            && message.TryGetProperty("images", out var images) && images.GetArrayLength() > 0
            && images[0].TryGetProperty("image_url", out var imageUrl)
            && imageUrl.TryGetProperty("url", out var url)
            && url.GetString() is { } dataUrl)
            return DecodeDataUrl(dataUrl);
        return null;
    }

    private async Task<byte[]?> TryGeminiAsync(string apiKey, string prompt, CancellationToken ct)
    {
        var payload = JsonSerializer.Serialize(new
        {
            contents = new[] { new { parts = new[] { new { text = prompt } } } },
            generationConfig = new { responseModalities = new[] { "IMAGE", "TEXT" } }
        });
        using var request = new HttpRequestMessage(HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{GeminiImageModel}:generateContent?key={apiKey}")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var response = await Client().SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) return null;

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (doc.RootElement.TryGetProperty("candidates", out var candidates) && candidates.GetArrayLength() > 0
            && candidates[0].TryGetProperty("content", out var content)
            && content.TryGetProperty("parts", out var parts))
        {
            foreach (var part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("inlineData", out var inline)
                    && inline.TryGetProperty("data", out var data)
                    && data.GetString() is { } base64)
                    return Convert.FromBase64String(base64);
            }
        }
        return null;
    }

    internal static byte[]? DecodeDataUrl(string dataUrl)
    {
        var comma = dataUrl.IndexOf(',');
        if (comma < 0) return null;
        try { return Convert.FromBase64String(dataUrl[(comma + 1)..]); }
        catch (FormatException) { return null; }
    }
}
