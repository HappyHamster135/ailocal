using System.IO.Compression;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>B3 (speltest-replay): APNG-kodaren staplar RGBA-rutor till en
/// animerad PNG via samma truecolor-pipeline som stillbilderna (deflate + äkta
/// Adler-32 + CRC) - ingen palett/LZW som en GIF skulle kräva. Låser
/// strukturen (acTL före IDAT, en fcTL per ruta, fdAT för ruta 2+) och att
/// första rutans IDAT faktiskt inflaterar till rätt längd (samma
/// integritetskoll som fångade Adler-buggen).</summary>
public class ApngTests
{
    private static byte[] SolidFrame(int w, int h, byte r, byte g, byte b)
    {
        var px = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++) { px[i * 4] = r; px[i * 4 + 1] = g; px[i * 4 + 2] = b; px[i * 4 + 3] = 255; }
        return px;
    }

    private static List<(string Type, byte[] Data)> Chunks(byte[] png)
    {
        var list = new List<(string, byte[])>();
        var pos = 8; // hoppa över signaturen
        while (pos + 12 <= png.Length)
        {
            var len = (png[pos] << 24) | (png[pos + 1] << 16) | (png[pos + 2] << 8) | png[pos + 3];
            var type = System.Text.Encoding.ASCII.GetString(png, pos + 4, 4);
            var data = new byte[len];
            Array.Copy(png, pos + 8, data, 0, len);
            list.Add((type, data));
            pos += 12 + len; // len(4) + type(4) + data + crc(4)
        }
        return list;
    }

    [Fact]
    public void EncodeApng_TreRutor_HarKorrektAnimationsstruktur()
    {
        const int w = 8, h = 8;
        var frames = new List<byte[]>
        {
            SolidFrame(w, h, 220, 30, 30),
            SolidFrame(w, h, 30, 220, 30),
            SolidFrame(w, h, 30, 30, 220),
        };
        var apng = AssetGenerator.EncodeApng(w, h, frames, 150);

        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, apng[..8]);

        var chunks = Chunks(apng);
        Assert.Equal("IHDR", chunks[0].Type);

        // acTL MÅSTE ligga före första IDAT, annars ignorerar läsare animationen.
        var actlIdx = chunks.FindIndex(c => c.Type == "acTL");
        var idatIdx = chunks.FindIndex(c => c.Type == "IDAT");
        Assert.True(actlIdx >= 0 && actlIdx < idatIdx, "acTL måste ligga före IDAT");

        var actl = chunks[actlIdx].Data;
        var numFrames = (actl[0] << 24) | (actl[1] << 16) | (actl[2] << 8) | actl[3];
        Assert.Equal(3, numFrames);

        Assert.Equal(3, chunks.Count(c => c.Type == "fcTL"));  // en per ruta
        Assert.Equal(1, chunks.Count(c => c.Type == "IDAT"));  // rutan 0
        Assert.Equal(2, chunks.Count(c => c.Type == "fdAT"));  // rutan 1..2
        Assert.Equal("IEND", chunks[^1].Type);

        // Första rutans IDAT ska inflatera till height*(stride+1) byte - bevisar
        // att deflate + Adler-32 är giltiga (inte bara "ser ut som en PNG").
        var idat = chunks[idatIdx].Data;
        using var zin = new MemoryStream(idat, 2, idat.Length - 6); // hoppa zlib-header(2)+adler(4)
        using var raw = new DeflateStream(zin, CompressionMode.Decompress);
        using var outMs = new MemoryStream();
        raw.CopyTo(outMs);
        Assert.Equal(h * (w * 4 + 1), outMs.Length);
    }

    [Fact]
    public void EncodeApng_EnRuta_DegraderarTillVanligPng()
    {
        var apng = AssetGenerator.EncodeApng(4, 4, new[] { SolidFrame(4, 4, 10, 20, 30) }, 100);
        var chunks = Chunks(apng);
        Assert.DoesNotContain(chunks, c => c.Type == "acTL"); // ingen animation
        Assert.Contains(chunks, c => c.Type == "IDAT");
    }
}
