using System.Text.RegularExpressions;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.2.0: Structured per-genre delivery contracts with grep-verifiable
/// constraints. Instead of prose criteria that only an LLM can review,
/// each genre lists REQUIRED code patterns, file expectations, and
/// minimum counts. The quality gate can verify ~70% automatically
/// (grep/stat), escalating only the remaining ~30% to model review.
///
/// This is the floor BELOW the DirectorPass creative contract:
/// GenreContracts guarantees the game has the right SHAPE (a platformer
/// has gravity and jumping), while DirectorPass demands it's INTERESTING
/// (5 handcrafted levels with unique themes).
/// </summary>
public static class GenreContracts
{
    public record Constraint(string Id, string Description, string GrepPattern, int MinCount = 1);
    public record GenreSpec(string Genre, Constraint[] MustHave, Constraint[] ShouldHave);

    private static readonly Dictionary<string, GenreSpec> _specs = new()
    {
        ["platformer"] = new("platformer",
            MustHave: [
                new("gravity", "Gravity / falling physics", @"gravity|GRAVITY|move_and_slide"),
                new("jump", "Jump mechanic", @"jump|JUMP|is_on_floor"),
                new("enemy", "At least 1 enemy type", @"enemy|fiende|ENEMY"),
                new("collectible", "Collectible items (coins/etc)", @"coin|collect|mynt|poang"),
                new("win_condition", "Win condition (flag/goal/level-end)", @"win|flag|goal|finish|level_complete|FINAL_LEVEL"),
                new("lose_condition", "Lose condition (HP/death/falls)", @"hp|health|die|dead|lose|lives"),
            ],
            ShouldHave: [
                new("coyote_time", "Coyote time / jump buffer", @"coyote|jump_buffer"),
                new("screenshake", "Screen shake on damage", @"shake|screenshake"),
                new("particles", "Particle effects", @"CPUParticles|particle|partiklar"),
                new("multiple_levels", "Multiple levels (>1)", @"level|LEVEL|niva", 2),
                new("difficulty_modes", "Difficulty modes", @"difficulty|svarighet|Easy|Normal|Hard"),
            ]
        ),
        ["party"] = new("party",
            MustHave: [
                new("board_tiles", "Board with tile positions", @"TILE_COUNT|tile_positions|board_tiles"),
                new("dice_roll", "Dice roll mechanic", @"dice|roll|tarning|_do_roll"),
                new("players_4", "4 players (1 human + 3 AI)", @"PLAYERS|pstate|4.*player"),
                new("turn_system", "Turn-based flow", @"turn_phase|turn_idx|next_player"),
                new("minigame_count", "At least 3 minigame types", @"minigame|_start_minigame", 3),
            ],
            ShouldHave: [
                new("star_economy", "Star/coin economy", @"stars?|STAR|coins?|COINS"),
                new("board_layouts", "Multiple board layouts", @"BOARD_RING|BOARD_SERPENTINE|layout", 2),
                new("ai_opponents", "AI opponents", @"ai.*true|not.*ai|PLAYERS.*ai"),
                new("difficulty_modes", "Difficulty modes", @"difficulty|Easy|Normal|Hard"),
                new("rounds_loop", "Round-based game loop", @"ROUNDS|round\s*[<>]"),
            ]
        ),
        ["racing"] = new("racing",
            MustHave: [
                new("vehicle_physics", "Vehicle physics (accel/steer)", @"accel|steer|heading|vel"),
                new("track", "Track with boundaries", @"track|bana|on_track|checkpoint"),
                new("laps", "Lap counting", @"lap|varv|LAPS"),
                new("timer", "Lap/race timer", @"timer|time|best"),
            ],
            ShouldHave: [
                new("multiple_tracks", "Multiple tracks", @"track|bana", 2),
                new("ai_racers", "AI opponent racers", @"ai|opponent|motstand"),
                new("checkpoint_order", "Ordered checkpoints", @"checkpoint.*order|CHECKPOINT"),
            ]
        ),
        ["artillery"] = new("artillery",
            MustHave: [
                new("projectile", "Projectile/ballistic firing", @"projectile|fire|shoot|simulate_shot"),
                new("terrain", "Destructible terrain", @"terrain|terrang|crater|height"),
                new("turn_based", "Turn-based combat", @"turn|tur|opponent|OPPONENTS"),
                new("aiming", "Aiming (angle + power)", @"angle|power|kraft|vinkel"),
            ],
            ShouldHave: [
                new("wind", "Wind affecting shots", @"wind|vind"),
                new("weapons", "Multiple weapon types", @"weapon|vapen|WEAPONS"),
                new("ai_difficulty", "AI with difficulty scaling", @"ai|difficulty|traffsakerhet"),
            ]
        ),
        ["rpg"] = new("rpg",
            MustHave: [
                new("movement_8way", "8-way or free movement", @"move_and_slide|velocity|speed"),
                new("enemies", "Enemies with HP", @"enemy|fiende|hp|health"),
                new("combat", "Combat/damage system", @"damage|attack|skada|hp"),
                new("pickups", "Item/coin pickups", @"item|pickup|coin|collect|plock"),
            ],
            ShouldHave: [
                new("waves", "Wave/spawn system", @"wave|spawn|FINAL_WAVE"),
                new("xp_level", "XP/level progression", @"xp|level|experience"),
                new("shop", "Shop between levels", @"shop|butik|buy|kopa"),
                new("boss", "Boss enemy", @"boss|BOSS"),
            ]
        ),
        ["shooter"] = new("shooter",
            MustHave: [
                new("projectile", "Projectile spawning", @"projectile|bullet|shoot|fire"),
                new("aiming", "Aiming direction", @"aim|direction|angle|look_at"),
                new("enemy_waves", "Enemy waves", @"wave|spawn|enemy"),
                new("hp_system", "HP/health system", @"hp|health|life"),
            ],
            ShouldHave: [
                new("powerups", "Power-up drops", @"powerup|upgrade|power"),
                new("score_combo", "Score/combo system", @"score|combo|poang"),
                new("difficulty_ramp", "Escalating difficulty", @"difficulty|harder|faster"),
            ]
        ),
        ["management"] = new("management",
            MustHave: [
                new("resource_system", "Resource/budget management", @"budget|gold|money|coins|currency"),
                new("roster", "Roster/team/employees", @"roster|team|trupp|players?|employees?"),
                new("rounds", "Round/simulation loop", @"round|season|week|omgang|SEASON"),
                new("buy_sell", "Buy/sell or trade mechanic", @"buy|sell|trade|market|kopa"),
            ],
            ShouldHave: [
                new("save_load", "Save/load system", @"save|load|spara|ladda"),
                new("ai_teams", "AI competitors/teams", @"ai|opponent|rival|motstand"),
                new("upgrades", "Upgrade system", @"upgrade|improve|level|uppgradera"),
                new("table_standings", "League table/standings", @"table|standing|tabell|liga"),
            ]
        ),
        ["puzzle"] = new("puzzle",
            MustHave: [
                new("grid_board", "Grid/board system", @"grid|board|rutnat|N\s*="),
                new("slide_move", "Slide/move mechanic", @"slide|move|swap|merge|_slide"),
                new("score_target", "Score target or win condition", @"TARGET|win|score|poang"),
            ],
            ShouldHave: [
                new("animations", "Move animations/tweening", @"tween|anim|Tween"),
                new("save_best", "Saved best score", @"best|highscore|save"),
                new("undo", "Undo capability", @"undo|angra"),
            ]
        ),
    };

