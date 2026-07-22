using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Result of a screenshot capture operation.
/// <paramref name="ImageBytes"/> is non-null when the capture succeeded and
/// the caller didn't supply an output path (capture went to a temp file).
/// </summary>
public sealed record ScreenshotResult(
    bool Success,
    string Output,
    string? FilePath,
    byte[]? ImageBytes = null);

/// <summary>
/// Result of a vision analysis operation.
/// <paramref name="Issues"/> is a list of visual bugs or anomalies detected
/// in the image, parsed from the model's structured analysis.
/// </summary>
/// <summary>v1.91: Model/tokens bär anropets faktiska användning (normaliserad
/// modell-slug i OpenRouter-form där det går, t.ex. "openai/gpt-4o") så
/// uppdragets kostnadsredovisning kan PRISSÄTTA visionsanropen i stället för
/// att bara räkna dem. 0/null = usage saknades i svaret (räknas oprissatt).</summary>
public sealed record VisionResult(
    bool Success,
    string Analysis,
    List<string> Issues,
    string? Model = null,
    long InputTokens = 0,
    long OutputTokens = 0);

/// <summary>
/// Captures screenshots of the primary screen (or a specific window by title)
/// and saves them as PNG files. Uses GDI+ (gdiplus.dll) directly on Windows
/// for single-file-build compatibility, mirroring the approach in
/// <see cref="DesktopControlService"/>.
///
/// Cross-platform: Windows uses GDI BitBlt; Linux/macOS returns an error with
/// platform-specific instructions.
/// </summary>
public sealed class ScreenshotTool
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    /// <summary>
    /// Captures a screenshot of the entire primary screen, or of a specific
    /// window whose title matches <paramref name="windowTitle"/> (case-insensitive
    /// substring match). Saves the PNG to <paramref name="outputPath"/>.
    /// When <paramref name="outputPath"/> is null, saves to a temp file and
    /// returns the bytes in <see cref="ScreenshotResult.ImageBytes"/>.
    /// </summary>
    public Task<ScreenshotResult> CaptureAsync(
        string? windowTitle,
        string? outputPath,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (!IsSupported)
        {
            var msg = RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                ? "Screenshot capture is not supported on Linux. Install xdotool + import (ImageMagick) or gnome-screenshot, or use a remote desktop tool."
                : RuntimeInformation.IsOSPlatform(OSPlatform.OSX)
                    ? "Screenshot capture is not supported on macOS. Use screencapture(1) or the built-in Cmd+Shift+4 shortcut."
                    : "Screenshot capture is only supported on Windows.";
            return Task.FromResult(new ScreenshotResult(false, msg, null));
        }

        string? tmp = null;
        try
        {
            var (w, h) = GetScreenSize();
            IntPtr hwnd;

            if (!string.IsNullOrWhiteSpace(windowTitle))
            {
                hwnd = FindWindowByTitle(windowTitle);
                if (hwnd == IntPtr.Zero)
                    return Task.FromResult(new ScreenshotResult(
                        false,
                        $"No window found with title matching \"{windowTitle}\".",
                        null));
            }
            else
            {
                hwnd = GetDesktopWindow();
            }

            // Get the window (or desktop) DC
            IntPtr hdcSrc = GetWindowDC(hwnd);
            if (hdcSrc == IntPtr.Zero)
                return Task.FromResult(new ScreenshotResult(
                    false,
                    "GetWindowDC returned 0 (no screen session?).",
                    null));

            IntPtr hdcDst = CreateCompatibleDC(hdcSrc);
            IntPtr hBmp = CreateCompatibleBitmap(hdcSrc, w, h);
            IntPtr hOld = SelectObject(hdcDst, hBmp);
            BitBlt(hdcDst, 0, 0, w, h, hdcSrc, 0, 0, 0x00CC0020 /* SRCCOPY */);
            SelectObject(hdcDst, hOld);

            // Resolve the output path
            string savePath = outputPath
                ?? Path.Combine(Path.GetTempPath(), "ailocal-screenshot-" + Guid.NewGuid().ToString("n") + ".png");
            Directory.CreateDirectory(Path.GetDirectoryName(savePath)!);

            var status = SaveBitmapToPng(hBmp, savePath);
            DeleteObject(hBmp);
            DeleteDC(hdcDst);
            ReleaseDC(hwnd, hdcSrc);

            if (status != 0)
                return Task.FromResult(new ScreenshotResult(
                    false,
                    "GDI+ save failed (status " + status + ").",
                    null));

            if (outputPath is not null)
                return Task.FromResult(new ScreenshotResult(
                    true,
                    $"Screenshot saved to {savePath}",
                    savePath));

            // Return the bytes from the temp file
            var bytes = File.ReadAllBytes(savePath);
            try { File.Delete(savePath); } catch { /* best-effort cleanup */ }
            return Task.FromResult(new ScreenshotResult(
                true,
                $"Screenshot captured ({bytes.Length} bytes).",
                null,
                bytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(new ScreenshotResult(
                false,
                $"{ex.GetType().Name}: {ex.Message}",
                null));
        }
        finally
        {
            if (tmp is not null)
            {
                try { File.Delete(tmp); } catch { }
            }
        }
    }

    // ---- GDI+ (gdiplus.dll) ----

    private static int SaveBitmapToPng(IntPtr hBmp, string path)
    {
        IntPtr gdip = IntPtr.Zero;
        var startup = new GdiplusStartupInput { GdiplusVersion = 1 };
        if (GdiplusStartup(out gdip, ref startup, IntPtr.Zero) != 0) return -1;
        try
        {
            IntPtr hBitmap;
            int st = GdipCreateBitmapFromHBITMAP(hBmp, IntPtr.Zero, out hBitmap);
            if (st != 0) return st;
            try
            {
                var pngClsId = new Guid("557CF406-1A04-11D3-9A73-0000F81EF32E");
                st = GdipSaveImageToFile(hBitmap, path, ref pngClsId, IntPtr.Zero);
                return st;
            }
            finally { GdipDisposeImage(hBitmap); }
        }
        finally { GdiplusShutdown(gdip); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GdiplusStartupInput
    {
        public int GdiplusVersion;
        public IntPtr DebugEventCallback;
        public int SuppressBackgroundThread;
        public int SuppressExternalCodecs;
    }

    [DllImport("gdiplus.dll")]
    private static extern int GdiplusStartup(out IntPtr token, ref GdiplusStartupInput input, IntPtr traint);

    [DllImport("gdiplus.dll")]
    private static extern void GdiplusShutdown(IntPtr token);

    [DllImport("gdiplus.dll")]
    private static extern int GdipCreateBitmapFromHBITMAP(IntPtr hbm, IntPtr hpal, out IntPtr bitmap);

    [DllImport("gdiplus.dll")]
    private static extern int GdipSaveImageToFile(IntPtr image, [MarshalAs(UnmanagedType.LPWStr)] string filename, ref Guid clsidEncoder, IntPtr encoderParams);

    [DllImport("gdiplus.dll")]
    private static extern int GdipDisposeImage(IntPtr image);

    // ---- GDI / User32 ----

    [DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr ReleaseDC(IntPtr hWnd, IntPtr hDc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindow(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hDc, int w, int h);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hDc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hDc);

    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(IntPtr hDcDst, int x, int y, int w, int h,
        IntPtr hDcSrc, int xSrc, int ySrc, uint rasterOp);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    private static (int, int) GetScreenSize()
    {
        int w = GetSystemMetrics(0); // SM_CXSCREEN
        int h = GetSystemMetrics(1); // SM_CYSCREEN
        return (w > 0 ? w : 1280, h > 0 ? h : 720);
    }

    /// <summary>
    /// Finds a top-level window whose title contains the given text
    /// (case-insensitive). Uses EnumWindows to enumerate all windows
    /// and match by substring, since FindWindow requires an exact match.
    /// </summary>
    private static IntPtr FindWindowByTitle(string titleSubstring)
    {
        IntPtr found = IntPtr.Zero;
        var target = titleSubstring.Trim();

        EnumWindows((hWnd, _) =>
        {
            var sb = new StringBuilder(512);
            int len = GetWindowText(hWnd, sb, sb.Capacity);
            if (len > 0 && sb.ToString().Contains(target, StringComparison.OrdinalIgnoreCase))
            {
                found = hWnd;
                return false; // stop enumeration
            }
            return true; // continue
        }, IntPtr.Zero);

        return found;
    }
}

/// <summary>
/// Analyzes images using a vision-capable AI model. Encodes the screenshot
/// as base64 and sends it to an OpenAI-compatible vision API (OpenAI,
/// OpenRouter, Anthropic, or Gemini) via direct HTTP calls, leveraging the
/// existing provider infrastructure for API key management and model config.
///
/// The question is typically something like "Does this game look correct?
/// Are there visual bugs?" and the response includes a structured analysis
/// with a list of specific issues found.
/// </summary>
public sealed class VisionAnalyzer
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly PersistentSettingsStore _settingsStore;
    private readonly ProviderSettings _providerSettings;
    private readonly ILogger<VisionAnalyzer>? _logger;

    public VisionAnalyzer(
        IHttpClientFactory httpClientFactory,
        PersistentSettingsStore settingsStore,
        ProviderSettings providerSettings,
        ILogger<VisionAnalyzer>? logger = null)
    {
        _httpClientFactory = httpClientFactory;
        _settingsStore = settingsStore;
        _providerSettings = providerSettings;
        _logger = logger;
    }

    /// <summary>
    /// Analyzes the image at <paramref name="imagePath"/> by sending it to a
    /// vision-capable model. The model is asked <paramref name="question"/>
    /// and the response is parsed for visual issues.
    ///
    /// Tries providers in this order: OpenAI (GPT-4V), OpenRouter (vision
    /// models), Anthropic (Claude 3 Vision), then falls back to the next
    /// configured provider. Returns the first successful analysis.
    /// </summary>
    public async Task<VisionResult> AnalyzeAsync(
        string imagePath,
        string question,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return new VisionResult(false, $"Image file not found: {imagePath}", []);

        if (string.IsNullOrWhiteSpace(question))
            question = "Describe what you see in this image. Are there any visual bugs, rendering issues, or anomalies?";

        // Read and encode the image
        byte[] imageBytes;
        try
        {
            imageBytes = await File.ReadAllBytesAsync(imagePath, ct);
        }
        catch (Exception ex)
        {
            return new VisionResult(false, $"Failed to read image: {ex.Message}", []);
        }

        if (imageBytes.Length == 0)
            return new VisionResult(false, "Image file is empty.", []);

        string mediaType = GetMediaType(imagePath);
        string base64 = Convert.ToBase64String(imageBytes);

        // Try providers in priority order
        var errors = new List<string>();
        var providerOrder = _providerSettings.Priority;
        if (providerOrder.Count == 0)
            providerOrder = ["openai", "openrouter", "anthropic", "gemini"];

        foreach (var provider in providerOrder)
        {
            ct.ThrowIfCancellationRequested();

            var result = await TryVisionProviderAsync(provider, base64, mediaType, question, ct);
            if (result is not null)
                return result;

            errors.Add($"{provider}: failed or unavailable");
        }

        var summary = "All vision-capable providers failed: " + string.Join(" | ", errors);
        _logger?.LogWarning("Vision analysis: {Summary}", summary);
        return new VisionResult(false, summary, []);
    }

    private async Task<VisionResult?> TryVisionProviderAsync(
        string provider,
        string base64,
        string mediaType,
        string question,
        CancellationToken ct)
    {
        try
        {
            return provider.ToLowerInvariant() switch
            {
                "openai" => await CallOpenAIVisionAsync(base64, mediaType, question, ct),
                "openrouter" => await CallOpenRouterVisionAsync(base64, mediaType, question, ct),
                "anthropic" => await CallAnthropicVisionAsync(base64, mediaType, question, ct),
                "gemini" => await CallGeminiVisionAsync(base64, mediaType, question, ct),
                _ => null
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Vision provider {Provider} failed", provider);
            return null;
        }
    }

    /// <summary>
    /// Calls the OpenAI Chat Completions API with an image content block
    /// (GPT-4V / GPT-4o). Uses the configured OpenAIModel or defaults to gpt-4o.
    /// </summary>
    private async Task<VisionResult?> CallOpenAIVisionAsync(
        string base64, string mediaType, string question, CancellationToken ct)
    {
        var apiKey = _settingsStore.GetApiKey("openai");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var model = _providerSettings.OpenAIModel ?? "gpt-4o";
        var http = _httpClientFactory.CreateClient("openai");

        var payload = new
        {
            model,
            max_tokens = 2048,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = question },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mediaType};base64,{base64}" }
                        }
                    }
                }
            }
        };

        return await CallVisionEndpointAsync(
            http, "https://api.openai.com/v1/chat/completions",
            apiKey, "Bearer", payload, "openai", ct);
    }

    /// <summary>
    /// Calls OpenRouter's OpenAI-compatible chat completions API with vision
    /// content. OpenRouter supports many vision models including Claude 3,
    /// GPT-4o, Gemini, etc.
    /// </summary>
    private async Task<VisionResult?> CallOpenRouterVisionAsync(
        string base64, string mediaType, string question, CancellationToken ct)
    {
        var apiKey = _settingsStore.GetApiKey("openrouter");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        // Use a vision-capable model via OpenRouter
        var model = _providerSettings.OpenRouterModel;
        if (string.IsNullOrWhiteSpace(model) || model.Equals("openrouter/auto", StringComparison.OrdinalIgnoreCase))
            model = "openai/gpt-4o"; // fallback to a known vision model

        var http = _httpClientFactory.CreateClient("openrouter");

        var payload = new
        {
            model,
            max_tokens = 2048,
            messages = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = question },
                        new
                        {
                            type = "image_url",
                            image_url = new { url = $"data:{mediaType};base64,{base64}" }
                        }
                    }
                }
            }
        };

        return await CallVisionEndpointAsync(
            http, "https://openrouter.ai/api/v1/chat/completions",
            apiKey, "Bearer", payload, "openrouter", ct);
    }

    /// <summary>
    /// Calls the Anthropic Messages API with an image content block
    /// (Claude 3 Vision). Anthropic uses a different content block format
    /// than OpenAI.
    /// </summary>
    private async Task<VisionResult?> CallAnthropicVisionAsync(
        string base64, string mediaType, string question, CancellationToken ct)
    {
        var apiKey = _settingsStore.GetApiKey("anthropic");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var model = _providerSettings.DefaultModel;
        if (string.IsNullOrWhiteSpace(model))
            model = "claude-3-5-sonnet-20241022";

        var http = _httpClientFactory.CreateClient("anthropic");

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["max_tokens"] = 2048,
            ["messages"] = new[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new
                        {
                            type = "image",
                            source = new
                            {
                                type = "base64",
                                media_type = mediaType,
                                data = base64
                            }
                        },
                        new { type = "text", text = question }
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages");
        httpRequest.Headers.TryAddWithoutValidation("x-api-key", apiKey);
        httpRequest.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return ParseAnthropicVisionResponse(body);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Anthropic vision call failed");
            return null;
        }
    }

    /// <summary>
    /// Calls the Gemini API with an image content block.
    /// Gemini uses yet another content block format.
    /// </summary>
    private async Task<VisionResult?> CallGeminiVisionAsync(
        string base64, string mediaType, string question, CancellationToken ct)
    {
        var apiKey = _settingsStore.GetApiKey("gemini");
        if (string.IsNullOrWhiteSpace(apiKey))
            return null;

        var model = _providerSettings.GeminiModel ?? "gemini-2.5-flash";
        var http = _httpClientFactory.CreateClient("gemini");

        // Strip the "data:image/" prefix from media type for Gemini's InlineData
        // e.g. "image/png" from "data:image/png;base64,..."
        var geminiMediaType = mediaType;
        if (geminiMediaType.StartsWith("data:"))
            geminiMediaType = geminiMediaType[5..];

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new object[]
                    {
                        new { inline_data = new { mime_type = geminiMediaType, data = base64 } },
                        new { text = question }
                    }
                }
            }
        };

        using var httpRequest = new HttpRequestMessage(
            HttpMethod.Post,
            $"https://generativelanguage.googleapis.com/v1beta/models/{model}:generateContent?key={apiKey}");
        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return ParseGeminiVisionResponse(body, model);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Gemini vision call failed");
            return null;
        }
    }

    /// <summary>
    /// Common handler for OpenAI-compatible vision endpoints (OpenAI, OpenRouter).
    /// Sends the vision request and parses the response.
    /// </summary>
    private async Task<VisionResult?> CallVisionEndpointAsync(
        HttpClient http,
        string endpoint,
        string apiKey,
        string authScheme,
        object payload,
        string providerName,
        CancellationToken ct)
    {
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint);
        httpRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(authScheme, apiKey);

        if (providerName == "openrouter")
        {
            httpRequest.Headers.TryAddWithoutValidation("HTTP-Referer", "https://github.com/HappyHamster135/ailocal");
            httpRequest.Headers.TryAddWithoutValidation("X-Title", "AiLocal");
        }

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(payload, JsonOpts), Encoding.UTF8, "application/json");

        try
        {
            using var response = await http.SendAsync(httpRequest, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
                return null;

            return ParseOpenAIVisionResponse(body, providerName);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "{Provider} vision call failed", providerName);
            return null;
        }
    }

    /// <summary>
    /// Parses an OpenAI-compatible vision response (choices[0].message.content).
    /// v1.91: extraherar också usage + modell (normaliserad till OpenRouter-slug
    /// för prissättning - "gpt-4o" via OpenAI direkt blir "openai/gpt-4o").
    /// </summary>
    internal static VisionResult ParseOpenAIVisionResponse(string body, string providerName = "openai")
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("choices", out var choices) ||
                choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
                return new VisionResult(false, "No choices in response", []);

            var message = choices[0].TryGetProperty("message", out var msg) ? msg : default;
            var content = message.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                ? c.GetString() ?? ""
                : "";

            if (string.IsNullOrWhiteSpace(content))
                return new VisionResult(false, "Empty response from vision model", []);

            var (model, inTok, outTok) = ReadOpenAiUsage(root, providerName);
            var issues = ExtractIssues(content);
            return new VisionResult(true, content, issues, model, inTok, outTok);
        }
        catch (Exception ex)
        {
            return new VisionResult(false, $"Parse error: {ex.Message}", []);
        }
    }

    /// <summary>Usage + modellslug ur ett OpenAI-format-svar. OpenRouter
    /// returnerar redan "leverantör/modell"; direkta OpenAI-svar prefixas så
    /// prislistan (OpenRouter-katalogen) känner igen dem.</summary>
    private static (string? Model, long InTok, long OutTok) ReadOpenAiUsage(JsonElement root, string providerName)
    {
        var model = root.TryGetProperty("model", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() : null;
        if (model is { Length: > 0 } && !model.Contains('/') && providerName == "openai")
            model = "openai/" + model;
        long inTok = 0, outTok = 0;
        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            if (usage.TryGetProperty("prompt_tokens", out var p) && p.TryGetInt64(out var pv)) inTok = pv;
            if (usage.TryGetProperty("completion_tokens", out var q) && q.TryGetInt64(out var qv)) outTok = qv;
        }
        return (model, inTok, outTok);
    }

    /// <summary>
    /// Parses an Anthropic vision response (content array with text blocks).
    /// </summary>
    internal static VisionResult ParseAnthropicVisionResponse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            var content = new StringBuilder();
            if (root.TryGetProperty("content", out var contentArray) &&
                contentArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in contentArray.EnumerateArray())
                {
                    if (block.TryGetProperty("type", out var type) &&
                        type.GetString() == "text" &&
                        block.TryGetProperty("text", out var text))
                    {
                        content.Append(text.GetString());
                    }
                }
            }

            var contentText = content.ToString();
            if (string.IsNullOrWhiteSpace(contentText))
            {
                // Check for refusal
                if (root.TryGetProperty("stop_reason", out var sr) && sr.GetString() == "refusal")
                    return new VisionResult(false, "Model declined to analyze the image.", []);
                return new VisionResult(false, "Empty response from vision model", []);
            }

            // v1.91: Anthropic-slugs (claude-*) prissätts av Anthropic-listan
            // som de är - ingen normalisering behövs.
            var model = root.TryGetProperty("model", out var mdl) && mdl.ValueKind == JsonValueKind.String
                ? mdl.GetString() : null;
            long inTok = 0, outTok = 0;
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
            {
                if (usage.TryGetProperty("input_tokens", out var it) && it.TryGetInt64(out var iv)) inTok = iv;
                if (usage.TryGetProperty("output_tokens", out var ot) && ot.TryGetInt64(out var ov)) outTok = ov;
            }
            var issues = ExtractIssues(contentText);
            return new VisionResult(true, contentText, issues, model, inTok, outTok);
        }
        catch (Exception ex)
        {
            return new VisionResult(false, $"Parse error: {ex.Message}", []);
        }
    }

    /// <summary>
    /// Parses a Gemini vision response (candidates[0].content.parts).
    /// v1.91: usageMetadata + "google/"-prefixad modell för prissättning.
    /// </summary>
    internal static VisionResult ParseGeminiVisionResponse(string body, string? requestModel = null)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
                return new VisionResult(false, "No candidates in response", []);

            var content = new StringBuilder();
            if (candidates[0].TryGetProperty("content", out var contentObj) &&
                contentObj.TryGetProperty("parts", out var parts) &&
                parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("text", out var text))
                        content.Append(text.GetString());
                }
            }

            var contentText = content.ToString();
            if (string.IsNullOrWhiteSpace(contentText))
                return new VisionResult(false, "Empty response from vision model", []);

            var model = root.TryGetProperty("modelVersion", out var mv) && mv.ValueKind == JsonValueKind.String
                ? mv.GetString() : requestModel;
            if (model is { Length: > 0 } && !model.Contains('/'))
                model = "google/" + model;
            long inTok = 0, outTok = 0;
            if (root.TryGetProperty("usageMetadata", out var um) && um.ValueKind == JsonValueKind.Object)
            {
                if (um.TryGetProperty("promptTokenCount", out var p) && p.TryGetInt64(out var pv)) inTok = pv;
                if (um.TryGetProperty("candidatesTokenCount", out var q) && q.TryGetInt64(out var qv)) outTok = qv;
            }
            var issues = ExtractIssues(contentText);
            return new VisionResult(true, contentText, issues, model, inTok, outTok);
        }
        catch (Exception ex)
        {
            return new VisionResult(false, $"Parse error: {ex.Message}", []);
        }
    }

    /// <summary>
    /// Extracts a list of issues or anomalies from the model's analysis text.
    /// Looks for bullet points, numbered items, or labeled sections that
    /// describe visual bugs, rendering issues, or anomalies.
    /// </summary>
    private static List<string> ExtractIssues(string analysis)
    {
        var issues = new List<string>();
        if (string.IsNullOrWhiteSpace(analysis))
            return issues;

        var lines = analysis.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and section headers
            if (trimmed.Length == 0) continue;

            // Match bullet points, numbered items, or checkmark/emoji-prefixed lines
            // that look like issue descriptions
            if (IsIssueLine(trimmed))
            {
                // Clean up the prefix
                var cleaned = CleanIssueLine(trimmed);
                if (cleaned.Length > 0 && !issues.Contains(cleaned))
                    issues.Add(cleaned);
            }
        }

        return issues;
    }

    private static bool IsIssueLine(string line)
    {
        // Bullet points, numbered items, emoji indicators, or labeled issues
        return line.StartsWith("- ") || line.StartsWith("* ") ||
               line.StartsWith("• ") ||
               (line.Length > 2 && char.IsDigit(line[0]) && (line[1] == '.' || line[1] == ')')) ||
               line.StartsWith("[ ]") || line.StartsWith("[x]") || line.StartsWith("[X]") ||
               line.StartsWith("⚠") || line.StartsWith("❌") || line.StartsWith("✅") ||
               line.StartsWith("🔴") || line.StartsWith("🟡") || line.StartsWith("🟢") ||
               line.StartsWith("Issue") || line.StartsWith("Bug") ||
               line.StartsWith("Anomaly") || line.StartsWith("Visual");
    }

    private static string CleanIssueLine(string line)
    {
        var cleaned = line.TrimStart(' ', '-', '*', '•', '\t');
        cleaned = cleaned.TrimStart('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
        cleaned = cleaned.TrimStart('.', ')', ' ', '\t');
        // Remove emoji prefixes
        while (cleaned.Length > 0 && char.IsSurrogate(cleaned[0]))
            cleaned = cleaned.Length > 1 ? cleaned[2..] : cleaned[1..];
        cleaned = cleaned.TrimStart(' ', ':', '\t');
        return cleaned;
    }

    /// <summary>
    /// Determines the MIME type for an image file based on its extension.
    /// Defaults to "image/png" for unknown extensions.
    /// </summary>
    private static string GetMediaType(string path)
    {
        var ext = Path.GetExtension(path)?.ToLowerInvariant() ?? "";
        return ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".webp" => "image/webp",
            _ => "image/png"
        };
    }
}