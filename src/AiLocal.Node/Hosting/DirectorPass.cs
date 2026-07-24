using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>
/// The creative director: before the build starts, one strong-model call
/// turns the user's weak prompt into a DELIVERY CONTRACT - game pillars, one
/// unique twist, and 4-8 MEASURABLE acceptance criteria ("5 banor med
/// stigande svårighetsgrad", not "bra gameplay"). The contract lands in
/// DESIGN.md and in the assignment text, and the quality gate follows it up
/// after the build: unmet criteria come back as findings that trigger a fix
/// round. This is what turns design from decoration into a contract - the
/// gate guaranteed WORKING before; now someone also demands INTERESTING.
/// Weak models that can't produce JSON get a deterministic genre-standard
/// contract instead (same floor philosophy as scaffolds and team tracks).
/// </summary>
public static class DirectorPass
{
    private const string DirectorSystem = """
        You are the CREATIVE DIRECTOR of a small game/app studio. Turn the
        user's request into a delivery contract for the developers.

        THINK LIKE A GAME DESIGNER, in this order, BEFORE writing the contract:
        1. GENRE: what does this genre actually mean - what loop makes it fun?
        2. REFERENCES: name 1-2 popular, well-known games in this genre and
           borrow ONE proven, beloved mechanic from them, adapted to this
           theme (mention the inspiration in the twist, e.g. "transferfonster
           som i Football Manager").
        3. THEME-SPECIFIC: every mechanic must only make sense in THIS theme.
           A hospital sim and a football sim must NEVER share a criteria list
           with swapped nouns - if a criterion would survive a theme swap
           unchanged, make it more specific to the theme.
        4. STRUCTURE: shape progression to the genre - seasons/divisions for a
           management sim, handcrafted levels for a platformer, waves for an
           arena game, escalating boards for a puzzle. Do NOT default to
           "3 levels" when the genre suggests something better.
        5. REPLAY VALUE: pick what fits THIS game - difficulty modes OR
           unlockables OR new game plus OR escalating seasons. Difficulty
           levels are one option, never a requirement.

        Respond with ONLY this JSON, nothing else:
        {"pillars":"one sentence: the experience pillars","twist":"one sentence: the unique twist (name the reference game it riffs on)","criteria":["...","..."]}

        Rules for criteria (5-9 items, in the user's language):
        - MEASURABLE and CHECKABLE: counts, named features, concrete content
          ("4 divisioner med upp-/nedflyttning", "3 fiendetyper", "ljud för
          köp/mål/vinst") - never vague quality words ("fun", "polished").
        - Achievable by one developer in one sitting on the existing scaffold.
        - INSPIRATION SEEDS in the user message are a springboard, not law:
          keep the ones that fit, REPLACE weak ones with better ideas from
          your genre analysis - your design judgment outranks the seed list.
        - At least TWO criteria must be NEW named mechanics that do NOT exist
          in a generic starter kit (the kit already has menus, score, save and
          basic play) - name each mechanic explicitly.
        - Keep the production floor but PHRASE IT FOR THE GENRE: working
          screens (title with instructions, pause, end with restart), sound on
          key events, and a saved best result - genre-fitting wording, not
          boilerplate.
        """;

    /// <summary>v1.97: en slumpad KREATIV VINKEL per körning - strukturell
    /// variation utöver genrefröna, så två körningar av samma prompt angriper
    /// designen från olika håll (förebild/twist/progression/risk/personlighet/
    /// återspelbarhet) i stället för att följa samma mall.</summary>
    internal static readonly string[] CreativeLenses =
    [
        "Bygg designen kring en FÖREBILD: välj ett känt, älskat spel i genren och anpassa dess mest omtyckta mekanik till det här temat.",
        "Bygg designen kring en OVÄNTAD TWIST som ändrar genrens vanliga loop på ett sätt spelaren inte sett förr.",
        "Bygg designen kring PROGRESSION: en tydlig resa från liten till stor, med namngivna milstolpar spelaren strävar mot.",
        "Bygg designen kring RISK/BELÖNING: varje viktigt beslut ska ha en frestande chansning med kännbar nedsida.",
        "Bygg designen kring PERSONLIGHET: namngivna karaktärer/entiteter med egenskaper och relationer spelaren bryr sig om.",
        "Bygg designen kring ÅTERSPELBARHET: slump och variation som gör varje omgång märkbart olik den förra.",
    ];

