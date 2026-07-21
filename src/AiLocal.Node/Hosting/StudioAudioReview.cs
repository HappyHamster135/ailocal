namespace AiLocal.Node.Hosting;

/// <summary>
/// The "ljuddesigner" studio role - a dedicated department reviewer that owns
/// AUDIO quality the way #1's artist owns art and #2's designer owns balance.
/// It flags what a real sound designer would: a game with no background music,
/// or no sound effects at all. A chiptune loop plus sfxr feedback per action is
/// the difference between "finished game" and "silent prototype", so the
/// findings steer the gate's fix round to add the missing audio
/// (generate_asset type=music / type=sfx). Pure file inspection - testable.
/// </summary>
public static class StudioAudioReview
{
    // Music = a long loop; sfx = short clips. At 22 kHz mono a few-second loop is
    // well over 120 kB, while sfxr one-shots stay far below it.
    private const long MusicBytes = 120_000;

    public static IReadOnlyList<string> Review(string projectRoot)
    {
        var findings = new List<string>();
        List<long> sizes;
        try
        {
            sizes = Directory.EnumerateFiles(projectRoot, "*.wav", SearchOption.AllDirectories)
                .Select(f => new FileInfo(f).Length)
                .Where(len => len > 0)
                .ToList();
        }
        catch
        {
            return findings; // aldrig krascha grinden for en filkoll
        }

        if (sizes.Count == 0)
        {
            findings.Add("STUDIOROLL ljuddesigner: spelet har INGET ljud - lagg till en bakgrundsmusik-loop " +
                "(generate_asset type=music, stamning ur spelet) OCH ljudeffekter (generate_asset type=sfx) pa spelarens handlingar.");
            return findings;
        }

        if (!sizes.Any(len => len >= MusicBytes))
            findings.Add("STUDIOROLL ljuddesigner: ingen bakgrundsmusik - generera en loop (generate_asset type=music) med " +
                "en stamning som passar spelet (t.ex. action/ambient/boss) och spela den loopande i _ready.");

        if (!sizes.Any(len => len < MusicBytes))
            findings.Add("STUDIOROLL ljuddesigner: inga korta ljudeffekter - lagg sfxr-ljud " +
                "(generate_asset type=sfx) pa spelarens handlingar (hopp/traff/plock/vinst).");

        return findings;
    }
}
