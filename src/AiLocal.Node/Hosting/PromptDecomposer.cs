using AiLocal.Core.Contracts;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.2.0: Prompt decomposition engine. Before the build starts, complex
/// game prompts are broken into structured sub-tasks that map to kits,
/// mechanics, and TeamBuild tracks. Weak models fail at decomposition
/// because they try to build everything at once in one file; this engine
/// gives them the blueprint.
///
/// Pipeline: User prompt → DetectGenre → Decompose → DirectorPass contract
/// → TeamBuild track assignment (or single-agent fallback).
/// </summary>
public static class PromptDecomposer
{
    public record SubTask(string Id, string Description, string Category,
        string? KitHint, string? MechanicHint, int EstimatedComplexity,
        bool Parallelizable);

    /// <summary>
    /// Decompose a game prompt into sub-tasks. Uses genre-specific
    /// decomposition rules with deterministic fallbacks per genre.
    /// Returns an ordered list: Phase 1 tasks first (must complete before
    /// Phase 2), then Phase 2 (parallelizable), then polish.
    /// </summary>
    public static IReadOnlyList<SubTask> Decompose(string prompt, string genre)
    {
        return genre switch
        {
            "party" => DecomposeParty(prompt),
            "platformer" => DecomposePlatformer(prompt),
            "rpg" or "roguelike" => DecomposeRpg(prompt),
            "shooter" => DecomposeShooter(prompt),
            "racing" => DecomposeRacing(prompt),
            "management" or "simulator" => DecomposeManagement(prompt),
            "artillery" => DecomposeArtillery(prompt),
            "puzzle" => DecomposePuzzle(prompt),
            _ => DecomposeGeneric(prompt, genre),
        };
    }

    // ──── PARTY (Mario Party-klassen) ────────────────────────────────────

    private static List<SubTask> DecomposeParty(string prompt)
    {
        var tasks = new List<SubTask>();

        // Phase 1: Core board (blocking)
        tasks.Add(new("party_board", "Build the board with 20+ tiles, dice roll, turn system, and 4 players",
            "core", "party", null, 3, false));
        tasks.Add(new("party_economy", "Implement coin collection, star buying, and tile effects (+coins, -coins, minigame trigger)",
            "core", null, "shop", 2, false));

        // Phase 2: Minigames (parallelizable — each can be a TeamBuild track).
        // v2.2: RIKTIG antalstolkning ("15 minigames" => 15 uppgifter) i
        // stället för Contains("5")-gissningen som gav 5 av 15. Kitgolvet
        // levererar 3 - dekomposern beskriver dem som ska LÄGGAS TILL.
        var mgCount = Math.Max(3, GenreContracts.RequestedMinigames(prompt) ?? 3);
        var mgTypes = new[] {
            ("mg_tap", "Tap Race: mash button to fill bar fastest", "reaction"),
            ("mg_dodge", "Dodge: avoid falling blocks with arrow keys", "skill"),
            ("mg_memory", "Memory: repeat the growing arrow sequence (Simon Says)", "memory"),
            ("mg_collect", "Collectathon: run around collecting coins before time runs out", "collection"),
            ("mg_quiz", "Quiz: answer questions correctly before opponents", "knowledge"),
            ("mg_aim", "Target Shots: aim and throw at moving targets, most hits wins", "skill"),
            ("mg_rhythm", "Beat Match: press the shown key on the beat, longest streak wins", "rhythm"),
            ("mg_sumo", "Sumo Push: shove opponents off a shrinking platform, last one standing", "battle"),
            ("mg_hotpotato", "Hot Potato: pass the bomb before it explodes, survivors score", "battle"),
            ("mg_maze", "Maze Dash: first to navigate the maze to the goal", "race"),
        };

        for (int i = 0; i < mgCount; i++)
        {
            if (i < mgTypes.Length)
            {
                var mg = mgTypes[i];
                tasks.Add(new(mg.Item1, mg.Item2, mg.Item3, null, "countdown_timer", 2, true));
            }
            else
            {
                tasks.Add(new($"mg_extra{i + 1}",
                    $"Invent and build ONE additional unique minigame (#{i + 1}) with a mechanic no other minigame uses - complete with countdown, 4-player scoring and its own sound",
                    "custom", null, "countdown_timer", 2, true));
            }
        }

        // Phase 3: Polish
        tasks.Add(new("party_polish", "Add particle effects, screenshake, sound effects, and score popups",
            "polish", null, "score_popup", 1, false));
        tasks.Add(new("party_results", "Build the results screen with rankings (stars then coins)",
            "ui", null, null, 1, false));

        return tasks;
    }

