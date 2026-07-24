using System.Diagnostics;
using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.35: Art.gd skickades med i VARJE Godot-scaffold sedan v2.9 och
/// användes av exakt noll kit. De tio kit som kom till i v2.28 ritade sina
/// spelpjäser som platta draw_rect/draw_circle med en 1px-kontur - fienden
/// var en röd cirkel, muren en grå ruta. Det var den tydligaste
/// "dåligt browserspel"-signalen som fanns kvar.
///
/// Det svåra att verifiera: en parse-koll kör ALDRIG _draw, eftersom varje
/// kits _draw börjar med `if state != "playing": return`. Sondtestet nedan
/// startar därför spelen på riktigt (_start(1)) och räknar hur många gånger
/// draw-signalen faktiskt fyras av - noll ritningar hade tyst dolt varje fel
/// i den nya ritkoden.
/// </summary>
public class ArtAdoptionTests
{
    // Kit vars ritkod lades om till Art.gd. Quiz och Gold Mine saknas med
    // flit: de ar rena Control-spel utan _draw.
    public static TheoryData<string, string> OmlagdaKit() => new()
    {
        { "bygg ett tower defense spel i godot", "TowerDefense" },
        { "bygg ett snake spel i godot", "Snake" },
        { "bygg ett breakout spel i godot", "Breakout" },
        { "bygg ett memory kortspel i godot", "Memory" },
        { "bygg ett minesweeper minroj spel i godot", "Minesweeper" },
        { "bygg ett tetris block spel i godot", "BlockPuzzle" },
        { "bygg ett roguelike dungeon spel i godot", "Roguelike" },
        { "bygg ett rpg aventyr i godot", "Rpg" },
    };