    /// <summary>Get the genre spec, or null for unsupported genres.</summary>
    public static GenreSpec? ForGenre(string genre) =>
        _specs.TryGetValue(genre, out var spec) ? spec : null;

    /// <summary>Run grep-verifiable constraint checks against project source.
    /// Returns (met_must, met_should, findings_list). "findings" are unmet
    /// constraints formatted as human-readable lines suitable for the quality
    /// gate report.</summary>
    /// <summary>Begärt antal minispel ur prompten ("15 minigames", "5 minispel")
    /// - gör "bygg 15 minispel" till ett MÄTBART krav i stället för en from
    /// förhoppning. Null när prompten inte anger något antal.</summary>
    public static int? RequestedMinigames(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:st\s+)?(?:olika\s+)?(?:minigames?|mini-?spel|minispel)",
            RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n)
            ? Math.Clamp(n, 1, 30) : null;
    }

    /// <summary>Begärt antal kartor/banor/brädor ur prompten ("3 kartor",
    /// "5 banor", "2 maps") - samma mätbarhetsprincip som minispelen.</summary>
    public static int? RequestedBoards(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var m = Regex.Match(text, @"(\d{1,2})\s*(?:olika\s+)?(?:kartor|maps?|banor|br[aä]dor|boards?)",
            RegexOptions.IgnoreCase);
        return m.Success && int.TryParse(m.Groups[1].Value, out var n)
            ? Math.Clamp(n, 1, 20) : null;
    }

    /// <summary>Räknar FAKTISKA minispel i projektet - naiv förekomsträkning
    /// av ordet "minigame" skalar inte med antalet. Tre konventioner räknas
    /// och största vinner: distinkta minigame_type == N-grenar (enfilskit),
    /// distinkta "# Minigame N"-rubriker, och Mg*.gd-filer (flerfilskit).</summary>
    internal static int CountMinigames(string projectRoot, string sourceText)
    {
        var distinct = new HashSet<string>();
        foreach (Match m in Regex.Matches(sourceText, @"minigame_type\s*==\s*(\d+)"))
            distinct.Add("t" + m.Groups[1].Value);
        var headers = new HashSet<string>();
        foreach (Match m in Regex.Matches(sourceText, @"#\s*Minigame\s+(\d+)", RegexOptions.IgnoreCase))
            headers.Add(m.Groups[1].Value);
        var files = 0;
        try
        {
            files = Directory.EnumerateFiles(projectRoot, "Mg*.gd", SearchOption.AllDirectories).Count();
        }
        catch { /* delbevis räcker */ }
        return Math.Max(files, Math.Max(distinct.Count, headers.Count));
    }

    public static (int Met, int Total, List<string> Findings) Verify(
        string projectRoot, string genre, string? assignment = null)
    {
        var spec = ForGenre(genre);
        if (spec is null)
            return (0, 0, []);

        var findings = new List<string>();
        var met = 0;
        var total = spec.MustHave.Length;

        // Gather all source text in the project (recursive, top 40 files)
        var sourceText = "";
        try
        {
            foreach (var file in Directory.EnumerateFiles(projectRoot, "*",
                SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".gd") || f.EndsWith(".cs") ||
                            f.EndsWith(".js") || f.EndsWith(".html") ||
                            f.EndsWith(".py"))
                .Take(40))
            {
                try { sourceText += File.ReadAllText(file) + "\n"; }
                catch { /* skip unreadable */ }
            }
        }
        catch { /* directory gone */ }

        foreach (var c in spec.MustHave)
        {
            // v2.2: minispelskravet räknar RIKTIGA minispel (grenar/rubriker/
            // filer) och skalar med promptens begärda antal ("15 minigames"
            // => kravet är 15, inte kitgolvets 3).
            if (c.Id == "minigame_count")
            {
                var required = Math.Max(c.MinCount, RequestedMinigames(assignment) ?? 0);
                var actual = CountMinigames(projectRoot, sourceText);
                if (actual >= required)
                    met++;
                else
                    findings.Add(
                        $"SAKNAS ({genre}): {actual} minispel implementerade av {required} begärda - " +
                        "lägg fler (konvention: nya Mg*.gd-filer, eller nya minigame_type == N-grenar med \"# Minigame N\"-rubrik)");
                continue;
            }
            var matches = Regex.Matches(sourceText, c.GrepPattern,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (matches.Count >= c.MinCount)
                met++;
            else
                findings.Add(
                    $"SAKNAS ({genre}): {c.Description} — " +
                    $"förväntade minst {c.MinCount} förekomst av \"{c.GrepPattern}\", " +
                    $"hittade {matches.Count}");
        }

        // Should-have: report as advisory, don't count toward met/total
        foreach (var c in spec.ShouldHave)
        {
            // v2.5: begärt kart-/banantal ur prompten gör layoutkravet
            // MÄTBART - distinkta BOARD_*-konstanter respektive "# Board N"-
            // rubriker räknas mot det begärda antalet (hårt krav då).
            if (c.Id == "board_layouts" && RequestedBoards(assignment) is { } reqBoards && reqBoards > 1)
            {
                var layouts = new HashSet<string>();
                foreach (Match m in Regex.Matches(sourceText, @"BOARD_([A-Z_]+)\s*:?="))
                    layouts.Add(m.Groups[1].Value);
                foreach (Match m in Regex.Matches(sourceText, @"#\s*Board\s+(\d+)", RegexOptions.IgnoreCase))
                    layouts.Add("h" + m.Groups[1].Value);
                if (layouts.Count < reqBoards)
                {
                    total += 1;
                    findings.Add(
                        $"SAKNAS ({genre}): {layouts.Count} kartor/brädlayouter implementerade av {reqBoards} begärda - " +
                        "lägg fler (konvention: BOARD_*-konstant per layout, eller \"# Board N\"-rubrik)");
                }
                else
                {
                    total += 1;
                    met += 1;
                }
                continue;
            }
            var matches = Regex.Matches(sourceText, c.GrepPattern,
                RegexOptions.IgnoreCase | RegexOptions.Multiline);
            if (matches.Count < c.MinCount)
                findings.Add(
                    $"REKOMMENDATION ({genre}): {c.Description} — " +
                    $"förväntade minst {c.MinCount} förekomst, hittade {matches.Count}");
        }

        return (met, total, findings);
    }
}
