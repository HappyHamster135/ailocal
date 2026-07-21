namespace AiLocal.Node.Hosting;

/// <summary>
/// Procedural chiptune: a loopable music track per mood (calm/action/victory)
/// built from a chord progression with a square-wave lead, triangle bass and
/// noise percussion - the "musik saknas"-gap in generated games. Deterministic
/// per (mood, seed); mono 22050 Hz to keep files small (~16 s loop ≈ 700 kB).
/// </summary>
public static class ChiptuneComposer
{
    private const int SampleRate = 22050;

    public static readonly string[] Moods = ["calm", "action", "victory"];

    public static string MoodFor(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("action") || p.Contains("strid") || p.Contains("fight") || p.Contains("boss") || p.Contains("intensiv")) return "action";
        if (p.Contains("seger") || p.Contains("victory") || p.Contains("vinst") || p.Contains("win") || p.Contains("fanfar")) return "victory";
        return "calm";
    }

    public static byte[] Render(string mood, int seed = 0)
    {
        var rng = new Random(unchecked(seed * 6271 + mood.GetHashCode(StringComparison.OrdinalIgnoreCase)));
        var (bpm, chords, leadPattern) = mood.ToLowerInvariant() switch
        {
            "action" => (150, new[] { 57, 53, 48, 55 },  // Am F C G (grundtoner, MIDI)
                         new[] { 0, 3, 7, 12, 7, 3, 0, 3 }),
            "victory" => (120, new[] { 48, 53, 55, 48 }, // C F G C
                          new[] { 0, 4, 7, 12, 16, 12, 7, 4 }),
            _ => (92, new[] { 48, 45, 53, 55 },          // C Am F G
                  new[] { 0, 7, 4, 7, 12, 7, 4, 7 }),
        };

        var beatSamples = (int)(SampleRate * 60.0 / bpm);
        var eighth = beatSamples / 2;
        const int beatsPerChord = 4;
        var total = chords.Length * beatsPerChord * beatSamples;
        var samples = new double[total];

        // Bas: triangelvåg på grundtonen, en ton per fjärdedel (oktav under).
        for (var chord = 0; chord < chords.Length; chord++)
        {
            for (var beat = 0; beat < beatsPerChord; beat++)
            {
                var start = (chord * beatsPerChord + beat) * beatSamples;
                var note = chords[chord] - 12 + (beat == 2 ? 7 : 0);
                AddTone(samples, start, beatSamples, Freq(note), 0.22, Wave.Triangle);
            }
        }

        // Lead: åttondelsmönster ur skalfigurer, med små slumpvariationer
        // (seedade) så varje spel får sin egen melodi i samma stil.
        for (var chord = 0; chord < chords.Length; chord++)
        {
            for (var step = 0; step < beatsPerChord * 2; step++)
            {
                var start = chord * beatsPerChord * beatSamples + step * eighth;
                var interval = leadPattern[step % leadPattern.Length];
                if (rng.NextDouble() < 0.15) interval += rng.NextDouble() < 0.5 ? 12 : -5;
                if (rng.NextDouble() < 0.1) continue; // paus - andrum
                AddTone(samples, start, (int)(eighth * 0.9), Freq(chords[chord] + 12 + interval), 0.16, Wave.Square);
            }
        }

        // Trummor: noise-hihat på åttondelar, tyngre noise-slag på 1 och 3.
        var drumRng = new Random(42);
        for (var step = 0; step < chords.Length * beatsPerChord * 2; step++)
        {
            var start = step * eighth;
            var accent = step % 4 == 0;
            AddNoise(samples, start, accent ? eighth / 3 : eighth / 6, accent ? 0.18 : 0.07, drumRng);
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
            return (true, $"Musikslinga \"{mood}\" komponerad (chiptune, loopbar, {new FileInfo(path).Length / 1024} kB wav).", path);
        }
        catch (Exception ex)
        {
            return (false, $"Musikgenerering misslyckades: {ex.Message}", outputPath);
        }
    }

    private enum Wave { Square, Triangle }

    private static double Freq(int midiNote) => 440.0 * Math.Pow(2, (midiNote - 69) / 12.0);

    private static void AddTone(double[] buffer, int start, int length, double freq, double volume, Wave wave)
    {
        var attack = Math.Min(length / 8, SampleRate / 100);
        for (var i = 0; i < length && start + i < buffer.Length; i++)
        {
            var t = (double)i / SampleRate;
            var phase = t * freq % 1.0;
            var raw = wave == Wave.Square
                ? (phase < 0.5 ? 0.5 : -0.5)
                : (phase < 0.5 ? phase * 4 - 1 : 3 - phase * 4);
            var env = i < attack ? (double)i / attack : 1.0 - (double)(i - attack) / (length - attack) * 0.6;
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