    static string Kits() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "AiLocal.Node", "Hosting", "GameScaffoldService.GodotKits.cs")).Replace("\r\n", "\n");

    [Fact]
    public void ArtBiblioteket_AnvandsNuFaktisktAvKiten()
    {
        var kits = Kits();
        // Fore v2.35: exakt 0. Vaktar mot att en framtida omskrivning tyst
        // faller tillbaka till nakna draw-anrop igen.
        foreach (var call in new[] { "Art.orb(self", "Art.tile(self", "Art.panel(self",
            "Art.token(self", "Art.bar(self" })
            Assert.True(kits.Contains(call), $"{call} anvands inte av nagot kit");
    }

    [Fact]
    public void SpelarenMarkerasMedArtToken_InteEnAnonymRuta()
    {
        // Art.token = orb med vit ring. Husets markor for "det har ar du" -
        // i roguelike och rpg var spelaren fore detta samma platta ruta som
        // fienderna, bara i en annan farg.
        var kits = Kits();
        Assert.True(kits.Split("Art.token(self").Length - 1 >= 2,
            "spelaren markeras inte som token i bade roguelike och rpg");
    }

    [Fact]
    public void SammanhangandeTerrangForblirPlatt()
    {
        // Medvetet designval, inte en gloms-bort: skugga per ruta pa ett
        // SAMMANHANGANDE golv blir brus, inte djup. Rutnat/brunn/mark ritas
        // darfor fortfarande platt - bara pjaserna PA dem far djup.
        var kits = Kits();
        Assert.Contains("det ar SAMMANHANGANDE terrang", kits);
        Assert.Contains("TOMMA rutor ar brunnen och forblir platta", kits);
    }

    /// <summary>
    /// Den dyraste lärdomen i v2.35, och andra gången huset går i samma
    /// fälla (första var Rig3D som självrefererade). Ett `class_name` blir
    /// bara ett användbart globalt namn EFTER ett importpass. Ett projekt
    /// som startas med `godot --path` på en färsk mapp har ingen
    /// klassregister-cache - då faller HELA filen med "Identifier not
    /// declared", inte bara den raden. Art.orb såg helt rimligt ut och
    /// sänkte varenda kit.
    /// </summary>
    [Fact]
    public void VarjeGDScript_SomAnvanderEttHusbibliotek_PreloadarDet()
    {
        string[] bibliotek = ["Art", "Shell", "Phys", "Nav3D", "Rig3D", "CharCustom", "Cast3D"];
        var brister = new List<string>();
        var granskade = 0;

        foreach (var fil in Directory.GetFiles(
            Path.Combine(RepoRoot(), "src", "AiLocal.Node", "Hosting"), "GameScaffoldService*.cs"))
        {
            var src = File.ReadAllText(fil).Replace("\r\n", "\n");
            foreach (System.Text.RegularExpressions.Match block in
                System.Text.RegularExpressions.Regex.Matches(src, "\"\"\"\n(.*?)\n\"\"\"",
                    System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                var gd = block.Groups[1].Value;
                if (!gd.Contains("extends ") && !gd.Contains("class_name ")) continue;
                // Kommentarer bort - Art.gd:s egen hjalptext namner Art.panel(...).
                var kod = string.Join("\n", gd.Split('\n')
                    .Select(l => { var i = l.IndexOf('#'); return i >= 0 ? l[..i] : l; }));

                foreach (var lib in bibliotek)
                {
                    if (kod.Contains($"class_name {lib}")) continue;  // biblioteket sjalvt
                    var anvands = System.Text.RegularExpressions.Regex.IsMatch(
                        kod, $@"\b{lib}\.[a-z_]+\(");
                    if (!anvands) continue;
                    granskade++;
                    if (kod.Contains($"const {lib} = preload(\"res://{lib}.gd\")")) continue;
                    var forsta = kod.Split('\n').FirstOrDefault(l => l.Contains("extends ")) ?? "?";
                    brister.Add($"{Path.GetFileName(fil)}: en GDScript-fil ({forsta.Trim()}) anropar "
                        + $"{lib}.* utan `const {lib} = preload(\"res://{lib}.gd\")`");
                }
            }
        }

        // Falskt-gront-vakt: en regex som slutar traffa nagot ger en tyst
        // gron bock. Facit i v2.35 ar 38 = 21 Shell-anropare + 8 Art-anropare
        // + 9 anvandare av 3D-biblioteken. Golvet 30 tal att kit tas bort
        // men avslojar att monstret slutat matcha.
        Assert.True(granskade >= 30, $"vakten granskade bara {granskade} anvandningar - regexen matchar inte langre");
        Assert.True(brister.Count == 0,
            "class_name racker inte utan importpass - hela filen faller:\n  " + string.Join("\n  ", brister));
    }

    [Fact]
    public void TowerSiege_BradetFyllerFonstretsBredd()
    {
        // Skarmdumpen avslojade det: 10 kolumner x 64 px = 640 av 1152, sa
        // hogra 45 procent av skarmen var tom bakgrund. Ett halvt spelplan
        // ser ut som ett oavslutat bygge - och vagen slutade dessutom mitt
        // pa planen nar bradet vaxte.
        var kits = Kits();
        var i = kits.IndexOf("const GRID_W := 18", StringComparison.Ordinal);
        Assert.True(i > 0, "Tower Siege fyller inte fonstrets bredd (18 * 64 = 1152)");
        var block = kits[i..(i + 6000)];
        Assert.Contains("const TILE := 64", block);
        // Vagen maste ga UT ur hogra kanten, annars forsvinner fienderna mitt i.
        Assert.Contains("Vector2(GRID_W * TILE,", block);
    }

    /// <summary>
    /// SKARPT dar godot finns. Startar varje omlagt kit pa riktigt och
    /// kraver att _draw KORS - annars bevisar testet ingenting om ritkoden.
    /// </summary>
    [Theory]
    [MemberData(nameof(OmlagdaKit))]
    public async Task Sond_KitetRitarMedArt_UtanSkriptfel(string prompt, string namn)
    {
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var dir = Directory.CreateTempSubdirectory("ailocal-art-").FullName;
        try
        {
            var res = new GameScaffoldService().Scaffold("godot", prompt, dir);
            var root = Directory.Exists(Path.Combine(dir, "Main.gd")) ? dir : ProjectDirOf(dir);
            Assert.True(File.Exists(Path.Combine(root, "Art.gd")), $"{namn}: ingen Art.gd i scaffoldet");

            File.WriteAllText(Path.Combine(root, "Probe.gd"), ProbeScript);
            File.WriteAllText(Path.Combine(root, "Probe.tscn"),
                "[gd_scene load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"Script\" path=\"res://Probe.gd\" id=\"1\"]\n\n"
                + "[node name=\"Probe\" type=\"Node2D\"]\nscript = ExtResource(\"1\")\n");
            var proj = Path.Combine(root, "project.godot");
            File.WriteAllText(proj, File.ReadAllText(proj)
                .Replace("run/main_scene=\"res://Main.tscn\"", "run/main_scene=\"res://Probe.tscn\""));

            await Run(godot, ["--headless", "--path", root, "--import"]);
            var output = await Run(godot, ["--headless", "--path", root]);

            Assert.False(output.Contains("SCRIPT ERROR") || output.Contains("Parse Error")
                || output.Contains("Invalid call") || output.Contains("Identifier not found"),
                $"{namn}: fel i ritkoden efter Art-omlaggningen:\n{output}");
            Assert.True(output.Contains("PROBE KLAR"), $"{namn}: sonden korde aldrig klart:\n{output}");
            Assert.True(output.Contains("PROBE SPELAR=true"),
                $"{namn}: _start(1) startade aldrig spelet - da passerar _draw aldrig sin state-vakt:\n{output}");
            // Karnan i hela testet: utan ritningar bevisar det ingenting.
            var drawn = Rader(output, "PROBE RITNINGAR=");
            Assert.True(drawn >= 1,
                $"{namn}: _draw kordes ALDRIG ({drawn} ritningar) - testet hade da tyst dolt varje fel i Art-koden:\n{output}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static int Rader(string output, string prefix)
    {
        var i = output.IndexOf(prefix, StringComparison.Ordinal);
        if (i < 0) return -1;
        var rest = output[(i + prefix.Length)..];
        var end = 0;
        while (end < rest.Length && char.IsDigit(rest[end])) end++;
        return end == 0 ? -1 : int.Parse(rest[..end]);
    }

    static string ProjectDirOf(string dir)
    {
        foreach (var d in Directory.GetDirectories(dir))
            if (File.Exists(Path.Combine(d, "project.godot"))) return d;
        return dir;
    }

    static async Task<string> Run(string exe, string[] args)
    {
        var psi = new ProcessStartInfo(exe)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var so = p.StandardOutput.ReadToEndAsync();
        var se = p.StandardError.ReadToEndAsync();
        try { await p.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token); }
        catch (OperationCanceledException) { try { p.Kill(true); } catch { } }
        return await so + "\n" + await se;
    }

    const string ProbeScript = """
extends Node2D
# Sond: startar det riktiga kitet och RAKNAR ritningar. Utan minst en
# ritning har testet inte sett den nya Art-koden over huvud taget.

var ritningar := 0

func _ready() -> void:
	var scene: PackedScene = load("res://Main.tscn")
	var main: Node = scene.instantiate()
	add_child(main)
	await get_tree().process_frame
	if main.has_signal("draw"):
		main.draw.connect(func(): ritningar += 1)
	if main.has_method("_start"):
		main._start(1)
	for i in range(20):
		if main.has_method("queue_redraw"):
			main.queue_redraw()
		await get_tree().process_frame
	print("PROBE SPELAR=%s" % str(str(main.get("state")) == "playing"))
	print("PROBE RITNINGAR=%d" % ritningar)
	print("PROBE KLAR")
	get_tree().quit()
""";

    static string RepoRoot()
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && d is not null; i++)
        {
            if (Directory.Exists(Path.Combine(d, "src", "AiLocal.Node"))) return d;
            d = Path.GetDirectoryName(d);
        }
        throw new DirectoryNotFoundException("hittar inte repo-roten");
    }
}
