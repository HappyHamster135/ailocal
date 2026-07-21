namespace AiLocal.Node.Hosting;

/// <summary>
/// Classic sfxr-style parametric sound synthesis - the retro-game SFX engine
/// (square/saw/sine/noise, envelope, frequency slide, vibrato, arpeggio,
/// low-pass filter) rendered straight to 16-bit mono WAV. This replaces the
/// bare sine beeps that used to pass for "sound effects": a jump now sounds
/// like a jump. Fully deterministic per (category, seed) so scaffolds and
/// regenerated assets are reproducible.
/// </summary>
public static class SfxrGenerator
{
    private const int SampleRate = 44100;

    public static readonly string[] Categories =
        ["jump", "coin", "hurt", "explosion", "shoot", "powerup", "select", "win", "lose"];

    /// <summary>Maps free-text (Swedish or English) to the closest category.</summary>
    public static string CategoryFor(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        if (p.Contains("hopp") || p.Contains("jump")) return "jump";
        if (p.Contains("mynt") || p.Contains("coin") || p.Contains("pickup") || p.Contains("plocka") || p.Contains("poäng") || p.Contains("point")) return "coin";
        if (p.Contains("skada") || p.Contains("hurt") || p.Contains("hit") || p.Contains("träff") || p.Contains("ont")) return "hurt";
        if (p.Contains("explosion") || p.Contains("smäll") || p.Contains("boom") || p.Contains("bomb")) return "explosion";
        if (p.Contains("skjut") || p.Contains("shoot") || p.Contains("laser") || p.Contains("skott")) return "shoot";
        if (p.Contains("powerup") || p.Contains("power-up") || p.Contains("uppgrader")) return "powerup";
        if (p.Contains("meny") || p.Contains("select") || p.Contains("klick") || p.Contains("blip") || p.Contains("knapp")) return "select";
        if (p.Contains("vinst") || p.Contains("win") || p.Contains("seger") || p.Contains("klara")) return "win";
        if (p.Contains("förlust") || p.Contains("lose") || p.Contains("game over") || p.Contains("död")) return "lose";
        return "select";
    }

    public static byte[] Render(string category, int seed = 0)
    {
        var cat = category.ToLowerInvariant();
        // Stabil seed per kategori. string.GetHashCode ar RANDOMISERAD per process
        // i .NET (verifierat: samma strang ger olika hash i skilda korningar), sa
        // den gamla seedformeln gjorde Render icke-deterministisk mellan korningar
        // trots doc-loftet. Kategori-INDEX ar stabilt och ger anda en egen
        // ljudkaraktar per kategori (variationen som A9 efterfragade).
        var idx = Array.IndexOf(Categories, cat);
        if (idx < 0) idx = Categories.Length; // okand kategori -> egen stabil seed
        var p = PresetFor(cat, new Random(unchecked(seed * 7919 + idx * 65537 + 17)));
        var samples = Synthesize(p);
        return WavWriter.Write(samples, SampleRate);
    }