    public sealed record Contract(string Pillars, string Twist, IReadOnlyList<string> Criteria)
    {
        public string ToMarkdown() =>
            "## Leveranskontrakt (regissören)\n" +
            $"Pelare: {Pillars}\nTwist: {Twist}\n\nKriterier:\n" +
            string.Join("\n", Criteria.Select(c => $"- [ ] {c}"));
    }

    /// <summary>Runs the director once per project: skipped when DESIGN.md
    /// already carries a contract (follow-ups keep the original contract).
    /// Returns the contract, model-made or fallback - never null for a build.</summary>
    public static async Task<Contract> RunAsync(
        string userPrompt,
        string projectRoot,
        string? strongModelHint,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        CancellationToken ct,
        string? engine = null,
        IReadOnlyList<string>? inspirationSeeds = null,
        IReadOnlyList<string>? pastLessons = null,
        IReadOnlyList<string>? operatorCriteria = null,
        string? gameLanguage = null)
    {
        // v1.97: slumpad kreativ vinkel per körning - samma prompt två gånger
        // ska inte ge samma designangrepp (vinkeln styr HUR regissören tänker,
        // fröna styr VAD den kan bygga kring).
        var lens = CreativeLenses[Random.Shared.Next(CreativeLenses.Length)];
        // v2.2: kuraterad visuell riktning (VisualStyleLib) - regissören får
        // ett konkret palettförslag per genre i stället för att "snygg
        // grafik" bli en from förhoppning. Förslaget är en SPRÅNGBRÄDA - en
        // egen sammanhållen riktning får ersätta det.
        var promptGenre = GameScaffoldService.DetectGenre(userPrompt);
        var style = VisualStyleLib.PickForGenre(promptGenre);
        var promptWithLens = userPrompt + "\n\nKREATIV VINKEL för just den här körningen: " + lens
            + $"\n\nVISUELL RIKTNING (förslag - gör den eller en egen till ett kriterium): {style.Name} - {style.Description}"
            // v2.10: genrens kvalitetsribba - vad som skiljer BRA från DÅLIGT
            // och vad toppspel i genren gör. Kontraktet ska sikta på BRA.
            + "\n\nGENRENS KVALITETSRIBBA (kontraktet ska sikta hit): " + GenreIdeaBank.QualityBar(promptGenre);
        var contract = await AskModelAsync(promptWithLens, strongModelHint, complete, ct, inspirationSeeds, pastLessons)
            ?? FallbackContract(userPrompt, inspirationSeeds);
        // Spelkänslan in i kontraktet för motorspel: regissörsmodeller är bra
        // på INNEHÅLL (5 banor, 3 fiendetyper) men glömmer KÄNSLAN - och
        // grinden följer bara upp det som står i kontraktet. Punkterna läggs
        // FÖRE skrivningen till DESIGN.md så uppföljningar läser tillbaka dem.
        // v2.18: operatörens förhandsval (stil/omfång från composern) läggs
        // FÖRST i kontraktet - de är hårda krav som väger tyngre än allt
        // regissören hittat på, och grinden följer upp dem som vanligt.
        if (operatorCriteria is { Count: > 0 })
            contract = contract with { Criteria = [.. operatorCriteria, .. contract.Criteria] };
        contract = contract with { Criteria = EnsureEngineFeelCriteria(contract.Criteria, engine, gameLanguage) };
        TryAppendToDesign(projectRoot, contract);
        return contract;
    }

