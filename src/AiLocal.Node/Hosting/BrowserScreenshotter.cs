using System.Diagnostics;

namespace AiLocal.Node.Hosting;

public sealed record BrowserShotResult(bool Success, string Output, string? ImagePath);

/// <summary>
/// Renders an HTML file in REAL Chromium headless and captures a PNG - the
/// visual half of playtesting. Jint-smoke (GameRuntimeSmokeTester) proves the
/// code runs; this proves the game actually DRAWS something a player would
/// see. Uses Edge (present on every Windows 10/11, same engine family as
/// WebView2) with Chrome as fallback - zero installs, zero visible windows.
/// --virtual-time-budget fast-forwards timers/RAF so the shot shows the game
/// a few seconds in, not a blank first frame.
/// </summary>
public class BrowserScreenshotter
{
    private static readonly string[] KnownBrowsers =
    [
        @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        @"C:\Program Files\Google\Chrome\Application\chrome.exe",
        @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
    ];

    public static string? FindBrowser()
    {
        foreach (var path in KnownBrowsers)
            if (File.Exists(path))
                return path;

        // Registrets App Paths är den kanoniska uppslagningen - fångar
        // per-user-Edge och ovanliga installationsvägar (utvecklingsmaskinen
        // saknade Edge på BÅDA standardvägarna; bara Chrome fanns).
        foreach (var exe in new[] { "msedge.exe", "chrome.exe" })
        {
            try
            {
                if (Microsoft.Win32.Registry.GetValue(
                        $@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{exe}",
                        null, null) is string value && File.Exists(value))
                    return value;
            }
            catch
            {
                // Registret otillgängligt (icke-Windows/behörighet) - fall igenom.
            }
        }

        var localEdge = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Microsoft", "Edge", "Application", "msedge.exe");
        return File.Exists(localEdge) ? localEdge : null;
    }

    public virtual async Task<BrowserShotResult> CaptureHtmlAsync(
        string htmlPath, string outputPng, TimeSpan virtualTime, CancellationToken ct)
    {
        var browser = FindBrowser();
        if (browser is null)
            return new(false, "Ingen Chromium-webbläsare (Edge/Chrome) hittades - hoppar över visuell skärmdump.", null);
        if (!File.Exists(htmlPath))
            return new(false, $"HTML-filen saknas: {htmlPath}", null);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPng))!);
        try { File.Delete(outputPng); } catch { /* föregående körning - best effort */ }

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
        psi.ArgumentList.Add("--hide-scrollbars");
        psi.ArgumentList.Add("--mute-audio");
        // Egen engångsprofil: annars kan en redan igång Edge kapa anropet
        // (öppnar flik i den KÖRANDE instansen och avslutar utan skärmdump).
        var profileDir = Path.Combine(Path.GetTempPath(), "ailocal-shot-" + Guid.NewGuid().ToString("n"));
        psi.ArgumentList.Add($"--user-data-dir={profileDir}");
        psi.ArgumentList.Add("--window-size=1280,800");
        psi.ArgumentList.Add($"--virtual-time-budget={(int)virtualTime.TotalMilliseconds}");
        psi.ArgumentList.Add($"--screenshot={Path.GetFullPath(outputPng)}");
        psi.ArgumentList.Add(new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri);

        try
        {
            using var process = new Process { StartInfo = psi };
            process.Start();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(45));
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                try { process.Kill(true); } catch { /* best effort */ }
                return new(false, "Webbläsaren svarade inte inom 45 s - skärmdumpen hoppas över.", null);
            }

            var ok = File.Exists(outputPng) && new FileInfo(outputPng).Length > 0;
            return ok
                ? new(true, $"Skärmdump tagen ({new FileInfo(outputPng).Length / 1024} kB) efter {virtualTime.TotalSeconds:0} s virtuell speltid.", outputPng)
                : new(false, $"Webbläsaren avslutade (kod {process.ExitCode}) utan att producera en skärmdump.", null);
        }
        catch (Exception ex)
        {
            return new(false, $"Skärmdump misslyckades: {ex.Message}", null);
        }
        finally
        {
            try { if (Directory.Exists(profileDir)) Directory.Delete(profileDir, recursive: true); } catch { /* best effort */ }
        }
    }
}
