namespace AiLocal.Node.Hosting;

/// <summary>
/// Detects the "planned instead of executed" failure: weak models sometimes
/// end a build run by PRESENTING a plan and asking for approval ("Here is my
/// plan... Let me know if this meets your expectations!") or by announcing a
/// next step they never take ("Jag börjar med att anpassa GameManager.gd.") -
/// the run then completes with a design document but no game. Nobody reads or
/// answers during an autonomous build, so WorkerRole uses this to answer for
/// the operator: execute. Deliberately conservative - only clear permission
/// questions and announced-but-unexecuted next steps as the FINAL sentence
/// trigger; a normal completion summary ("Klart! Spelet har...") never does.
/// </summary>
public static class PlanOnlyDetector
{
    public static bool LooksUnexecuted(string? finalAnswer)
    {
        if (string.IsNullOrWhiteSpace(finalAnswer)) return false;
        var text = finalAnswer.Trim();
        // Bara slutet räknas - en plan i början följd av utfört arbete och en
        // sammanfattning är helt normalt.
        var tail = text.Length <= 700 ? text : text[^700..];
        var lower = tail.ToLowerInvariant();

        string[] askMarkers =
        [
            "let me know if", "meets your expectations", "does this plan",
            "shall i proceed", "shall i continue", "should i proceed", "should i continue",
            "would you like me to", "do you want me to", "waiting for your",
            "here is my plan", "here's my plan", "if this plan",
            "låt mig veta", "lat mig veta", "säg till om", "sag till om",
            "godkänner du", "godkanner du", "ska jag fortsätta", "ska jag fortsatta",
            "ska jag gå vidare", "ska jag ga vidare", "vill du att jag",
            "stämmer planen", "stammer planen",
        ];
        if (askMarkers.Any(m => lower.Contains(m, StringComparison.Ordinal))) return true;

        var lastSentence = LastSentence(lower);
        string[] futureStarts =
        [
            "jag börjar", "jag borjar", "nu börjar jag", "nu borjar jag",
            "låt oss börja", "lat oss borja", "jag fortsätter med att", "jag fortsatter med att",
            "först ska jag", "forst ska jag", "nästa steg är att", "nasta steg ar att",
            "i'll start", "i will start", "i'll begin", "i will begin",
            "let's start", "let's begin", "next, i will", "next i will",
        ];
        return futureStarts.Any(m => lastSentence.StartsWith(m, StringComparison.Ordinal));
    }

    /// <summary>Last sentence of the text, with trailing punctuation/markup
    /// stripped. Splits on ". "/"! "/"? "/newline - a bare '.' would cut
    /// inside file names like GameManager.gd.</summary>
    private static string LastSentence(string lower)
    {
        var t = lower.TrimEnd().TrimEnd('.', '!', '?', ')', '*', '`', '"', '\'', ' ');
        var cut = Math.Max(
            Math.Max(t.LastIndexOf(". ", StringComparison.Ordinal), t.LastIndexOf("! ", StringComparison.Ordinal)),
            Math.Max(t.LastIndexOf("? ", StringComparison.Ordinal), t.LastIndexOf('\n')));
        var sentence = cut >= 0 ? t[(cut + 1)..] : t;
        return sentence.TrimStart('.', '!', '?', ' ', '*', '`', '#', '-').Trim();
    }
}
