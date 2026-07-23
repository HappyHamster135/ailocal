namespace AiLocal.Node.Hosting;

/// <summary>
/// Regissörens idéverkstad: kuraterade inspirationsfrön per genre. Noden
/// slumpar 2-3 frön per körning (Random.Shared - variation ÄR poängen, till
/// skillnad från kitens fasta seeds) och regissören bygger twist + nya
/// mekaniker kring dem. Det är svaret på "blir det samma spel varje gång?":
/// kitet förblir det deterministiska golvet, men två körningar av samma
/// prompt drar olika frön och divergerar därifrån - även med svaga modeller,
/// som bygger bra kring ett GIVET frö men aldrig hittar på egna, och även i
/// nyckellösa fallbacks (frön blir kriterier rakt av).
/// </summary>
public static class GenreIdeaBank
{
    private static readonly string[] Management =
    [
        "skaderisker och dagsform som paverkar varje omgangs resultat",
        "en ungdomsakademi som producerar egna talanger over tid",
        "derbyn mot en namngiven rival med extra insats och storre publik",
        "sponsoravtal med krav - bonus vid uppfyllt mal, straff vid miss",
        "vadereffekter som paverkar omgangens utfall och intakter",
        "moral/lagkansla som stiger och faller med resultat och beslut",
        "en skandal-/pressmekanik med svara val och konsekvenser",
        "laneavtal och delbetalningar pa marknaden",
        "en cupturnering som lopar parallellt med ligan",
        "styrelsens fortroende - sjunker det till noll far du sparken",
        "veckans traningsval (attack/forsvar/vila) som paverkar nasta omgang",
        "supportrarnas humor som ger eller tar hemmafordel",
    ];

    private static readonly string[] TopDown =
    [
        "en boss med tva faser pa sista vagen",
        "vapenuppgraderingar som droppas av besegrade fiender",
        "ett skyddsobjekt som maste forsvaras medan vagorna pagar",
        "dag/natt-cykel som andrar fiendernas beteende och fart",
        "fallor i arenan som skadar bade spelaren och fienderna",
        "en butik mellan vagorna dar mynten spenderas",
        "elitfiender med skold som kraver taktikbyte",
        "helande brunnar med begransade laddningar per vag",
        "en combo-matare som belonar aggressivt spel med multiplikator",
        "en foljeslagare med egen AI som maste hallas vid liv",
    ];

    private static readonly string[] Platformer =
    [
        "dubbelhopp som lases upp efter halva spelet",
        "rorliga plattformar och krossfallor med tydlig rytm",
        "samlarmynt som laser upp en hemlig bonusbana",
        "en jaktsekvens dar nagot obevekligt jagar spelaren",
        "vaggstuds for att na hemliga ytor",
        "tidspress-banor med brons/silver/guld-medaljer",
        "flygande fiender som kraver timing i stallet for spring",
        "checkpoints som kostar poang att aktivera",
        "gravitationszoner som vander hoppet upp och ner",
        "nyckel-och-dorr-struktur som far banan att kannas som ett pussel",
    ];

    private static readonly string[] General =
    [
        "en riskmekanik dar spelaren kan satsa poang for hogre belonning",
        "en daglig utmaning med eget slumpfro och egen topplista",
        "combo-/kedjebonusar som belonar skicklighet och flyt",
        "en upplasbar extra svarighetsniva med egen highscore",
        "en hjalpfigur/mentor som ger tips forsta gangen nagot hander",
        "kosmetiska belonningar (farger/teman) vid poangmilstolpar",
        "ett paskagg som firar nar spelaren slar sitt rekord",
        "adaptiv svarighet som kanner av spelarens niva och justerar",
    ];

    /// <summary>Bank per genre - samma grupper som Godot-kitvalet.</summary>
    public static IReadOnlyList<string> SeedsFor(string genre) => genre switch
    {
        "management" or "simulator" or "idle" => Management,
        "rpg" or "roguelike" or "shooter" => TopDown,
        "platformer" => Platformer,
        "artillery" => Artillery,
        "party" => Party,
        _ => General,
    };

    // v1.98: artilleri/duell-genren (ShellShock Live/Worms-klassen).
    private static readonly string[] Artillery =
    [
        "upplasbara vapen mellan duellerna (klustergranat, borrprojektil, napalm)",
        "terrang-teman per motstandare (oken, snoberg, manlandskap) med olika studs/friktion",
        "skoldgenerator som blockerar EN traff och maste skjutas sonder forst",
        "bransle per tur: flytta tanken en bit i stallet for att skjuta",
        "nedgravd power-up i terrangen som frigors nar nagon spranger fram den",
        "overladdning: riskera sjalvskada for 50% extra kraft",
        "bossmotstandare med dubbelskott och egen signaturattack",
        "forstorbara broar/plattformar som tankarna star pa",
        "vind som VAXLAR mitt i duellen och visas som prognos en tur i forvag",
        "rikoschett-vapen som studsar en gang mot terrangen",
    ];

    // v2.1.0: party/bradspel (Mario Party-klassen) - sammansatt spel med
    // bradlage, tarning, flera minispel och 2+ bradlayouter.
    private static readonly string[] Party =
    [
        "ett fjarde minispel: plata-pussel dar spelare springer till ruttor med ratt svar",
        "stjarnans pris stiger for varje kopt stjarna (inflation over rundor)",
        "en olycksruta som BLANDAR alla spelares positioner",
        "duellrutor: tva spelare slas om mynt med en snabb reaktionsduell",
        "en bankruta dar spelare kan satta in mynt och fa ranta var tredje runda",
        "bonusrundor med dubbla myntbelopp var fjarde runda",
        "tarning med specialytor (stjarna, olycka, teleport) utover 1-6",
        "en butik mellan rundorna dar mynt koper power-ups (extra tarning, skold)",
        "allianser: tva spelare slar ihop sina mynt tillfalligt for en runda",
        "en tredje bradlayout: spiralformat som tvingar ihop spelare",
        // v2.3: Pummel Party-/Lego Party-elementen (agarens referensspel).
        "sabotage-items i Pummel Party-stil: kop och kasta en falla pa en ruta (bjornsax, bomb) som drabbar nasta spelare som landar",
        "myntstold: en item/ruta later dig stjala 5-10 mynt fran ledaren (comeback-mekanik)",
        "battle-minispel dar SISTA overlevande tar potten (knuffa av plattform, krympande yta)",
        "bygg-minispel i Lego-stil: stapla fallande block hogst innan tiden gar ut",
        "ett elakt slumphjul i rundans slut (Pummel-stil): byt platser, tappa mynt, dubbla nasta minispel",
        "kosmetiska upplasningar for spelarens figur (hattar/farger) vid milstolpar - syns pa bradan",
    ];

    /// <summary>Slumpar <paramref name="count"/> olika frön ur genrens bank.
    /// Random.Shared avsiktligt - två körningar SKA dra olika.</summary>
    public static IReadOnlyList<string> PickSeeds(string genre, int count = 3)
    {
        var bank = SeedsFor(genre);
        count = Math.Clamp(count, 1, bank.Count);
        return [.. Enumerable.Range(0, bank.Count)
            .OrderBy(_ => Random.Shared.Next())
            .Take(count)
            .Select(i => bank[i])];
    }
}
