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

        Respond with ONLY this JSON, nothing else:
        {"pillars":"one sentence: the experience pillars","twist":"one sentence: the unique twist","criteria":["...","..."]}

        Rules for criteria (4-8 items, in the user's language):
        - MEASURABLE and CHECKABLE: counts, named features, concrete content
          ("5 levels with rising difficulty", "3 enemy types", "sound effects
          for jump/coin/hit", "pause menu with restart") - never vague quality
          words ("fun", "polished").
        - Achievable by one developer in one sitting on the existing scaffold.
        - When the user message contains INSPIRATION SEEDS: build the twist
          around them and turn EACH seed into one concrete criterion.
        - At least TWO criteria must be NEW named mechanics that do NOT exist
          in a generic starter kit (the kit already has menus, score, save,
          difficulty levels and basic play) - name each mechanic explicitly.
        """;

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
        IReadOnlyList<string>? pastLessons = null)
    {
        var contract = await AskModelAsync(userPrompt, strongModelHint, complete, ct, inspirationSeeds, pastLessons)
            ?? FallbackContract(userPrompt, inspirationSeeds);
        // Spelkänslan in i kontraktet för motorspel: regissörsmodeller är bra
        // på INNEHÅLL (5 banor, 3 fiendetyper) men glömmer KÄNSLAN - och
        // grinden följer bara upp det som står i kontraktet. Punkterna läggs
        // FÖRE skrivningen till DESIGN.md så uppföljningar läser tillbaka dem.
        contract = contract with { Criteria = EnsureEngineFeelCriteria(contract.Criteria, engine) };
        TryAppendToDesign(projectRoot, contract);
        return contract;
    }

    /// <summary>Standing production criteria for engine games (godot/unity):
    /// sound on actions, visible feedback, screen transitions, difficulty levels
    /// that actually differ, game-feel/juice (C1), and smooth performance (C3).
    /// Only criteria whose topic the contract does not already cover are added -
    /// the director's own wording wins.</summary>
    internal static IReadOnlyList<string> EnsureEngineFeelCriteria(IReadOnlyList<string> criteria, string? engine)
    {
        if (engine is not ("godot" or "unity"))
            return criteria;

        (string[] Keywords, string Criterion)[] feel =
        [
            (["ljud", "sound", "sfx", "audio"],
                "Ljudeffekt på varje viktig spelarhandling (välja/köpa/träffa/plocka) och tydlig ljudsignal vid vinst och förlust"),
            (["animation", "feedback", "blink", "skaka", "shake"],
                "Synlig feedback på varje interaktion: knapptryck, träffar och poängändringar ska märkas direkt (animation/färgblink/skalpuls)"),
            (["övergång", "overgang", "transition", "fade"],
                "Mjuka övergångar mellan skärmarna (titel/spel/resultat) - inga hårda klipp"),
            (["svårighet", "svarighet", "difficulty"],
                "Svårighetsgraderna ska kännas mätbart olika i spel (olika startvärden/fiendefart), inte bara heta olika"),
            // C1 (game-feel/juice): den konkreta grejen som skiljer en prototyp
            // från ett produktionsspel. Mer specifik än "synlig feedback" ovan.
            (["juice", "partik", "particle", "screenshake", "skärmskak", "skarmskak", "tween", "hit-stop", "hitstop", "easing"],
                "Game-feel/juice: screenshake vid träffar/kollisioner, partiklar vid poäng/träffar/explosioner, och tweenad rörelse med easing i stället för hårda hopp - spelet ska KÄNNAS, inte bara fungera"),
            // C3 (prestanda): grinden mäter redan FPS och underkänner hackiga
            // byggen - kriteriet gör det till ett uttalat mål så modellen undviker
            // tunga per-frame-mönster från början.
            (["prestanda", "fps", "smidig", "performance", "ruckel", "hack", "allokering"],
                "Smidig prestanda: 60 FPS-mål, inga tunga per-frame-allokeringar (skapa noder/texturer/resurser i _ready, inte varje bildruta) - spelet får inte hacka ens med många objekt på skärmen"),
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
        var contract = isGame
            ? new Contract(
                "Lätt att lära, svår att bemästra - varje omgång ska kännas rättvis.",
                "Svårigheten stiger märkbart och belönar skicklighet.",
                [
                    "Minst 3 nivåer/vågor med stigande svårighetsgrad",
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
