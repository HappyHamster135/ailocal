using System.Runtime.InteropServices;

namespace AiLocal.Node.Hosting;

/// <summary>
/// P3: opt-in desktop control. Captures the screen and injects mouse/keyboard
/// input on THIS machine so the agent (via the Studio "Skärm" tab or
/// /api/desktop/*) can see what a built game looks like and click around.
///
/// GUARDED by Worker.AllowDesktopControl (default OFF) - the dashboard and
/// endpoint layer refuse to call this unless the operator turned it on. This
/// is a powerful capability, so it is never available by accident.
///
/// Windows-only: uses GDI BitBlt for capture and SendInput for input. On
/// non-Windows it degrades to "not supported" rather than throwing.
///
/// Screen capture goes through GDI+ (gdiplus.dll) directly rather than
/// System.Drawing.Common, because the latter's HBITMAP->Bitmap path fails to
/// resolve its native entry point inside a single-file self-contained build.
/// </summary>
public sealed class DesktopControlService
{
    public bool IsSupported => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    public string? LastError { get; private set; }

    public byte[]? CaptureScreen()
    {
        LastError = null;
        if (!IsSupported) { LastError = "Plattformen är inte Windows."; return null; }
        string? tmp = null;
        try
        {
            var (w, h) = GetScreenSize();
            var hwnd = GetDesktopWindow();
            IntPtr hdcSrc = GetWindowDC(hwnd);
            if (hdcSrc == IntPtr.Zero) { LastError = "GetWindowDC returnerade 0 (ingen skärm-session?)."; return null; }
            IntPtr hdcDst = CreateCompatibleDC(hdcSrc);
            IntPtr hBmp = CreateCompatibleBitmap(hdcSrc, w, h);
            IntPtr hOld = SelectObject(hdcDst, hBmp);
            BitBlt(hdcDst, 0, 0, w, h, hdcSrc, 0, 0, 0x00CC0020 /* SRCCOPY */);
            SelectObject(hdcDst, hOld);

            // Encode the HBITMAP to a PNG via GDI+ directly (no System.Drawing).
            tmp = Path.Combine(Path.GetTempPath(), "ailocal-screen-" + Guid.NewGuid().ToString("n") + ".png");
            var status = SaveBitmapToPng(hBmp, tmp);
            DeleteObject(hBmp);
            DeleteDC(hdcDst);
            ReleaseDC(hwnd, hdcSrc);

            if (status != 0) { LastError = "GDI+ save misslyckades (status " + status + ")."; return null; }
            return File.ReadAllBytes(tmp);
        }
        catch (Exception ex) { LastError = ex.GetType().Name + ": " + ex.Message; return null; }
        finally { if (tmp is not null) { try { File.Delete(tmp); } catch { } } }
    }

    public void Click(int x, int y)
    {
        if (!IsSupported) return;
        SendMouse(MouseEventF.Absolute | MouseEventF.Move, x, y);
        SendMouse(MouseEventF.Absolute | MouseEventF.LeftDown, x, y);
        SendMouse(MouseEventF.Absolute | MouseEventF.LeftUp, x, y);
    }

    public void TypeText(string text)
    {
        if (!IsSupported || string.IsNullOrEmpty(text)) return;
        foreach (var ch in text)
        {
            short vk = VkKeyScan(ch);
            bool shift = (vk & 0x100) != 0;
            byte b = (byte)(vk & 0xFF);
            if (shift) KeyEvent(0x10, true);
            KeyEvent(b, true);
            KeyEvent(b, false);
            if (shift) KeyEvent(0x10, false);
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
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    private static (int, int) GetScreenSize()
    {
        int w = GetSystemMetrics(0);   // SM_CXSCREEN
        int h = GetSystemMetrics(1);   // SM_CYSCREEN
        return (w > 0 ? w : 1280, h > 0 ? h : 720);
    }

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [Flags]
    private enum MouseEventF : uint
    {
        Absolute = 0x8000,
        LeftDown = 0x0002,
        LeftUp = 0x0004,
        Move = 0x0001
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private static void SendMouse(MouseEventF flags, int x, int y)
    {
        var input = new INPUT
        {
            type = 0, // INPUT_MOUSE
            mi = new MOUSEINPUT
            {
                dx = x,
                dy = y,
                dwFlags = (uint)flags,
                time = 0,
                dwExtraInfo = UIntPtr.Zero
            }
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static void KeyEvent(byte vk, bool down) =>
        keybd_event(vk, 0, down ? 0u : 0x0002u, UIntPtr.Zero);
}
