using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>#4 - studioroller: den nya ljuddesigner-rollen (StudioAudioReview)
/// granskar ett spels ljud och flaggar saknad musik/sfx sa spel inte levereras
/// tysta - samma disciplin som artist (#1) och designer (#2).</summary>
public class StudioRolesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ailocal-studio-" + Guid.NewGuid().ToString("n"));
    public StudioRolesTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { } }

    [Fact]
    public void Audio_IngetLjud_FlaggarAllt()
    {
        var f = StudioAudioReview.Review(_dir);
        Assert.Contains(f, x => x.Contains("ljuddesigner") && x.Contains("INGET ljud"));
    }

    [Fact]
    public void Audio_BaraSfx_FlaggarSaknadMusikMenInteSfx()
    {
        File.WriteAllBytes(Path.Combine(_dir, "coin.wav"), new byte[800]); // kort = sfx
        var f = StudioAudioReview.Review(_dir);
        Assert.Contains(f, x => x.Contains("bakgrundsmusik"));
        Assert.DoesNotContain(f, x => x.Contains("ljudeffekter"));
    }

    [Fact]
    public void Audio_MusikOchSfx_Rent()
    {
        File.WriteAllBytes(Path.Combine(_dir, "coin.wav"), new byte[800]);       // sfx
        File.WriteAllBytes(Path.Combine(_dir, "music.wav"), new byte[200_000]);  // lang loop = musik
        Assert.Empty(StudioAudioReview.Review(_dir));
    }
}
