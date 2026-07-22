using System.Text;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>One developer's slice of a team build. Difficulty ("hard" |
/// "medium" | "simple") lets the team assign a STRONGER model to the harder
/// tracks and cheaper models to the easy ones - different models collaborating
/// on one goal, one machine (B5/multi-modell).</summary>
public sealed record TeamTrack(string Title, string Description, string Difficulty = "medium");

/// <summary>
/// The "small company" build: an ARCHITECT model call splits the remaining
/// work into independent tracks, one DEVELOPER agent per track runs in its
/// own git worktree (true filesystem isolation - GitIsolationService), the
/// branches merge back one by one, and any track whose merge conflicts is
/// REDONE sequentially on top of the merged result so the build always
/// converges. The caller's quality gate (AssignmentQualityGate) then judges
/// the merged whole exactly like a single-agent run.
///
/// Runs on ONE worker: worktrees need a shared repo, and a cross-machine
/// team would need remote sync this deliberately does not have yet. With a
/// cloud provider the tracks genuinely run in parallel; with one local GPU
/// they interleave - correctness is the same either way.
/// </summary>
public static class TeamBuild
{
    private const int MaxTeamSize = 4;

    /// <summary>Returns null when a team build is impossible here (no git, or
    /// the repo could not be initialized/committed) - the caller falls back to
    /// the ordinary single-agent run, so team mode can never BREAK a build.</summary>
    public static async Task<AgentRunResult?> RunAsync(
        string assignment,
        int teamSize,
        string workspaceRoot,
        AgentAccessLevel accessLevel,
        string? modelHint,
        string system,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        Func<string, AgentToolExecutor> executorFor,
        Func<AgentStep, Task> emit,
        GitService git,
        GitIsolationService isolation,
        CancellationToken ct,
        string? architectHint = null,
        Func<string, string?>? modelForTrack = null,
        // Max$-taket (B5): spårens och redo-rundornas looppar måste få samma
        // tak som huvudflödets loop - annars är taket dött för hela team-
        // bygget (granskningen v1.83). Null = ingen gräns, som förr.
        decimal? maxCostUsd = null,
        Func<decimal>? spentSoFar = null)
    {
        teamSize = Math.Clamp(teamSize, 2, MaxTeamSize);
        // Per-spår-modell: hårda spår får den starka modellen, enkla en billig -
        // arkitektens svårighetsbedömning avgör. Utan resolver (eller okänd
        // svårighet) faller allt tillbaka på modelHint = tidigare beteende.
        string? ModelFor(TeamTrack t) => modelForTrack?.Invoke(t.Difficulty) is { Length: > 0 } m ? m : modelHint;

        // ---- 1. Grund: repo + baslinje ----------------------------------
        // Varje tidigt avhopp EMITTAS med orsak - "Team-läget slutförde inte
        // bygget" utan förklaring gick inte att felsöka (rapporterat: föll
        // tillbaka direkt, och orsaken visade sig vara att git saknades på
        // maskinen).
        if (!await git.InitAsync(workspaceRoot, ct))
        {
            await emit(new AgentStep("tool_error",
                "Team-läget: git init misslyckades i arbetsytan - finns git på den här datorn? " +
                "(noden försöker provisionera git automatiskt före team-byggen från v1.44.0)."));
            return null;
        }
        EnsureGitignore(workspaceRoot);
        await git.CommitAsync(workspaceRoot, "AiLocal team: baslinje", ct);
        // Ingen gren = ingen commit gick att göra (tom mapp, trasig git-
        // identitet, ...) - worktrees kräver en riktig HEAD att utgå från.
        if (await git.GetCurrentBranchAsync(workspaceRoot, ct) is null)
        {
            await emit(new AgentStep("tool_error",
                "Team-läget: baslinje-commiten gick inte att skapa (ingen gren/HEAD i arbetsytan)."));
            return null;
        }

        // ---- 2. Arkitekten ----------------------------------------------
        await emit(new AgentStep("tool_call", "teamarkitekt (delar upp arbetet i oberoende spår)"));
        // Arkitekten är ETT anrop som styr hela teamets riktning - den körs
        // på den starka tiern (architectHint) även när utvecklarna kör
        // billigare modeller.
        var tracks = await PlanTracksAsync(assignment, workspaceRoot, teamSize, architectHint ?? modelHint, complete, ct)
            ?? FallbackTracks(assignment, workspaceRoot);
        tracks = tracks.Take(teamSize).ToList();
        if (tracks.Count < 2)
            tracks = FallbackTracks(assignment, workspaceRoot).Take(teamSize).ToList();
        await emit(new AgentStep("tool_result",
            string.Join("\n", tracks.Select((t, i) => $"Spår {i + 1} [{t.Difficulty}]: {t.Title} - {t.Description}"))));

        // ---- 3. Worktrees + parallella utvecklare -----------------------
        var runs = new List<TrackRun>();
        foreach (var track in tracks)
        {
            var iso = await isolation.CreateAsync(workspaceRoot, track.Title, ct: ct);
            runs.Add(new TrackRun(track, iso));
        }

        var iterations = 0;
        var inputTokens = 0;
        var outputTokens = 0;
        var usageLock = new object();
        void AddUsage(AgentRunResult r)
        {
            lock (usageLock)
            {
                iterations += r.Iterations;
                inputTokens += r.TotalUsage.InputTokens;
                outputTokens += r.TotalUsage.OutputTokens;
            }
        }

        await Task.WhenAll(runs.Where(r => r.Iso is not null).Select(async run =>
        {
            // v1.95: inhägna spårets executor i worktreen - live sågs spår
            // skriva med absoluta vägar RAKT I HUVUDROTEN (förbi isolationen)
            // så två spår krockade i samma Main.gd.
            var trackExecutor = executorFor(run.Iso!.WorktreePath);
            trackExecutor.ConfineToRoot = true;
            var loop = new AgentLoop(complete, trackExecutor, maxCostUsd, spentSoFar);
            Func<AgentStep, Task> trackEmit = step =>
                emit(new AgentStep(step.Kind, $"[{run.Track.Title}] {step.Detail}"));
            var trackModel = ModelFor(run.Track);
            await trackEmit(new AgentStep("thinking",
                $"svårighet {run.Track.Difficulty} - modell {(string.IsNullOrWhiteSpace(trackModel) ? "auto" : trackModel)}"));
            var windowStart = DateTime.UtcNow;
            var result = await loop.RunAsync(
                TrackPrompt(assignment, run.Track, tracks.Count), accessLevel, trackModel,
                onStep: trackEmit, ct, system: system);
            AddUsage(result);
            // v1.96: samma iterationstak-fortsättning som ensamagenten (v1.32) -
            // live dog två av fyra spår på 50-taket MITT i riktigt arbete och
            // kasserades. Tak + filframsteg i senaste fönstret = fortsätt med
            // historiken kvar, upp till 4 fönster; utan framsteg = runaway-stopp.
            for (var round = 2; result.HitIterationCap && round <= 4; round++)
            {
                if (ProjectRootDetector.NewestWriteUtc(run.Iso!.WorktreePath) < windowStart)
                {
                    await trackEmit(new AgentStep("tool_error",
                        "iterationstaket nåddes utan filändringar i senaste rundan - spåret stannar här (runaway-skydd)."));
                    break;
                }
                await trackEmit(new AgentStep("thinking",
                    $"iterationstaket nått men spåret gör framsteg - fortsätter där det slutade (runda {round} av 4)."));
                windowStart = DateTime.UtcNow;
                result = await loop.RunAsync(
                    "Du nådde iterationstaket men ditt spår är inte klart än. Fortsätt EXAKT där du slutade - " +
                    "slutför återstoden av spåret, kör verify, och avsluta när allt är på plats.",
                    accessLevel, trackModel, onStep: trackEmit, ct, history: result.Messages, system: system);
                AddUsage(result);
            }

            // Plan-i-stället-för-utförande per spår (samma vakt som huvud-
            // flödets, v1.39.0): "Here is my plan... Let me know" räknas
            // aldrig som genomfört spår - noden svarar åt teamledaren: utför.
            if (result.Success && PlanOnlyDetector.LooksUnexecuted(result.FinalAnswer))
            {
                await trackEmit(new AgentStep("tool_error",
                    "spåret avslutade med en plan/fråga i stället för att utföra - noden svarar automatiskt: utför planen."));
                result = await loop.RunAsync(
                    "Planen är godkänd. UTFÖR den nu i sin helhet - fråga aldrig om lov; ingen människa läser " +
                    "under bygget. Skapa/ändra filerna med write_file/edit_file, kör verify, och avsluta först " +
                    "när ändringarna ligger på disk.",
                    accessLevel, trackModel, onStep: trackEmit, ct, history: result.Messages, system: system);
                AddUsage(result);
            }

            // Svaga modeller "svarar" gärna med prosa och noll verktygsanrop
            // (observerat live: båda spåren skrev ingenting). Samma filosofi
            // som kvalitetsgrindens skrev-inget-underkännande, fast per spår:
            // en tvingande korrigeringsrunda med historiken kvar.
            if (result.Success && !await git.HasUncommittedChangesAsync(run.Iso.WorktreePath, ct))
            {
                await trackEmit(new AgentStep("tool_error",
                    "spåret skrev inga filer - en tvingande korrigeringsrunda körs."));
                result = await loop.RunAsync(
                    "Du skrev inga filer alls. Genomför ditt spår PÅ RIKTIGT nu: skapa/ändra filerna med " +
                    "write_file/edit_file, kör verify, och avsluta först när ändringarna ligger på disk. " +
                    "Svara inte med text, planer eller frågor - de kastas bort.",
                    accessLevel, trackModel, onStep: trackEmit, ct, history: result.Messages, system: system);
                AddUsage(result);
            }

            run.Result = result;
            // Committa spårets arbete i worktreet så merge har något att ta.
            var commit = await isolation.CommitAsync(run.Iso.TaskId, $"AiLocal team: {run.Track.Title}");
            run.ProducedChanges = commit.Success;
        }));

        // ---- 4. Merge, spår för spår ------------------------------------
        var summary = new StringBuilder();
        var redo = new List<TeamTrack>();
        foreach (var run in runs)
        {
            if (run.Iso is null)
            {
                // Worktreet kunde inte skapas - kör spåret sekventiellt efteråt.
                redo.Add(run.Track);
                continue;
            }
            if (!run.ProducedChanges)
            {
                // En tom gren "mergar" alltid (Already up to date) - att kalla
                // det "klart" vore exakt det falska-Klar-mönster grinden finns
                // för att stoppa. Ärlig rapport + kassering i stället.
                var reason = run.Result is { Success: true }
                    ? "producerade inga ändringar"
                    : "misslyckades utan att skriva något";
                await emit(new AgentStep("tool_error",
                    $"[{run.Track.Title}] {reason} - spåret kasseras."));
                await isolation.DiscardAsync(run.Iso.TaskId, ct);
                summary.AppendLine($"- {run.Track.Title}: {reason}.");
                continue;
            }
            if (run.Result is not { Success: true })
            {
                // v1.95: ett spår som slog i taket/föll MEN skrev riktiga
                // ändringar kasseras INTE längre - live kasserades ALLA fyra
                // spåren (timmar av arbete) och bygget började om från noll.
                // Ändringarna mergas; kvalitetsgrinden + fixrundorna tar resten.
                await emit(new AgentStep("thinking",
                    $"[{run.Track.Title}] nådde taket/föll men skrev riktiga ändringar - mergas ändå (grinden tar resten)."));
            }

            var (merged, mergeOutput) = await isolation.MergeAsync(run.Iso.TaskId, ct);
            if (merged)
            {
                summary.AppendLine($"- {run.Track.Title}: klart och mergat.");
            }
            else
            {
                // Konflikt: kasta grenen och GÖR OM spåret ovanpå det som
                // redan mergats - sekventiellt kan aldrig kollidera, så
                // bygget konvergerar alltid.
                await emit(new AgentStep("tool_error",
                    $"[{run.Track.Title}] merge-konflikt ({FirstLine(mergeOutput)}) - spåret görs om ovanpå det mergade resultatet."));
                await git.AbortMergeAsync(workspaceRoot, ct);
                await isolation.DiscardAsync(run.Iso.TaskId, ct);
                redo.Add(run.Track);
            }
        }

        // ---- 5. Redo-på-toppen ------------------------------------------
        foreach (var track in redo)
        {
            if (ct.IsCancellationRequested) break;
            await emit(new AgentStep("tool_call", $"gör om spåret \"{track.Title}\" ovanpå det mergade projektet"));
            var loop = new AgentLoop(complete, executorFor(workspaceRoot), maxCostUsd, spentSoFar);
            var redoModel = ModelFor(track);
            var result = await loop.RunAsync(
                RedoPrompt(assignment, track), accessLevel, redoModel,
                onStep: step => emit(new AgentStep(step.Kind, $"[{track.Title}] {step.Detail}")),
                ct, system: system);
            AddUsage(result);
            // Samma plan-vakt som i worktree-spåren - omtag som slutar i en
            // plan konvergerar aldrig.
            if (result.Success && PlanOnlyDetector.LooksUnexecuted(result.FinalAnswer))
            {
                result = await loop.RunAsync(
                    "Planen är godkänd. UTFÖR den nu i sin helhet - skapa/ändra filerna, kör verify, " +
                    "och avsluta först när ändringarna ligger på disk.",
                    accessLevel, redoModel,
                    onStep: step => emit(new AgentStep(step.Kind, $"[{track.Title}] {step.Detail}")),
                    ct, history: result.Messages, system: system);
                AddUsage(result);
            }
            // Samma ärlighetsregel som för worktree-spåren: bara ett omtag som
            // faktiskt committade ändringar får kallas klart.
            var redoCommit = result.Success
                ? await git.CommitAsync(workspaceRoot, $"AiLocal team (redo): {track.Title}", ct)
                : new GitCommitResult(false, "");
            summary.AppendLine(result.Success && redoCommit.Success
                ? $"- {track.Title}: klart (omgjort sekventiellt)."
                : $"- {track.Title}: misslyckades även i omtaget.");
        }

        var landed = summary.ToString().Split('\n').Count(l => l.Contains(": klart"));
        if (landed == 0)
        {
            // Hela teamet gick bet - ge inte upp bygget: null låter anroparen
            // köra om som EN agent, där kvalitetsgrindens tvingande fixrundor
            // och eskalering tar vid. Teamet får aldrig ge ett sämre utfall
            // än vad en ensam agent hade gett.
            await emit(new AgentStep("tool_error",
                "Inget spår producerade ändringar - teamet avbryts och bygget körs som en ensam agent i stället."));
            return null;
        }
        var final = $"Team-bygge med {tracks.Count} spår:\n{summary.ToString().TrimEnd()}";
        return new AgentRunResult(
            true, final,
            [new AgentStep("done", final)],
            iterations,
            [],
            new TokenUsage(inputTokens, outputTokens));
    }