    public static (bool Success, string Output, string FilePath) GenerateToFile(string category, int seed, string outputPath)
    {
        try
        {
            if (!Categories.Contains(category)) category = CategoryFor(category);
            var path = Path.ChangeExtension(Path.GetFullPath(outputPath), ".wav");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, Render(category, seed));
            return (true, $"Ljudeffekt \"{category}\" genererad (sfxr-syntes, {new FileInfo(path).Length / 1024} kB wav).", path);
        }
        catch (Exception ex)
        {
            return (false, $"Ljudgenerering misslyckades: {ex.Message}", outputPath);
        }
    }

    // ---- Parametrar ------------------------------------------------------

    private sealed class Params
    {
        public int WaveType;          // 0 square, 1 saw, 2 sine, 3 noise
        public double BaseFreq;       // 0..1
        public double FreqSlide;      // -1..1
        public double Duty = 0.5;
        public double VibDepth;
        public double VibSpeed;
        public double EnvSustain;
        public double EnvPunch;       // extra volym i sustain-start 0..1
        public double EnvDecay;
        public double ArpMod = 1.0;   // frekvensmultiplikator
        public double ArpTime;        // sekunder tills arpeggiot slår till
        public double LpfCutoff = 1.0; // 0..1 (1 = av)
        public double LpfSweep;
        public double HpfCutoff;
    }

    private static double R(Random rng, double min, double max) => min + rng.NextDouble() * (max - min);

    private static Params PresetFor(string category, Random rng) => category switch
    {
        "jump" => new Params
        {
            WaveType = 0, Duty = R(rng, 0.2, 0.6), BaseFreq = R(rng, 0.3, 0.5),
            FreqSlide = R(rng, 0.15, 0.35),
            EnvSustain = R(rng, 0.1, 0.2), EnvDecay = R(rng, 0.1, 0.25),
        },
        "coin" => new Params
        {
            WaveType = 0, Duty = 0.5, BaseFreq = R(rng, 0.5, 0.7),
            EnvSustain = R(rng, 0.05, 0.1), EnvPunch = R(rng, 0.3, 0.6), EnvDecay = R(rng, 0.15, 0.3),
            ArpMod = 1.5, ArpTime = R(rng, 0.04, 0.07),
        },
        "hurt" => new Params
        {
            WaveType = 3, BaseFreq = R(rng, 0.15, 0.3), FreqSlide = R(rng, -0.5, -0.3),
            EnvSustain = 0.05, EnvDecay = R(rng, 0.12, 0.22),
        },
        "explosion" => new Params
        {
            WaveType = 3, BaseFreq = R(rng, 0.05, 0.18), FreqSlide = R(rng, -0.25, -0.1),
            EnvSustain = R(rng, 0.1, 0.25), EnvPunch = R(rng, 0.3, 0.6), EnvDecay = R(rng, 0.3, 0.55),
            VibDepth = R(rng, 0.05, 0.12), VibSpeed = R(rng, 0.2, 0.5),
            LpfCutoff = R(rng, 0.4, 0.8), LpfSweep = -0.05,
        },
        "shoot" => new Params
        {
            WaveType = 1, BaseFreq = R(rng, 0.5, 0.85), FreqSlide = R(rng, -0.45, -0.25),
            Duty = R(rng, 0.1, 0.4), EnvSustain = R(rng, 0.04, 0.1), EnvDecay = R(rng, 0.08, 0.18),
            HpfCutoff = 0.05,
        },
        "powerup" => new Params
        {
            WaveType = 1, BaseFreq = R(rng, 0.25, 0.4), FreqSlide = R(rng, 0.15, 0.3),
            VibDepth = R(rng, 0.1, 0.25), VibSpeed = R(rng, 0.4, 0.7),
            EnvSustain = R(rng, 0.25, 0.4), EnvDecay = R(rng, 0.2, 0.4),
        },
        "select" => new Params
        {
            WaveType = 0, Duty = 0.3, BaseFreq = R(rng, 0.4, 0.6),
            EnvSustain = R(rng, 0.02, 0.05), EnvDecay = R(rng, 0.04, 0.09),
        },
        "win" => new Params
        {
            WaveType = 0, Duty = 0.5, BaseFreq = 0.42,
            EnvSustain = 0.35, EnvPunch = 0.3, EnvDecay = 0.35,
            ArpMod = 1.26, ArpTime = 0.12, VibDepth = 0.04, VibSpeed = 0.5,
        },
        "lose" => new Params
        {
            WaveType = 2, BaseFreq = 0.35, FreqSlide = -0.15,
            EnvSustain = 0.3, EnvDecay = 0.45, VibDepth = 0.08, VibSpeed = 0.3,
        },
        _ => PresetFor("select", rng),
    };

    // ---- Syntesen (sfxr-troget kärnflöde) --------------------------------

    private static short[] Synthesize(Params p)
    {
        var sustain = Math.Max(1, (int)(p.EnvSustain * SampleRate));
        var decay = Math.Max(1, (int)(p.EnvDecay * SampleRate));
        var total = sustain + decay;
        var samples = new short[total];

        // fperiod-modellen ur sfxr: period styrs av basfrekvens och glider.
        var fperiod = 100.0 / (p.BaseFreq * p.BaseFreq + 0.001);
        var fslide = 1.0 - Math.Pow(p.FreqSlide, 3) * 0.01;
        var arpSample = p.ArpTime > 0 ? (int)(p.ArpTime * SampleRate) : int.MaxValue;
        var duty = Math.Clamp(p.Duty, 0.05, 0.5);
        var vibPhase = 0.0;
        var noise = new double[32];
        var noiseRng = new Random(1234);
        for (var i = 0; i < 32; i++) noise[i] = noiseRng.NextDouble() * 2 - 1;

        var phase = 0;
        var lpCutoff = p.LpfCutoff;
        var lpPole = Math.Pow(lpCutoff, 3) * 0.1;
        var lpValue = 0.0;
        var hpValue = 0.0;
        var hpPole = Math.Pow(p.HpfCutoff, 2) * 0.1;

        for (var i = 0; i < total; i++)
        {
            fperiod *= fslide;
            if (i == arpSample) fperiod /= p.ArpMod;

            // Lagpassets brytpunkt kan svepa over tiden (LpfSweep) - satt av t.ex.
            // explosion for ett morknande efterslag. Noll sweep = konstant filter
            // (byte-identiskt beteende med tidigare for alla andra kategorier).
            if (p.LpfSweep != 0.0)
            {
                lpCutoff = Math.Clamp(lpCutoff + p.LpfSweep * 0.0004, 0.02, 1.0);
                lpPole = Math.Pow(lpCutoff, 3) * 0.1;
            }

            var rfperiod = fperiod;
            if (p.VibDepth > 0)
            {
                vibPhase += p.VibSpeed * 0.01;
                rfperiod = fperiod * (1.0 + Math.Sin(vibPhase) * p.VibDepth);
            }
            var period = Math.Max(8, (int)rfperiod);

            // Envelope (attack-rampen var alltid 0 i sfxr-presetsen: punch-sustain -> decay)
            double env;
            if (i < sustain)
                env = 1.0 + p.EnvPunch * (1.0 - (double)i / sustain);
            else
                env = 1.0 - (double)(i - sustain) / decay;
            env = Math.Max(0, env);

            // 8x översampling per utsample för renare kanter (sfxr gör detta).
            var sample = 0.0;
            for (var sub = 0; sub < 8; sub++)
            {
                phase++;
                if (phase >= period)
                {
                    phase %= period;
                    if (p.WaveType == 3)
                        for (var n = 0; n < 32; n++) noise[n] = noiseRng.NextDouble() * 2 - 1;
                }
                var fp = (double)phase / period;
                var raw = p.WaveType switch
                {
                    0 => fp < duty ? 0.5 : -0.5,
                    1 => 1.0 - fp * 2.0,
                    2 => Math.Sin(fp * Math.PI * 2),
                    _ => noise[(int)(fp * 32) & 31],
                };
                lpValue += (raw - lpValue) * (lpCutoff >= 1.0 ? 1.0 : lpPole * 8);
                var filtered = lpValue;
                if (p.HpfCutoff > 0)
                {
                    hpValue += (filtered - hpValue) * hpPole * 8;
                    filtered -= hpValue;
                }
                sample += filtered;
            }
            sample = sample / 8.0 * env * 0.8;
            samples[i] = (short)(Math.Clamp(sample, -1.0, 1.0) * short.MaxValue);
        }
        return samples;
    }
}

/// <summary>Minimal RIFF/PCM 16-bit mono WAV writer, shared by the synths.</summary>
public static class WavWriter
{
    public static byte[] Write(short[] samples, int sampleRate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        var dataSize = samples.Length * 2;
        w.Write("RIFF"u8);
        w.Write(36 + dataSize);
        w.Write("WAVE"u8);
        w.Write("fmt "u8);
        w.Write(16);
        w.Write((short)1);            // PCM
        w.Write((short)1);            // mono
        w.Write(sampleRate);
        w.Write(sampleRate * 2);      // byte rate
        w.Write((short)2);            // block align
        w.Write((short)16);           // bits
        w.Write("data"u8);
        w.Write(dataSize);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }
}
