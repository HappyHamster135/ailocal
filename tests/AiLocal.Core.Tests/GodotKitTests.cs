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
        foreach (var required in new[] { "project.godot", "Main.tscn", "Main.gd", "export_presets.cfg", "DESIGN.md", "README.md", "coin.wav", "win.wav" })
            Assert.True(File.Exists(Path.Combine(root, required)), $"{required} saknas i kitet");

        Assert.Equal(ProjectVerifier.ProjectKind.Godot, new ProjectVerifier().Detect(root));

        // project.godot pekar på Main.tscn, och varje res:// i scenen finns.
        Assert.Contains("run/main_scene=\"res://Main.tscn\"", File.ReadAllText(Path.Combine(root, "project.godot")));
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
            Assert.Contains("Varvet", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("_physics_process", script);
            Assert.Contains("checkpoint", script);
            Assert.Contains("CPUParticles2D", script);  // C1 juice
            Assert.Contains("shake", script);
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
            Assert.Contains("Kuben", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("CharacterBody3D", script);
            Assert.Contains("Camera3D", script);
            Assert.Contains("CPUParticles3D", script);  // C1 juice
            Assert.Contains("shake", script);
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
            Assert.Contains("Tvatusen", File.ReadAllText(Path.Combine(root, "project.godot")));
            var script = File.ReadAllText(Path.Combine(root, "Main.gd"));
            Assert.Contains("_slide", script);
            Assert.Contains("TARGET", script);
            AssertKitComplete(root);
        }
        finally { Cleanup(root); }
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
            "bygg ett 2d plattformsspel i godot"                      // v1.85: Pixel Rush i GDScript
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