    /// <summary>Standing production criteria for engine games (godot/unity):
    /// sound on actions, visible feedback, screen transitions, difficulty levels
    /// that actually differ, game-feel/juice (C1), and smooth performance (C3).
    /// Only criteria whose topic the contract does not already cover are added -
    /// the director's own wording wins.</summary>
    internal static IReadOnlyList<string> EnsureEngineFeelCriteria(IReadOnlyList<string> criteria, string? engine, string? gameLanguage = null)
    {
        if (engine is not ("godot" or "unity"))
            return criteria;

        // v2.23: språkväljaren (ägarens önskan sedan v1.99) - nodinställningen
        // GameLanguage styr det stående språkkriteriet. Engelska förblir
        // standard; "sv" ger riktig svenska med å/ä/ö (kiten är engelska, så
        // kriteriet tvingar agenten att översätta spelartexten den rör).
        var languageCriterion = string.Equals(gameLanguage, "sv", StringComparison.OrdinalIgnoreCase)
            ? "All spelartext på SVENSKA med korrekta å/ä/ö och professionell ton (operatörens nodinställning - översätt kitets engelska spelartexter) - och ALDRIG råa formatsträngar (%d/%s), synliga BBCode-taggar eller rådumpade data i UI:t"
            : "All spelartext på ENGELSKA med professionell ton (om inte användaren uttryckligen bett om annat språk) - och ALDRIG råa formatsträngar (%d/%s), synliga BBCode-taggar eller rådumpade data i UI:t";

        (string[] Keywords, string Criterion)[] feel =
        [
            (["ljud", "sound", "sfx", "audio"],
                "Ljudeffekt på varje viktig spelarhandling (välja/köpa/träffa/plocka) och tydlig ljudsignal vid vinst och förlust"),
            (["animation", "feedback", "blink", "skaka", "shake"],
                "Synlig feedback på varje interaktion: knapptryck, träffar och poängändringar ska märkas direkt (animation/färgblink/skalpuls)"),
            (["övergång", "overgang", "transition", "fade"],
                "Mjuka övergångar mellan skärmarna (titel/spel/resultat) - inga hårda klipp"),
            // v1.97: svårighetsgrader är ETT sätt att ge replay-värde, inte ett
            // tvång - regissörens val (upplåsningar/new game+/svårighetsgrader)
            // släcker det stående kriteriet via nyckelorden.
            (["svårighet", "svarighet", "difficulty", "upplås", "upplas", "unlock", "new game", "replay", "återspel", "aterspel", "genomspel"],
                "Ett replay-värde som KÄNNS: svårighetsgrader som skiljer sig mätbart ELLER upplåsningar/new game+ - en andra genomspelning ska inte vara identisk med den första"),
            // C1 (game-feel/juice): den konkreta grejen som skiljer en prototyp
            // från ett produktionsspel. Mer specifik än "synlig feedback" ovan.
            (["juice", "partik", "particle", "screenshake", "skärmskak", "skarmskak", "tween", "hit-stop", "hitstop", "easing"],
                "Game-feel/juice: screenshake vid träffar/kollisioner, partiklar vid poäng/träffar/explosioner, och tweenad rörelse med easing i stället för hårda hopp - spelet ska KÄNNAS, inte bara fungera"),
            // C3 (prestanda): grinden mäter redan FPS och underkänner hackiga
            // byggen - kriteriet gör det till ett uttalat mål så modellen undviker
            // tunga per-frame-mönster från början.
            (["prestanda", "fps", "smidig", "performance", "ruckel", "hack", "allokering"],
                "Smidig prestanda: 60 FPS-mål, inga tunga per-frame-allokeringar (skapa noder/texturer/resurser i _ready, inte varje bildruta) - spelet får inte hacka ens med många objekt på skärmen"),
            // v1.99 (ägarens beslut efter en levererad build med ASCII-svenska,
            // råa %s-strängar och synlig BBCode): spel skrivs på ENGELSKA som
            // standard - professionellt OCH encodingsäkert. Ber användaren
            // uttryckligen om ett annat språk vinner det via nyckelorden.
            (["engelsk", "english", "språk", "sprak", "language", "svenska på", "lokaliser"],
                languageCriterion),
            // v2.9 (ägarens "första Bloons-spelet"-skärmdump): nakna
            // draw_rect/draw_circle-entiteter läses som programmer-art.
            // Art.gd skickas med i varje Godot-scaffold - kriteriet gör
            // användningen till ett kontraktskrav för allt agentritat.
            (["art.gd", "kontur", "outline", "skugga", "shadow", "programmer-art", "grafisk finish", "visuell finish"],
                "Grafisk finish: allt agentritat använder Art.gd-hjälparna (Art.orb/tile/panel/token/bg - kontur, skugga, ljushighlight, djup) - ALDRIG nakna osminkade draw_rect/draw_circle för spelentiteter"),
            // v2.15 (ägarens dom: "startmeny, settings, välja gubbe - sånt som
            // ALLA spel har, även gratis"): spelSKALET är ett kontraktskrav.
            // Shell.gd skickas med i varje Godot-scaffold med färdiga
            // byggstenar så kravet kostar minuter, inte timmar.
            (["huvudmeny", "main menu", "meny med", "startmeny", "options", "settings", "inställning", "installning"],
                "Riktigt SPELSKAL: titelskärmen är en HUVUDMENY med valbara knappar (Play, Options, Quit - plus karaktärs-/ban-/lägesval när spelet har figurer eller flera banor), och Options-skärmen har volym, mute och fullskärm som SPARAS mellan körningar - Shell.gd-hjälparna (Shell.menu/options_panel/character_select/startup) finns i projektet och ska användas"),
        ];

        var result = new List<string>(criteria);
        foreach (var (keywords, criterion) in feel)
            if (!result.Any(c => keywords.Any(k => c.Contains(k, StringComparison.OrdinalIgnoreCase))))
                result.Add(criterion);
        return result;
    }

