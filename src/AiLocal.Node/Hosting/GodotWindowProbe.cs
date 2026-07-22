using System.Runtime.InteropServices;

namespace AiLocal.Node.Hosting;

public sealed record WindowProbeResult(
    bool Ran,
    bool Responded,
    bool ContinuouslyAnimating,
    string Notes,
    string? ScreenshotPath,
    string? TitleScreenshotPath = null,
    string? ReplayPath = null);

/// <summary>
/// Interactive QA for ENGINE games - the window-level counterpart of the
/// HTML5 CDP probe: play the running game by posting real key messages to
/// its window, capture pixels before/after, and judge "does the game react
/// to the player?". Same honesty rules as the CDP probe (v1.37/v1.38):
/// - stabilise first: if the window already changes with NO input, pixel
///   comparison cannot isolate input response from animation - the game gets
///   an honest benefit of the doubt instead of a false finding;
/// - a static window that stays identical after arrows/WASD/Enter/Space is
///   flagged: looking right but not responding is the failure class no other
///   check sees.
/// PostMessage (not SendInput) so the probe never depends on the game having
/// focus - the quality gate must not fail because the operator touched the
/// mouse. Windows-only by design; everywhere else it degrades calmly.
/// </summary>
public static class GodotWindowProbe
{
    private const double AnimationThreshold = 0.02;  // andel pixlar ändrade utan input
    private const double ResponseThreshold = 0.005;  // andel pixlar ändrade efter input

    public static async Task<WindowProbeResult> PlayAsync(
        System.Diagnostics.Process process, string screenshotPath, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return new(false, false, false, "Fönstersonden stöds bara på Windows.", null);

        try
        {
            var hwnd = await WindowCapturer.WaitForVisibleWindowAsync(process, TimeSpan.FromSeconds(15), ct);
            if (hwnd == IntPtr.Zero)
                return new(false, false, false, "Spelet visade aldrig något fönster (avslutat/headless miljö?).", null);

            // Låt titelskärmen/första scenen ritas klart.
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
            var (before, width, height) = WindowCapturer.TryCaptureRgba(hwnd);
            if (before is null)
                return new(false, false, false, "Kunde inte läsa fönstrets pixlar.", null);

            // Titeldumpen sparas direkt: startskärmen är visionens underlag
            // för "finns spelnamn/startval?", mittspelsdumpen kan inte svara.
            var titlePath = InteractiveProbe.TitlePathFor(screenshotPath);
            SavePng(titlePath, before, width, height);

            // Stabilisering: två dumpar UTAN input avgör om spelet animerar
            // kontinuerligt (då kan inputrespons inte isoleras via pixlar).
            await Task.Delay(1200, ct);
            var (idle, _, _) = WindowCapturer.TryCaptureRgba(hwnd);
            if (idle is null)
            {
                SavePng(screenshotPath, before, width, height);
                return new(true, false, false, "Andra dumpen misslyckades - ingen inputbedömning gjordes.", screenshotPath, titlePath);
            }

            var idleDiff = PixelDiffRatio(before, idle);
            if (idleDiff > AnimationThreshold)
            {
                await SendGameplayKeysAsync(hwnd, ct);
                await Task.Delay(1200, ct);
                var (final, _, _) = WindowCapturer.TryCaptureRgba(hwnd);
                SavePng(screenshotPath, final ?? idle, width, height);
                var animReplay = await CaptureReplayAsync(hwnd, screenshotPath, ct);
                return new(true, true, true,
                    $"Spelet animerar kontinuerligt ({idleDiff:P1} av pixlarna ändras utan input) - " +
                    "inputrespons kan inte isoleras via pixeljämförelse; ärligt benefit of the doubt.",
                    screenshotPath, titlePath, animReplay);
            }

            await SendGameplayKeysAsync(hwnd, ct);
            await Task.Delay(1200, ct);
            var (after, _, _) = WindowCapturer.TryCaptureRgba(hwnd);
            if (after is null)
            {
                SavePng(screenshotPath, idle, width, height);
                return new(true, false, false, "Dumpen efter tangenttrycken misslyckades.", screenshotPath, titlePath);
            }
            SavePng(screenshotPath, after, width, height);
            var replay = await CaptureReplayAsync(hwnd, screenshotPath, ct);

            var inputDiff = PixelDiffRatio(idle, after);
            var responded = inputDiff > ResponseThreshold;
            return new(true, responded, false,
                responded
                    ? $"Spelet reagerar på tangenttryck ({inputDiff:P1} av pixlarna ändrades)."
                    : "Fönstret är oförändrat efter tangenttryck (piltangenter/WASD/Enter/Space) - " +
                      "spelet verkar inte reagera på spelarens input.",
                screenshotPath, titlePath, replay);
        }
        catch (OperationCanceledException)
        {
            return new(false, false, false, "Fönstersonden avbröts (tiden tog slut).", null);
        }
        catch (Exception ex)
        {
            return new(false, false, false, $"Fönstersonden misslyckades: {ex.Message}", null);
        }
    }