    private sealed class TrackRun(TeamTrack track, IsolatedTask? iso)
    {
        public TeamTrack Track { get; } = track;
        public IsolatedTask? Iso { get; } = iso;
        public AgentRunResult? Result { get; set; }
        public bool ProducedChanges { get; set; }
    }

    // ---- Arkitekten -----------------------------------------------------

    private const string ArchitectSystem = """
        You are the ARCHITECT of a small development team. Split the user's
        build request into INDEPENDENT work tracks, one per developer. The
        project base already exists - the tracks describe how to EXTEND it.

        Rules:
        - Tracks must be as file-independent as possible (different features,
          different files). Never give two tracks the same responsibility.
        - Each description tells ONE developer concretely what to build, in
          the same language the user wrote in.
        - CONCRETE FILES: every description must NAME the 2-5 files the
          developer creates or edits (e.g. "skapa enemies.gd + koppla in i
          Game.tscn") - a developer given vague scope writes prose instead
          of code. Small scope beats grand scope: each track must be
          finishable by one developer in one sitting.
        - NEVER create a planning/design/documentation-only track. Every
          track produces working code on disk.
        - RATE each track's difficulty as "hard", "medium", or "simple" by how
          much careful reasoning the CODE needs: core gameplay/mechanics/state
          = hard; content/levels/variation = medium; audio/menus/polish = simple.
          The team gives a STRONGER model to harder tracks and a cheaper one to
          the easy tracks, so an honest rating spends the budget where it matters.
        - Respond with ONLY this JSON, nothing else:
          {"tracks":[{"title":"...","description":"...","difficulty":"hard|medium|simple"}]}
        """;

