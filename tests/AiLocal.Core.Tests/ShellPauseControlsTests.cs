using System.Diagnostics;
using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.34: spelskalets tre hål. Granskningen fann att `get_tree().paused`
/// förekom NOLL gånger - pausen var en flagga, så tweens, timers och
/// partiklar rullade vidare bakom PAUSED-texten. Vidare fanns ingen
/// namngiven input-map alls (kiten blandade ui_* med råa KEY_W), vilket
/// gjorde både omkoppling och handkontroll omöjligt, och ingen
/// credits/version-skärm.
///
/// Innehållstesterna nedan är billiga vakter. Det som FAKTISKT bevisar att
/// pausen fungerar är sondtestet längst ner: det pausar ett riktigt träd i
/// riktig Godot och mäter att bildruteräknaren slutar ticka - en skärmdump
/// eller en parse-koll hade sett likadan ut med den gamla trasiga flaggan.
/// </summary>
// v2.36: alla klasser som STARTAR godot delar samma xunit-samling och
// kor darfor aldrig parallellt. Fonstersonden foll slumpvis i full svit
// men var gron riktad: dess WaitForVisibleWindow pa 15 s hann inte nar
// atton andra godot-processer slogs om cpu:n. Riktad gron + full svit
// rod = parallellkrock, aldrig logikfel.
[Collection("GodotProcess")]
public class ShellPauseControlsTests
{
    // Normaliserad: filerna har CRLF, och ett test som soker "]\n" far -1
    // och kraschar pa radslut i stallet for pa det den skulle vakta.
    static string Shell() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "AiLocal.Node", "Hosting", "GameScaffoldService.cs")).Replace("\r\n", "\n");

    static string Kits() => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "AiLocal.Node", "Hosting", "GameScaffoldService.GodotKits.cs")).Replace("\r\n", "\n");

    [Fact]
    public void Shell_PausarHelaTradet_InteBaraEnFlagga()
    {
        var src = Shell();
        Assert.Contains("static func pause(parent: Node", src);
        Assert.Contains("tree.paused = true", src);
        Assert.Contains("tree.paused = false", src);
        // Utan WHEN_PAUSED pa overlayen blir pausmenyn oklickbar - tradet
        // star still och knapparna far ingen gui-input.
        Assert.Contains("overlay.process_mode = Node.PROCESS_MODE_WHEN_PAUSED", src);
        // Egen CanvasLayer: kiten skickar an "ui", an "hud", an "self" som
        // foralder. En Control under en Node2D lever i varldskoordinater och
        // hade glidit med kameran - och hamnat under spelets egen HUD.
        Assert.Contains("var layer := CanvasLayer.new()", src);
        Assert.Contains("layer.layer = 100", src);
        // Escape maste ta sig UT igen. Kitets egen _unhandled_input ar pausad,
        // sa genvagen far ligga pa knappen (Control hanterar shortcuts sjalv).
        Assert.Contains("resume.shortcut = sc", src);
    }

    [Fact]
    public void IngetKit_HarKvarDenFalskaPausflaggan()
    {
        // Fyra kit satte state = "paused" och trodde sig pausade.
        Assert.DoesNotContain("state = \"paused\"", Kits());
        Assert.Contains("Shell.pause(self", Kits());
    }

    [Fact]
    public void NamngivnaHandlingar_TackerTangentbordOchHandkontroll()
    {
        var src = Shell();
        Assert.Contains("const ACTIONS :=", src);
        foreach (var act in new[] { "move_left", "move_right", "move_up", "move_down",
            "fire", "interact", "pause_game" })
            Assert.Contains($"\"{act}\"", src);
        Assert.Contains("InputEventJoypadButton.new()", src);
        Assert.Contains("InputEventJoypadMotion.new()", src);
        // physical_keycode, inte keycode: annars styr en AZERTY-spelare med
        // fel tangenter.
        Assert.Contains("k.physical_keycode = code", src);
        // startup() maste lagga pa bindningarna INNAN forsta rutan.
        Assert.Contains("apply_binds(data)", src);
    }

    [Fact]
    public void Omkoppling_KanAldrigLasaUteSpelaren()
    {
        var src = Shell();
        // ui_* far ALDRIG bindas om - da kan spelaren binda bort sin egen
        // vag ut ur menyn och maste doda processen.
        var actionsBlock = src[src.IndexOf("const ACTIONS :=", StringComparison.Ordinal)..];
        actionsBlock = actionsBlock[..actionsBlock.IndexOf("]\n", StringComparison.Ordinal)];
        Assert.DoesNotContain("ui_", actionsBlock);
        // Och Escape under infangning ska AVBRYTA, inte bindas.
        Assert.Contains("if k.physical_keycode == KEY_ESCAPE:", src);
    }

    [Fact]
    public void ControlsOchCredits_NasFranOptionsSomAllaKitRedanAnropar()
    {
        var src = Shell();
        Assert.Contains("static func controls_panel(parent: Node", src);
        Assert.Contains("static func credits_panel(parent: Node", src);
        // Detta ar hela poangen: 20 av 21 kit anropar options_panel, sa
        // bada skarmarna nas utan en enda kit-andring.
        var opts = src[src.IndexOf("static func options_panel(", StringComparison.Ordinal)..];
        opts = opts[..opts.IndexOf("# ---------- karaktarsval", StringComparison.Ordinal)];
        Assert.Contains("controls_panel(overlay", opts);
        Assert.Contains("credits_panel(overlay", opts);
        var kits = Kits();
        var callers = kits.Split("Shell.options_panel").Length - 1;
        Assert.True(callers >= 18, $"bara {callers} kit anropar options_panel - vinsten uteblir");
    }

    [Fact]
    public void CreditsHarEnVersionAttVisa()
    {
        Assert.Contains("config/version=\\\"1.0.0\\\"", Kits());
        Assert.Contains("application/config/version", Shell());
        // Och en fil dar byggagenten kan skriva riktiga namn.
        Assert.Contains("CREDITS.txt", Shell());
    }

    /// <summary>
    /// SKARPT dar godot finns. Bygger ett riktigt scaffold, pausar tradet och
    /// mäter att bildruteräknaren STANNAR - det enda beviset som skiljer en
    /// fungerande paus från den gamla flaggan.
    /// </summary>
    [Fact]
    public async Task Sond_PausenStopparFaktisktTradet_OchResumeStartarDetIgen()
    {
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var dir = Directory.CreateTempSubdirectory("ailocal-shell-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("godot", "bygg ett plattformsspel", dir);
            Assert.True(File.Exists(Path.Combine(dir, "Shell.gd")), "scaffoldet gav ingen Shell.gd");

            File.WriteAllText(Path.Combine(dir, "Probe.gd"), ProbeScript);
            File.WriteAllText(Path.Combine(dir, "Probe.tscn"),
                "[gd_scene load_steps=2 format=3]\n\n"
                + "[ext_resource type=\"Script\" path=\"res://Probe.gd\" id=\"1\"]\n\n"
                + "[node name=\"Probe\" type=\"Node2D\"]\nscript = ExtResource(\"1\")\n");
            var proj = Path.Combine(dir, "project.godot");
            File.WriteAllText(proj, File.ReadAllText(proj)
                .Replace("run/main_scene=\"res://Main.tscn\"", "run/main_scene=\"res://Probe.tscn\""));

            // --path importerar ALDRIG av sig sjalvt (v2.12-fyndet): utan en
            // egen importkorning startar spelet utan sina resurser.
            await Run(godot, ["--headless", "--path", dir, "--import"], TimeSpan.FromMinutes(3));
            var output = await Run(godot, ["--headless", "--path", dir], TimeSpan.FromMinutes(3));

            Assert.DoesNotContain("Parse Error", output);
            Assert.DoesNotContain("SCRIPT ERROR", output);
            Assert.Contains("PROBE KLAR", output);

            void Kravs(string rad) => Assert.True(output.Contains(rad),
                $"sonden sa INTE '{rad}'. Hela utdatan:\n{output}");

            Kravs("PROBE ACTION_MOVE=true");
            Kravs("PROBE ACTION_PAUSE=true");
            Kravs("PROBE PAD=true");
            Kravs("PROBE UI_CANCEL_KVAR=true");
            Kravs("PROBE PAUSED=true");
            Kravs("PROBE OVERLAY_WHEN_PAUSED=true");
            // Karnan: noll bildrutor under pausen, men de rullar igen efterat.
            Kravs("PROBE TICKS_UNDER_PAUS=0");
            Kravs("PROBE RESUME_FINNS=true");
            Kravs("PROBE ESC_GENVAG=true");
            Kravs("PROBE PAUS_EGEN_LAYER=true");
            Kravs("PROBE EFTER_RESUME_PAUSED=false");
            Kravs("PROBE PAUS_STADAD=true");
            Kravs("PROBE TICKS_EFTER_ROR_SIG=true");
            Kravs("PROBE CONTROLS_RADER=7");
            Kravs("PROBE CREDITS_HAR_VERSION=true");
            Kravs("PROBE OPTIONS_HAR_CONTROLS=true");
            Kravs("PROBE OPTIONS_HAR_CREDITS=true");
            Kravs("PROBE OMBINDNING_TOG=true");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    static async Task<string> Run(string exe, string[] args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(exe)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var proc = Process.Start(psi)!;
        var so = proc.StandardOutput.ReadToEndAsync();
        var se = proc.StandardError.ReadToEndAsync();
        try { await proc.WaitForExitAsync(new CancellationTokenSource(timeout).Token); }
        catch (OperationCanceledException) { try { proc.Kill(true); } catch { } }
        return await so + "\n" + await se;
    }

    const string ProbeScript = """
extends Node2D
# Sond: bevisar att pausen ar RIKTIG. ticks rors bara nar tradet gar.

var ticks := 0
var rader: Array = []

func _process(_d: float) -> void:
	ticks += 1

func _ready() -> void:
	Shell.startup()
	rader.append("ACTION_MOVE=%s" % str(InputMap.has_action("move_left")))
	rader.append("ACTION_PAUSE=%s" % str(InputMap.has_action("pause_game")))
	var pad := false
	for e in InputMap.action_get_events("move_left"):
		if e is InputEventJoypadMotion or e is InputEventJoypadButton:
			pad = true
	rader.append("PAD=%s" % str(pad))
	rader.append("UI_CANCEL_KVAR=%s" % str(InputMap.has_action("ui_cancel")))

	await get_tree().process_frame
	await get_tree().process_frame
	var fore := ticks
	var ov: Control = Shell.pause(self)
	rader.append("PAUSED=%s" % str(get_tree().paused))
	rader.append("OVERLAY_WHEN_PAUSED=%s" % str(ov.process_mode == Node.PROCESS_MODE_WHEN_PAUSED))
	for i in range(10):
		await get_tree().process_frame
	rader.append("TICKS_UNDER_PAUS=%d" % (ticks - fore))

	var resume: Button = null
	for c in ov.get_children():
		if c is VBoxContainer:
			for b in c.get_children():
				if b is Button and (b as Button).text == "Resume":
					resume = b
	rader.append("RESUME_FINNS=%s" % str(resume != null))
	rader.append("ESC_GENVAG=%s" % str(resume != null and resume.shortcut != null))
	# Egen CanvasLayer overst: annars glider menyn med kameran och hamnar
	# under spelets egen HUD.
	var lay := ov.get_parent()
	rader.append("PAUS_EGEN_LAYER=%s" % str(lay is CanvasLayer and (lay as CanvasLayer).layer >= 100))
	resume.pressed.emit()
	await get_tree().process_frame
	await get_tree().process_frame
	rader.append("EFTER_RESUME_PAUSED=%s" % str(get_tree().paused))
	rader.append("PAUS_STADAD=%s" % str(not is_instance_valid(lay)))
	var efter := ticks
	for i in range(6):
		await get_tree().process_frame
	rader.append("TICKS_EFTER_ROR_SIG=%s" % str(ticks - efter >= 4))

	var cp: Control = Shell.controls_panel(self, func(): pass)
	rader.append("CONTROLS_RADER=%d" % _rader_i(cp))
	cp.queue_free()

	var cr: Control = Shell.credits_panel(self, func(): pass)
	var ver := false
	for l in _alla_etiketter(cr):
		if l.begins_with("Version "):
			ver = true
	rader.append("CREDITS_HAR_VERSION=%s" % str(ver))
	cr.queue_free()

	var op: Control = Shell.options_panel(self, func(): pass)
	var knappar: Array = []
	for c in op.get_children():
		if c is VBoxContainer:
			for b in c.get_children():
				if b is Button:
					knappar.append((b as Button).text)
	rader.append("OPTIONS_HAR_CONTROLS=%s" % str(knappar.has("Controls")))
	rader.append("OPTIONS_HAR_CREDITS=%s" % str(knappar.has("Credits")))
	op.queue_free()

	# Ombindning ska FAKTISKT byta tangenthandelsen i InputMap.
	var data := Shell.load_settings()
	data["binds"] = {"move_left": KEY_J}
	Shell.apply_binds(data)
	var tog := false
	for e in InputMap.action_get_events("move_left"):
		if e is InputEventKey and (e as InputEventKey).physical_keycode == KEY_J:
			tog = true
	rader.append("OMBINDNING_TOG=%s" % str(tog))

	for r in rader:
		print("PROBE " + str(r))
	print("PROBE KLAR")
	get_tree().quit()

func _rader_i(panel: Control) -> int:
	var n := 0
	for c in panel.get_children():
		if c is VBoxContainer:
			for row in c.get_children():
				if row is HBoxContainer:
					n += 1
	return n

func _alla_etiketter(panel: Control) -> Array:
	var out: Array = []
	for c in panel.get_children():
		if c is VBoxContainer:
			for l in c.get_children():
				if l is Label:
					out.append((l as Label).text)
	return out
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
