using System.Text.RegularExpressions;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.2.0: Anti-pattern database for game development.
/// GameKnowledgeBase covers TECHNICAL errors (NullReferenceException, parse
/// errors). This covers DESIGN anti-patterns — things that compile but produce
/// a bad game. Each entry has a detection rule (regex/grep), a fix suggestion,
/// and a counter so frequently-recurring patterns can be [ADDRESS] The goal: catch "3 identical levels", "difficulty that doesn't differ",
/// "forgot game over screen" and similar quality problems BEFORE the playtest.
/// </summary>
public static class AntiPatternDb
{
    public record AntiPattern(string Id, string Description, string Severity,
        string DetectionRule, string FixSuggestion, int OccurrenceCount);

    private static readonly List<AntiPattern> _patterns = new()
    {
        // ──── CONTENT VARIETY ────────────────────────────────────────────────
        new("ap_identical_levels",
            "All levels share identical enemy types and counts",
            "medium",
            @"(level_1|level1|niva1).*(level_2|level2|niva2)",
            "Each level MUST introduce at least one new enemy type or increase enemy count by 50%+. "
            + "Add a 'level_data' array with per-level enemy counts and types.",
            0),

        new("ap_no_variation",
            "Same values repeated for all difficulty modes",
            "high",
            @"difficulty.*\[\s*\d+,\s*\d+,\s*\d+\]",
            "Difficulty values MUST differ by at least 30% between modes. "
            + "Easy: 0.6x base, Normal: 1.0x base, Hard: 1.5x base. "
            + "Check your difficulty arrays — if Easy=Normal=Hard, the game has no replay value.",
            0),

        new("ap_one_enemy_type",
            "Only one enemy type defined",
            "medium",
            @"enemy|ENEMY|fiende",
            "A game with only one enemy type gets repetitive after 30 seconds. "
            + "Add at least 2 more enemy types with different behaviors (patrol, chase, shoot).",
            0),

        // ──── MISSING SCREENS ────────────────────────────────────────────────
        new("ap_no_title_screen",
            "Game starts directly without a title/start screen",
            "high",
            @"(state\s*==\s*""playing""|state\s*!=\s*""title"")",
            "Every game needs a title screen with instructions. "
            + "Add a 'title' state with game name, controls hint, and a Start button.",
            0),

        new("ap_no_game_over",
            "No game over / win screen — game just stops or loops forever",
            "high",
            @"win|game.over|finish|result|GAME.OVER|FINISH|show_result",
            "When the player wins or loses, they need to SEE it. "
            + "Add a results screen showing score, rank, or final stats with a 'Play Again' button.",
            0),

        new("ap_no_pause",
            "No pause functionality",
            "low",
            @"pause|paus|PAUSE",
            "Add Esc/P to pause: track a 'paused' flag and skip _process/_physics_process when true.",
            0),

        // ──── CODE QUALITY ───────────────────────────────────────────────────
        // v2.8: "%s" % [args] AR korrekt GDScript-formatering - det gamla
        // monstret (%s|%d|%f rakt av) flaggade ALL riktig formatering och
        // fick en agent att sanera bort fungerande kod ur hela kallan
        // (live-sett: 4,3M tokens brann, spelet kraschade). Flagga bara
        // literaler som TILLDELAS .text UTAN %-operator = det spelaren
        // faktiskt ser ratt pa skarmen (samma logik som GdScriptLint.CheckUx).
        new("ap_format_strings",
            "Raw %s/%d placeholders assigned to .text without the % operator (player sees them literally)",
            "medium",
            @"\.text\s*\+?=\s*""[^""\n]*%[sdf][^""\n]*""\s*$",
            "Apply the format operator: node.text = \"Score: %d\" % [score] - "
            + "template literals stored in variables and formatted later are fine.",
            0),

        new("ap_magic_numbers",
            "Hardcoded magic numbers without named constants",
            "low",
            @"=\s*\d{2,}(?!.*#|.*//)",
            "Replace bare numbers with named constants at the top of the file. "
            + "'const PLAYER_SPEED := 300' is self-documenting; '300' is not.",
            0),

        new("ap_no_physics_delta",
            "Movement not multiplied by delta time",
            "high",
            @"position\.(x|y)\s*\+?=.*[^*]\s*\bdelta\b",
            "All movement MUST be scaled by delta: 'position.x += speed * delta'. "
            + "Without delta, game speed varies with framerate — 144Hz screens run 2.4x faster than 60Hz.",
            0),

        new("ap_tabs_spaces_mixed",
            "Mixed tabs and spaces in GDScript (Godot parse error risk)",
            "high",
            @"\t.*\n    |    .*\n\t",
            "GDScript requires CONSISTENT indentation. Choose tabs OR spaces, never both. "
            + "Godot 4 editor defaults to tabs; if copy-pasting from web, convert spaces to tabs.",
            0),

        // ──── BALANCE ────────────────────────────────────────────────────────
        new("ap_unwinnable_difficulty",
            "Enemy speed > player speed on any difficulty mode",
            "high",
            @"enemy.*speed.*>.*player.*speed|fiende.*hastighet.*>",
            "The player must be able to outrun or outmaneuver enemies. "
            + "Enemy speed should be 60-90% of player speed for fair gameplay. "
            + "Check your speed arrays — hard mode enemies shouldn't be faster than the player.",
            0),

        new("ap_no_healing",
            "No way to recover HP during gameplay",
            "medium",
            @"heal|health.pickup|potion|medkit",
            "Players need a way to recover HP. Add health pickups, healing zones, or HP regen between levels.",
            0),

        new("ap_spawn_camping",
            "Enemies spawn directly on top of the player",
            "medium",
            @"spawn.*player.*pos|player.*pos.*spawn",
            "Enemies should spawn at least 200px away from the player. "
            + "Add a distance check: 'if (spawn_pos.distance_to(player_pos) < MIN_SPAWN_DIST): continue'",
            0),

        // ──── UX ─────────────────────────────────────────────────────────────
        new("ap_no_feedback",
            "Player actions have no visual/audio feedback",
            "medium",
            @"play\(|sfx|sound|audio|_play\(",
            "Every player action (jump, collect, hit, die, win) should have a sound effect. "
            + "Add _play('jump'), _play('coin'), _play('hurt'), _play('win') calls.",
            0),

        new("ap_no_instructions",
            "No controls/instructions visible on title screen",
            "low",
            @"controls|styrning|arrows?|piltangenter|Space|Enter|WASD",
            "The title screen should show basic controls. "
            + "Add a line: 'Arrow keys to move, Space to jump, Esc to pause'.",
            0),
    };

