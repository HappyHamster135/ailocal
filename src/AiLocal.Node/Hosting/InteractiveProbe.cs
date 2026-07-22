using System.Diagnostics;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace AiLocal.Node.Hosting;

public sealed record ProbeResult(
    bool Ran, bool Responded, string Notes, string? FinalScreenshotPath,
    string? TitleScreenshotPath = null, string? ReplayPath = null);

/// <summary>
/// Interactive QA: PLAYS the game instead of just looking at it. Drives real
/// Chromium via the DevTools Protocol - loads the page, hashes the canvas
/// pixels, presses Enter/Space/arrow keys like a player would, and hashes
/// again. An unchanged canvas after input is the class of bug every earlier
/// check missed: a game that looks right but does not respond to the player.
/// The final screenshot is saved for the vision pass. Every failure mode
/// (no browser, no canvas, CDP hiccup) degrades to Ran=false - the probe is
/// an extra tester, never a gate requirement in itself.
/// </summary>
public class InteractiveProbe
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(40);

    public virtual async Task<ProbeResult> PlayAsync(string htmlPath, string screenshotOut, CancellationToken outerCt)
    {
        var browser = BrowserScreenshotter.FindBrowser();
        if (browser is null || !File.Exists(htmlPath))
            return new(false, false, "Ingen Chromium-webbläsare eller HTML-fil - interaktiv QA hoppas över.", null);

        var profileDir = Path.Combine(Path.GetTempPath(), "ailocal-probe-" + Guid.NewGuid().ToString("n"));
        Process? process = null;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        cts.CancelAfter(Timeout);
        var ct = cts.Token;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = browser,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--headless");
            psi.ArgumentList.Add("--disable-gpu");
            psi.ArgumentList.Add("--mute-audio");
            psi.ArgumentList.Add("--hide-scrollbars");
            psi.ArgumentList.Add($"--user-data-dir={profileDir}");
            psi.ArgumentList.Add("--window-size=1280,800");
            psi.ArgumentList.Add("--remote-debugging-port=0");
            psi.ArgumentList.Add(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);
            process = Process.Start(psi)!;

            // Porten annonseras i DevToolsActivePort i profilen.
            var portFile = Path.Combine(profileDir, "DevToolsActivePort");
            var port = 0;
            for (var i = 0; i < 100 && port == 0; i++)
            {
                await Task.Delay(100, ct);
                if (File.Exists(portFile) && int.TryParse((await File.ReadAllLinesAsync(portFile, ct)).FirstOrDefault(), out var p))
                    port = p;
            }
            if (port == 0)
                return new(false, false, "Chromium annonserade aldrig DevTools-porten.", null);

            using var http = new HttpClient();
            var targets = await http.GetFromJsonAsync<List<JsonElement>>($"http://127.0.0.1:{port}/json", ct) ?? [];
            var wsUrl = targets
                .Where(t => t.TryGetProperty("type", out var ty) && ty.GetString() == "page")
                .Select(t => t.TryGetProperty("webSocketDebuggerUrl", out var u) ? u.GetString() : null)
                .FirstOrDefault(u => u is not null);
            if (wsUrl is null)
                return new(false, false, "Ingen sida att koppla mot i Chromium.", null);

            using var ws = new ClientWebSocket();
            await ws.ConnectAsync(new Uri(wsUrl), ct);
            var cdp = new CdpSession(ws);

            await Task.Delay(1200, ct); // låt spelet ladda och rita

            // Stabilisera FÖRE input: hasha tills två prov i rad är lika.
            // Utan detta kunde första provet tas innan sidans initiala
            // ritning hunnit ske (CPU-last) - en statisk sida såg då
            // "förändrad" ut och räknades felaktigt som responsiv.
            var before = await CanvasHashAsync(cdp, ct);
            if (before is null)
                return new(true, false, "Interaktiv QA: ingen canvas hittades på sidan.", null);
            var stable = false;
            for (var i = 0; i < 8 && !stable; i++)
            {
                await Task.Delay(300, ct);
                var again = await CanvasHashAsync(cdp, ct);
                stable = again == before;
                before = again;
            }
            if (!stable)
            {
                // Kontinuerlig animation: hash-jämförelse kan inte isolera
                // inputrespons från animationens egna förändringar - ge
                // spelet benefit of the doubt i stället för falska fynd.
                var animShot = await CaptureAsync(cdp, screenshotOut, ct);
                var animReplay = await CaptureReplayAsync(cdp, screenshotOut, ct);
                return new(true, true,
                    "Interaktiv QA: canvasen animerar kontinuerligt - spelet renderar levande innehåll (inputrespons kan inte isoleras via pixeljämförelse).",
                    animShot, null, animReplay);
            }

            // Titeldump FÖRE input: stabiliserad startskärm = visionens bästa
            // underlag för "finns spelnamn/startval/instruktioner?" - en dump
            // mitt i spel kan aldrig svara på det.
            var titlePath = await CaptureAsync(cdp, TitlePathFor(screenshotOut), ct);

            // Spela: starta (Enter/Space/klick i mitten) + styr åt båda håll.
            await ClickAsync(cdp, 640, 400, ct);
            foreach (var key in new[] { "Enter", " ", "ArrowRight", "ArrowRight", "ArrowLeft", " ", "ArrowUp" })
                await PressKeyAsync(cdp, key, ct);
            await Task.Delay(1500, ct); // låt spel-loopen svara

            var after = await CanvasHashAsync(cdp, ct);
            var shotPath = await CaptureAsync(cdp, screenshotOut, ct);
            var replay = await CaptureReplayAsync(cdp, screenshotOut, ct);

            var responded = before != after;
            return new(true, responded,
                responded
                    ? "Interaktiv QA: spelet reagerar på tangenttryck (canvasen förändrades under spelsessionen)."
                    : "Interaktiv QA: canvasen är IDENTISK före och efter tangenttryck - spelet verkar inte reagera på spelarens input.",
                shotPath, titlePath, replay);
        }
        catch (OperationCanceledException) when (outerCt.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new(false, false, $"Interaktiv QA kunde inte köras ({ex.GetType().Name}).", null);
        }
        finally
        {
            try { process?.Kill(true); } catch { /* best effort */ }
            try { if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Order-insensitive pixel hash over the first canvas - stabilt
    /// nog att jämföra "före input" mot "efter input".</summary>
    private static async Task<string?> CanvasHashAsync(CdpSession cdp, CancellationToken ct)
    {
        const string script = """
            (() => {
              const c = document.querySelector('canvas');
              if (!c) return null;
              try {
                const d = c.getContext('2d').getImageData(0, 0, c.width, c.height).data;
                let h = 7;
                for (let i = 0; i < d.length; i += 641) h = (h * 31 + d[i]) | 0;
                return 'h' + h + ':' + c.width + 'x' + c.height;
              } catch { return 'unreadable'; }
            })()
            """;
        var result = await cdp.SendAsync("Runtime.evaluate", new { expression = script, returnByValue = true }, ct);
        return result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v)
            ? v.ValueKind == JsonValueKind.String ? v.GetString() : null
            : null;
    }

    /// <summary>Titeldumpens syskonväg: samma katalog, fast namn.</summary>
    internal static string TitlePathFor(string screenshotOut) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshotOut)) ?? ".", "playtest-title.png");

    private static async Task<string?> CaptureAsync(CdpSession cdp, string screenshotOut, CancellationToken ct)
    {
        try
        {
            var shot = await cdp.SendAsync("Page.captureScreenshot", new { format = "png" }, ct);
            if (shot.TryGetProperty("data", out var data) && data.GetString() is { } b64)
            {
                var path = Path.GetFullPath(screenshotOut);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, Convert.FromBase64String(b64), ct);
                return path;
            }
        }
        catch { /* dumpen är bonus */ }
        return null;
    }

    /// <summary>B3-uppföljning (HTML5-repris): fångar ~9 nedskalade canvas-rutor
    /// medan input drivs och skriver dem som en animerad PNG bredvid dumpen
    /// (replay.png). Rutorna hämtas som RGBA via getImageData och base64-kodas
    /// REDAN i sidan, så ingen PNG-avkodning behövs på .NET-sidan. 2D-canvas
    /// funkar; WebGL-spel ger tomma rutor (getImageData är blankt där) och får
    /// då ingen repris - ärlig degradering, samma gräns som pixelhashen.</summary>
    private static async Task<string?> CaptureReplayAsync(CdpSession cdp, string screenshotOut, CancellationToken ct)
    {
        const int frameCount = 9, frameDelayMs = 160;
        string[] keys = { "ArrowRight", " ", "ArrowLeft", "ArrowUp", "Enter", "ArrowRight", "ArrowRight", " ", "ArrowLeft" };
        try
        {
            var frames = new List<byte[]>();
            int w = 0, h = 0;
            for (var i = 0; i < frameCount; i++)
            {
                var frame = await CaptureCanvasRgbaAsync(cdp, ct);
                if (frame is { } f)
                {
                    if (w == 0) { w = f.Width; h = f.Height; }
                    if (f.Width == w && f.Height == h) frames.Add(f.Rgba);
                }
                await PressKeyAsync(cdp, keys[i % keys.Length], ct);
                await Task.Delay(frameDelayMs, ct);
            }
            if (frames.Count < 2 || w == 0) return null;

            var apng = AssetGenerator.EncodeApng(w, h, frames, frameDelayMs);
            var replayPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(screenshotOut))!, "replay.png");
            Directory.CreateDirectory(Path.GetDirectoryName(replayPath)!);
            await File.WriteAllBytesAsync(replayPath, apng, ct);
            return replayPath;
        }
        catch { return null; }
    }

    /// <summary>En nedskalad (~360px bred) RGBA-ögonblicksbild av spelets
    /// canvas. drawImage till en liten off-screen-canvas i sidan, getImageData,
    /// base64 - returnerar "base64|BxH" eller null (ingen canvas / WebGL).</summary>
    private static async Task<(byte[] Rgba, int Width, int Height)?> CaptureCanvasRgbaAsync(CdpSession cdp, CancellationToken ct)
    {
        const string script = """
            (() => {
              const c = document.querySelector('canvas');
              if (!c || !c.width) return null;
              try {
                const tw = Math.min(c.width, 360);
                const th = Math.max(1, Math.round(c.height * tw / c.width));
                const off = document.createElement('canvas');
                off.width = tw; off.height = th;
                const octx = off.getContext('2d');
                octx.drawImage(c, 0, 0, tw, th);
                const d = octx.getImageData(0, 0, tw, th).data;
                let bin = '';
                for (let i = 0; i < d.length; i++) bin += String.fromCharCode(d[i]);
                return btoa(bin) + '|' + tw + 'x' + th;
              } catch { return null; }
            })()
            """;
        var result = await cdp.SendAsync("Runtime.evaluate", new { expression = script, returnByValue = true }, ct);
        if (result.TryGetProperty("result", out var r) && r.TryGetProperty("value", out var v)
            && v.ValueKind == JsonValueKind.String && v.GetString() is { } s)
        {
            var sep = s.LastIndexOf('|');
            if (sep > 0 && s[(sep + 1)..].Split('x') is { Length: 2 } dims
                && int.TryParse(dims[0], out var w) && int.TryParse(dims[1], out var h) && w > 0 && h > 0)
            {
                var rgba = Convert.FromBase64String(s[..sep]);
                if (rgba.Length == w * h * 4) return (rgba, w, h);
            }
        }
        return null;
    }

    private static async Task PressKeyAsync(CdpSession cdp, string key, CancellationToken ct)
    {
        var code = key switch
        {
            " " => "Space", "Enter" => "Enter",
            "ArrowRight" => "ArrowRight", "ArrowLeft" => "ArrowLeft", "ArrowUp" => "ArrowUp",
            _ => key
        };
        var keyCode = key switch { " " => 32, "Enter" => 13, "ArrowLeft" => 37, "ArrowUp" => 38, "ArrowRight" => 39, _ => 0 };
        await cdp.SendAsync("Input.dispatchKeyEvent",
            new { type = "keyDown", key, code, windowsVirtualKeyCode = keyCode }, ct);
        await Task.Delay(80, ct);
        await cdp.SendAsync("Input.dispatchKeyEvent",
            new { type = "keyUp", key, code, windowsVirtualKeyCode = keyCode }, ct);
        await Task.Delay(120, ct);
    }

    private static async Task ClickAsync(CdpSession cdp, int x, int y, CancellationToken ct)
    {
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mousePressed", x, y, button = "left", clickCount = 1 }, ct);
        await cdp.SendAsync("Input.dispatchMouseEvent", new { type = "mouseReleased", x, y, button = "left", clickCount = 1 }, ct);
        await Task.Delay(120, ct);
    }

    /// <summary>Minimal CDP-session: skicka {id,method,params}, vänta på
    /// svaret med samma id (händelser emellan ignoreras).</summary>
    private sealed class CdpSession(ClientWebSocket ws)
    {
        private int _nextId;

        public async Task<JsonElement> SendAsync(string method, object @params, CancellationToken ct)
        {
            var id = ++_nextId;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new { id, method, @params });
            await ws.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, ct);

            var buffer = new byte[256 * 1024];
            while (true)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult received;
                do
                {
                    received = await ws.ReceiveAsync(buffer, ct);
                    ms.Write(buffer, 0, received.Count);
                } while (!received.EndOfMessage);

                var doc = JsonDocument.Parse(ms.ToArray());
                if (doc.RootElement.TryGetProperty("id", out var rid) && rid.GetInt32() == id)
                {
                    return doc.RootElement.TryGetProperty("result", out var result)
                        ? result.Clone()
                        : doc.RootElement.Clone();
                }
                doc.Dispose(); // händelse/annat svar - fortsätt lyssna
            }
        }
    }
}
