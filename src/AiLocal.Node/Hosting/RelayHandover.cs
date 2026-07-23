namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.18: STAFETTEN (ägarens idé: "automatiska handovers"). En lång körning
/// som släpar hela konversationen vidare vid varje iterationstak svällde
/// live till 8M input-tokens - kontexten blir dyrare och modellen trögare
/// för varje varv. I stället: när ett pass når taket skriver det en
/// strukturerad ÖVERLÄMNING (HANDOVER.md - synlig i projektet), och ett
/// FÄRSKT pass med TOM kontext tar över: uppdraget + överlämningen +
/// projektfilerna. Konstant kontextstorlek per pass, färska ögon, och
/// KLART-listan skyddar det som redan byggts. Fail-open: går överlämningen
/// inte att skriva används den gamla historik-fortsättningen.
/// </summary>
public static class RelayHandover
{
    /// <summary>Sista turen i det gamla passet: skriv överlämningen (körs
    /// med behörighet Av = ren text, inga verktygsanrop kan slinka med).</summary>
    public const string RequestPrompt =
        "STOPP - iterationstaket är nått och en FÄRSK utvecklare tar över efter dig. " +
        "Den ser INTE den här konversationen - bara projektfilerna och din överlämning. " +
        "Skriv nu ÖVERLÄMNINGEN som ren text med exakt dessa rubriker:\n" +
        "## KLART (byggt och verifierat - får INTE rivas eller skrivas om)\n" +
        "## ATERSTAR (konkreta punkter i prioritetsordning, med filnamn)\n" +
        "## KANDA PROBLEM (buggar, varningar, halvfärdiga delar)\n" +
        "## NASTA STEG (exakt var nästa pass ska börja, första handlingen)\n" +
        "Var konkret: filnamn, funktionsnamn och värden. Inga artigheter, ingen kod - bara överlämningen.";

    /// <summary>Det färska passets uppdragstext.</summary>
    public static string RelayPrompt(string originalAssignment, string handover, int pass, int maxPasses) =>
        $"STAFETTVÄXLING (pass {pass} av {maxPasses}): en tidigare AI-utvecklare har arbetat på uppdraget " +
        "och lämnat över till dig. Du ser inte deras konversation - läs projektfilerna.\n\n" +
        "URSPRUNGSUPPDRAGET:\n" + originalAssignment + "\n\n" +
        "ÖVERLÄMNINGEN FRÅN FÖRRA PASSET:\n" + handover + "\n\n" +
        "Regler för ditt pass:\n" +
        "- Det som står under KLART får inte rivas eller skrivas om - läs filerna innan du ändrar dem.\n" +
        "- Beta av ATERSTAR-listan i prioritetsordning; börja med NASTA STEG.\n" +
        "- Kör verify när du är klar och åtgärda det den rapporterar.\n" +
        "- Uppdatera HANDOVER.md om du själv når taket.";

    /// <summary>Duger texten som överlämning? Kräver substans och minst en
    /// av rubrikerna - en ursäkt eller ett kort "klart!" ska falla tillbaka
    /// till historik-fortsättningen i stället.</summary>
    public static bool LooksUsable(string? handover)
    {
        if (string.IsNullOrWhiteSpace(handover) || handover.Length < 120) return false;
        var t = handover.ToUpperInvariant();
        return t.Contains("KLART") || t.Contains("ATERSTAR") || t.Contains("ÅTERSTÅR") || t.Contains("NASTA STEG") || t.Contains("NÄSTA STEG");
    }
}