    private static async Task<List<TeamTrack>?> PlanTracksAsync(
        string assignment, string workspaceRoot, int teamSize, string? modelHint,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete, CancellationToken ct)
    {
        try
        {
            var projectRoot = ProjectRootDetector.Detect(workspaceRoot) ?? workspaceRoot;
            var files = ListProjectFiles(projectRoot);
            var prompt = $"Build request: {assignment}\n\nCurrent project files:\n{files}\n\nSplit the remaining work into exactly {teamSize} independent tracks.";
            var response = await complete(new ChatRequest
            {
                System = ArchitectSystem,
                Messages = [new ChatMessage("user", prompt)],
                ModelHint = modelHint,
                MaxTokens = 800
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content))
                return null;
            return ParseTracks(response.Response.Content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    internal static List<TeamTrack>? ParseTracks(string content)
    {
        var text = content.Trim();
        if (text.StartsWith("```"))
        {
            var firstNewline = text.IndexOf('\n');
            var lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewline >= 0 && lastFence > firstNewline)
                text = text[(firstNewline + 1)..lastFence].Trim();
        }
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        try
        {
            using var doc = JsonDocument.Parse(text[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("tracks", out var tracksEl) || tracksEl.ValueKind != JsonValueKind.Array)
                return null;
            var tracks = new List<TeamTrack>();
            foreach (var el in tracksEl.EnumerateArray())
            {
                var title = el.TryGetProperty("title", out var t) ? t.GetString() : null;
                var description = el.TryGetProperty("description", out var d) ? d.GetString() : null;
                var difficulty = el.TryGetProperty("difficulty", out var df) ? df.GetString() : null;
                if (!string.IsNullOrWhiteSpace(title) && !string.IsNullOrWhiteSpace(description))
                    tracks.Add(new TeamTrack(title.Trim(), description.Trim(), NormalizeDifficulty(difficulty)));
            }
            return tracks.Count >= 2 ? tracks : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Klampar arkitektens svar till "hard"/"medium"/"simple" - okänt/
    /// tomt blir "medium" (den routade standardmodellen).</summary>
    internal static string NormalizeDifficulty(string? raw)
    {
        var d = (raw ?? "").Trim().ToLowerInvariant();
        return d is "hard" or "simple" ? d : "medium";
    }

    /// <summary>Deterministic floor, same philosophy as the pre-scaffold: a
    /// weak model that can't produce parseable JSON still gets a sensible
    /// team split instead of killing the whole team run.</summary>
    internal static List<TeamTrack> FallbackTracks(string assignment, string workspaceRoot)
    {
        var projectRoot = ProjectRootDetector.Detect(workspaceRoot) ?? workspaceRoot;
        var engine = GameBuilder.DetectEngine(projectRoot);
        var isGame = engine != "unknown"
            || assignment.Contains("spel", StringComparison.OrdinalIgnoreCase)
            || assignment.Contains("game", StringComparison.OrdinalIgnoreCase);

        // Filråden måste följa motorn - "lägg koden i en js-fil" i ett
        // Godot-projekt skickar utvecklaren åt helt fel håll.
        var fileHint = engine switch
        {
            "godot" => "Lägg ny logik i EGNA .gd-skript/scener och koppla in dem med små riktade edit_file-ändringar i befintliga scener.",
            "unity" => "Lägg ny logik i EGNA C#-skript under Assets och koppla in dem med små riktade edit_file-ändringar.",
            _ => "Lägg ny logik i en EGEN js-fil och länka in den med en <script src>-rad via edit_file."
        };

        return isGame
            ?
            [
                new("Innehåll och nivåer", $"Utöka spelet med mer innehåll: fler nivåer/vågor/varianter med stigande svårighetsgrad. {fileHint}", "medium"),
                new("Ljud och effekter", $"Förbättra ljudbilden och effekterna: ljud för varje viktig händelse och visuella effekter vid poäng, träffar och game over. {fileHint}", "simple"),
                new("Meny och polish", $"Förbättra menyer och känsla: startskärm med instruktioner, paus, game over med highscore och konsekvent färgtema. Gör små riktade edit_file-ändringar - skriv aldrig om hela filer andra spår rör.", "simple"),
                new("Mekanik och variation", $"Lägg till en ny spelmekanik som ger djup (power-ups, combo eller liknande) kopplad till poängsystemet. {fileHint}", "hard")
            ]
            :
            [
                new("Kärnfunktioner", "Implementera och förbättra applikationens kärnfunktioner enligt uppdraget. Håll dig till kärnlogikens filer.", "hard"),
                new("Robusthet och tester", "Lägg till felhantering, indata-validering och tester för den befintliga funktionaliteten. Skapa testerna i egna filer.", "medium"),
                new("Gränssnitt och finish", "Förbättra användarupplevelsen: tydliga utskrifter/gränssnitt, hjälptext och en README som förklarar hur allt används.", "simple"),
                new("Utökade funktioner", "Lägg till närliggande funktioner som gör applikationen mer komplett, i egna filer/moduler.", "medium")
            ];
    }

    // ---- Prompter -------------------------------------------------------

    private static string TrackPrompt(string assignment, TeamTrack track, int teamSize) =>
        $"{assignment}\n\n=== DIN ROLL I TEAMET ===\n" +
        $"Du är EN av {teamSize} utvecklare som bygger detta samtidigt. Projektet finns redan i din arbetsmapp - läs DESIGN.md och koden först, och UTÖKA det.\n" +
        $"DITT SPÅR: {track.Title}\n{track.Description}\n\n" +
        "Regler för teamarbetet:\n" +
        "- ARBETA ENBART GENOM VERKTYGEN (write_file/edit_file/verify). Ett svar utan verktygsanrop kastas bort - ingen människa läser det.\n" +
        "- Håll dig till ditt spår - andra utvecklare arbetar parallellt med sina.\n" +
        "- Använd RELATIVA vägar (t.ex. \"Main.gd\"). Din arbetsmapp är en ISOLERAD git-worktree - absoluta vägar utanför den avvisas; huvudprojektet mergas efteråt.\n" +
        "- Redigera ALDRIG filer via powershell/python-enradare (har setts korrumpera filer) - använd edit_file/write_file.\n" +
        "- Lägg ny kod i EGNA filer när det går, och koppla in dem med små edit_file-ändringar. Skriv ALDRIG om hela filer som andra spår också rör.\n" +
        "- Kör verify innan du avslutar.";

    private static string RedoPrompt(string assignment, TeamTrack track) =>
        $"{assignment}\n\n=== OMTAG EFTER MERGE-KONFLIKT ===\n" +
        $"Övriga teamets arbete är redan inarbetat i projektet i din arbetsmapp. Genomför nu DITT spår ovanpå det:\n" +
        $"SPÅR: {track.Title}\n{track.Description}\n\n" +
        "Läs den befintliga koden först och gör riktade ändringar (edit_file). Kör verify innan du avslutar.";

    // ---- Hjälpare -------------------------------------------------------

    private static void EnsureGitignore(string root)
    {
        try
        {
            var path = Path.Combine(root, ".gitignore");
            var existing = File.Exists(path) ? File.ReadAllText(path) : "";
            var needed = new[] { ".worktrees/", "node_modules/", "build/", "__pycache__/" };
            var missing = needed.Where(n => !existing.Contains(n)).ToArray();
            if (missing.Length > 0)
                File.AppendAllText(path, (existing.Length > 0 && !existing.EndsWith('\n') ? "\n" : "") + string.Join("\n", missing) + "\n");
        }
        catch
        {
            // .gitignore är en trevnad - utan den fungerar bygget ändå.
        }
    }

    private static string ListProjectFiles(string root)
    {
        try
        {
            var skip = new[] { ".git", ".worktrees", "node_modules", "build", "bin", "obj", "__pycache__" };
            return string.Join("\n", Directory
                .EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f))
                .Where(rel => !skip.Any(s => rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains(s, StringComparer.OrdinalIgnoreCase)))
                .Take(40));
        }
        catch
        {
            return "(kunde inte lista filer)";
        }
    }

    private static string FirstLine(string text)
    {
        var trimmed = (text ?? "").Trim();
        var i = trimmed.IndexOf('\n');
        return i < 0 ? trimmed : trimmed[..i];
    }
}
