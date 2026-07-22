using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks v1.36.0: the asset pipeline (sfxr SFX, chiptune music,
/// style-consistent image prompts) and the project portfolio's snapshot/
/// rollback machinery.</summary>
public class AssetAndProjectTests : IDisposable
{
    private readonly string _dir;

    public AssetAndProjectTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-asset-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // ---- Sfxr --------------------------------------------------------------

    [Fact]
    public void Sfxr_ProducesValidDeterministicWav()
    {
        var a = SfxrGenerator.Render("jump", seed: 7);
        var b = SfxrGenerator.Render("jump", seed: 7);
        var other = SfxrGenerator.Render("explosion", seed: 7);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(a, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(a, 8, 4));
        Assert.True(a.Length > 4000, "misstänkt kort ljud");
        Assert.Equal(a, b);              // deterministisk per (kategori, seed) i processen
        Assert.NotEqual(a.Length, other.Length); // olika kategorier låter olika

        // KRYSSDETERMINISM (A9): Render seedas via kategori-INDEX, inte den
        // randomiserade string.GetHashCode (verifierat: samma strang gav olika
        // hash i skilda processer -> ljudet lat olika varje omstart). Langden
        // bestams enbart av den seedade System.Random (heltalsmatematik, ingen
        // Math.Sin/Pow) sa guldvardena ar maskin-/process-oberoende och failar
        // om hash-seedning nagonsin ateranfors.
        Assert.Equal(JUMP_LEN, a.Length);
        Assert.Equal(COIN_LEN, SfxrGenerator.Render("coin", seed: 7).Length);
    }

    private const int JUMP_LEN = 27420;
    private const int COIN_LEN = 25574;

    [Theory]
    [InlineData("ljud när spelaren hoppar", "jump")]
    [InlineData("coin pickup sound", "coin")]
    [InlineData("explosion när bossen dör", "explosion")]
    [InlineData("game over-ljud", "lose")]
    public void Sfxr_CategoryFor_MapsSwedishAndEnglish(string prompt, string expected)
    {
        Assert.Equal(expected, SfxrGenerator.CategoryFor(prompt));
    }

    // ---- Chiptune ----------------------------------------------------------

    [Fact]
    public void Chiptune_ProducesLoopableTrack()
    {
        var wav = ChiptuneComposer.Render("action", seed: 3);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
        // 4 ackord x 4 slag vid 150 bpm ≈ 6,4 s -> minst ett par hundra kB.
        Assert.True(wav.Length > 200_000, $"för kort spår: {wav.Length} byte");
        Assert.Equal(wav, ChiptuneComposer.Render("action", seed: 3));
    }

    [Theory]
    [InlineData("bakgrundsmusik till bossstriden", "boss")]
    [InlineData("lugn menymusik", "calm")]
    [InlineData("segerfanfar", "victory")]
    [InlineData("atmosfarisk ambient till menyn", "ambient")]
    [InlineData("spannande skrackmusik", "tense")]
    [InlineData("sorgsen melankolisk scen", "sad")]
    [InlineData("utforskande aventyrsmusik", "exploration")]
    public void Chiptune_MoodFor_Maps(string prompt, string expected)
    {
        Assert.Equal(expected, ChiptuneComposer.MoodFor(prompt));
    }

    [Fact]
    public void Chiptune_NyaStamningar_GerGiltigDeterministiskWav()
    {
        foreach (var mood in new[] { "ambient", "tense", "boss", "sad", "exploration" })
        {
            var a = ChiptuneComposer.Render(mood, seed: 5);
            Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(a, 0, 4));
            Assert.True(a.Length > 100_000, $"{mood}: för kort spår");
            Assert.Equal(a, ChiptuneComposer.Render(mood, seed: 5));   // deterministisk per (mood, seed)
        }
        // Ambient (sine-pad, inga trummor) låter inte som action.
        Assert.NotEqual(ChiptuneComposer.Render("ambient", 5), ChiptuneComposer.Render("action", 5));
    }

    // ---- Stilkonsekvens ----------------------------------------------------

    [Fact]
    public void AssetStyle_UsesDesignArtDirection()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<!DOCTYPE html><html></html>");
        File.WriteAllText(Path.Combine(_dir, "DESIGN.md"),
            "# Spelet\n\n## Art Direction\n16-bit pixelart med pastellpalett och tjocka konturer\n\n## Ljud\nchiptune");
        var prompt = AssetStyle.Apply(_dir, "sprite", "en rymdfarkost");
        Assert.Contains("pastellpalett", prompt);
        Assert.StartsWith("en rymdfarkost", prompt);
    }

    [Fact]
    public void AssetStyle_NoDesign_GetsGenericStyle_AndSfxUntouched()
    {
        Assert.Contains("enhetlig palett", AssetStyle.Apply(_dir, "sprite", "en gubbe"));
        Assert.Equal("hoppljud", AssetStyle.Apply(_dir, "sfx", "hoppljud"));
    }

    [Fact]
    public void AssetStyle_TilesetsOchBakgrunder_FarSammaArtbibelOchPalett()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<!DOCTYPE html><html></html>");
        File.WriteAllText(Path.Combine(_dir, "DESIGN.md"),
            "# Spelet\n\n## Art Direction\n16-bit pixelart med tjocka konturer\n\n## Palett\nmorka blaa toner med varm orange accent\n");

        // C10: tileset OCH background får nu art-bibeln (passerade tidigare orört).
        var tileset = AssetStyle.Apply(_dir, "tileset", "grasmark");
        Assert.Contains("ART-BIBEL", tileset);
        Assert.Contains("pixelart", tileset);
        Assert.Contains("orange accent", tileset); // paletten naglad fast

        var bg = AssetStyle.Apply(_dir, "background", "skog");
        Assert.Contains("ART-BIBEL", bg);
        Assert.Contains("pixelart", bg);
    }

    // ---- Ljudvägen genom AssetGenerator ------------------------------------

    [Fact]
    public async Task AssetGenerator_SfxAndMusic_WriteWavsWithoutAnyKeys()
    {
        var gen = new AssetGenerator();
        var sfx = await gen.GenerateAsync("sfx", "hoppljud", null, null, Path.Combine(_dir, "jump.wav"), CancellationToken.None);
        Assert.True(sfx.Success, sfx.Output);
        Assert.True(File.Exists(sfx.FilePath));

        var music = await gen.GenerateAsync("music", "lugn menymusik", null, null, Path.Combine(_dir, "menu.wav"), CancellationToken.None);
        Assert.True(music.Success, music.Output);
        Assert.True(new FileInfo(music.FilePath!).Length > 100_000);
    }

    [Fact]
    public void CloudImage_DecodeDataUrl_HandlesPrefixAndGarbage()
    {
        var png = new byte[] { 1, 2, 3, 4 };
        Assert.Equal(png, CloudImageGenerator.DecodeDataUrl("data:image/png;base64," + Convert.ToBase64String(png)));
        Assert.Null(CloudImageGenerator.DecodeDataUrl("ingen kommatecken"));
    }

    // ---- Snapshots + rollback ----------------------------------------------

    [Fact]
    public void Snapshots_CaptureListRestore_RoundTrips()
    {
        // Unikt namn per körning: snapshotlagret ligger i nodens datamapp och
        // nycklas på REL-sökvägen - "spelet" skulle samla zippar över körningar.
        var project = Path.Combine(_dir, "spelet-" + Guid.NewGuid().ToString("n")[..8]);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "index.html"), "VERSION 1");

        var captured = ProjectSnapshots.Capture(_dir, project, "första bygget", clean: true, engine: "html5");
        Assert.True(captured.Success, captured.Output);

        var list = ProjectSnapshots.List(_dir, project);
        var snap = Assert.Single(list);
        Assert.Equal("första bygget", snap.Label);
        Assert.True(snap.Clean);

        // "gör spelet svårare" gjorde det sämre - skriv över och ångra.
        File.WriteAllText(Path.Combine(project, "index.html"), "TRASIG VERSION 2");
        File.WriteAllText(Path.Combine(project, "skräp.txt"), "x");
        var restored = ProjectSnapshots.Restore(_dir, project, snap.File);
        Assert.True(restored.Success, restored.Output);
        Assert.Equal("VERSION 1", File.ReadAllText(Path.Combine(project, "index.html")));
        Assert.False(File.Exists(Path.Combine(project, "skräp.txt")));
    }

    [Fact]
    public void Snapshots_RootProject_GetsStableKey()
    {
        Assert.Equal("_rot", ProjectSnapshots.KeyFor(_dir, _dir));
    }

    [Fact]
    public void Snapshots_Restore_RejectsPathTricks()
    {
        var project = Path.Combine(_dir, "spelet");
        Directory.CreateDirectory(project);
        var (success, _) = ProjectSnapshots.Restore(_dir, project, "..\\..\\ond.zip");
        Assert.False(success);
    }
}
