namespace AiLocal.Node.Hosting;

/// <summary>
/// Procedural music: a loopable track per mood, built from a chord progression
/// with a triangle bass, a melodic lead and (mood-dependent) noise percussion.
/// Beyond the original three retro moods it now has ATMOSPHERIC moods too -
/// ambient/tense/sad layer a sustained SINE PAD (a warm held chord under the
/// melody) and drop the drums, so a menu or an emotional scene gets real
/// atmosphere instead of a chiptune beat. Deterministic per (mood, seed); mono
/// 22050 Hz to keep files small.
/// </summary>
public static class ChiptuneComposer
{
    private const int SampleRate = 22050;

    public static readonly string[] Moods =
        ["calm", "action", "victory", "ambient", "tense", "boss", "sad", "exploration"];

    public static string MoodFor(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("boss") || p.Contains("slutstrid") || p.Contains("final battle")) return "boss";
        if (p.Contains("action") || p.Contains("strid") || p.Contains("fight") || p.Contains("intensiv") || p.Contains("kamp")) return "action";
        if (p.Contains("seger") || p.Contains("victory") || p.Contains("vinst") || p.Contains("win") || p.Contains("fanfar")) return "victory";
        if (p.Contains("ambient") || p.Contains("atmosf") || p.Contains("drommig") || p.Contains("drömmig") || p.Contains("stamningsfull") || p.Contains("stämningsfull")) return "ambient";
        if (p.Contains("tense") || p.Contains("spann") || p.Contains("spänn") || p.Contains("skrack") || p.Contains("skräck") || p.Contains("hotfull") || p.Contains("mysteri") || p.Contains("suspense")) return "tense";
        if (p.Contains("sorg") || p.Contains("sad") || p.Contains("melankol") || p.Contains("trist") || p.Contains("emotionell")) return "sad";
        if (p.Contains("utforsk") || p.Contains("explore") || p.Contains("aventyr") || p.Contains("äventyr") || p.Contains("resa") || p.Contains("varld") || p.Contains("värld")) return "exploration";
        return "calm";
    }

    private sealed record MoodConfig(
        int Bpm, int[] Chords, int[] Lead, bool Minor, bool Drums, bool Pad, Wave LeadWave, double LeadVol);

    private static MoodConfig ConfigFor(string mood) => mood switch
    {
        "action" => new(150, [57, 53, 48, 55], [0, 3, 7, 12, 7, 3, 0, 3], true, true, false, Wave.Square, 0.16),
        "victory" => new(120, [48, 53, 55, 48], [0, 4, 7, 12, 16, 12, 7, 4], false, true, false, Wave.Square, 0.16),
        "boss" => new(168, [45, 44, 50, 43], [0, 3, 5, 7, 10, 7, 5, 3], true, true, true, Wave.Square, 0.17),
        "tense" => new(84, [45, 46, 44, 45], [0, 1, 3, 1, 0, 11, 0, 3], true, false, true, Wave.Sine, 0.11),
        "ambient" => new(66, [48, 53, 45, 50], [0, 7, 4, 12, 7, 4, 0, 7], false, false, true, Wave.Sine, 0.10),
        "sad" => new(72, [45, 50, 43, 48], [0, 3, 7, 3, 0, 10, 0, 3], true, false, true, Wave.Sine, 0.11),
        "exploration" => new(104, [48, 55, 53, 50], [0, 4, 7, 9, 7, 4, 2, 4], false, true, true, Wave.Triangle, 0.13),
        _ => new(92, [48, 45, 53, 55], [0, 7, 4, 7, 12, 7, 4, 7], false, true, false, Wave.Square, 0.16),
    };

    public static byte[] Render(string mood, int seed = 0)
    {
        mood = (mood ?? "calm").ToLowerInvariant();
        var rng = new Random(unchecked(seed * 6271 + mood.GetHashCode(StringComparison.OrdinalIgnoreCase)));
        var cfg = ConfigFor(mood);

        var beatSamples = (int)(SampleRate * 60.0 / cfg.Bpm);
        var eighth = beatSamples / 2;
        const int beatsPerChord = 4;
        var total = cfg.Chords.Length * beatsPerChord * beatSamples;
        var samples = new double[total];

        // Bas: triangelvåg på grundtonen (oktav under), en ton per fjärdedel.
        for (var chord = 0; chord < cfg.Chords.Length; chord++)
            for (var beat = 0; beat < beatsPerChord; beat++)
            {
                var start = (chord * beatsPerChord + beat) * beatSamples;
                var note = cfg.Chords[chord] - 12 + (beat == 2 ? 7 : 0);
                AddTone(samples, start, beatSamples, Freq(note), 0.22, Wave.Triangle);
            }

        // Pad: en sustained sine-triad per ackord - den varma/ambient-texturen
        // som lyfter atmosfäriska stämningar bortom ren chiptune.
        if (cfg.Pad)
        {
            var third = cfg.Minor ? 3 : 4;
            for (var chord = 0; chord < cfg.Chords.Length; chord++)
            {
                var start = chord * beatsPerChord * beatSamples;
                var len = beatsPerChord * beatSamples;
                foreach (var interval in new[] { 0, third, 7 })
                    AddPad(samples, start, len, Freq(cfg.Chords[chord] + interval), 0.06);
            }
        }

        // Lead: åttondelsmönster ur skalfigurer, seedade småvariationer.
        for (var chord = 0; chord < cfg.Chords.Length; chord++)
            for (var step = 0; step < beatsPerChord * 2; step++)
            {
                var start = chord * beatsPerChord * beatSamples + step * eighth;
                var interval = cfg.Lead[step % cfg.Lead.Length];
                if (rng.NextDouble() < 0.15) interval += rng.NextDouble() < 0.5 ? 12 : -5;
                if (rng.NextDouble() < 0.12) continue; // paus - andrum
                AddTone(samples, start, (int)(eighth * 0.9), Freq(cfg.Chords[chord] + 12 + interval), cfg.LeadVol, cfg.LeadWave);
            }

        // Trummor: bara stämningar som vill ha dem (atmosfäriska hoppar över).
        if (cfg.Drums)
        {
            var drumRng = new Random(42);
            for (var step = 0; step < cfg.Chords.Length * beatsPerChord * 2; step++)
            {
                var start = step * eighth;
                var accent = step % 4 == 0;
                AddNoise(samples, start, accent ? eighth / 3 : eighth / 6, accent ? 0.18 : 0.07, drumRng);
            }
        }

        var pcm = new short[total];
        for (var i = 0; i < total; i++)
            pcm[i] = (short)(Math.Clamp(samples[i], -1.0, 1.0) * short.MaxValue);
        return WavWriter.Write(pcm, SampleRate);
    }

    public static (bool Success, string Output, string FilePath) GenerateToFile(string mood, int seed, string outputPath)
    {
        try
        {
            if (!Moods.Contains(mood)) mood = MoodFor(mood);
            var path = Path.ChangeExtension(Path.GetFullPath(outputPath), ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Render(mood, seed));
            return (true, $"Musikslinga \"{mood}\" komponerad (loopbar, {new FileInfo(path).Length / 1024} kB wav).", path);
        }
        catch (Exception ex)
        {
            return (false, $"Musikgenerering misslyckades: {ex.Message}", outputPath);
        }
    }

    private enum Wave { Square, Triangle, Sine }

    private static double Freq(int midiNote) => 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);

    private static void AddTone(double[] buffer, int start, int length, double freq, double volume, Wave wave)
    {
        var attack = Math.Min(length / 8, SampleRate / 100);
        for (var i = 0; i < length && start + i < buffer.Length; i++)
        {
            var t = (double)i / SampleRate;
            var phase = t * freq % 1.0;
            var raw = wave switch
            {
                Wave.Square => phase < 0.5 ? 0.5 : -0.5,
                Wave.Sine => Math.Sin(phase * 2 * Math.PI),
                _ => phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4, // Triangle
            };
            var env = i < attack ? (double)i / attack : 1.0 - (double)(i - attack) / (length - attack) * 0.6;
            buffer[start + i] += raw * volume * env;
        }
    }

    /// <summary>A sustained, slowly fading sine with a gentle vibrato - the pad
    /// voice. Long attack/release so held chords swell instead of clicking.</summary>
    private static void AddPad(double[] buffer, int start, int length, double freq, double volume)
    {
        var fade = Math.Max(1, length / 4);
        for (var i = 0; i < length && start + i < buffer.Length; i++)
        {
            var t = (double)i / SampleRate;
            var raw = Math.Sin(t * freq * 2 * Math.PI) * (1.0 + 0.03 * Math.Sin(t * 5 * 2 * Math.PI));
            var env = i < fade ? (double)i / fade : (i > length - fade ? (double)(length - i) / fade : 1.0);
            buffer[start + i] += raw * volume * env;
        }
    }

    private static void AddNoise(double[] buffer, int start, int length, double volume, Random rng)
    {
        for (var i = 0; i < length && start + i < buffer.Length; i++)
        {
            var env = 1.0 - (double)i / length;
            buffer[start + i] += (rng.NextDouble() * 2 - 1) * volume * env * env;
        }
    }
}