    private static void SavePng(string path, byte[] rgba, int width, int height)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllBytes(path, AssetGenerator.EncodePng(width, height, rgba));
        }
        catch { /* dumpen är underlag, aldrig ett krav */ }
    }

    /// <summary>B3 (speltest-repris): fångar ~9 rutor medan input drivs och
    /// skriver dem som en animerad PNG bredvid dumpen (replay.png). Uppdrags-
    /// resultatet visar den som "så här ser ditt spel ut när det spelas" utan
    /// att öppna något. Rent additivt och bäst-möjligt: en misslyckad repris
    /// får aldrig fälla speltestet, så allt fångas i en try. Returnerar
    /// sökvägen till repriser, annars null.</summary>
    private static async Task<string?> CaptureReplayAsync(IntPtr hwnd, string screenshotPath, CancellationToken ct)
    {
        const int frameCount = 9, targetWidth = 360, frameDelayMs = 160;
        int[] keys = { 0x27, 0x44, 0x20, 0x25, 0x26, 0x0D, 0x57, 0x28, 0x27 }; // RIGHT,D,SPACE,LEFT,UP,ENTER,W,DOWN,RIGHT
        try
        {
            var frames = new List<byte[]>();
            int w = 0, h = 0;
            for (var i = 0; i < frameCount; i++)
            {
                var (frame, fw, fh) = WindowCapturer.TryCaptureRgba(hwnd);
                if (frame is not null)
                {
                    if (w == 0) { w = fw; h = fh; }
                    if (fw == w && fh == h) frames.Add(frame); // hoppa ev. storleksskiften
                }
                PostKey(hwnd, keys[i % keys.Length], down: true);
                await Task.Delay(frameDelayMs, ct);
                PostKey(hwnd, keys[i % keys.Length], down: false);
                await Task.Delay(frameDelayMs, ct);
            }
            if (frames.Count < 2 || w == 0) return null; // en enda ruta är ingen repris

            // Skala ner så reprisen blir några hundra kB, inte flera MB.
            var outW = Math.Min(w, targetWidth);
            var outH = w > targetWidth ? Math.Max(1, (int)((long)h * targetWidth / w)) : h;
            var scaled = w > targetWidth
                ? frames.Select(f => Downscale(f, w, h, outW, outH)).ToList()
                : frames;

            var apng = AssetGenerator.EncodeApng(outW, outH, scaled, frameDelayMs);
            var replayPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!, "replay.png");
            Directory.CreateDirectory(Path.GetDirectoryName(replayPath)!);
            await File.WriteAllBytesAsync(replayPath, apng, ct);
            return replayPath;
        }
        catch { return null; }
    }

    /// <summary>Enkel nearest-neighbor-nedskalning av en RGBA-buffert (repris-
    /// rutor behöver bara vara igenkännbara, inte perfekta).</summary>
    private static byte[] Downscale(byte[] rgba, int w, int h, int targetW, int targetH)
    {
        var outRgba = new byte[targetW * targetH * 4];
        for (var y = 0; y < targetH; y++)
        {
            var srcY = (int)((long)y * h / targetH);
            for (var x = 0; x < targetW; x++)
            {
                var srcX = (int)((long)x * w / targetW);
                var si = (srcY * w + srcX) * 4;
                var di = (y * targetW + x) * 4;
                outRgba[di] = rgba[si];
                outRgba[di + 1] = rgba[si + 1];
                outRgba[di + 2] = rgba[si + 2];
                outRgba[di + 3] = rgba[si + 3];
            }
        }
        return outRgba;
    }

    /// <summary>Andel provtagna pixlar som skiljer sig märkbart (kanalsumma
    /// > 30). Olika längd = helt olika (1.0).</summary>
    internal static double PixelDiffRatio(byte[] a, byte[] b)
    {
        if (a.Length != b.Length || a.Length < 4) return 1.0;
        var step = Math.Max(4, a.Length / 4 / 4000 * 4); // ~4000 pixlar
        long different = 0, samples = 0;
        for (var i = 0; i + 2 < a.Length; i += step)
        {
            var delta = Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]);
            if (delta > 30) different++;
            samples++;
        }
        return samples == 0 ? 1.0 : (double)different / samples;
    }

    /// <summary>En kort spelsession: håll höger (autorepeat), tryck övriga
    /// riktningar samt Enter/Space (ui_accept i Godot). PostMessage köar till
    /// fönstrets meddelandeloop utan att kräva fokus.</summary>
    private static async Task SendGameplayKeysAsync(IntPtr hwnd, CancellationToken ct)
    {
        const int VK_RIGHT = 0x27, VK_LEFT = 0x25, VK_UP = 0x26, VK_DOWN = 0x28,
            VK_SPACE = 0x20, VK_RETURN = 0x0D, VK_W = 0x57, VK_D = 0x44;

        for (var i = 0; i < 8; i++)
        {
            PostKey(hwnd, VK_RIGHT, down: true);
            await Task.Delay(40, ct);
        }
        PostKey(hwnd, VK_RIGHT, down: false);

        foreach (var vk in new[] { VK_LEFT, VK_UP, VK_DOWN, VK_D, VK_W, VK_SPACE, VK_RETURN })
        {
            PostKey(hwnd, vk, down: true);
            await Task.Delay(45, ct);
            PostKey(hwnd, vk, down: false);
            await Task.Delay(45, ct);
        }
    }

    private static void PostKey(IntPtr hwnd, int vk, bool down)
    {
        var scan = MapVirtualKeyW((uint)vk, 0);
        var lParam = 1u | (scan << 16);
        if (!down) lParam |= 0xC0000000;
        PostMessageW(hwnd, down ? 0x0100u : 0x0101u, (IntPtr)vk, (IntPtr)lParam);
    }

    [DllImport("user32.dll")] private static extern bool PostMessageW(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