    // ──── PLATFORMER ─────────────────────────────────────────────────────

    private static List<SubTask> DecomposePlatformer(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("plat_core", "Build the platformer base: player with gravity, jump, movement, ground collision",
            "core", "platformer", null, 3, false));
        tasks.Add(new("plat_enemies", "Add 2-3 enemy types (patrol, chase, flying) with death-on-stomp",
            "ai", null, "enemy_patrol", 2, true));
        tasks.Add(new("plat_levels", "Build 3+ levels with increasing difficulty and unique visual themes",
            "level", null, null, 3, true));
        tasks.Add(new("plat_collectibles", "Add coins/collectibles with score tracking and pickup effects",
            "pickup", null, "score_popup", 1, true));
        tasks.Add(new("plat_checkpoints", "Add checkpoints with respawn and visual activation",
            "gameplay", null, "checkpoint", 1, true));
        tasks.Add(new("plat_polish", "Add screenshake, double jump, particle effects, damage flash",
            "polish", null, "double_jump", 2, false));
        return tasks;
    }

    // ──── RPG ────────────────────────────────────────────────────────────

    private static List<SubTask> DecomposeRpg(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("rpg_core", "Build the RPG base: 8-way movement, camera follow, HP system",
            "core", "rpg", "camera_follow", 3, false));
        tasks.Add(new("rpg_combat", "Add combat: weapon swings, damage numbers, enemy HP bars",
            "combat", null, "health_bar", 2, true));
        tasks.Add(new("rpg_enemies", "Add 3+ enemy types with patrol AI and different behaviors",
            "ai", null, "enemy_patrol", 2, true));
        tasks.Add(new("rpg_items", "Add item pickups, inventory system, and equipment",
            "items", null, null, 2, true));
        tasks.Add(new("rpg_waves", "Add wave/arena system with escalating difficulty",
            "system", null, null, 2, true));
        tasks.Add(new("rpg_shop", "Add shop between waves to spend collected coins",
            "economy", null, "shop", 1, true));
        return tasks;
    }

    // ──── SHOOTER ────────────────────────────────────────────────────────

    private static List<SubTask> DecomposeShooter(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("shooter_core", "Build shooter base: player movement, aiming, projectile spawning",
            "core", "shooter", null, 3, false));
        tasks.Add(new("shooter_waves", "Add enemy wave system with increasing spawn count and speed",
            "system", null, null, 2, true));
        tasks.Add(new("shooter_powerups", "Add power-up drops: rapid fire, spread shot, shield",
            "powerup", null, "powerup_timer", 2, true));
        tasks.Add(new("shooter_juice", "Add screenshake on hit, muzzle flash, damage flash on enemies",
            "juice", null, "damage_flash", 2, true));
        return tasks;
    }

    // ──── RACING ─────────────────────────────────────────────────────────

    private static List<SubTask> DecomposeRacing(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("race_core", "Build racing base: vehicle physics, track, lap counting, timer",
            "core", "racing", null, 3, false));
        tasks.Add(new("race_ai", "Add 3 AI racers with difficulty-based speed and pathfinding",
            "ai", null, null, 2, true));
        tasks.Add(new("race_tracks", "Add 2-3 different track layouts with increasing complexity",
            "level", null, null, 2, true));
        tasks.Add(new("race_polish", "Add checkpoint effects, skid marks, camera easing",
            "polish", null, "camera_follow", 1, false));
        return tasks;
    }

    // ──── MANAGEMENT ─────────────────────────────────────────────────────

    private static List<SubTask> DecomposeManagement(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("mgmt_core", "Build management base: budget, roster, round simulation, market",
            "core", "management", null, 4, false));
        tasks.Add(new("mgmt_save", "Add save/load system for career progress",
            "system", null, null, 1, true));
        tasks.Add(new("mgmt_upgrades", "Add upgrade system: better staff, facilities, marketing",
            "economy", null, "shop", 2, true));
        tasks.Add(new("mgmt_table", "Add league table/standings with promotion/relegation",
            "ui", null, null, 2, true));
        return tasks;
    }

    // ──── ARTILLERY ──────────────────────────────────────────────────────

    private static List<SubTask> DecomposeArtillery(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("arty_core", "Build artillery base: destructible terrain, aiming, projectile physics",
            "core", "artillery", null, 4, false));
        tasks.Add(new("arty_ai", "Add AI opponents with difficulty-based accuracy",
            "ai", null, null, 2, true));
        tasks.Add(new("arty_weapons", "Add 3 weapon types with different trajectories and damage",
            "weapons", null, null, 2, true));
        tasks.Add(new("arty_wind", "Add wind system that affects projectile trajectory",
            "system", null, null, 1, true));
        return tasks;
    }

    // ──── PUZZLE ─────────────────────────────────────────────────────────

    private static List<SubTask> DecomposePuzzle(string prompt)
    {
        var tasks = new List<SubTask>();
        tasks.Add(new("puz_core", "Build puzzle base: grid, slide/swap mechanic, scoring",
            "core", "puzzle", null, 2, false));
        tasks.Add(new("puz_levels", "Add 5+ puzzle boards with increasing target scores",
            "level", null, null, 2, true));
        tasks.Add(new("puz_undo", "Add undo and hint system",
            "system", null, null, 1, true));
        return tasks;
    }

    // ──── GENERIC (unknown genre) ────────────────────────────────────────

    private static List<SubTask> DecomposeGeneric(string prompt, string genre)
    {
        // Fallback: minimal decomposition based on keywords in the prompt
        var tasks = new List<SubTask>();
        tasks.Add(new("gen_core", $"Build core game loop for genre '{genre}': movement, interaction, win/lose",
            "core", genre, null, 3, false));

        if (prompt.Contains("enemy") || prompt.Contains("fiende") || prompt.Contains("combat"))
            tasks.Add(new("gen_combat", "Add enemies and combat system",
                "combat", null, "enemy_patrol", 2, true));

        if (prompt.Contains("coin") || prompt.Contains("score") || prompt.Contains("mynt") || prompt.Contains("poang"))
            tasks.Add(new("gen_score", "Add scoring/collectible system with UI",
                "pickup", null, "score_popup", 1, true));

        if (prompt.Contains("shop") || prompt.Contains("buy") || prompt.Contains("butik") || prompt.Contains("kopa"))
            tasks.Add(new("gen_shop", "Add shop/economy system",
                "economy", null, "shop", 1, true));

        tasks.Add(new("gen_polish", "Add polish: sound effects, particles, transitions",
            "polish", null, null, 1, false));

        return tasks;
    }

    /// <summary>
    /// Check if a prompt is "complex" — has enough sub-systems that it
    /// would benefit from TeamBuild decomposition rather than a single
    /// agent building everything.
    /// </summary>
    public static bool IsComplex(string prompt)
    {
        var signals = new[] {
            "multi", "flera", "multiple", "party", "minigame", "minispel",
            "manager", "tycoon", "rpg", "roguelike", "dungeon", "waves",
            "season", "career", "league", "liga", "tournament",
        };
        var count = signals.Count(s => prompt.Contains(s, StringComparison.OrdinalIgnoreCase));
        return count >= 2;
    }

    /// <summary>
    /// Convert decomposed sub-tasks into a TeamBuild-compatible track list.
    /// Phase 1 tasks become the main track; Phase 2 tasks become parallel
    /// worktree tracks. Returns (mainTrackDescription, parallelTracks).
    /// </summary>
    public static (string MainTrack, List<(string Id, string Description, string? Mechanic)> ParallelTracks)
        ToTeamBuildTracks(IReadOnlyList<SubTask> tasks)
    {
        var phase1 = tasks.Where(t => !t.Parallelizable).ToList();
        var phase2 = tasks.Where(t => t.Parallelizable).ToList();

        var mainTrack = string.Join("; ", phase1.Select(t => t.Description));
        var parallel = phase2.Select(t => (t.Id, t.Description, t.MechanicHint)).ToList();

        return (mainTrack, parallel);
    }
}
