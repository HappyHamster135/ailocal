using System.Runtime.InteropServices;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Captures a native window (a just-launched Godot/Unity game) to a PNG, so
/// the same vision review that judges HTML5 screenshots can judge engine
/// builds - before this, the quality gate could SEE web games but was blind
/// to engine games even though Godot is the default engine. Windows-only by
/// design (the node ships win-x64); everywhere else Capture degrades to a
/// calm, honest failure and the playtest continues without a screenshot.
///
/// Capture strategy: PrintWindow with PW_RENDERFULLCONTENT first (works for
/// GPU-composited windows and even partially occluded ones); if that comes
/// back essentially black (some exclusive GL/Vulkan contexts do), fall back
/// to copying the window's rectangle straight from the screen - the game was
/// just launched, so it is the foreground window.
/// </summary>
public static class WindowCapturer
{
    /// <summary>Waits for the process to show a visible main window, lets it
    /// render its first frames, then captures it. Cancellation and every
    /// failure path return a calm (false, reason, null).</summary>
    public static async Task<(bool Success, string Output, string? ImagePath)> CaptureProcessWindowAsync(
        System.Diagnostics.Process process, string outputPath, TimeSpan settle, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows())
            return (false, "Fönsterdump stöds bara på Windows.", null);

        try
        {
            var hwnd = await WaitForVisibleWindowAsync(process, TimeSpan.FromSeconds(8), ct);
            if (hwnd == IntPtr.Zero)
                return (false, "Spelprocessen visade aldrig något fönster (headless miljö/avslutad process?).", null);

            // Låt titelskärmen ritas klart innan dumpen tas.
            await Task.Delay(settle, ct);
            process.Refresh();
            if (process.HasExited)
                return (false, "Spelet avslutades innan dumpen hann tas.", null);

            return Capture(hwnd, outputPath);
        }
        catch (OperationCanceledException)
        {
            return (false, "Fönsterdumpen avbröts (tiden tog slut).", null);
        }
        catch (Exception ex)
        {
            return (false, $"Fönsterdumpen misslyckades: {ex.Message}", null);
        }
    }

    /// <summary>Väntar in ett synligt huvudfönster från processen (spel
    /// laddar först, fönstret kommer efter någon sekund). IntPtr.Zero när
    /// processen dog, inte gick att läsa, eller aldrig visade fönster.</summary>
    internal static async Task<IntPtr> WaitForVisibleWindowAsync(
        System.Diagnostics.Process process, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            IntPtr hwnd;
            try
            {
                process.Refresh();
                if (process.HasExited) return IntPtr.Zero;
                hwnd = process.MainWindowHandle;
            }
            catch
            {
                return IntPtr.Zero;
            }
            if (hwnd != IntPtr.Zero && IsWindowVisible(hwnd)) return hwnd;
            await Task.Delay(250, ct);
        }
        return IntPtr.Zero;
    }

    /// <summary>Rå RGBA-fångst av fönstret (PrintWindow → skärmkopie-fallback
    /// vid svart), utan PNG-omvägen - fönstersondens pixeljämförelser läser
    /// samma bild flera gånger per sekund.</summary>
    internal static (byte[]? Rgba, int Width, int Height) TryCaptureRgba(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            return (null, 0, 0);
        int width = rect.Right - rect.Left, height = rect.Bottom - rect.Top;
        if (width < 40 || height < 40)
            return (null, 0, 0);

        var rgba = CapturePixels(hwnd, rect, width, height, useScreenCopy: false);
        if (rgba is null || LooksBlack(rgba))
        {
            // Exklusiva GL/Vulkan-kontexter kan ge svart via PrintWindow -
            // spelet startades nyss och ligger överst, så skärmkopian ser det.
            var screenCopy = CapturePixels(hwnd, rect, width, height, useScreenCopy: true);
            if (screenCopy is not null && (rgba is null || !LooksBlack(screenCopy)))
                rgba = screenCopy;
        }
        return (rgba, width, height);
    }

    internal static (bool Success, string Output, string? ImagePath) Capture(IntPtr hwnd, string outputPath)
    {
        var (rgba, width, height) = TryCaptureRgba(hwnd);
        if (rgba is null)
            return (false, "Kunde inte läsa fönstrets pixlar (saknas/för litet fönster?).", null);

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        File.WriteAllBytes(outputPath, AssetGenerator.EncodePng(width, height, rgba));
        return (true, $"Fönsterdump {width}x{height} sparad.", outputPath);
    }

    /// <summary>BGRA-toppstyrd DIB → RGBA. useScreenCopy tar rutan direkt
    /// från skärmen i stället för via PrintWindow.</summary>
    private static byte[]? CapturePixels(IntPtr hwnd, Rect rect, int width, int height, bool useScreenCopy)
    {
        var screenDc = IntPtr.Zero;
        var memDc = IntPtr.Zero;
        var dib = IntPtr.Zero;
        try
        {
            screenDc = GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) return null;
            memDc = CreateCompatibleDC(screenDc);
            if (memDc == IntPtr.Zero) return null;

            var info = new BitmapInfo
            {
                Size = Marshal.SizeOf<BitmapInfo>(),
                Width = width,
                Height = -height, // negativ höjd = top-down, matchar PNG-encodern
                Planes = 1,
                BitCount = 32,
                Compression = 0
            };
            dib = CreateDIBSection(memDc, ref info, 0, out var bits, IntPtr.Zero, 0);
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return null;
            var previous = SelectObject(memDc, dib);

            bool ok;
            if (useScreenCopy)
                ok = BitBlt(memDc, 0, 0, width, height, screenDc, rect.Left, rect.Top, 0x00CC0020 /* SRCCOPY */);
            else
                ok = PrintWindow(hwnd, memDc, 2 /* PW_RENDERFULLCONTENT */);
            GdiFlush();
            SelectObject(memDc, previous);
            if (!ok) return null;

            var bgra = new byte[width * height * 4];
            Marshal.Copy(bits, bgra, 0, bgra.Length);
            var rgba = new byte[bgra.Length];
            for (var i = 0; i < bgra.Length; i += 4)
            {
                rgba[i] = bgra[i + 2];
                rgba[i + 1] = bgra[i + 1];
                rgba[i + 2] = bgra[i];
                rgba[i + 3] = 255; // GDI lämnar ofta alfa=0 - dumpen är alltid ogenomskinlig
            }
            return rgba;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (dib != IntPtr.Zero) DeleteObject(dib);
            if (memDc != IntPtr.Zero) DeleteDC(memDc);
            if (screenDc != IntPtr.Zero) ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>Sant när dumpen i praktiken är helsvart (medelljushet under
    /// tröskeln på ett glest pixelurval) - signalen att prova skärmkopian.</summary>
    internal static bool LooksBlack(byte[] rgba)
    {
        if (rgba.Length < 4) return true;
        long sum = 0;
        var samples = 0;
        var step = Math.Max(4, rgba.Length / 4 / 2000 * 4); // ~2000 pixlar
        for (var i = 0; i + 2 < rgba.Length; i += step)
        {
            sum += (rgba[i] + rgba[i + 1] + rgba[i + 2]) / 3;
            samples++;
        }
        return samples == 0 || sum / samples < 6;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public int Size, Width, Height;
        public short Planes, BitCount;
        public int Compression, SizeImage, XPelsPerMeter, YPelsPerMeter, ClrUsed, ClrImportant;
        // BITMAPINFO:s färgtabell används inte vid 32bpp BI_RGB.
    }

    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out Rect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDc);
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hWnd, IntPtr hDc, uint flags);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hDc);
    [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hDc, IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll")] private static extern bool BitBlt(
        IntPtr dest, int destX, int destY, int width, int height, IntPtr src, int srcX, int srcY, uint rop);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateDIBSection(
        IntPtr hDc, ref BitmapInfo info, uint usage, out IntPtr bits, IntPtr section, uint offset);
}
