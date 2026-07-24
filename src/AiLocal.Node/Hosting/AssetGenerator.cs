using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Result from an asset generation operation.
/// </summary>
/// <param name="Success">Whether the generation succeeded.</param>
/// <param name="Output">Human-readable description of the result or error message.</param>
/// <param name="FilePath">Filesystem path to the generated asset, if one was written.</param>
public sealed record AssetResult(bool Success, string Output, string? FilePath);

/// <summary>
/// Generates game/app assets (images, audio, 3D models) using the Replicate API
/// as the primary backend, with a procedural fallback when no API key is configured.
///
/// Called by the agent's <c>generate_asset</c> tool. The type string is mapped to
/// a Replicate model; unsupported types fall through to the procedural sprite/wave
/// generator so the agent always gets something usable.
/// </summary>
public sealed class AssetGenerator
{
    private const string ReplicateBase = "https://api.replicate.com/v1";
    private const int MaxPollAttempts = 120;
    private const int PollIntervalMs = 1000;
    private const int HttpTimeoutSeconds = 300;

    // ── Model mappings ──────────────────────────────────────────────────────

    private static readonly Dictionary<string, string> ImageModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["image"]      = "black-forest-labs/flux-schnell",
        ["texture"]    = "black-forest-labs/flux-schnell",
        ["sprite"]     = "stability-ai/sdxl:39ed52f2a78e934b3ba6e2a89f5b1c712de7dfea535525255b1aa35c5565e08b",
        ["ui"]         = "black-forest-labs/flux-schnell",
        ["background"] = "black-forest-labs/flux-schnell",
    };

    private static readonly Dictionary<string, string> AudioModels = new(StringComparer.OrdinalIgnoreCase)
    {
        ["music"] = "meta/musicgen:671ac645ce5e552cc63a54a2bbff63fcf798043055d2dac5fc9e36a837244cf4",
        ["sfx"]   = "riffusion/riffusion:8cf61ea6c56afd61d8f5b9ffd14d7c216c0a93844ce2d82ac1c9ecc9c7f24e05",
        ["audio"] = "meta/musicgen:671ac645ce5e552cc63a54a2bbff63fcf798043055d2dac5fc9e36a837244cf4",
    };

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // Both dependencies are OPTIONAL: the session/assignment call sites build
    // this ad-hoc without DI, and the previous non-null signature had them
    // passing `null!` - which meant the very first `_logger.LogInformation`
    // (the no-API-key branch) threw NullReferenceException BEFORE the
    // procedural fallback could run, so generate_asset was dead whenever
    // REPLICATE_API_TOKEN was unset (the common case).
    private readonly IHttpClientFactory? _httpFactory;
    private readonly ILogger<AssetGenerator>? _logger;

    // Fallback client when no IHttpClientFactory is supplied - shared so an
    // agent generating many assets doesn't exhaust sockets.
    private static readonly HttpClient FallbackHttp = new() { Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds) };

    private readonly CloudImageGenerator? _cloudImages;

    public AssetGenerator(IHttpClientFactory? httpFactory = null, ILogger<AssetGenerator>? logger = null,
        CloudImageGenerator? cloudImages = null)
    {
        _httpFactory = httpFactory;
        _logger = logger;
        _cloudImages = cloudImages;
    }

    /// <summary>Stabil över processer (string.GetHashCode är randomiserad) -
    /// samma prompt ska ge samma ljud/musik varje gång.</summary>
    private static int StableSeed(string s)
    {
        unchecked
        {
            var hash = 23;
            foreach (var c in s ?? "") hash = hash * 31 + c;
            return hash;
        }
    }

    /// <summary>v2.29: rollen avgör dragtabellen (fiender får horn, röda ögon
    /// utan glans, mörkare ramper) så en fiende inte blir en omfärgad spelare.
    /// Slugen väger tyngst; annars läses beskrivningen.</summary>
    private static string RoleFor(string slug, string? prompt)
    {
        if (slug is "enemy" or "boss" or "monster" or "foe") return "enemy";
        var p = (prompt ?? "").ToLowerInvariant();
        foreach (var w in new[] { "enemy", "fiende", "monster", "boss", "skurk", "villain" })
            if (p.Contains(w, StringComparison.Ordinal)) return "enemy";
        return "player";
    }

    /// <summary>
    /// Returns the Replicate API token from the environment variable or null if unset.
    /// </summary>
    private static string? ResolveApiToken() =>
        Environment.GetEnvironmentVariable("REPLICATE_API_TOKEN");

    /// <summary>
    /// Generate an asset of the given <paramref name="type"/> using the prompt.
    /// Tries the Replicate API first; falls back to procedural generation when the
    /// API token is missing or the call fails.
    /// </summary>
    public async Task<AssetResult> GenerateAsync(
        string type, string prompt, int? width, int? height,
        string outputPath, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);

        type = type.Trim().ToLowerInvariant();
        outputPath = outputPath.Trim();

        // v2.16: pixelart-läget (":pixelart"-suffix från verktyget) - moln-
        // bilden efterbehandlas till ÄKTA pixelart + animerad .tres.
        var pixelart = type.EndsWith(":pixelart", StringComparison.Ordinal);
        if (pixelart)
            type = type[..type.LastIndexOf(':')];

        // Ensure output directory exists.
        var outDir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(outDir))
            Directory.CreateDirectory(outDir);

        // ---- v2.29: NAMNGIVEN KARAKTÄR via projektets rollista -------------
        // "character:<slug>" löser upp mot art/cast/<slug>.json. Finns figuren
        // redan returneras den OFÖRÄNDRAD - ingen ny bild målas. Det är låset
        // mot att agenten ber om "hjälten" tre gånger och får tre olika gubbar.
        if (type.StartsWith("character:", StringComparison.Ordinal))
        {
            var slug = type["character:".Length..];
            var root = CharacterCast.FindProjectRoot(outputPath);
            var bible = ArtBibleStore.Load(root)
                ?? ArtBibleStore.LoadOrCreate(root, GameScaffoldService.DetectGenre(prompt), prompt);
            var seed = VisualStyleLib.StableHash(prompt ?? slug);
            var (spec, created) = CharacterCast.Resolve(root, slug, null, RoleFor(slug, prompt), bible, seed);
            var (png, tres) = CharacterSheetBuilder.WriteInto(root, spec);
            // v2.29: hall Cast3D.gd i takt sa en nytillagd figur gar att
            // anvanda i 3D direkt, inte forst efter nasta scaffold.
            Cast3DScript.WriteInto(root);
            var full = Path.Combine(root, png);
            return new AssetResult(true,
                created
                    ? $"Karaktären '{spec.Slug}' skapad och sparad i projektets rollista ({spec.Traits.Body}, {spec.Traits.Hair}, {spec.Traits.Face}). "
                      + $"Sheet: {png}, animationer: {tres} (idle+walk). Samma id ger samma figur i alla framtida byggen."
                    : $"Karaktären '{spec.Slug}' finns redan och återanvändes OFÖRÄNDRAD ({png} / {tres}). "
                      + "Ingen ny figur genererades - det är så identiteten hålls stabil.",
                full);
        }

        // ---- Ljud och musik: alltid de lokala synthesizerarna --------------
        // Deterministiska, gratis och markant bättre än både molnvägen (som
        // krävde en env-token ingen satte) och de gamla sinuspipen.
        if (type is "sfx" or "sound" or "ljud" or "ljudeffekt")
        {
            var (ok, output, path) = SfxrGenerator.GenerateToFile(
                SfxrGenerator.CategoryFor(prompt), StableSeed(prompt), outputPath);
            return new AssetResult(ok, output, ok ? path : null);
        }
        if (type is "music" or "audio" or "musik")
        {
            var (ok, output, path) = ChiptuneComposer.GenerateToFile(
                ChiptuneComposer.MoodFor(prompt), StableSeed(prompt), outputPath);
            return new AssetResult(ok, output, ok ? path : null);
        }

        // ---- Bilder: appens EGNA nycklar (OpenRouter/Gemini) först ---------
        // Riktig spelgrafik i stället för procedurell pixel-art så fort en
        // nyckel finns; varje fel faller tyst vidare till nästa nivå.
        if (type is "image" or "texture" or "sprite" or "ui" or "background" && _cloudImages is { HasAnyKey: true })
        {
            // Pixelart-läget styr molnprompten mot det pipen behöver: EN
            // figur, centrerad, enfärgad bakgrund (flood-fillens förutsättning).
            // v2.17: be molnet om PIXELART-STIL direkt - modellerna är bra på
            // stilen i hög upplösning (platta kluster, konturer) och då blir
            // nedskalningen ren i stället för en formlös klump.
            // v2.25: bakgrunder får en EGEN promptriktning - hel scen utan
            // figurer (bakgrundsvägen behåller hela bilden, ingen flood-fill).
            var cloudPrompt = pixelart
                ? (type == "background"
                    ? prompt + ", 16-bit pixel art style game background, chunky pixels, limited color palette, "
                      + "flat cel shading, layered scenery with parallax depth, wide landscape view, no characters, no text, no watermark"
                    : prompt + ", 16-bit pixel art style, chunky pixels, limited color palette, dark outlines, "
                      + "flat cel shading, single subject centered, full body visible, plain solid light background, no text, no watermark")
                : prompt;
            var png = await _cloudImages.TryGenerateAsync(cloudPrompt, ct);
            if (png is not null)
            {
                var pngPath = Path.ChangeExtension(Path.GetFullPath(outputPath), ".png");
                if (pixelart && type == "background")
                {
                    var targetW = Math.Clamp(width ?? 240, 64, 480);
                    var plate = PixelArtPipeline.ToPixelArtBackground(png, targetW);
                    if (plate is not null)
                    {
                        await File.WriteAllBytesAsync(pngPath, plate, ct);
                        return new AssetResult(true,
                            $"Pixelart-BAKGRUND genererad (molnbild -> helbilds-pixelplatta, {targetW}px bred, opak). "
                            + "Ladda fullskärm via TextureRect (STRETCH_KEEP_ASPECT_COVERED) eller Sprite2D (centered=false + skala) - NEAREST-filtret i kitet håller pixlarna skarpa.",
                            pngPath);
                    }
                    _logger?.LogInformation("Bakgrunds-efterbehandlingen föll - sparar råbilden i stället");
                }
                else if (pixelart)
                {
                    var target = Math.Clamp(Math.Max(width ?? 48, height ?? 0), 8, 256);
                    var processed = PixelArtPipeline.ToPixelArt(png, target);
                    if (processed is not null)
                    {
                        await File.WriteAllBytesAsync(pngPath, processed, ct);
                        var tres = type == "sprite" ? TryWriteAnimatedFrames(processed, pngPath) : null;
                        return new AssetResult(true,
                            $"Äkta pixelart genererad (molnbild -> transparens/grid/palett/kontur, {target}px)."
                            + (tres is null ? "" : $" ANIMERAD: {tres} (idle+walk) - ladda i AnimatedSprite2D via load(\"res://...{tres}\")."),
                            pngPath);
                    }
                    _logger?.LogInformation("Pixelart-efterbehandlingen föll - sparar råbilden i stället");
                }
                await File.WriteAllBytesAsync(pngPath, png, ct);
                return new AssetResult(true,
                    $"Bild genererad via molnmodell med appens API-nyckel ({png.Length / 1024} kB png).", pngPath);
            }
            _logger?.LogInformation("Molnbildgenerering gav inget resultat - faller vidare för '{Type}'", type);
        }

        // v2.25: bakgrund UTAN molnbild - den procedurella pixelscenen
        // (PixelBackdrop) i stället för identicon-plattan. Pixelart-läget
        // föredrar alltid backdropen; utan Replicate-token gäller den för
        // alla bakgrunder (deterministisk: samma prompt = samma bild).
        if (type == "background" && (pixelart || string.IsNullOrWhiteSpace(ResolveApiToken())))
        {
            var bg = PixelBackdrop.Build(prompt,
                Math.Clamp(width ?? 240, 64, 480), Math.Clamp(height ?? 135, 36, 270));
            var bgPath = Path.ChangeExtension(Path.GetFullPath(outputPath), ".png");
            await File.WriteAllBytesAsync(bgPath, bg, ct);
            return new AssetResult(true,
                "Procedurell pixelart-bakgrund (deterministisk scen per prompt - tema väljs på nyckelord som skog/natt/rymd/öken/stad). "
                + "Ladda fullskärm via TextureRect (STRETCH_KEEP_ASPECT_COVERED) eller Sprite2D (centered=false + skala) - NEAREST-filtret i kitet håller pixlarna skarpa.",
                bgPath);
        }

        // Pixelart-sprite UTAN molnbild: den procedurella riggen (PixelAnimator)
        // ger en riktig animerad gubbe direkt - aldrig en stum platta.
        if (pixelart && type == "sprite")
        {
            var sheet = PixelAnimator.Build(prompt, Math.Clamp(width ?? 24, 12, 64));
            var pngPath = Path.ChangeExtension(Path.GetFullPath(outputPath), ".png");
            await File.WriteAllBytesAsync(pngPath, sheet.Png, ct);
            var tresName = TryWriteFramesTres(sheet, pngPath);
            return new AssetResult(true,
                "Procedurell animerad pixelart-sprite (idle+walk)."
                + (tresName is null ? "" : $" ANIMERAD: {tresName} - ladda i AnimatedSprite2D."), pngPath);
        }

        // Resolve the API key.
        var apiKey = ResolveApiToken();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger?.LogInformation("REPLICATE_API_TOKEN not set; falling back to procedural generation for '{Type}'", type);
            return GenerateProcedural(type, prompt, width, height, outputPath);
        }

        // Try Replicate API for known model types; fall through to procedural
        // for unsupported types.
        var result = type switch
        {
            "image" or "texture" or "sprite" or "ui" or "background"
                => await GenerateImageAsync(apiKey, type, prompt, width, height, outputPath, ct),

            "audio" or "music" or "sfx"
                => await GenerateAudioAsync(apiKey, type, prompt, outputPath, ct),

            "model3d"
                => await GenerateModel3dAsync(apiKey, prompt, outputPath, ct),

            _ => null // unknown type → procedural fallback
        };

        if (result is not null && result.Success)
            return result;

        // Replicate call failed or type unsupported → fallback.
        _logger?.LogWarning(
            "Replicate generation failed or unsupported type '{Type}'; falling back to procedural. Error: {Error}",
            type, result?.Output ?? "unsupported type");

        return GenerateProcedural(type, prompt, width, height, outputPath);
    }

    /// <summary>v2.16: EN pixelart-sprite -> animerad .tres via puppet-frames
    /// (SpriteAnimator). Returnerar .tres-filnamnet, eller null när sprite-
    /// PNG:n inte kan dekodas eller inget godot-projekt hittas.</summary>
    static string? TryWriteAnimatedFrames(byte[] spritePng, string spritePngPath)
    {
        try
        {
            var decoded = PixelArtPipeline.DecodePng(spritePng);
            if (decoded is null) return null;
            var (rgba, w, h) = decoded.Value;
            return TryWriteFramesTres(SpriteAnimator.BuildSheet(rgba, w, h), spritePngPath);
        }
        catch { return null; }
    }

    /// <summary>Skriver sheet-PNG + .tres bredvid spriten. res://-vägen kräver
    /// projektroten (project.godot letas uppåt, max 6 nivåer) - utanför ett
    /// godot-projekt hoppas .tres:en tyst över.</summary>
    static string? TryWriteFramesTres(AnimatedSpriteSheet sheet, string spritePngPath)
    {
        try
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(spritePngPath))!;
            var baseName = Path.GetFileNameWithoutExtension(spritePngPath);
            var sheetPath = Path.Combine(dir, baseName + "_sheet.png");
            File.WriteAllBytes(sheetPath, sheet.Png);

            string? rel = null;
            var probe = dir;
            for (var i = 0; i < 6 && probe is not null; i++)
            {
                if (File.Exists(Path.Combine(probe, "project.godot")))
                {
                    rel = Path.GetRelativePath(probe, sheetPath).Replace('\\', '/');
                    break;
                }
                probe = Path.GetDirectoryName(probe);
            }
            if (rel is null) return null;

            var tresPath = Path.Combine(dir, baseName + "_frames.tres");
            File.WriteAllText(tresPath, GodotSpriteFrames.Build(rel, sheet));
            return Path.GetFileName(tresPath);
        }
        catch { return null; }
    }

    // ── Replicate: Image ────────────────────────────────────────────────────

    private async Task<AssetResult> GenerateImageAsync(
        string apiKey, string type, string prompt,
        int? width, int? height, string outputPath, CancellationToken ct)
    {
        var model = ImageModels.GetValueOrDefault(type, ImageModels["image"]);
        var w = Math.Clamp(width ?? 1024, 256, 2048);
        var h = Math.Clamp(height ?? 1024, 256, 2048);

        var input = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["width"]  = w,
            ["height"] = h,
            ["num_outputs"] = 1
        };

        return await ReplicatePredictionAsync(apiKey, model, input, outputPath, ct);
    }

    // ── Replicate: Audio ────────────────────────────────────────────────────

    private async Task<AssetResult> GenerateAudioAsync(
        string apiKey, string type, string prompt,
        string outputPath, CancellationToken ct)
    {
        var model = AudioModels.GetValueOrDefault(type, AudioModels["audio"]);

        var input = new Dictionary<string, object?>
        {
            ["prompt"]       = prompt,
            ["duration"]     = 8,
            ["num_outputs"]  = 1
        };

        return await ReplicatePredictionAsync(apiKey, model, input, outputPath, ct);
    }

    // ── Replicate: 3D Model ─────────────────────────────────────────────────

    private async Task<AssetResult> GenerateModel3dAsync(
        string apiKey, string prompt,
        string outputPath, CancellationToken ct)
    {
        // Use the stability-ai/stable-zero123 model for 3D model generation.
        const string model = "stability-ai/stable-zero123:1b2a5d8c2b5c6a7d8e9f0a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c";

        // Build a prompt that describes the 3D object.
        var input = new Dictionary<string, object?>
        {
            ["prompt"] = prompt,
            ["num_outputs"] = 1
        };

        return await ReplicatePredictionAsync(apiKey, model, input, outputPath, ct);
    }

    // ── Replicate: Prediction lifecycle ──────────────────────────────────────

    /// <summary>
    /// Creates a Replicate prediction, polls until completion, and downloads
    /// the first output URL to <paramref name="outputPath"/>.
    /// </summary>
    private async Task<AssetResult> ReplicatePredictionAsync(
        string apiKey, string modelVersion,
        Dictionary<string, object?> input,
        string outputPath, CancellationToken ct)
    {
        HttpClient http;
        if (_httpFactory is not null)
        {
            http = _httpFactory.CreateClient("Replicate");
            http.Timeout = TimeSpan.FromSeconds(HttpTimeoutSeconds);
        }
        else
        {
            // Shared fallback client: timeout is preset in the field initializer
            // and must not be mutated here - HttpClient throws if Timeout is set
            // after the first request.
            http = FallbackHttp;
        }

        try
        {
            // 1) Create the prediction.
            var createPayload = new Dictionary<string, object?>
            {
                ["version"] = ExtractVersion(modelVersion),
                ["input"]   = input
            };

            using var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{ReplicateBase}/predictions");
            createRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");
            createRequest.Headers.TryAddWithoutValidation("Prefer", "wait");
            createRequest.Content = new StringContent(
                JsonSerializer.Serialize(createPayload, JsonOpts), Encoding.UTF8, "application/json");

            using var createResponse = await http.SendAsync(createRequest, ct);
            var createBody = await createResponse.Content.ReadAsStringAsync(ct);

            if (!createResponse.IsSuccessStatusCode)
            {
                var error = SummarizeReplicateError(createBody);
                return new AssetResult(false, $"Replicate create failed ({createResponse.StatusCode}): {error}", null);
            }

            using var createDoc = JsonDocument.Parse(createBody);
            var predictionId = createDoc.RootElement.TryGetProperty("id", out var idEl)
                ? idEl.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(predictionId))
                return new AssetResult(false, "Replicate returned no prediction id", null);

            // If the prediction completed synchronously (Prefer: wait), grab the
            // status immediately.
            var status = createDoc.RootElement.TryGetProperty("status", out var stEl)
                ? stEl.GetString()
                : "starting";

            // 2) Poll until done.
            string? outputUrl = null;

            for (var attempt = 0; attempt < MaxPollAttempts; attempt++)
            {
                if (status is "succeeded" or "failed" or "canceled")
                {
                    if (status == "succeeded")
                    {
                        // Grab the first output URL.
                        if (createDoc.RootElement.TryGetProperty("output", out var outEl))
                        {
                            outputUrl = outEl.ValueKind switch
                            {
                                JsonValueKind.Array when outEl.GetArrayLength() > 0
                                    => outEl[0].GetString(),
                                JsonValueKind.String
                                    => outEl.GetString(),
                                _ => null
                            };
                        }
                    }
                    break;
                }

                await Task.Delay(PollIntervalMs, ct);

                using var pollRequest = new HttpRequestMessage(HttpMethod.Get,
                    $"{ReplicateBase}/predictions/{predictionId}");
                pollRequest.Headers.TryAddWithoutValidation("Authorization", $"Bearer {apiKey}");

                using var pollResponse = await http.SendAsync(pollRequest, ct);
                var pollBody = await pollResponse.Content.ReadAsStringAsync(ct);

                if (!pollResponse.IsSuccessStatusCode)
                {
                    var error = SummarizeReplicateError(pollBody);
                    return new AssetResult(false, $"Replicate poll failed ({pollResponse.StatusCode}): {error}", null);
                }

                using var pollDoc = JsonDocument.Parse(pollBody);
                status = pollDoc.RootElement.TryGetProperty("status", out var psEl)
                    ? psEl.GetString()
                    : "unknown";

                if (status == "succeeded")
                {
                    if (pollDoc.RootElement.TryGetProperty("output", out var outEl))
                    {
                        outputUrl = outEl.ValueKind switch
                        {
                            JsonValueKind.Array when outEl.GetArrayLength() > 0
                                => outEl[0].GetString(),
                            JsonValueKind.String
                                => outEl.GetString(),
                            _ => null
                        };
                    }
                }

                // Carry the parsed doc forward so we don't need to re-parse on success.
                // For the next iteration we need to keep the latest doc.
                // We use the last poll's doc for the final result.
                if (status == "succeeded" || status == "failed")
                {
                    // Swap the create doc for the poll doc so error info is from the latest.
                    // We'll just handle this inline below.
                    createDoc.Dispose(); // dispose old
                    // Keep the poll doc alive — but we can't reassign createDoc (ref struct).
                    // Re-parse from pollBody when needed instead.
                }
            }

            if (status != "succeeded" || string.IsNullOrWhiteSpace(outputUrl))
            {
                var msg = status switch
                {
                    "failed"    => "Replicate prediction failed",
                    "canceled"  => "Replicate prediction was canceled",
                    _           => "Replicate prediction timed out after polling"
                };
                return new AssetResult(false, msg, null);
            }

            // 3) Download output.
            using var dlRequest = new HttpRequestMessage(HttpMethod.Get, outputUrl!);
            using var dlResponse = await http.SendAsync(dlRequest, ct);
            dlResponse.EnsureSuccessStatusCode();

            await using var dlStream = await dlResponse.Content.ReadAsStreamAsync(ct);
            await using var fileStream = File.Create(outputPath);
            await dlStream.CopyToAsync(fileStream, ct);

            _logger?.LogInformation("Generated asset via Replicate ({Model}): {Path}", modelVersion, outputPath);
            return new AssetResult(true, $"Generated via Replicate ({modelVersion})", outputPath);
        }
        catch (OperationCanceledException)
        {
            return new AssetResult(false, "Asset generation was canceled", null);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Replicate asset generation failed");
            return new AssetResult(false, $"Replicate error: {ex.Message}", null);
        }
    }

    // ── Procedural fallback ─────────────────────────────────────────────────

    /// <summary>
    /// Generates an asset procedurally without any external API call.
    /// Handles all known types; for 3D it returns a placeholder message since
    /// procedural 3D generation is not feasible.
    /// </summary>
    private AssetResult GenerateProcedural(
        string type, string prompt, int? width, int? height,
        string outputPath)
    {
        var ext = Path.GetExtension(outputPath)?.ToLowerInvariant();

        var w = Math.Clamp(width ?? 64, 16, 512);
        var h = Math.Clamp(height ?? 64, 16, 512);

        return type switch
        {
            "image" or "texture" or "ui"
                => WritePlaceholderPng(outputPath, w, h, prompt, isSprite: false),

            "sprite"
                => GenerateSimpleSprite(outputPath, w, h, prompt),

            "audio" or "music" or "sfx"
                => GenerateSimpleAudio(outputPath, type, prompt),

            "model3d"
                => new AssetResult(true,
                    "Procedural 3D model generation is not available. " +
                    "Provide a REPLICATE_API_TOKEN for 3D model generation via Replicate.", null),

            _ => new AssetResult(false, $"Unknown asset type '{type}'", null)
        };
    }

    /// <summary>
    /// Generates a simple colored-rectangle PNG as a placeholder sprite.
    /// Uses raw PNG byte construction with Deflate compression so no external
    /// image library is required.
    /// </summary>
    public static AssetResult GenerateSimpleSprite(
        string outputPath, int width, int height, string? prompt = null)
    {
        try
        {
            // Real pixel-art shapes (hero/enemy/coin/heart/star/tree/ship,
            // symmetric identicon fallback) instead of the old flat colored
            // rectangle - a usable sprite even fully offline.
            var bytes = CreatePixelSprite(width, height, prompt ?? "sprite");
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, bytes);

            return new AssetResult(true,
                $"Generated procedural pixel sprite ({SpriteShapeFor(prompt ?? "sprite")}): {width}x{height} PNG", outputPath);
        }
        catch (Exception ex)
        {
            return new AssetResult(false, $"Failed to generate sprite: {ex.Message}", null);
        }
    }

    // ---- Pixel-art sprites -------------------------------------------------
    // 12x12 patterns scaled nearest-neighbor to the requested size, so the
    // result reads as crisp pixel art at any resolution. Palette characters:
    // ' '=transparent, '#'=outline, 'o'=main color, '+'=highlight, '*'=accent.

    private static readonly Dictionary<string, string[]> SpritePatterns = new()
    {
        ["hero"] =
        [
            "   ####    ",
            "  #++++#   ",
            "  #+**+#   ",
            "  #++++#   ",
            "   #oo#    ",
            " ##oooo##  ",
            "#o#oooo#o# ",
            "#o#oooo#o# ",
            "   #oo#    ",
            "  #o##o#   ",
            "  #o##o#   ",
            " ##  ##    ",
        ],
        ["enemy"] =
        [
            "  ##  ##   ",
            "  #o##o#   ",
            " #oooooo#  ",
            "#oo*oo*oo# ",
            "#oooooooo# ",
            "#o+oooo+o# ",
            "#oo++++oo# ",
            " #oooooo#  ",
            "  #o##o#   ",
            " #o#  #o#  ",
            " ##    ##  ",
            "           ",
        ],
        ["coin"] =
        [
            "   ####    ",
            "  #++++#   ",
            " #+oooo+#  ",
            "#+oo**oo+# ",
            "#+o****o+# ",
            "#+o****o+# ",
            "#+oo**oo+# ",
            " #+oooo+#  ",
            "  #++++#   ",
            "   ####    ",
            "           ",
            "           ",
        ],
        ["heart"] =
        [
            " ###  ###  ",
            "#+++##+++# ",
            "#+o++o+oo# ",
            "#+oooooooo#",
            "#oooooooo# ",
            " #oooooo#  ",
            "  #oooo#   ",
            "   #oo#    ",
            "    ##     ",
            "           ",
            "           ",
            "           ",
        ],
        ["star"] =
        [
            "     #     ",
            "    #o#    ",
            "    #o#    ",
            "####o+o####",
            "#oo+++++oo#",
            " #o+++++o# ",
            "  #o+++o#  ",
            "  #o+o+o#  ",
            " #o## ##o# ",
            " ##     ## ",
            "           ",
            "           ",
        ],
        ["tree"] =
        [
            "   ####    ",
            "  #oooo#   ",
            " #oo+ooo#  ",
            "#ooo+oooo# ",
            "#oo+++ooo# ",
            " #oooooo#  ",
            "  #oooo#   ",
            "   #**#    ",
            "   #**#    ",
            "   #**#    ",
            "  #****#   ",
            "           ",
        ],
        ["ship"] =
        [
            "     #     ",
            "    #+#    ",
            "    #o#    ",
            "   #ooo#   ",
            "   #o*o#   ",
            "  #oo*oo#  ",
            "  #ooooo#  ",
            " #o#ooo#o# ",
            " #o#ooo#o# ",
            "  # #*# #  ",
            "    #*#    ",
            "           ",
        ],
    };

    internal static string SpriteShapeFor(string prompt)
    {
        var p = prompt.ToLowerInvariant();
        if (p.Contains("hero") || p.Contains("hjalte") || p.Contains("hjälte") || p.Contains("player")
            || p.Contains("spelare") || p.Contains("character") || p.Contains("karaktar") || p.Contains("gubbe")) return "hero";
        if (p.Contains("enemy") || p.Contains("fiende") || p.Contains("monster") || p.Contains("slime")
            || p.Contains("goblin") || p.Contains("boss")) return "enemy";
        if (p.Contains("coin") || p.Contains("mynt") || p.Contains("gold") || p.Contains("guld")) return "coin";
        if (p.Contains("heart") || p.Contains("hjarta") || p.Contains("hjärta") || p.Contains("liv")
            || p.Contains("health") || p.Contains("hp")) return "heart";
        if (p.Contains("star") || p.Contains("stjarna") || p.Contains("stjärna")) return "star";
        if (p.Contains("tree") || p.Contains("trad") || p.Contains("träd") || p.Contains("skog")) return "tree";
        if (p.Contains("ship") || p.Contains("skepp") || p.Contains("rocket") || p.Contains("raket")) return "ship";
        return "pattern";
    }

    internal static byte[] CreatePixelSprite(int width, int height, string prompt)
    {
        var shape = SpriteShapeFor(prompt);
        var grid = SpritePatterns.TryGetValue(shape, out var pattern)
            ? pattern
            : IdenticonPattern(prompt);

        var rows = grid.Length;
        var cols = grid.Max(r => r.Length);
        var (mr, mg, mb) = DeriveColor(prompt);
        // Palette: outline near-black, highlight lighter, accent hue-rotated.
        (byte, byte, byte) Light() => ((byte)Math.Min(255, mr + 70), (byte)Math.Min(255, mg + 70), (byte)Math.Min(255, mb + 70));
        (byte, byte, byte) Accent() => (mg, mb, mr);

        var stride = width * 4;
        var raw = new byte[height * stride];
        for (var y = 0; y < height; y++)
        {
            var gy = Math.Min(rows - 1, y * rows / Math.Max(1, height));
            var row = grid[gy];
            for (var x = 0; x < width; x++)
            {
                var gx = Math.Min(cols - 1, x * cols / Math.Max(1, width));
                var ch = gx < row.Length ? row[gx] : ' ';
                var i = y * stride + x * 4;
                if (ch == ' ') continue; // transparent
                var (r, g, b) = ch switch
                {
                    '#' => ((byte)24, (byte)24, (byte)32),
                    '+' => Light(),
                    '*' => Accent(),
                    _ => (mr, mg, mb)
                };
                raw[i] = r; raw[i + 1] = g; raw[i + 2] = b; raw[i + 3] = 255;
            }
        }
        return EncodePng(width, height, raw);
    }

    /// <summary>Deterministic, mirrored 12x12 pattern seeded by the prompt -
    /// an identicon-style sprite for prompts no named shape matches, so two
    /// different prompts still yield visually distinct art.</summary>
    private static string[] IdenticonPattern(string prompt)
    {
        var seed = 17;
        foreach (var c in prompt) seed = seed * 31 + c;
        var rnd = new Random(seed);
        var rows = new string[12];
        for (var y = 0; y < 12; y++)
        {
            var half = new char[6];
            for (var x = 0; x < 6; x++)
            {
                var v = rnd.Next(100);
                half[x] = v < 38 ? 'o' : v < 50 ? '+' : v < 58 ? '#' : ' ';
            }
            var mirrored = new char[12];
            for (var x = 0; x < 6; x++) { mirrored[x] = half[x]; mirrored[11 - x] = half[x]; }
            rows[y] = new string(mirrored);
        }
        return rows;
    }

    /// <summary>
    /// Generates a simple WAV audio file — a square-wave tone for SFX or a
    /// simple chord for music — so the agent always gets playable audio even
    /// without the Replicate API.
    /// </summary>
    private static AssetResult GenerateSimpleAudio(
        string outputPath, string type, string prompt)
    {
        try
        {
            var isMusic = type is "music" or "audio";
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            // Music: play a C-major chord; SFX: a short square-wave beep.
            var wav = isMusic
                ? MakeChordWav(220, 4.0) // deep chord, 4 seconds
                : MakeToneWav(440, 0.5); // A4 beep, 0.5 seconds

            File.WriteAllBytes(outputPath, wav);

            return new AssetResult(true,
                $"Generated procedural {(isMusic ? "chord" : "tone")} WAV", outputPath);
        }
        catch (Exception ex)
        {
            return new AssetResult(false, $"Failed to generate audio: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Writes a placeholder PNG with a simple centered-pixel pattern.
    /// </summary>
    private static AssetResult WritePlaceholderPng(
        string outputPath, int width, int height, string prompt, bool isSprite)
    {
        try
        {
            var bytes = CreatePlaceholderPng(width, height, DeriveColor(prompt));
            var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllBytes(outputPath, bytes);

            var label = isSprite ? "sprite" : "placeholder";
            return new AssetResult(true, $"Generated procedural {label}: {width}x{height} PNG", outputPath);
        }
        catch (Exception ex)
        {
            return new AssetResult(false, $"Failed to write placeholder: {ex.Message}", null);
        }
    }

    // ── PNG construction ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a valid PNG file in memory. The image is a solid rectangle
    /// shaded with the derived color, with a simple cross-hatch or border
    /// pattern so it is visually identifiable as a placeholder.
    /// </summary>
    private static byte[] CreatePlaceholderPng(int width, int height, (byte R, byte G, byte B) color)
    {
        // Generate raw RGBA pixel data (top-down, unpremultiplied).
        var stride = width * 4;
        var raw = new byte[height * stride];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * stride + x * 4;
                var (r, g, b) = color;

                // Draw a border (4px) in a darker shade.
                var isBorder = x < 4 || x >= width - 4 || y < 4 || y >= height - 4;
                // Draw a cross (every 8th pixel) for visual texture.
                var isCross = (x + y) % 16 < 2 || (x - y + height) % 16 < 2;

                if (isBorder)
                {
                    r = (byte)(r * 0.6);
                    g = (byte)(g * 0.6);
                    b = (byte)(b * 0.6);
                }
                else if (isCross && width > 32 && height > 32)
                {
                    r = (byte)Math.Min(255, r + 40);
                    g = (byte)Math.Min(255, g + 40);
                    b = (byte)Math.Min(255, b + 40);
                }

                raw[i]     = r;
                raw[i + 1] = g;
                raw[i + 2] = b;
                raw[i + 3] = 255;
            }
        }

        return EncodePng(width, height, raw);
    }

    /// <summary>
    /// Encodes raw RGBA pixel data into a valid PNG file using the minimal
    /// required chunks (IHDR, IDAT, IEND) with Deflate compression.
    /// </summary>
    internal static byte[] EncodePng(int width, int height, byte[] rawRgba)
    {
        using var ms = new MemoryStream();

        // PNG signature.
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

        // IHDR chunk.
        WriteChunk(ms, "IHDR", () =>
        {
            WriteBigEndian(ms, width);
            WriteBigEndian(ms, height);
            ms.WriteByte(8);       // bit depth
            ms.WriteByte(6);       // color type: RGBA
            ms.WriteByte(0);       // compression
            ms.WriteByte(0);       // filter
            ms.WriteByte(0);       // interlace
        });

        // IDAT chunk: filter bytes + compressed pixel data.
        // Each scanline starts with a filter byte (0 = None).
        var stride = width * 4;
        var filtered = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            filtered[y * (stride + 1)] = 0; // filter: None
            Buffer.BlockCopy(rawRgba, y * stride, filtered, y * (stride + 1) + 1, stride);
        }

        var compressed = DeflateCompress(filtered);

        WriteChunk(ms, "IDAT", () => ms.Write(compressed, 0, compressed.Length));

        // IEND chunk.
        WriteChunk(ms, "IEND", () => { });

        return ms.ToArray();
    }

    /// <summary>
    /// Encodes RGBA frames as an animated PNG (APNG) - reuses the exact
    /// truecolor PNG pipeline above (deflate + real Adler-32 + CRC) so it needs
    /// no palette or LZW like a GIF would. Browsers animate it inline in an
    /// &lt;img&gt;; non-APNG viewers just see the first frame. All frames must
    /// share width x height. A single frame degrades to a plain PNG.
    /// </summary>
    internal static byte[] EncodeApng(int width, int height, IReadOnlyList<byte[]> framesRgba, int frameDelayMs)
    {
        if (framesRgba is null || framesRgba.Count == 0)
            throw new ArgumentException("minst en bildruta krävs", nameof(framesRgba));
        if (framesRgba.Count == 1)
            return EncodePng(width, height, framesRgba[0]);

        using var ms = new MemoryStream();
        ms.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, 0, 8);

        WriteChunk(ms, "IHDR", () =>
        {
            WriteBigEndian(ms, width);
            WriteBigEndian(ms, height);
            ms.WriteByte(8);   // bit depth
            ms.WriteByte(6);   // color type: RGBA
            ms.WriteByte(0);   // compression
            ms.WriteByte(0);   // filter
            ms.WriteByte(0);   // interlace
        });

        // acTL MÅSTE ligga före första IDAT: antal rutor + antal spelningar
        // (0 = oändlig loop).
        WriteChunk(ms, "acTL", () =>
        {
            WriteBigEndian(ms, framesRgba.Count);
            WriteBigEndian(ms, 0u);
        });

        var delayNum = (ushort)Math.Clamp(frameDelayMs, 1, 65535);
        uint seq = 0;

        for (var i = 0; i < framesRgba.Count; i++)
        {
            var fcTlSeq = seq++;
            WriteChunk(ms, "fcTL", () =>
            {
                WriteBigEndian(ms, fcTlSeq);
                WriteBigEndian(ms, width);
                WriteBigEndian(ms, height);
                WriteBigEndian(ms, 0);   // x_offset
                WriteBigEndian(ms, 0);   // y_offset
                ms.WriteByte((byte)(delayNum >> 8)); ms.WriteByte((byte)delayNum);  // delay_num
                ms.WriteByte(0x03); ms.WriteByte(0xE8);   // delay_den = 1000 -> num/1000 s
                ms.WriteByte(0);   // dispose_op = NONE
                ms.WriteByte(0);   // blend_op = SOURCE (rutan ersätter helt)
            });

            var compressed = CompressScanlines(width, height, framesRgba[i]);
            if (i == 0)
            {
                WriteChunk(ms, "IDAT", () => ms.Write(compressed, 0, compressed.Length));
            }
            else
            {
                var fdatSeq = seq++;
                WriteChunk(ms, "fdAT", () =>
                {
                    WriteBigEndian(ms, fdatSeq);
                    ms.Write(compressed, 0, compressed.Length);
                });
            }
        }

        WriteChunk(ms, "IEND", () => { });
        return ms.ToArray();
    }

    /// <summary>Filter-prefixar (None) och deflate-komprimerar en RGBA-ruta -
    /// samma bytes som IDAT/fdAT vill ha.</summary>
    private static byte[] CompressScanlines(int width, int height, byte[] rgba)
    {
        var stride = width * 4;
        var filtered = new byte[height * (stride + 1)];
        for (var y = 0; y < height; y++)
        {
            filtered[y * (stride + 1)] = 0; // filter: None
            Buffer.BlockCopy(rgba, y * stride, filtered, y * (stride + 1) + 1, stride);
        }
        return DeflateCompress(filtered);
    }

    /// <summary>
    /// Compresses data using Deflate (zlib wrapper).
    /// </summary>
    private static byte[] DeflateCompress(byte[] data)
    {
        using var output = new MemoryStream();
        // Write a minimal zlib header (deflate, window bits 15, no dict, default compression).
        output.WriteByte(0x78);
        output.WriteByte(0x9C);
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(data, 0, data.Length);
        }
        // zlib trailer: the REAL Adler-32 of the uncompressed data, big-endian.
        // A dummy 0-checksum here decodes fine in lenient readers (browsers) but
        // strict libpng (Godot) rejects the whole PNG as ERR_FILE_CORRUPT - so
        // every generated sprite was technically corrupt until now.
        uint a = 1, b = 0;
        foreach (var by in data)
        {
            a = (a + by) % 65521;
            b = (b + a) % 65521;
        }
        var adler = (b << 16) | a;
        output.WriteByte((byte)(adler >> 24));
        output.WriteByte((byte)(adler >> 16));
        output.WriteByte((byte)(adler >> 8));
        output.WriteByte((byte)adler);
        return output.ToArray();
    }

    /// <summary>
    /// Writes a PNG chunk: length, type, data, CRC.
    /// </summary>
    private static void WriteChunk(MemoryStream ms, string type, Action writeData)
    {
        var dataStart = ms.Position;
        // Skip 4 bytes for length (we'll patch it later).
        ms.Write(new byte[4], 0, 4);
        var typeBytes = Encoding.ASCII.GetBytes(type);
        ms.Write(typeBytes, 0, 4);

        var beforeData = ms.Position;
        writeData();
        var afterData = ms.Position;

        var dataLen = (int)(afterData - beforeData);

        // Go back and write the length.
        var currentPos = ms.Position;
        ms.Position = dataStart;
        WriteBigEndian(ms, dataLen);
        ms.Position = currentPos;

        // Compute CRC over type + data.
        var crcInput = new byte[4 + dataLen];
        Array.Copy(typeBytes, 0, crcInput, 0, 4);
        ms.Position = beforeData;
        ms.Read(crcInput, 4, dataLen);
        ms.Position = afterData;

        var crc = ComputeCrc32(crcInput);
        WriteBigEndian(ms, crc);
    }

    /// <summary>
    /// Simplified CRC-32 (PKZIP variant) for PNG chunk validation.
    /// Uses a precomputed lookup table for performance.
    /// </summary>
    private static uint ComputeCrc32(byte[] data)
    {
        // Precomputed CRC-32 table (PKZIP).
        ReadOnlySpan<uint> table =
        [
            0x00000000, 0x77073096, 0xEE0E612C, 0x990951BA, 0x076DC419, 0x706AF48F,
            0xE963A535, 0x9E6495A3, 0x0EDB8832, 0x79DCB8A4, 0xE0D5E91E, 0x97D2D988,
            0x09B64C2B, 0x7EB17CBD, 0xE7B82D07, 0x90BF1D91, 0x1DB71064, 0x6AB020F2,
            0xF3B97148, 0x84BE41DE, 0x1ADAD47D, 0x6DDDE4EB, 0xF4D4B551, 0x83D385C7,
            0x136C9856, 0x646BA8C0, 0xFD62F97A, 0x8A65C9EC, 0x14015C4F, 0x63066CD9,
            0xFA0F3D63, 0x8D080DF5, 0x3B6E20C8, 0x4C69105E, 0xD56041E4, 0xA2677172,
            0x3C03E4D1, 0x4B04D447, 0xD20D85FD, 0xA50AB56B, 0x35B5A8FA, 0x42B2986C,
            0xDBBBC9D6, 0xACBCF940, 0x32D86CE3, 0x45DF5C75, 0xDCD60DCF, 0xABD13D59,
            0x26D930AC, 0x51DE003A, 0xC8D75180, 0xBFD06116, 0x21B4F4B5, 0x56B3C423,
            0xCFBA9599, 0xB8BDA50F, 0x2802B89E, 0x5F058808, 0xC60CD9B2, 0xB10BE924,
            0x2F6F7C87, 0x58684C11, 0xC1611DAB, 0xB6662D3D, 0x76DC4190, 0x01DB7106,
            0x98D220BC, 0xEFD5102A, 0x71B18589, 0x06B6B51F, 0x9FBFE4A5, 0xE8B8D433,
            0x7807C9A2, 0x0F00F934, 0x9609A88E, 0xE10E9818, 0x7F6A0DBB, 0x086D3D2D,
            0x91646C97, 0xE6635C01, 0x6B6B51F4, 0x1C6C6162, 0x856530D8, 0xF262004E,
            0x6C0695ED, 0x1B01A57B, 0x8208F4C1, 0xF50FC457, 0x65B0D9C6, 0x12B7E950,
            0x8BBEB8EA, 0xFCB9887C, 0x62DD1DDF, 0x15DA2D49, 0x8CD37CF3, 0xFBD44C65,
            0x4DB26158, 0x3AB551CE, 0xA3BC0074, 0xD4BB30E2, 0x4ADFA541, 0x3DD895D7,
            0xA4D1C46D, 0xD3D6F4FB, 0x4369E96A, 0x346ED9FC, 0xAD678846, 0xDA60B8D0,
            0x44042D73, 0x33031DE5, 0xAA0A4C5F, 0xDD0D7CC9, 0x5005713C, 0x270241AA,
            0xBE0B1010, 0xC90C2086, 0x5768B525, 0x206F85B3, 0xB966D409, 0xCE61E49F,
            0x5EDEF90E, 0x29D9C998, 0xB0D09822, 0xC7D7A8B4, 0x59B33D17, 0x2EB40D81,
            0xB7BD5C3B, 0xC0BA6CAD, 0xEDB88320, 0x9ABFB3B6, 0x03B6E20C, 0x74B1D29A,
            0xEAD54739, 0x9DD277AF, 0x04DB2615, 0x73DC1683, 0xE3630B12, 0x94643B84,
            0x0D6D6A3E, 0x7A6A5AA8, 0xE40ECF0B, 0x9309FF9D, 0x0A00AE27, 0x7D079EB1,
            0xF00F9344, 0x8708A3D2, 0x1E01F268, 0x6906C2FE, 0xF762575D, 0x806567CB,
            0x196C3671, 0x6E6B06E7, 0xFED41B76, 0x89D32BE0, 0x10DA7A5A, 0x67DD4ACC,
            0xF9B9DF6F, 0x8EBEEFF9, 0x17B7BE43, 0x60B08ED5, 0xD6D6A3E8, 0xA1D1937E,
            0x38D8C2C4, 0x4FDFF252, 0xD1BB67F1, 0xA6BC5767, 0x3FB506DD, 0x48B2364B,
            0xD80D2BDA, 0xAF0A1B4C, 0x36034AF6, 0x41047A60, 0xDF60EFC3, 0xA867DF55,
            0x316E8EEF, 0x4669BE79, 0xCB61B38C, 0xBC66831A, 0x256FD2A0, 0x5268E236,
            0xCC0C7795, 0xBB0B4703, 0x220216B9, 0x5505262F, 0xC5BA3BBE, 0xB2BD0B28,
            0x2BB45A92, 0x5CB36A04, 0xC2D7FFA7, 0xB5D0CF31, 0x2CD99E8B, 0x5BDEAE1D,
            0x9B64C2B0, 0xEC63F226, 0x756AA39C, 0x026D930A, 0x9C0906A9, 0xEB0E363F,
            0x72076785, 0x05005713, 0x95BF4A82, 0xE2B87A14, 0x7BB12BAE, 0x0CB61B38,
            0x92D28E9B, 0xE5D5BE0D, 0x7CDCEFB7, 0x0BDBDF21, 0x86D3D2D4, 0xF1D4E242,
            0x68DDB3F8, 0x1FDA836E, 0x81BE16CD, 0xF6B9265B, 0x6FB077E1, 0x18B74777,
            0x88085AE6, 0xFF0F6A70, 0x66063BCA, 0x11010B5C, 0x8F659EFF, 0xF862AE69,
            0x616BFFD3, 0x166CCF45, 0xA00AE278, 0xD70DD2EE, 0x4E048354, 0x3903B3C2,
            0xA7672661, 0xD06016F7, 0x4969474D, 0x3E6E77DB, 0xAED16A4A, 0xD9D65ADC,
            0x40DF0B66, 0x37D83BF0, 0xA9BCAE53, 0xDEBB9EC5, 0x47B2CF7F, 0x30B5FFE9,
            0xBDBDF21C, 0xCABAC28A, 0x53B39330, 0x24B4A3A6, 0xBAD03605, 0xCDD70693,
            0x54DE5729, 0x23D967BF, 0xB3667A2E, 0xC4614AB8, 0x5D681B02, 0x2A6F2B94,
            0xB40BBE37, 0xC30C8EA1, 0x5A05DF1B, 0x2D02EF8D
        ];

        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc = table[(int)((crc ^ b) & 0xFF)] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFFu;
    }

    // ── WAV construction ────────────────────────────────────────────────────

    /// <summary>
    /// Creates a minimal valid mono WAV file (16-bit PCM, 8 kHz) with a
    /// square-wave tone at the given frequency and duration.
    /// </summary>
    private static byte[] MakeToneWav(int freqHz, double seconds)
    {
        const int sampleRate = 8000;
        var n = (int)(sampleRate * seconds);
        return WriteWavPcm(n, sampleRate, (i, t) =>
            (short)(Math.Sign(Math.Sin(2 * Math.PI * freqHz * t)) * 9000));
    }

    /// <summary>
    /// Creates a WAV with a C-major chord (C4, E4, G4) square-wave, creating
    /// a fuller sound appropriate for background music.
    /// </summary>
    private static byte[] MakeChordWav(double baseFreq, double seconds)
    {
        const int sampleRate = 8000;
        var n = (int)(sampleRate * seconds);
        var freqs = new[] { baseFreq, baseFreq * 5.0 / 4.0, baseFreq * 3.0 / 2.0 };
        return WriteWavPcm(n, sampleRate, (i, t) =>
        {
            var sum = 0.0;
            foreach (var f in freqs)
                sum += Math.Sign(Math.Sin(2 * Math.PI * f * t)) * 6000;
            return (short)Math.Clamp(sum, -32768, 32767);
        });
    }

    /// <summary>
    /// Writes a 16-bit PCM mono WAV file from a sample generator function.
    /// </summary>
    private static byte[] WriteWavPcm(int sampleCount, int sampleRate, Func<int, double, short> sampleGen)
    {
        var dataSize = sampleCount * 2;
        var totalSize = 44 + dataSize;
        var wav = new byte[totalSize];

        // RIFF header.
        Encoding.ASCII.GetBytes("RIFF", 0, 4, wav, 0);
        BitConverter.GetBytes(36 + dataSize).CopyTo(wav, 4);
        Encoding.ASCII.GetBytes("WAVE", 0, 4, wav, 8);
        Encoding.ASCII.GetBytes("fmt ", 0, 4, wav, 12);
        BitConverter.GetBytes(16).CopyTo(wav, 16);                               // chunk size
        BitConverter.GetBytes((short)1).CopyTo(wav, 20);                         // PCM
        BitConverter.GetBytes((short)1).CopyTo(wav, 22);                         // mono
        BitConverter.GetBytes(sampleRate).CopyTo(wav, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(wav, 28);                   // byte rate
        BitConverter.GetBytes((short)2).CopyTo(wav, 32);                         // block align
        BitConverter.GetBytes((short)16).CopyTo(wav, 34);                        // bits per sample
        Encoding.ASCII.GetBytes("data", 0, 4, wav, 36);
        BitConverter.GetBytes(dataSize).CopyTo(wav, 40);

        // PCM data.
        for (var i = 0; i < sampleCount; i++)
        {
            var t = (double)i / sampleRate;
            var sample = sampleGen(i, t);
            BitConverter.GetBytes(sample).CopyTo(wav, 44 + i * 2);
        }

        return wav;
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Extracts just the model version hash from a "owner/name:hash" string.
    /// If there is no colon, the full string is used as-is.
    /// </summary>
    private static string ExtractVersion(string modelVersion)
    {
        var colon = modelVersion.LastIndexOf(':');
        return colon >= 0 ? modelVersion[(colon + 1)..] : modelVersion;
    }

    /// <summary>
    /// Writes a 32-bit unsigned integer in big-endian byte order (PNG standard).
    /// </summary>
    private static void WriteBigEndian(Stream stream, uint value)
    {
        Span<byte> buf = stackalloc byte[4];
        buf[0] = (byte)(value >> 24);
        buf[1] = (byte)(value >> 16);
        buf[2] = (byte)(value >> 8);
        buf[3] = (byte)value;
        stream.Write(buf);
    }

    /// <summary>
    /// Writes a 32-bit signed integer in big-endian byte order.
    /// </summary>
    private static void WriteBigEndian(Stream stream, int value) =>
        WriteBigEndian(stream, (uint)value);

    /// <summary>
    /// Derives a stable RGB color from a prompt string using a simple hash,
    /// so the same prompt always produces the same placeholder color.
    /// </summary>
    private static (byte R, byte G, byte B) DeriveColor(string prompt)
    {
        var hash = 5381;
        foreach (var c in prompt)
            hash = ((hash << 5) + hash) ^ c;

        return (
            (byte)((hash >> 16) & 0xFF),
            (byte)((hash >> 8) & 0xFF),
            (byte)(hash & 0xFF)
        );
    }

    /// <summary>
    /// Extracts the error message from a Replicate API error response body.
    /// </summary>
    private static string SummarizeReplicateError(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("detail", out var detail))
                return detail.GetString() ?? "unknown error";
            if (doc.RootElement.TryGetProperty("error", out var err))
                return err.ToString();
        }
        catch { /* not JSON */ }
        return body.Length > 200 ? body[..200] : body;
    }
}