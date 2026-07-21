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
        _ => General,
    };

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