    /// <summary>Scan source code for known anti-patterns.
    /// Returns list of (pattern, suggestion) for matched issues.</summary>
    public static List<(AntiPattern Pattern, string Context)> Scan(string sourceCode, string engine)
    {
        var findings = new List<(AntiPattern, string)>();

        // Engine-specific: GDScript tabs/spaces check
        if (engine == "godot" && ContainsMixedIndent(sourceCode))
        {
            var ap = _patterns.First(p => p.Id == "ap_tabs_spaces_mixed");
            findings.Add((ap, "Mixed tabs and spaces detected in GDScript"));
        }

        foreach (var ap in _patterns.Where(p => p.Id != "ap_tabs_spaces_mixed"))
        {
            try
            {
                var matches = Regex.Matches(sourceCode, ap.DetectionRule,
                    RegexOptions.IgnoreCase | RegexOptions.Multiline);
                // For "MUST HAVE" patterns (like title screen, game over),
                // we check that the pattern EXISTS (positive check). For
                // "SHOULD NOT HAVE" patterns (like format strings), we check
                // that it does NOT exist (negative check).
                var isShouldNotHave = ap.Id.StartsWith("ap_format") ||
                                      ap.Id.StartsWith("ap_magic") ||
                                      ap.Id.StartsWith("ap_tabs");

                if (isShouldNotHave && matches.Count > 0)
                {
                    findings.Add((ap, $"Found {matches.Count} occurrence(s)"));
                }
                else if (!isShouldNotHave && matches.Count == 0)
                {
                    findings.Add((ap, "Pattern NOT found — missing feature"));
                }
            }
            catch (RegexParseException)
            {
                // Skip broken regex patterns silently
            }
        }

        return findings;
    }

    private static bool ContainsMixedIndent(string source)
    {
        var hasTabs = source.Contains('\t');
        var has4Spaces = source.Contains("\n    ");
        return hasTabs && has4Spaces;
    }

    /// <summary>Format findings as human-readable quality gate output.</summary>
    public static IReadOnlyList<string> FormatFindings(
        List<(AntiPattern Pattern, string Context)> findings)
    {
        return findings.Select(f =>
            $"DESIGN-REKO ({f.Pattern.Severity}): {f.Pattern.Description}. " +
            $"Fix: {f.Pattern.FixSuggestion} [Kontext: {f.Context}]"
        ).ToList();
    }
}