    public static bool AlreadyContracted(string projectRoot)
    {
        try
        {
            var design = Path.Combine(projectRoot, "DESIGN.md");
            return File.Exists(design) && File.ReadAllText(design).Contains("## Leveranskontrakt", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    /// <summary>Reads the contract criteria back out of DESIGN.md ("- [ ] x"
    /// lines under the contract heading), for the gate's follow-up.</summary>
    public static IReadOnlyList<string> ReadCriteria(string projectRoot)
    {
        try
        {
            var design = Path.Combine(projectRoot, "DESIGN.md");
            if (!File.Exists(design)) return [];
            var lines = File.ReadAllLines(design);
            var inContract = false;
            var criteria = new List<string>();
            foreach (var line in lines)
            {
                if (line.StartsWith("## ", StringComparison.Ordinal))
                    inContract = line.Contains("Leveranskontrakt", StringComparison.Ordinal);
                else if (inContract && line.TrimStart().StartsWith("- [ ]", StringComparison.Ordinal))
                    criteria.Add(line.TrimStart()[5..].Trim());
            }
            return criteria;
        }
        catch { return []; }
    }

    /// <summary>The follow-up: ONE model call after a successful build that
    /// checks the delivery against the contract. Returns unmet criteria (or
    /// empty). Providerless/parse failures return empty - the follow-up is an
    /// extra pair of eyes, never a blocker.</summary>
    public static async Task<IReadOnlyList<string>> ReviewAsync(
        IReadOnlyList<string> criteria,
        string projectRoot,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        CancellationToken ct,
        string? reviewModelHint = null)
    {
        if (criteria.Count == 0) return [];
        try
        {
            var evidence = BuildEvidence(projectRoot);
            // Cross-modell-granskning: en OBEROENDE modell (reviewModelHint,
            // ideal en starkare/annan an den som byggde) bedomer bade kontraktet
            // OCH uppenbara fel - tva olika modeller fangar fler felmoder an en
            // modell som granskar sitt eget arbete.
            var prompt = "Delivery contract criteria:\n" + string.Join("\n", criteria.Select(c => "- " + c)) +
                "\n\nProject evidence (files and key contents):\n" + evidence +
                "\n\nYou are an INDEPENDENT reviewer - a DIFFERENT model than the one that built this. List what is clearly wrong:\n" +
                "1) Which contract criteria are clearly NOT met by the evidence?\n" +
                "2) Any OBVIOUS bugs or clearly-missing production quality (broken/empty screens, crash risks, missing core game-feel).\n" +
                "3) Obvious BALANCE problems from the tuning values in the code: unwinnable difficulty (enemies too fast/strong or timers too short to ever beat), trivial difficulty (the player essentially cannot lose), or difficulty levels that are effectively identical (same speeds/counts). Read the actual numbers and reason about whether a real player could win the hardest level and could lose the easiest.\n" +
                "Respond ONLY with JSON: {\"unmet\":[\"concrete problem\"]} (max 8 items). " +
                "Be strict about counts but give benefit of the doubt when evidence is ambiguous - do NOT invent nitpicks.";
            var response = await complete(new ChatRequest
            {
                System = "You are a meticulous, independent QA lead reviewing another developer's delivery against its contract and for obvious defects.",
                Messages = [new ChatMessage("user", prompt)],
                ModelHint = reviewModelHint,
                MaxTokens = 500
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content)) return [];

            var text = response.Response.Content;
            var start = text.IndexOf('{');
            var end = text.LastIndexOf('}');
            if (start < 0 || end <= start) return [];
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("unmet", out var unmet) || unmet.ValueKind != JsonValueKind.Array)
                return [];
            return unmet.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Take(8)
                .ToList();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    // ---- Internt ---------------------------------------------------------

    private static async Task<Contract?> AskModelAsync(
        string userPrompt, string? strongModelHint,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete, CancellationToken ct,
        IReadOnlyList<string>? inspirationSeeds = null,
        IReadOnlyList<string>? pastLessons = null)
    {
        try
        {
            var message = userPrompt;
            if (inspirationSeeds is { Count: > 0 })
                message += "\n\nINSPIRATION SEEDS (build the twist around these, one criterion each):\n" +
                    string.Join("\n", inspirationSeeds.Select(s => "- " + s));
            if (pastLessons is { Count: > 0 })
                message += "\n\nLARDOMAR FRAN TIDIGARE SPEL I SAMMA GENRE (studiominne - se till att INTE upprepa dessa brister den har gangen):\n" +
                    string.Join("\n", pastLessons.Select(s => "- " + s));
            var response = await complete(new ChatRequest
            {
                System = DirectorSystem,
                Messages = [new ChatMessage("user", message)],
                ModelHint = strongModelHint,
                MaxTokens = 600
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content)) return null;
            return ParseContract(response.Response.Content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return null; }
    }

    internal static Contract? ParseContract(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(content[start..(end + 1)]);
            var root = doc.RootElement;
            var pillars = root.TryGetProperty("pillars", out var p) ? p.GetString() : null;
            var twist = root.TryGetProperty("twist", out var t) ? t.GetString() : null;
            if (!root.TryGetProperty("criteria", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;
            var criteria = arr.EnumerateArray()
                .Select(e => e.GetString())
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Select(s => s!)
                .Take(8)
                .ToList();
            return criteria.Count >= 3
                ? new Contract(pillars ?? "spelglädje", twist ?? "-", criteria)
                : null;
        }
        catch (JsonException) { return null; }
    }

    /// <summary>Deterministic floor when the model can't do JSON.</summary>
    internal static Contract FallbackContract(string prompt, IReadOnlyList<string>? inspirationSeeds = null)
    {
        var isGame = prompt.Contains("spel", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("game", StringComparison.OrdinalIgnoreCase);
        // v1.97: fallbacken var EN mall för alla genrer ("Minst 3 nivåer/vågor"
        // även för en managementsimulator) och krävde alltid svårighetsgrader -
        // exakt det "samma spel med utbytta substantiv"-mönster ägaren såg.
        // Nu: progression formuleras per genre, och replay-mekanismen slumpas
        // (svårighetsgrader är ETT alternativ av tre, inte ett tvång).
        var genre = GameScaffoldService.DetectGenre(prompt);
        var progression = genre switch
        {
            "management" or "simulator" or "idle" =>
                "En karriärstege med minst 3 divisioner/ligor att avancera genom - från botten till toppen, med upp- och nedflyttning",
            "racing" =>
                "Minst 3 banor/cuper med stigande svårighet och egen karaktär (layout, motstånd)",
            "puzzle" =>
                "Minst 5 brädor/nivåer med stigande svårighet där nya moment introduceras",
            "rpg" or "roguelike" or "shooter" =>
                "Minst 3 vågor/zoner med stigande svårighet och nya fiendetyper per steg",
            "artillery" =>
                "En motstandarstege med minst 3 namngivna motstandare med stigande traffsakerhet/HP och egen personlighet",
            "party" =>
                "Minst 2 olika bradlayouter och 3 fungerande minispel med olika mekaniker (reaktion, minne, skicklighet)",
            "towerdefense" =>
                "Minst 10 vagor med stigande svårighet, 3 torn-typer och 3 fiendetyper",
            "snake" =>
                "Stigande fart, highscore-system, minst 3 svarighetsgrader",
            "breakout" =>
                "Minst 3 rader tegelstenar, bollfysik med vinkelbaserad studs, 3 liv",
            "quiz" =>
                "Minst 10 fragor med tidspress, 4 svarsval, poang baserad pa svarstid",
            "memory" =>
                "4x4 rutnat med 8 par, dragrakning, snabb matchning belonas",
            "minesweeper" =>
                "10x10 rutnat med 15 minor, vansterklick+hogerklick, flood fill",
            "blockpuzzle" =>
                "7 standardblock, rotering, snabbfall, rensa rader for poang, stigande fart",
            _ =>
                "Minst 3 handbyggda banor/nivåer med stigande svårighetsgrad och egen karaktär",
        };
        string[] replayOptions =
        [
            "Tre svårighetsgrader som känns mätbart olika (olika startvärden/motstånd)",
            "Upplåsbart innehåll efter framsteg (nya lag/banor/förmågor) som syns på titelskärmen",
            "New game+-läge efter seger: en andra genomspelning med höjd utmaning och bevarat rekord",
        ];
        var replay = replayOptions[Random.Shared.Next(replayOptions.Length)];
        var pillars = genre is "management" or "simulator" or "idle"
            ? "Meningsfulla beslut varje omgång - resan från botten till toppen ska kännas förtjänad."
            : "Lätt att lära, svår att bemästra - varje omgång ska kännas rättvis.";
        // v2.2: kuraterad visuell identitet även i nyckellösa körningar -
        // "snygg grafik" blir ett KONKRET palettkriterium ur VisualStyleLib.
        var style = VisualStyleLib.PickForGenre(genre);
        var contract = isGame
            ? new Contract(
                pillars,
                "Svårigheten stiger märkbart och belönar skicklighet.",
                [
                    progression,
                    replay,
                    $"Sammanhållen visuell identitet ({style.Name}: {style.Description}) - samma palett på alla skärmar",
                    "Poängsystem med sparat highscore",
                    "Ljudeffekter för minst 3 olika händelser",
                    "Startskärm med instruktioner samt paus",
                    "Game over-skärm med omstart",
                ])
            : new Contract(
                "Gör EN sak riktigt bra med tydlig återkoppling till användaren.",
                "Fungerar direkt utan konfiguration.",
                [
                    "Kärnfunktionen komplett och körbar från start",
                    "Hjälptext/README som förklarar användningen",
                    "Felhantering med begripliga meddelanden",
                    "Minst 3 automatiska tester som passerar",
                ]);

        // Fröna blir kriterier RAKT AV i fallbacken: standardkontraktet var
        // deterministiskt, så nyckellösa/svaga körningar gjorde samma spel
        // varje gång - nu divergerar även de (fröna slumpas per körning).
        if (inspirationSeeds is { Count: > 0 })
            contract = contract with
            {
                Twist = "Byggt kring: " + string.Join("; ", inspirationSeeds) + ".",
                Criteria = [.. contract.Criteria, .. inspirationSeeds.Select(s => "Bygg in mekaniken: " + s)]
            };
        return contract;
    }

    private static void TryAppendToDesign(string projectRoot, Contract contract)
    {
        try
        {
            var design = Path.Combine(projectRoot, "DESIGN.md");
            if (AlreadyContracted(projectRoot)) return;
            Directory.CreateDirectory(projectRoot);
            File.AppendAllText(design,
                (File.Exists(design) ? "\n\n" : "# Design\n\n") + contract.ToMarkdown() + "\n");
        }
        catch
        {
            // Kontraktet lever ändå i uppdragstexten - skrivfel får inte stoppa.
        }
    }

    private static string BuildEvidence(string projectRoot)
    {
        var sb = new System.Text.StringBuilder();
        try
        {
            var skip = new[] { ".git", ".worktrees", "node_modules", "build", "dist", "screenshots", "__pycache__" };
            var files = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                .Where(f => !Path.GetRelativePath(projectRoot, f)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Any(s => skip.Contains(s, StringComparer.OrdinalIgnoreCase)))
                .Take(30)
                .ToList();
            sb.AppendLine("Filer: " + string.Join(", ",
                files.Select(f => $"{Path.GetRelativePath(projectRoot, f)} ({new FileInfo(f).Length} B)")));
            foreach (var file in files.Where(f =>
                Path.GetExtension(f).ToLowerInvariant() is ".html" or ".js" or ".gd" or ".py" or ".cs").Take(4))
            {
                var content = File.ReadAllText(file);
                if (content.Length > 3000) content = content[..3000] + "…";
                sb.AppendLine($"\n--- {Path.GetRelativePath(projectRoot, file)} ---\n{content}");
            }
        }
        catch { /* delbevis räcker */ }
        return sb.ToString();
    }
}
