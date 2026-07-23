using System.Text.RegularExpressions;

namespace AiLocal.Node.Hosting;

/// <summary>Operatörens förhandsval ur composern, parsade ur uppdragstexten.</summary>
public sealed record BuildChoices(string? Style, string? Scope, bool AskFirst, string CleanAssignment);

/// <summary>
/// v2.18: FÖRHANDSVALEN - composern skickar stil/omfång/fråga-först som
/// ASCII-taggar först i uppdragstexten ("[STIL: pixelart]"). Textinbakning
/// i stället för nya API-fält: Host-forward, Overseer-proxy och GAMLA noder
/// behöver inga ändringar (en äldre nod ser bara ofarlig text i prompten).
/// Valen blir HÅRDA kontraktspunkter hos regissören - den slipper gissa
/// grunderna och lägger kraften på innehåll och mekanik.
/// </summary>
public static class BuildDirectives
{
    static readonly Regex StyleRx = new(@"^\s*\[STIL:\s*(pixelart|iso|3d|vektor)\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex ScopeRx = new(@"^\s*\[OMFANG:\s*(litet|standard|stort)\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);
    static readonly Regex AskRx = new(@"^\s*\[FORHANDSFRAGOR\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public static BuildChoices Parse(string assignment)
    {
        var text = assignment ?? "";
        string? style = null;
        string? scope = null;
        var ask = false;
        var m = StyleRx.Match(text);
        if (m.Success) { style = m.Groups[1].Value.ToLowerInvariant(); text = StyleRx.Replace(text, "", 1); }
        m = ScopeRx.Match(text);
        if (m.Success) { scope = m.Groups[1].Value.ToLowerInvariant(); text = ScopeRx.Replace(text, "", 1); }
        if (AskRx.IsMatch(text)) { ask = true; text = AskRx.Replace(text, "", 1); }
        return new BuildChoices(style, scope, ask, text.Trim());
    }

    /// <summary>Stilvalet som hård kontraktspunkt (läggs i regissörens
    /// leveranskontrakt). Null när ingen stil valdes (Auto).</summary>
    public static string? StyleCriterion(string? style) => style switch
    {
        "pixelart" =>
            "STIL (operatörens val - hårt krav): ÄKTA PIXELART. All spelgrafik i pixelart: sprites via " +
            "generate_asset style:'pixelart' eller PixelAnimator-mönstret, nearest-filter, sluten mörk kontur " +
            "och 2-3 nyanser per yta, begränsad harmonisk palett. Inga släta gradienter eller vektorformer för spelentiteter.",
        "iso" =>
            "STIL (operatörens val - hårt krav): 2.5D/ISOMETRISK PIXELVY - världen ritas i isometriskt rutnät " +
            "(romb-tiles, 2:1) med djupsortering (y-sort/z-index efter position); grafiken följer pixelart-reglerna " +
            "(kontur, 2-3 nyanser per yta, nearest-filter).",
        "3d" =>
            "STIL (operatörens val - hårt krav): RIKTIG 3D - Node3D/Camera3D med enhetlig lågpoly-look, " +
            "tydlig ljussättning (DirectionalLight + ambient), skuggor och en sammanhållen materialpalett.",
        "vektor" =>
            "STIL (operatörens val - hårt krav): REN VEKTOR-2D - platta former med mjuka kurvor via " +
            "Art.gd-hjälparna (kontur, skugga, ljushighlight), harmonisk palett; ingen pixelestetik.",
        _ => null
    };

    /// <summary>Omfångsvalet som kontraktspunkt. Null när inget valdes.</summary>
    public static string? ScopeCriterion(string? scope) => scope switch
    {
        "litet" =>
            "OMFÅNG (operatörens val): LITET ARKADSPEL - ett fokuserat, komplett och POLERAT spel med en kärnmekanik " +
            "som sitter perfekt. Hellre 3 finslipade banor än 10 halvfärdiga.",
        "stort" =>
            "OMFÅNG (operatörens val): STORT PROJEKT - flera system/lägen/banor med progression mellan dem; " +
            "planera i milstolpar och bygg systemen så de kan växa (data-drivna banor, återanvändbara komponenter).",
        _ => null
    };

    /// <summary>Max antal stafettpass för omfånget (styr kontrollpunkterna
    /// i RunWithContinuations - stora projekt får fler pass).</summary>
    public static int MaxPasses(string? scope) => scope switch
    {
        "litet" => 3,
        "stort" => 6,
        _ => 4
    };
}
