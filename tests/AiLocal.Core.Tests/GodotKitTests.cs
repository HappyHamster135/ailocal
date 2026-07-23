using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.45.0: Godot-genrekiten. Låser att management-/top-down-prompts får
/// sina riktiga GDScript-kit (inte plattformaren), att kiten är kompletta
/// (projekt, scen, skript, ljud, design) och att varje res://-referens i
/// scenen pekar på en fil som faktiskt finns. GDScript-syntaxen verifieras
/// separat med riktig `godot --headless` (miljöberoende - dokumenterat i
/// releaseprocessen), inte här.
/// </summary>
public class GodotKitTests
{
    private static (string Root, string[] Files) ScaffoldTo(string prompt)
    {
        var parent = Path.Combine(Path.GetTempPath(), "ailocal-godotkit-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(parent);
        var result = new GameScaffoldService().Scaffold("godot", prompt, parent);
        Assert.True(result.Success, result.Output);
        _parents[result.Path] = parent;
        return (result.Path, result.Files);
    }

    // root -> den GUID-namngivna parent som ÄGS av testet och är säker att
    // radera. Cleanup fick ALDRIG härleda parenten via GetDirectoryName:
    // när scaffolden lägger projektet direkt i parent (tom rot) pekade
    // GetDirectoryName(root) på %TEMP% SJÄLV - rekursiv radering av hela
    // temp-katalogen mitt under parallella testklasser (sänkte slumpvisa
    // tester i varje full svitkörning tills detta hittades).
    private static readonly Dictionary<string, string> _parents = [];

    private static void AssertKitComplete(string root)
    {
        // v2.4: music.wav = bakgrundsmusiken (ChiptuneComposer) som ALLA kit
        // numera loopar. v2.9: Art.gd = agenternas ritbibliotek (kontur/
        // skugga/djup) - skickas med i varje Godot-scaffold.
        foreach (var required in new[] { "project.godot", "Main.tscn", "Main.gd", "export_presets.cfg", "DESIGN.md", "README.md", "coin.wav", "win.wav", "music.wav", "Art.gd", "Shell.gd" })
            Assert.True(File.Exists(Path.Combine(root, required)), $"{required} saknas i kitet");
        Assert.Contains("music", File.ReadAllText(Path.Combine(root, "Main.gd")));

        Assert.Equal(ProjectVerifier.ProjectKind.Godot, new ProjectVerifier().Detect(root));

        // project.godot pekar på Main.tscn, och varje res:// i scenen finns.
        var projectFile = File.ReadAllText(Path.Combine(root, "project.godot"));
        Assert.Contains("run/main_scene=\"res://Main.tscn\"", projectFile);
        // v1.93: stretch - 1:1 pa dator, skalar ratt pa telefonskarmar.
        Assert.Contains("window/stretch/mode=\"canvas_items\"", projectFile);
        var scene = File.ReadAllText(Path.Combine(root, "Main.tscn"));
        foreach (System.Text.RegularExpressions.Match m in
                 System.Text.RegularExpressions.Regex.Matches(scene, "res://([A-Za-z0-9_./-]+)"))
            Assert.True(File.Exists(Path.Combine(root, m.Groups[1].Value)), $"{m.Value} refereras men saknas");

        // Ljudfilerna som Main.gd laddar dynamiskt finns också.
        var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
        foreach (var wav in new[] { "click.wav", "coin.wav", "hurt.wav", "win.wav" })
        {
            Assert.Contains(wav[..wav.IndexOf('.')], script);
            Assert.True(File.Exists(Path.Combine(root, wav)), $"{wav} saknas");
        }
    }

    [Fact]
    public void FotbollsmanagerIGodot_FarManagementKitet()
    {
        var (root, _) = ScaffoldTo("bygg ett 2d fotbolls management simulator spel i godot med tre svårighetsgrader");
        try
        {
            Assert.Contains("Management / Tycoon", File.ReadAllText(Path.Combine(root, "DESIGN.md")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            // Produktionsribban: svårighetsgrader, spara/ladda, marknad, tabell.
            Assert.Contains("SEASON_LENGTH", script);
            Assert.Contains("save_game", script);
            Assert.Contains("load_game", script);
            Assert.Contains("show_market", script);
            Assert.Contains("show_table", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void AventyrIGodot_FarTopDownKitet()
    {
        var (root, _) = ScaffoldTo("bygg ett top-down äventyrsspel i godot där man överlever vågor");
        try
        {
            Assert.Contains("Top-down", File.ReadAllText(Path.Combine(root, "DESIGN.md")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("FINAL_WAVE", script);
            Assert.Contains("move_and_slide", script);
            Assert.Contains("load_highscore", script);
            // C1 (game-feel/juice): screenshake + partiklar inbakat i golvet.
            Assert.Contains("CPUParticles2D", script);
            Assert.Contains("spawn_burst", script);
            Assert.Contains("shake", script);
            // v1.93: touchkontroller - runtime-gatade (datorspel oforandrade).
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void PlattformareIGodot_FarPixelRushKitetIGdscript()
    {
        // v1.85: plattformaren ar GDScript som de andra kiten (C#/mono-kittet
        // kunde aldrig headless-verifieras och fick aldrig juice-passet).
        var (root, _) = ScaffoldTo("bygg ett plattformsspel i godot");
        try
        {
            Assert.Contains("Pixel Rush", File.ReadAllText(Path.Combine(root, "README.md")));
            Assert.Contains("Plattformare", File.ReadAllText(Path.Combine(root, "DESIGN.md")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            // Produktionsribban: riktig plattformsfysik, stamp, 3 nivaer, highscore.
            Assert.Contains("JUMP_VELOCITY", script);
            Assert.Contains("coyote", script);
            Assert.Contains("FINAL_LEVEL", script);
            Assert.Contains("move_and_slide", script);
            Assert.Contains("load_highscore", script);
            // C1 (game-feel/juice): screenshake + partiklar inbakat i golvet.
            Assert.Contains("CPUParticles2D", script);
            Assert.Contains("spawn_burst", script);
            Assert.Contains("shake", script);
            // v1.93: touchkontroller - runtime-gatade (datorspel oforandrade).
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            // v2.13: autopiloten - kvalitetsgrindens demospelare, gatad bakom
            // AILOCAL_AUTOPILOT sa vanliga spelare aldrig markar den.
            Assert.Contains("AILOCAL_AUTOPILOT", script);
            Assert.Contains("PhysicsRayQueryParameters2D", script);
            // Inga C#-filer kvar - mono-beroendet ar borta.
            Assert.Empty(Directory.GetFiles(root, "*.cs"));
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void RacingIGodot_FarVarvetKitet()
    {
        const string prompt = "bygg ett racingspel i godot med bilar och tre varv";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("racing", GameScaffoldService.DetectGenre(prompt));
            Assert.Contains("The Circuit", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("_physics_process", script);
            Assert.Contains("checkpoint", script);
            Assert.Contains("CPUParticles2D", script);  // C1 juice
            Assert.Contains("shake", script);
            // v1.93: touchkontroller (GAS/BROMS + styrning) - runtime-gatade.
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void TreDimensionelltIGodot_FarKubenKitet()
    {
        const string prompt = "bygg ett 3d samlarspel i godot";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("godot", GameScaffoldService.PickEngine(prompt));
            Assert.Contains("The Cube", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("CharacterBody3D", script);
            Assert.Contains("Camera3D", script);
            Assert.Contains("CPUParticles3D", script);  // C1 juice
            Assert.Contains("shake", script);
            // v1.93: touchkontroller - runtime-gatade (datorspel oforandrade).
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void PusselIGodot_FarTvatusenKitet()
    {
        const string prompt = "bygg ett pusselspel i godot dar man slajdar ihop lika";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("puzzle", GameScaffoldService.DetectGenre(prompt));
            Assert.Contains("Twenty48", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("_slide", script);
            Assert.Contains("TARGET", script);
            // v1.93: touchkontroller (dpad for slajd) - runtime-gatade.
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void ArtilleriIGodot_FarKanonadenKitet()
    {
        // v1.98: artillerigenren (ShellShock Live/Worms-klassen) - forsta
        // kittet med versus-form: turbaserad duell mot AI i stallet for
        // ensam progression. Utan detta golv foll artilleriprompts till
        // top-down-kitet och "ett spel som shellshock live" var omojligt.
        const string prompt = "bygg ett artillerispel som shellshock live i godot";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("artillery", GameScaffoldService.DetectGenre(prompt));
            Assert.Contains("Cannonade", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            // Produktionsribban: forstorbar pixelterrang, kratrar, vind,
            // AI som provskjuter, motstandarstege, vapenarsenal.
            Assert.Contains("fill_rect", script);
            Assert.Contains("crater", script);
            Assert.Contains("wind", script);
            Assert.Contains("simulate_shot", script);
            Assert.Contains("OPPONENTS", script);
            Assert.Contains("weapons", script);
            Assert.Contains("CPUParticles2D", script);  // C1 juice
            Assert.Contains("shake", script);
            // Touchkontroller - runtime-gatade (datorspel oforandrade).
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }
    [Fact]
    public void Partyspel_FarBoardBashKitet()
    {
        // v2.1.0: party/bradspel (Board Bash) - forsta sammansatta kitet
        foreach (var prompt in new[]
        {
            "bygg ett mario party liknande spel i godot med minigames och brador",
            "bygg ett party bradspel med minispel i godot",
            "bygg ett board game i godot med tarning och minigames",
        })
        {
            Assert.Equal("party", GameScaffoldService.DetectGenre(prompt));
        }
        Assert.NotEqual("party", GameScaffoldService.DetectGenre("ett plattformsspel med party-tema"));
    }

    [Fact]
    public void PartyspelIGodot_FarBoardBashKitet()
    {
        const string prompt = "bygg ett mario party liknande spel i godot med minigames";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("party", GameScaffoldService.DetectGenre(prompt));
            Assert.Contains("Board Bash", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("TILE_COUNT", script);
            Assert.Contains("turn_phase", script);
            Assert.Contains("minigame", script);
            Assert.Contains("PLAYERS", script);
            Assert.Contains("_do_roll", script);
            Assert.Contains("_start_minigame", script);
            Assert.Contains("BOARD_RING", script);
            Assert.Contains("BOARD_SERPENTINE", script);
            Assert.Contains("ROUNDS", script);
            Assert.Contains("CPUParticles2D", script);
            Assert.Contains("shake", script);
            Assert.Contains("TouchScreenButton", script);
            Assert.Contains("is_touchscreen_available", script);
            // v2.15 spelskalet: riktig huvudmeny + karaktarsval + minigame-
            // menyn + options + quit, byggt pa Shell.gd-hjalparna.
            Assert.Contains("Shell.menu", script);
            Assert.Contains("Shell.options_panel", script);
            Assert.Contains("Shell.character_select", script);
            Assert.Contains("CHARACTERS", script);
            Assert.Contains("_start_practice", script);
            Assert.Contains("get_tree().quit()", script);
            // v2.13-monstret: sondens demospelare aven i partyt.
            Assert.Contains("AILOCAL_AUTOPILOT", script);
            // v2.16: riktiga animerade gubbar + levande bakgrund.
            Assert.Contains("player_frames.tres", script);
            Assert.Contains("_ensure_tokens", script);
            Assert.Contains("_update_tokens", script);
            Assert.Contains("confetti", script);
            // v2.17: pixelart-brickor + skarpa pixlar.
            Assert.Contains("_make_tile_tex", script);
            Assert.Contains("draw_texture_rect", script);
            Assert.Contains("TEXTURE_FILTER_NEAREST", script);
            Assert.Contains("default_texture_filter=0", File.ReadAllText(Path.Combine(root, "project.godot")));
            // v2.19: partydjupet - 5 minispel + 3 tematiska braden.
            Assert.Contains("BOARD_SPIRAL", script);
            Assert.Contains("COIN GRAB", script);
            Assert.Contains("QUICK DRAW", script);
            Assert.Contains("mg_qd_wins", script);
            Assert.Contains("mg_coin_score", script);
            Assert.Contains("board_bg_top", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }



    [Fact]
    public void FpsIGodot_FarStrikeArenaKitet()
    {
        // v2.5: fps-prompts foll till top-down-2D ("first person shooter"
        // traffade shooter-regeln) eller samlarspelet ("3d fps") - nu finns
        // first person-golvet. "60 fps" ar ett PRESTANDAKRAV, inte en genre.
        const string prompt = "bygg ett litet fps spel i godot";
        Assert.Equal("fps", GameScaffoldService.DetectGenre(prompt));
        Assert.Equal("fps", GameScaffoldService.DetectGenre("ett first person shooter i godot"));
        Assert.Equal("fps", GameScaffoldService.DetectGenre("3d fps arena"));
        Assert.NotEqual("fps", GameScaffoldService.DetectGenre("bygg ett plattformsspel som haller 60 fps"));
        Assert.NotEqual("fps", GameScaffoldService.DetectGenre("racing i 120fps"));

        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Contains("Strike Arena", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("Camera3D", script);
            Assert.Contains("MOUSE_MODE_CAPTURED", script);   // first person pa riktigt
            Assert.Contains("angle_to", script);              // matematisk siktkontroll
            Assert.Contains("FINAL_WAVE", script);
            Assert.Contains("CPUParticles3D", script);        // 3D-juice
            Assert.Contains("crosshair", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void Party3dIGodot_FarFlerfilskittet()
    {
        // v2.3: "3d mario party" ska ge party-3D-golvet (flerfilskittet),
        // INTE 3D-samlarspelet The Cube - och konventionen (en Mg*.gd per
        // minispel) ar det GenreContracts.CountMinigames raknar och det
        // teamsparen bygger vidare pa.
        const string prompt = "bygg ett 3d mario party spel i godot med minigames";
        var (root, _) = ScaffoldTo(prompt);
        try
        {
            Assert.Equal("party", GameScaffoldService.DetectGenre(prompt));
            Assert.Contains("Board Bash 3D", File.ReadAllText(Path.Combine(root, "project.godot")));
            foreach (var f in new[] { "MgRace3D.gd", "MgFall3D.gd", "MgCollect3D.gd", "MECHANICS.md" })
                Assert.True(File.Exists(Path.Combine(root, f)), $"{f} saknas");
            // Olika ljud: egna wav-filer per minispel + tarning/stjarna.
            foreach (var w in new[] { "dice.wav", "star.wav", "mg_race.wav", "mg_fall.wav", "mg_collect.wav" })
                Assert.True(File.Exists(Path.Combine(root, w)), $"{w} saknas");
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("MINIGAMES", script);
            Assert.Contains("minigame_done", script);
            Assert.Contains("CPUParticles3D", script);   // 3D-juice
            Assert.Contains("CapsuleMesh", script);      // 3D-modeller i kod
            // Raknaren ser flerfilskonventionen: 3 Mg*.gd = 3 minispel.
            Assert.True(GenreContracts.CountMinigames(root, script) >= 3);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void BegartAntalMinispel_BlirMatbartKrav()
    {
        // Malprompten: "15 minigames" ska BLI kravet - och ett kit med 3
        // ska underkannas pa just den punkten tills 15 finns.
        Assert.Equal(15, GenreContracts.RequestedMinigames("En mapp 15 minigames med 3d modeller"));
        Assert.Equal(3, GenreContracts.RequestedBoards("ett party med 3 kartor"));
        Assert.Equal(4, GenreContracts.RequestedBoards("4 banor och annat"));
        Assert.Null(GenreContracts.RequestedBoards("ett party utan antal"));
        Assert.Equal(5, GenreContracts.RequestedMinigames("bygg 5 minispel"));
        Assert.Null(GenreContracts.RequestedMinigames("bygg ett partyspel"));

        var (root, _) = ScaffoldTo("bygg ett 3d mario party spel i godot med minigames");
        try
        {
            var (met, total, findings) = GenreContracts.Verify(root, "party",
                "bygg ett mario party spel med 15 minigames");
            Assert.True(met < total, "minispelskravet borde vara ouppfyllt (3 av 15)");
            Assert.Contains(findings, f => f.Contains("15 begärda"));
            // Utan begart antal racker kitgolvets 3.
            var (met2, total2, _) = GenreContracts.Verify(root, "party", "bygg ett partyspel");
            Assert.Equal(total2, met2);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ArtGd_ParsarMedRiktigGodot_CheckOnly()
    {
        // Art.gd laddas forst i RUNTIME av agentkod - --quit-parsen ser den
        // aldrig (Mg-fil-laxan v2.3). Validera den explicit per fil.
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var (root, _) = ScaffoldTo("bygg ett litet plattformsspel i godot");
        try
        {
            Assert.True(File.Exists(Path.Combine(root, "Art.gd")));
            var psi = new System.Diagnostics.ProcessStartInfo(godot)
            {
                ArgumentList = { "--headless", "--path", root, "--check-only", "--script", "res://Art.gd" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var so = proc.StandardOutput.ReadToEndAsync();
            var se = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            var output = await so + "\n" + await se;
            Assert.False(output.Contains("SCRIPT ERROR") || output.Contains("Parse Error"),
                "Art.gd parsar inte:\n" + output);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public async Task ShellGd_ParsarMedRiktigGodot_CheckOnly()
    {
        // v2.15: Shell.gd (spelskalet) laddas ocksa i runtime via class_name -
        // validera explicit per fil, samma monster som Art.gd.
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var (root, _) = ScaffoldTo("bygg ett litet plattformsspel i godot");
        try
        {
            Assert.True(File.Exists(Path.Combine(root, "Shell.gd")));
            var psi = new System.Diagnostics.ProcessStartInfo(godot)
            {
                ArgumentList = { "--headless", "--path", root, "--check-only", "--script", "res://Shell.gd" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            var so = proc.StandardOutput.ReadToEndAsync();
            var se = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            var output = await so + "\n" + await se;
            Assert.False(output.Contains("SCRIPT ERROR") || output.Contains("Parse Error"),
                "Shell.gd parsar inte:\n" + output);
        }
        finally { Cleanup(root); }
    }

    [Fact]
    public void TeamFallback_GodotPromptUtanGolv_FarAldrigJsRad()
    {
        // v2.6, live-buggen: arbetsytan hade bara DESIGN.md (inget kit
        // scaffoldat) => DetectEngine "unknown" => spårens filråd blev
        // "egen js-fil" => tre spår byggde WEBBSPEL i ett godot-uppdrag.
        // Nu följer rådet PROMPTENS motorval när golvet saknas.
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-teamhint-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "DESIGN.md"), "# Design");
            var tracks = TeamBuild.FallbackTracks(
                "bygg ett 3d mario party pummel party spel i godot med 15 minigames och en karta", dir);
            Assert.True(tracks.Count >= 2);
            foreach (var t in tracks)
            {
                Assert.DoesNotContain("js-fil", t.Description);
                Assert.Contains(".gd", t.Description);
            }
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { } }
    }

    [Fact]
    public void TankOrdstammen_TrafferInteTankar()
    {
        // "tanks" i genreregeln ar avsiktligt PLURAL: prefixet "tank" hade
        // fangat "tankar/tanke" (svenska for tankar!) och skickat
        // reflektionsspel till artillerikitet.
        Assert.NotEqual("artillery", GameScaffoldService.DetectGenre("ett spel om tankar och minnen"));
        Assert.Equal("artillery", GameScaffoldService.DetectGenre("bygg ett spel med tanks som skjuter pa varandra"));
        Assert.Equal("artillery", GameScaffoldService.DetectGenre("worms-liknande artilleri i godot"));
    }

    [Fact]
    public async Task GodotHeadless_ParsarKiten_UtanSkriptfel()
    {
        // Miljöberoende men SKARPT där godot finns i verktygskatalogen (dev-
        // maskinen har den sedan v1.45.0). Utan godot finns inget att parsa
        // här - kiten filverifieras av testerna ovan, och nodens kvalitets-
        // grind auto-provisionerar godot och parsar skarpt vid varje bygge.
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        foreach (var prompt in new[]
        {
            "bygg ett fotbolls management spel i godot",
            "top-down äventyr i godot där man överlever vågor",
            "bygg ett racingspel i godot med bilar och tre varv",   // C1 juice: Varvet
            "bygg ett 3d samlarspel i godot",                         // C1 juice: Kuben (CPUParticles3D)
            "bygg ett 2d plattformsspel i godot",                     // v1.85: Pixel Rush i GDScript
            "bygg ett artillerispel som shellshock live i godot",     // v1.98: Cannonade
            "bygg ett mario party liknande spel i godot med minigames", // v2.1: Board Bash (fangade 6 riktiga parse-fel forra passet)
            "bygg ett 3d mario party spel i godot med minigames",       // v2.3: Board Bash 3D (flerfilskittet)
            "bygg ett litet fps spel i godot"                           // v2.5: Strike Arena (first person)
        })
        {
            var (root, _) = ScaffoldTo(prompt);
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(godot)
                {
                    ArgumentList = { "--headless", "--path", root, "--quit" },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = System.Diagnostics.Process.Start(psi)!;
                var stdoutTask = proc.StandardOutput.ReadToEndAsync();
                var stderrTask = proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token);
                var output = await stdoutTask + "\n" + await stderrTask;
                Assert.False(
                    output.Contains("SCRIPT ERROR") || output.Contains("Parse Error"),
                    "GDScript-fel i kitet:\n" + output);
            }
            finally { Cleanup(root); }
        }
    }

    private static void Cleanup(string root)
    {
        // Felsökningskrok: AILOCAL_KEEP_KIT=1 lämnar kvar scaffoldade kit i
        // temp så de kan inspekteras/köras manuellt efter en testkörning.
        if (Environment.GetEnvironmentVariable("AILOCAL_KEEP_KIT") == "1") return;
        var target = _parents.TryGetValue(root, out var parent) ? parent : root;
        try { Directory.Delete(target, recursive: true); } catch { /* städning */ }
    }
}
