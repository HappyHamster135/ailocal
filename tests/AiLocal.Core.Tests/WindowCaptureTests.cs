using System.Diagnostics;
using System.Runtime.InteropServices;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.41.0: fönsterdumpen som ger motorspel samma vision-öga som HTML5-spel.
/// Capture-testet går mot skrivbordsfönstret (finns alltid på en Windows-
/// maskin med aktiv session) i stället för att starta ett program - Notepad/
/// cmd kan öppnas i en ANNAN process på Win11 (Store-appar, Windows Terminal)
/// och ger flakiga MainWindowHandle-nollor.
/// </summary>
public class WindowCaptureTests
{
    [Fact]
    public void LooksBlack_SkiljerSvartFranInnehall()
    {
        var black = new byte[64 * 64 * 4]; // allt 0 = svart, alfa 0
        Assert.True(WindowCapturer.LooksBlack(black));

        var lit = new byte[64 * 64 * 4];
        for (var i = 0; i < lit.Length; i += 4) { lit[i] = 120; lit[i + 1] = 80; lit[i + 2] = 200; lit[i + 3] = 255; }
        Assert.False(WindowCapturer.LooksBlack(lit));

        Assert.True(WindowCapturer.LooksBlack([]));
    }

    [Fact]
    public async Task CaptureProcessWindow_ProcessUtanFonster_DegraderarArligt()
    {
        // En process som avslutas direkt utan fönster - dumpen ska ge ett
        // lugnt nej, aldrig kasta eller hänga.
        var psi = new ProcessStartInfo("cmd.exe", "/c exit 0")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        };
        using var proc = Process.Start(psi)!;
        var output = Path.Combine(Path.GetTempPath(), "ailocal-cap-" + Guid.NewGuid().ToString("n") + ".png");

        var result = await WindowCapturer.CaptureProcessWindowAsync(
            proc, output, TimeSpan.FromMilliseconds(100), CancellationToken.None);

        Assert.False(result.Success);
        Assert.False(File.Exists(output));
    }

    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();

    [Fact]
    public void Capture_RiktigtFonster_SparasSomPng()
    {
        // Windows-only P/Invoke - på annan plattform finns inget att bevisa
        // (CaptureProcessWindowAsync svarar då "stöds bara på Windows").
        if (!OperatingSystem.IsWindows()) return;

        var output = Path.Combine(Path.GetTempPath(), "ailocal-cap-" + Guid.NewGuid().ToString("n") + ".png");
        try
        {
            var (success, note, imagePath) = WindowCapturer.Capture(GetDesktopWindow(), output);

            Assert.True(success, note);
            Assert.Equal(output, imagePath);
            var bytes = File.ReadAllBytes(output);
            Assert.True(bytes.Length > 1000, $"PNG misstänkt liten: {bytes.Length} bytes");
            // PNG-signaturen: dumpen ska vara en riktig bild, inte råbytes.
            Assert.Equal([137, 80, 78, 71], bytes.Take(4).Select(b => (int)b).ToArray());
            // Falskt-grönt-vakten (v1.34-lärdomen): avkoda pixlarna och bevisa
            // att dumpen faktiskt VISAR något - en aktiv Windows-session är
            // aldrig helsvart. Utan detta kunde en trasig blit ge en giltig
            // men helsvart PNG som ändå passerade storleks-asserten.
            var rgba = DecodeOwnPng(bytes);
            Assert.False(WindowCapturer.LooksBlack(rgba), "Skrivbordsdumpen är helsvart - bliten läser inte riktiga pixlar.");
        }
        finally
        {
            try { File.Delete(output); } catch { /* städning */ }
        }
    }

    /// <summary>Avkodar exakt det format nodens egen encoder skriver (en
    /// IDAT, filter 0 per rad, RGBA8) - ingen generell PNG-läsare.</summary>
    private static byte[] DecodeOwnPng(byte[] png)
    {
        static int Be32(byte[] b, int o) => (b[o] << 24) | (b[o + 1] << 16) | (b[o + 2] << 8) | b[o + 3];
        var width = Be32(png, 16);
        var height = Be32(png, 20);

        // Chunk-vandring till IDAT.
        var offset = 8;
        byte[]? idat = null;
        while (offset + 8 <= png.Length)
        {
            var len = Be32(png, offset);
            var type = System.Text.Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
            {
                idat = png.Skip(offset + 8).Take(len).ToArray();
                break;
            }
            offset += 12 + len;
        }
        Assert.NotNull(idat);

        // zlib-header (2 bytes) före deflate-strömmen.
        using var deflate = new System.IO.Compression.DeflateStream(
            new MemoryStream(idat!, 2, idat!.Length - 2), System.IO.Compression.CompressionMode.Decompress);
        using var raw = new MemoryStream();
        deflate.CopyTo(raw);
        var filtered = raw.ToArray();

        var stride = width * 4;
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            Assert.Equal(0, filtered[y * (stride + 1)]); // filter: None
            Buffer.BlockCopy(filtered, y * (stride + 1) + 1, rgba, y * stride, stride);
        }
        return rgba;
    }
}
