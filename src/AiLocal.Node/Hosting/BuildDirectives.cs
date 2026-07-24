using System.Text.RegularExpressions;

namespace AiLocal.Node.Hosting;

/// <summary>Operatörens förhandsval ur composern, parsade ur uppdragstexten.</summary>
public sealed record BuildChoices(
    string? Style, string? Scope, bool AskFirst, string CleanAssignment,
    IReadOnlyList<string>? Features = null)
{
    public IReadOnlyList<string> FeatureList => Features ?? [];
}

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
    static readonly Regex FeatRx = new(@"^\s*\[FUNKTIONER:\s*([a-z0-9_, ]+)\]\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

    public static BuildChoices Parse(string assignment)
    {
        var text = assignment ?? "";
        string? style = null;
        string? scope = null;
        var ask = false;
        List<string> feats = [];
        var m = StyleRx.Match(text);
        if (m.Success) { style = m.Groups[1].Value.ToLowerInvariant(); text = StyleRx.Replace(text, "", 1); }
        m = ScopeRx.Match(text);
        if (m.Success) { scope = m.Groups[1].Value.ToLowerInvariant(); text = ScopeRx.Replace(text, "", 1); }
        m = FeatRx.Match(text);
        if (m.Success)
        {
            feats = [.. m.Groups[1].Value.Split(',')
                .Select(f => f.Trim().ToLowerInvariant())
                .Where(f => f.Length > 0 && Catalog.ContainsKey(f))
                .Distinct()];
            text = FeatRx.Replace(text, "", 1);
        }
        if (AskRx.IsMatch(text)) { ask = true; text = AskRx.Replace(text, "", 1); }
        return new BuildChoices(style, scope, ask, text.Trim(), feats);
    }

    /// <summary>v2.30: FUNKTIONSVALEN. Katalogen kopplar varje kryssruta till
    /// (a) en färdig modul i game_module-biblioteket där en finns, och
    /// (b) en HÅRD kontraktspunkt. Poängen: de åtta modulerna fanns redan men
    /// nåddes bara om agenten själv råkade välja att kalla på dem - ett
    /// erbjudande, inte ett krav. Nu blir de ett krav grinden följer upp.</summary>
    public sealed record Feature(string Key, string Label, string? Module, string Criterion);

    public static readonly IReadOnlyDictionary<string, Feature> Catalog =
        new Dictionary<string, Feature>(StringComparer.OrdinalIgnoreCase)
        {
            ["shop"] = new("shop", "Butik", null,
                "FUNKTION (operatörens val - hårt krav): BUTIK. En butiksskärm där spelaren växlar intjänad valuta " +
                "mot minst 5 meningsfulla uppgraderingar/föremål som MÄRKS i spelet. Priserna stiger, det köpta " +
                "syns i HUD:en och sparas mellan körningar."),
            ["achievements"] = new("achievements", "Prestationer", null,
                "FUNKTION (operatörens val - hårt krav): PRESTATIONER. Minst 8 namngivna achievements med villkor, " +
                "en lista i menyn som visar låsta/upplåsta, popup när en låses upp, och sparning i user://."),
            ["inventory"] = new("inventory", "Inventarie", "inventory",
                "FUNKTION (operatörens val - hårt krav): INVENTARIE. Spelaren plockar upp, bär och ANVÄNDER föremål " +
                "via en egen skärm; föremålen har effekt i spelet och följer med i sparningen. Använd game_module 'inventory'."),
            ["quest"] = new("quest", "Uppdrag", "quest",
                "FUNKTION (operatörens val - hårt krav): UPPDRAG. Minst 3 uppdrag med mål, framstegsräknare, " +
                "synlig uppdragslogg och belöning vid avklarat. Använd game_module 'quest'."),
            ["dialog"] = new("dialog", "Dialog", "dialog",
                "FUNKTION (operatörens val - hårt krav): DIALOG. NPC:er man pratar med via dialogrutor med " +
                "namngiven talare och minst ett val som får en konsekvens. Använd game_module 'dialog'."),
            ["xp"] = new("xp", "Nivåer/XP", "xp",
                "FUNKTION (operatörens val - hårt krav): NIVÅER. XP som samlas, nivåer som höjs med KÄNNBAR effekt " +
                "(skada/HP/fart), synlig XP-mätare och nivå-uppspel. Använd game_module 'xp'."),
            ["combat"] = new("combat", "Strid/HP", "combat",
                "FUNKTION (operatörens val - hårt krav): STRID. HP, skada, träffåterkoppling (blink/knuff/ljud), " +
                "död och återupplivning. Använd game_module 'combat'."),
            ["enemyai"] = new("enemyai", "Smart fiende-AI", "enemyai",
                "FUNKTION (operatörens val - hårt krav): FIENDE-AI. Fienderna patrullerar, upptäcker, jagar och " +
                "tappar spelaren - inte rak linje mot spelaren. Minst 2 beteendetyper. Använd game_module 'enemyai'."),
            ["hotseat"] = new("hotseat", "Lokal flerspelare", null,
                "FUNKTION (operatörens val - hårt krav): LOKAL FLERSPELARE. 2-4 spelare vid samma tangentbord med " +
                "EGNA tangentuppsättningar, spelarval i menyn och gemensam resultatskärm."),
            ["tutorial"] = new("tutorial", "Inlärning", null,
                "FUNKTION (operatörens val - hårt krav): INLÄRNING. En kort, spelbar introduktion som lär ut " +
                "kontrollerna i steg med synliga instruktioner - inte en textvägg på titelskärmen."),
        };

    /// <summary>Kontraktspunkter för valda funktioner.</summary>
    public static IReadOnlyList<string> FeatureCriteria(IReadOnlyList<string>? features)
    {
        if (features is null || features.Count == 0) return [];
        return [.. features
            .Where(Catalog.ContainsKey)
            .Select(f => Catalog[f].Criterion)];
    }

    /// <summary>Modulnamnen som ska hämtas via game_module - en rad i
    /// uppdraget så agenten vet att koden redan finns och slipper skriva om
    /// samma system sämre från noll.</summary>
    public static string? ModuleHint(IReadOnlyList<string>? features)
    {
        if (features is null || features.Count == 0) return null;
        var mods = features
            .Where(Catalog.ContainsKey)
            .Select(f => Catalog[f].Module)
            .Where(mod => mod is not null)
            .Distinct()
            .ToList();
        if (mods.Count == 0) return null;
        return "FÄRDIGA MODULER: hämta " + string.Join(", ", mods.Select(mod => $"'{mod}'")) +
               " med game_module (action='get', engine=projektets motor) och anpassa in dem - " +
               "de är beprövade och sparar både tid och buggar. Skriv INTE om dem från noll.";
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
