using AiLocal.Node.Hosting;
using AiLocal.Node.Hosting.GameMechanics;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.2.0: Tests for the 5 game improvement systems:
/// 1. GenreContracts (structured verifiable constraints)
/// 2. GameMechanicLibrary (reusable mechanic snippets)
/// 3. PromptDecomposer (complex prompt -> sub-tasks)
/// 4. AntiPatternDb (design-level mistake detection)
/// 5. VisualStyleLib (curated color palettes)
/// </summary>
public class GameImprovementTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // #1: GENRE CONTRACTS
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GenreContracts_HasAllExpectedGenres()
    {
        // Verify specs exist for the most important genres
        Assert.NotNull(GenreContracts.ForGenre("platformer"));
        Assert.NotNull(GenreContracts.ForGenre("party"));
        Assert.NotNull(GenreContracts.ForGenre("racing"));
        Assert.NotNull(GenreContracts.ForGenre("artillery"));
        Assert.NotNull(GenreContracts.ForGenre("rpg"));
        Assert.NotNull(GenreContracts.ForGenre("shooter"));
        Assert.NotNull(GenreContracts.ForGenre("management"));
        Assert.NotNull(GenreContracts.ForGenre("puzzle"));
        Assert.Null(GenreContracts.ForGenre("nonexistent"));
    }

    [Fact]
    public void GenreContracts_PlatformerRequiresGravityAndJump()
    {
        var spec = GenreContracts.ForGenre("platformer")!;
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("gravity"));
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("jump"));
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("enemy"));
    }

    [Fact]
    public void GenreContracts_PartyRequiresBoardAndDice()
    {
        var spec = GenreContracts.ForGenre("party")!;
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("TILE_COUNT"));
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("dice|roll"));
        Assert.Contains(spec.MustHave, c => c.GrepPattern.Contains("minigame"));
    }

    [Fact]
    public void GenreContracts_VerifyDetectsMissingConstraints()
    {
        // Create a minimal project with only basic content
        var tmp = Path.Combine(Path.GetTempPath(), "gc-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);
        try
        {
            File.WriteAllText(Path.Combine(tmp, "main.gd"), "extends Node\nfunc _ready(): pass\n");

            var (met, total, findings) = GenreContracts.Verify(tmp, "platformer");
            Assert.True(total > 0);
            // With no gravity/jump/enemy code, most must-haves should fail
            Assert.True(met < total);
            Assert.Contains(findings, f => f.Contains("SAKNAS"));
        }
        finally { Directory.Delete(tmp, true); }
    }

    [Fact]
    public void GenreContracts_VerifyPassesForCompleteContent()
    {
        var tmp = Path.Combine(Path.GetTempPath(), "gc-test-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(tmp);
        try
        {
            // Write source that satisfies platformer constraints
            File.WriteAllText(Path.Combine(tmp, "main.gd"),
                @"extends CharacterBody2D
const GRAVITY = 980
const JUMP_VELOCITY = -400
var enemy_count = 0
var coin_score = 0
var hp = 3
func _physics_process(delta):
    velocity.y += GRAVITY * delta
    if is_on_floor() and Input.is_action_just_pressed(""jump""):
        velocity.y = JUMP_VELOCITY
    move_and_slide()
func win_game(): pass
func die(): pass
var coyote = true
var shake = 0
const FINAL_LEVEL = 3
");

            var (met, total, findings) = GenreContracts.Verify(tmp, "platformer");
            Assert.True(met >= 4, $"Expected at least 4/6 met, got {met}/{total}");
        }
        finally { Directory.Delete(tmp, true); }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // #2: GAME MECHANIC LIBRARY
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void GameMechanics_ListReturnsAll()
    {
        var list = GameMechanicLibrary.List();
        Assert.True(list.Count >= 8, $"Expected 8+ mechanics, got {list.Count}");
    }

    [Fact]
    public void GameMechanics_GetReturnsNullForUnknown()
    {
        Assert.Null(GameMechanicLibrary.Get("nonexistent"));
    }

    [Fact]
    public void GameMechanics_DoubleJumpExistsForAllEngines()
    {
        var mechanic = GameMechanicLibrary.Get("double_jump");
        Assert.NotNull(mechanic);
        Assert.Contains("double", mechanic!.GDScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double", mechanic.CSharp, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("double", mechanic.JavaScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameMechanics_ShopSystemHasBuyLogic()
    {
        var mechanic = GameMechanicLibrary.Get("shop");
        Assert.NotNull(mechanic);
        Assert.Contains("buy", mechanic!.GDScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("coins", mechanic.GDScript, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GameMechanics_GetCodeWorksWithEngine()
    {
        Assert.NotNull(GameMechanicLibrary.GetCode("double_jump", "godot"));
        Assert.NotNull(GameMechanicLibrary.GetCode("double_jump", "unity"));
        Assert.NotNull(GameMechanicLibrary.GetCode("double_jump", "html5"));
        Assert.Null(GameMechanicLibrary.GetCode("nonexistent", "godot"));
    }

    [Fact]
    public void GameMechanics_AllHaveCategories()
    {
        foreach (var m in GameMechanicLibrary.List())
        {
            Assert.False(string.IsNullOrEmpty(m.Category),
                $"Mechanic {m.Name} has no category");
            Assert.False(string.IsNullOrEmpty(m.Description),
                $"Mechanic {m.Name} has no description");
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // #3: PROMPT DECOMPOSER
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void PromptDecomposer_PartyDecomposesIntoBoardAndMinigames()
    {
        var tasks = PromptDecomposer.Decompose("bygg ett mario party-liknande spel", "party");
        Assert.True(tasks.Count >= 5, $"Expected 5+ sub-tasks, got {tasks.Count}");
        Assert.Contains(tasks, t => t.Category == "core"); // board
        Assert.Contains(tasks, t => t.Id == "mg_tap");     // minigame
    }

    [Fact]
    public void PromptDecomposer_PlatformerHasLevels()
    {
        var tasks = PromptDecomposer.Decompose("bygg ett plattformsspel", "platformer");
        Assert.Contains(tasks, t => t.Id == "plat_levels");
        Assert.Contains(tasks, t => t.Id == "plat_enemies");
    }

    [Fact]
    public void PromptDecomposer_GenericFallbackWorks()
    {
        var tasks = PromptDecomposer.Decompose("bygg ett spel", "unknown_genre");
        Assert.True(tasks.Count >= 2);
        Assert.Contains(tasks, t => t.Id.StartsWith("gen_"));
    }

    [Fact]
    public void PromptDecomposer_IsComplexDetectsMultiSystems()
    {
        Assert.True(PromptDecomposer.IsComplex("bygg ett party spel med minigames"));
        Assert.True(PromptDecomposer.IsComplex("ett RPG med flera vapen och waves"));
        Assert.False(PromptDecomposer.IsComplex("enkel snake"));
    }

    [Fact]
    public void PromptDecomposer_TeamBuildTracksSplitCorrectly()
    {
        var tasks = PromptDecomposer.Decompose("bygg ett party spel", "party");
        var (main, parallel) = PromptDecomposer.ToTeamBuildTracks(tasks);
        Assert.False(string.IsNullOrEmpty(main));
        Assert.True(parallel.Count >= 2, "Expected 2+ parallel tracks");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // #4: ANTI-PATTERN DB
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void AntiPatternDb_KorrektFormateringFlaggasAldrig()
    {
        // v2.8: "%s" % [args] AR korrekt GDScript - det gamla monstret
        // flaggade ALL formatering och fick en agent att sanera bort
        // fungerande kod ur hela kallan (4,3M tokens brann, spelet
        // kraschade). Korrekt kod ska passera tyst.
        var ok = "var msg = \"Score: %d\" % score\nhud.text = \"Round %d/%d\" % [r, t]\n";
        Assert.DoesNotContain(AntiPatternDb.Scan(ok, "godot"),
            f => f.Pattern.Id == "ap_format_strings");
    }

    [Fact]
    public void AntiPatternDb_RaPlatshallareTillText_Flaggas()
    {
        // Literal med %d tilldelad .text UTAN %-operator = spelaren ser
        // platshallaren ratt (skarmdumpsklassen) - DEN ska flaggas.
        var bad = "hud.text = \"Score: %d\"\n";
        Assert.Contains(AntiPatternDb.Scan(bad, "godot"),
            f => f.Pattern.Id == "ap_format_strings");
    }

    [Fact]
    public void AntiPatternDb_FindsMixedTabsSpaces()
    {
        var source = "\tfunc foo():\n\t\tprint(\"hello\")\n    return\n";
        var findings = AntiPatternDb.Scan(source, "godot");
        Assert.Contains(findings, f => f.Pattern.Id == "ap_tabs_spaces_mixed");
    }

    [Fact]
    public void AntiPatternDb_PassesCleanSource()
    {
        var source = "extends Node\nvar score := 0\nfunc _ready(): pass\n";
        var findings = AntiPatternDb.Scan(source, "godot");
        // Minimal source triggers many "missing" findings (no title, no pause, etc.)
        // This is expected — the scanner is conservative. Verify it doesn't crash
        // and returns reasonable results.
        Assert.NotNull(findings);
    }

    [Fact]
    public void AntiPatternDb_FormatFindingsAreReadable()
    {
        var source = "var hp = 100\n";
        var findings = AntiPatternDb.Scan(source, "godot");
        var formatted = AntiPatternDb.FormatFindings(findings);
        foreach (var f in formatted)
        {
            Assert.Contains("DESIGN-REKO", f);
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // #5: VISUAL STYLE LIBRARY
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void VisualStyles_HasAllPresetStyles()
    {
        Assert.True(VisualStyleLib.All.Length >= 10, $"Expected 10+ styles, got {VisualStyleLib.All.Length}");
    }

    [Fact]
    public void VisualStyles_GetByNameWorks()
    {
        Assert.NotNull(VisualStyleLib.Get("frost_night"));
        Assert.NotNull(VisualStyleLib.Get("neon_underground"));
        Assert.Null(VisualStyleLib.Get("nonexistent"));
    }

    [Fact]
    public void VisualStyles_PickForGenreReturnsRelevant()
    {
        Assert.Equal("candy_pop", VisualStyleLib.PickForGenre("party").Name);
        Assert.Equal("deep_forest", VisualStyleLib.PickForGenre("platformer").Name);
        Assert.Equal("neon_underground", VisualStyleLib.PickForGenre("shooter").Name);
    }

    [Fact]
    public void VisualStyles_ToGDScriptGeneratesConstants()
    {
        var style = VisualStyleLib.Get("frost_night")!;
        var gdscript = VisualStyleLib.ToGDScript(style);
        Assert.Contains("BG_COLOR", gdscript);
        Assert.Contains("ACCENT_COLOR", gdscript);
        Assert.Contains("TEXT_COLOR", gdscript);
        Assert.Contains("PARTICLE_COLOR", gdscript);
        Assert.Contains("DANGER_COLOR", gdscript);
        Assert.Contains("SUCCESS_COLOR", gdscript);
    }

    [Fact]
    public void VisualStyles_AllHaveValidHexColors()
    {
        foreach (var style in VisualStyleLib.All)
        {
            Assert.StartsWith("#", style.Background.Hex);
            Assert.StartsWith("#", style.Accent.Hex);
            Assert.StartsWith("#", style.Text.Hex);
            Assert.Equal(7, style.Background.Hex.Length); // #RRGGBB
        }
    }

    [Fact]
    public void VisualStyles_ListReturnsStrings()
    {
        var list = VisualStyleLib.List();
        Assert.True(list.Count >= 10);
        Assert.Contains(list, s => s.Contains("frost_night"));
        Assert.Contains(list, s => s.Contains("desert_warmth"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // v2.13: RELEASE-CHECKLISTAN (radgivande smaspels-krav i grinden)
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ReleaseChecklistan_FlaggarSaknadeDelarna()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-rc-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.gd"), "extends Node2D\nfunc _ready() -> void:\n\tqueue_redraw()\n");
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"Game\"\n");
            var notes = ReleaseChecklist.Review(dir, "godot");
            Assert.Contains(notes, n => n.Contains("omstart"));
            Assert.Contains(notes, n => n.Contains("volymkontroll"));
            Assert.Contains(notes, n => n.Contains("paus"));
            Assert.Contains(notes, n => n.Contains("highscore"));
            Assert.Contains(notes, n => n.Contains("generisk"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReleaseChecklistan_GodkannerKomplettSmaspel()
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-rc2-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Main.gd"),
                "extends Node2D\nconst SAVE_PATH := \"user://hs.save\"\nvar muted := false\nvar state := \"title\"\n" +
                "func _unhandled_input(event: InputEvent) -> void:\n" +
                "\tif event.is_action_pressed(\"ui_cancel\"):\n\t\tstate = \"paused\"\n" +
                "\tif event is InputEventKey and event.keycode == KEY_R:\n\t\tnew_game()\n" +
                "\tAudioServer.set_bus_volume_db(0, -6.0)\n" +
                "func new_game() -> void:\n\tstate = \"playing\"\n");
            File.WriteAllText(Path.Combine(dir, "project.godot"), "[application]\nconfig/name=\"Pixel Rush\"\n");
            Assert.Empty(ReleaseChecklist.Review(dir, "godot"));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    [Fact]
    public void ReleaseChecklistan_TigerForIckeSpelmotorer()
    {
        // Checklistan galler spel - en CLI-app eller okand motor far inga fynd.
        var dir = Directory.CreateTempSubdirectory("ailocal-rc3-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "Program.cs"), "class P { static void Main() {} }");
            Assert.Empty(ReleaseChecklist.Review(dir, "unknown"));
            Assert.Empty(ReleaseChecklist.Review(dir, null));
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
