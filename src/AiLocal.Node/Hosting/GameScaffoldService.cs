using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// P1: "Skapa nytt spel" - generates a complete, buildable Unity or Godot
/// project skeleton from a short prompt so the agent (and you) don't start
/// from an empty folder. The skeleton is a real project: opening it in the
/// engine and pressing Build just works. The agent then fills in the actual
/// game logic via the file API, and Studio's "Bygg spel" runs the headless
/// build.
///
/// The prompt is matched against a few well-known shapes (2d / 3d /
/// platformer / topdown) to pick sensible defaults; anything else falls back
/// to a minimal but valid project of the chosen engine.
/// </summary>
public sealed partial class GameScaffoldService
{
    public record ScaffoldResult(bool Success, string Path, string Engine, string[] Files, string Output);

    public ScaffoldResult Scaffold(string engine, string prompt, string root)
    {
        engine = (engine ?? "").Trim().ToLowerInvariant();
        // 'auto' / empty => let the tool pick the best engine for the prompt.
        // Games default to html5 (zero-install, runs anywhere) unless the
        // prompt clearly wants a heavier engine (unity/godot/3d).
        if (engine is "" or "auto")
            engine = PickEngine(prompt);
        if (engine != "unity" && engine != "godot" && engine != "html5")
            return new(false, "", "", [], "engine maste vara 'unity', 'godot', 'html5' eller 'auto' (tomt = automatiskt val).");
        if (string.IsNullOrWhiteSpace(root))
            return new(false, "", engine, [], "root (mapp att skapa projektet i) kravs.");
        // Non-empty root -> scaffold into a fresh subfolder derived from the
        // prompt instead of refusing. Workspaces are long-lived; the second
        // build in one used to dead-end on "root-mappen ar inte tom".
        root = ScaffoldPaths.ForProject(root, prompt, "spelprojekt");

        Directory.CreateDirectory(root);
        var files = engine == "unity"
            ? ScaffoldUnity(root, prompt)
            : engine == "godot"
                ? ScaffoldGodot(root, prompt)
                : ScaffoldHtml5(root, prompt);
        return new(true, root, engine, files, $"{engine} projekt skapat i {root} ({files.Length} filer).");
    }

    /// <summary>Pick the engine for a game prompt. Default is GODOT - the
    /// app's goal is studio-grade games, and a browser toy is not that (user
    /// report: "alla vet att man inte kan göra ett riktigt studiospel i
    /// html"). Html5 is chosen only when the prompt explicitly asks for a
    /// browser/web game - the 16 html5 genre kits are still one word away
    /// ("webbspel"). Unity when named, or for 3D. The agent can always
    /// override by passing an explicit engine.</summary>
    internal static string PickEngine(string prompt)
    {
        var p = (prompt ?? "").ToLowerInvariant();
        // Godot är husmotorn (enda med full verify-/exportkedja): nämns den
        // uttryckligen vinner den - även över unity. "unity eller godot"
        // valde tidigare unity, och agenten rev sedan själv unity-kittet och
        // byggde om i godot mitt i körningen (rapporterat, ~5 min slöseri).
        if (p.Contains("godot")) return "godot";
        if (p.Contains("unity")) return "unity";
        // Negerat webb ("inte html", "ej i html", "not html") är ett
        // AVSTÅNDSTAGANDE - utan vakten valde "riktigt spel, inte html"
        // paradoxalt nog html5 så fort ingen motor namngavs.
        var webWish = p.Contains("html") || p.Contains("browser") || p.Contains("webbläsar") || p.Contains("webblasar")
            || p.Contains("webbspel") || p.Contains("web game") || p.Contains("i webben");
        var webNegated = System.Text.RegularExpressions.Regex.IsMatch(
            p, @"\b(inte|ej|utan|not|no)\s+(ett\s+|en\s+|i\s+|a\s+)?(html|browser|webb|web)");
        if (webWish && !webNegated) return "html5";
        // 3D gar till GODOT (verifierat 3D-kit) i stallet for otestad best-effort-
        // Unity; unity valjs bara nar den namns uttryckligen (fangat ovan).
        if (p.Contains("3d")) return "godot";
        return "godot";
    }

    static string[] ScaffoldUnity(string root, string prompt)
    {
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var isPlatformer = prompt.Contains("platform", StringComparison.OrdinalIgnoreCase);
        var name = new DirectoryInfo(root).Name;
        Directory.CreateDirectory(root);
        var files = new List<string>();
        // .csproj so the engine (and our headless build) sees a buildable C# project.
        Write(root, $"{name}/{name}.csproj", Csproj(name));
        files.Add($"{name}/{name}.csproj");

        // A complete, playable 2D platformer (not a stub): player controller
        // with gravity/jump + animation, patrolling enemies, collectible
        // coins, a UI (score/hp/level), 3 levels of progression, win/lose,
        // and a generated scene that wires it all up. Open in Unity and press
        // Play, or build headless - it just works.
        Write(root, "Assets/PlayerController.cs", UnityPlayerController());
        Write(root, "Assets/Enemy.cs", UnityEnemy());
        Write(root, "Assets/Coin.cs", UnityCoin());
        Write(root, "Assets/GameManager.cs", UnityGameManager());
        Write(root, "Assets/UIManager.cs", UnityUIManager());
        files.Add("Assets/PlayerController.cs"); files.Add("Assets/Enemy.cs");
        files.Add("Assets/Coin.cs"); files.Add("Assets/GameManager.cs"); files.Add("Assets/UIManager.cs");

        // Scene (with .meta) so there is something to open + build.
        var sceneGuid = Guid.NewGuid().ToString("N").Substring(0, 32);
        Write(root, "Assets/Scenes/SampleScene.unity", UnityScene(sceneGuid));
        Write(root, "Assets/Scenes/SampleScene.unity.meta", "fileFormatVersion: 2\nguid: " + sceneGuid + "\n" +
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        files.Add("Assets/Scenes/SampleScene.unity"); files.Add("Assets/Scenes/SampleScene.unity.meta");

        // Minimal project settings so Unity accepts the folder as a project.
        Write(root, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.2.13f1\nm_EditorVersionWithRevision: 6000.2.13f1 (default)\n");
        Write(root, "Packages/manifest.json", "{\n  \"dependencies\": {\n    \"com.unity.modules.physics2d\": \"1.0.0\",\n    \"com.unity.modules.audio\": \"1.0.0\",\n    \"com.unity.modules.ui\": \"1.0.0\",\n    \"com.unity.modules.uielements\": \"1.0.0\"\n  }\n}\n");
        Write(root, "ProjectSettings/EditorBuildSettings.asset",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n" +
            "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n" +
            "  - enabled: 1\n    path: Assets/Scenes/SampleScene.unity\n    guid: " + sceneGuid + "\n");
        files.Add("ProjectSettings/ProjectVersion.txt"); files.Add("Packages/manifest.json");
        files.Add("ProjectSettings/EditorBuildSettings.asset");

        // App icon (embedded into the built EXE via PlayerSettings) + a Steam
        // appid placeholder so the project is one edit away from a store page.
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "steam_appid.txt", "4800000\n");  // placeholder; replace with your real Steam app id
        files.Add("steam_appid.txt");

        Write(root, "README.md",
            "# Pixel Rush - 2D Platformer (Unity)\n\n" +
            "Oppna projektet i Unity 6000.x och tryck Play, eller bygg headless:\n" +
            "`Unity -batchmode -buildWindows64Player build/<mappnamn>.exe` (eller Studio-knappen 'Bygg spel').\n\n" +
            "Styrning: Vänster/Höger eller A/D, Space/Up/W för hopp, Esc för paus.\n" +
            "Samla mynt, undvik fiender, nå flaggan. Klara 3 nivåer för att vinna.\n" +
            "App-ikon: icon.ico (satt i PlayerSettings > Icon). Steam: ersatt steam_appid.txt med ditt app-id.\n");
        files.Add("README.md");
        return files.ToArray();
    }

    static string[] ScaffoldGodot(string root, string prompt)
    {
        // v2.2: golvet + MECHANICS.md - genrens relevanta, testade mekanik-
        // snuttar ur GameMechanicLibrary läggs som fil i projektet så
        // byggagenten KLISTRAR IN beprövad kod (double_jump, checkpoint,
        // countdown...) i stället för att uppfinna den svagt från noll.
        var scaffolded = ScaffoldGodotCore(root, prompt);
        scaffolded = AppendMusic(root, prompt ?? "", scaffolded);
        scaffolded = AppendArtLib(root, scaffolded);
        scaffolded = AppendShellLib(root, scaffolded);
        scaffolded = AppendSfxBank(root, scaffolded);
        scaffolded = AppendCharacterSprites(root, prompt ?? "", scaffolded);
        return AppendMechanicsDoc(root, prompt ?? "", scaffolded);
    }

    /// <summary>v2.15: SPELSKALET (ägarens dom: "startmeny, settings, välja
    /// gubbe/map/minigames - sånt som ALLA spel har, även gratis"). Shell.gd
    /// ger byggstenarna varje riktigt spel behöver: riktig huvudmeny med
    /// navigerbara val, options-skärm (volym/mute/fullskärm som SPARAS mellan
    /// körningar) och karaktärsval. Kiten använder dem; regissörskriteriet +
    /// release-checklistan tvingar agentbyggen att göra detsamma.</summary>
    static string[] AppendShellLib(string root, string[] files)
    {
        try
        {
            Write(root, "Shell.gd", GodotShellLib);
            return [.. files, "Shell.gd"];
        }
        catch
        {
            return files;
        }
    }

    const string GodotShellLib = """
class_name Shell
# Shell.gd - SPELSKALET som byggstenar: riktig huvudmeny, options-skarm
# (volym, mute, fullskarm - sparas mellan korningar i user://) och
# karaktarsval. Alla funktioner ar statiska: Shell.menu(...),
# Shell.options_panel(...), Shell.character_select(...).
# Anropa Shell.startup() forst i _ready sa sparade installningar galler
# fran forsta rutan. Extra spel-egna nycklar (t.ex. vald karaktar) far
# lagras i samma dictionary via load_settings/save_settings.
# Player text in ENGLISH (house rule).

const SETTINGS_PATH := "user://shell_settings.save"

static func load_settings() -> Dictionary:
	var data := {"volume": 1.0, "muted": false, "fullscreen": false}
	if FileAccess.file_exists(SETTINGS_PATH):
		var f := FileAccess.open(SETTINGS_PATH, FileAccess.READ)
		if f:
			var parsed: Variant = JSON.parse_string(f.get_as_text())
			if typeof(parsed) == TYPE_DICTIONARY:
				for k in (parsed as Dictionary).keys():
					data[k] = parsed[k]
	return data

static func save_settings(data: Dictionary) -> void:
	var f := FileAccess.open(SETTINGS_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(data))

static func apply_settings(data: Dictionary) -> void:
	var vol: float = clampf(float(data.get("volume", 1.0)), 0.0, 1.0)
	AudioServer.set_bus_volume_db(0, linear_to_db(maxf(vol, 0.001)))
	AudioServer.set_bus_mute(0, bool(data.get("muted", false)))
	var want_full: bool = bool(data.get("fullscreen", false))
	var is_full: bool = DisplayServer.window_get_mode() == DisplayServer.WINDOW_MODE_FULLSCREEN
	if want_full != is_full:
		DisplayServer.window_set_mode(DisplayServer.WINDOW_MODE_FULLSCREEN if want_full else DisplayServer.WINDOW_MODE_WINDOWED)

static func startup() -> Dictionary:
	var data := load_settings()
	apply_settings(data)
	return data

# ---------- huvudmenyn ----------
# entries: [["Play", callable], ["Options", callable], ...]. Forsta knappen
# far fokus => upp/ner + Enter fungerar direkt (fokusgrannar ar automatiska
# i en VBox). FULL_RECT + centrerad alignment - PRESET_CENTER satter bara
# pivoten och hogerforskjuter innehallet.
static func menu(parent: Node, entries: Array, top_offset: float = 0.0) -> Control:
	var overlay := Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.offset_top = top_offset
	parent.add_child(overlay)
	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var first := true
	for e in entries:
		var b := Button.new()
		b.text = str(e[0])
		b.custom_minimum_size = Vector2(320, 46)
		b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		var cb: Callable = e[1]
		b.pressed.connect(cb)
		box.add_child(b)
		if first:
			first = false
			b.call_deferred("grab_focus")
	return overlay

# ---------- options ----------
# Volym-slider + mute + fullskarm; varje andring appliceras och SPARAS
# direkt. on_back stanger skarmen (anroparen bygger om sin meny).
static func options_panel(parent: Node, on_back: Callable) -> Control:
	var data := load_settings()
	var overlay := Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(overlay)
	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 14)
	overlay.add_child(box)
	var title := Label.new()
	title.text = "OPTIONS"
	title.add_theme_font_size_override("font_size", 44)
	title.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.7))
	title.add_theme_constant_override("outline_size", 10)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)

	var vol_row := HBoxContainer.new()
	vol_row.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	vol_row.add_theme_constant_override("separation", 12)
	box.add_child(vol_row)
	var vol_label := Label.new()
	vol_label.text = "Volume"
	vol_label.custom_minimum_size = Vector2(110, 0)
	vol_row.add_child(vol_label)
	var slider := HSlider.new()
	slider.min_value = 0.0
	slider.max_value = 100.0
	slider.step = 5.0
	slider.value = clampf(float(data.get("volume", 1.0)), 0.0, 1.0) * 100.0
	slider.custom_minimum_size = Vector2(240, 24)
	slider.value_changed.connect(func(v: float):
		data["volume"] = v / 100.0
		apply_settings(data)
		save_settings(data))
	vol_row.add_child(slider)

	var mute := CheckButton.new()
	mute.text = "Mute"
	mute.button_pressed = bool(data.get("muted", false))
	mute.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	mute.toggled.connect(func(on: bool):
		data["muted"] = on
		apply_settings(data)
		save_settings(data))
	box.add_child(mute)

	var full := CheckButton.new()
	full.text = "Fullscreen"
	full.button_pressed = bool(data.get("fullscreen", false))
	full.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	full.toggled.connect(func(on: bool):
		data["fullscreen"] = on
		apply_settings(data)
		save_settings(data))
	box.add_child(full)

	var back := Button.new()
	back.text = "Back"
	back.custom_minimum_size = Vector2(320, 46)
	back.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	back.pressed.connect(on_back)
	box.add_child(back)
	back.call_deferred("grab_focus")
	return overlay

# ---------- karaktarsval ----------
# names/colors ar parallella listor; selected markeras med ram. on_pick(i)
# far index - anroparen sparar valet och stanger skarmen.
static func character_select(parent: Node, names: Array, colors: Array, selected: int, on_pick: Callable) -> Control:
	var overlay := Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(overlay)
	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 18)
	overlay.add_child(box)
	var title := Label.new()
	title.text = "CHOOSE YOUR CHARACTER"
	title.add_theme_font_size_override("font_size", 40)
	title.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.7))
	title.add_theme_constant_override("outline_size", 10)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)
	var row := HBoxContainer.new()
	row.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	row.add_theme_constant_override("separation", 16)
	box.add_child(row)
	var first := true
	for i in range(names.size()):
		var col := VBoxContainer.new()
		col.add_theme_constant_override("separation", 6)
		row.add_child(col)
		var swatch := ColorRect.new()
		swatch.color = colors[i] if i < colors.size() else Color.WHITE
		swatch.custom_minimum_size = Vector2(64, 64)
		swatch.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		col.add_child(swatch)
		var b := Button.new()
		b.text = str(names[i]) + (" *" if i == selected else "")
		b.custom_minimum_size = Vector2(110, 40)
		var idx := i
		b.pressed.connect(func(): on_pick.call(idx))
		col.add_child(b)
		if first or i == selected:
			first = false
			b.call_deferred("grab_focus")
	return overlay
""";

    /// <summary>v2.11: GUBBARNA - animerade karaktärssprites till ALLA Godot-
    /// scaffolds (ägarens "utan gubbar, utan animationer"). Topdown/plattform
    /// hade PixelAnimator-sprites; övriga kit och agentbyggen ritade cirklar.
    /// Nu får varje projekt player/enemy-spritesheets + SpriteFrames (idle +
    /// walk) att koppla i AnimatedSprite2D - och agentprompten förbjuder
    /// platta cirklar som karaktärer.</summary>
    static string[] AppendCharacterSprites(string root, string prompt, string[] files)
    {
        try
        {
            var extra = new List<string>();
            if (!File.Exists(Path.Combine(root, "player_frames.tres")))
            {
                var playerSheet = PixelAnimator.Build(prompt);
                Write(root, "player.png", playerSheet.Png);
                Write(root, "player_frames.tres", GodotSpriteFrames.Build("player.png", playerSheet));
                extra.Add("player.png"); extra.Add("player_frames.tres");
            }
            if (!File.Exists(Path.Combine(root, "enemy_frames.tres")))
            {
                var enemySheet = PixelAnimator.Build(prompt + " fiende monster");
                Write(root, "enemy.png", enemySheet.Png);
                Write(root, "enemy_frames.tres", GodotSpriteFrames.Build("enemy.png", enemySheet));
                extra.Add("enemy.png"); extra.Add("enemy_frames.tres");
            }
            return [.. files, .. extra];
        }
        catch
        {
            return files;
        }
    }

    /// <summary>v2.10: LJUDBANKEN - Art.gd-principen för ljud. Agenter
    /// återanvänder annars ETT pip för alla nya händelser (enklaste vägen).
    /// Varje scaffold får en palett av färdiga, OLIKA sfxr-ljud att koppla
    /// till nya händelser; agentprompten kräver olika ljud per händelse.</summary>
    static string[] AppendSfxBank(string root, string[] files)
    {
        try
        {
            var extra = new List<string>();
            foreach (var (name, category, seed) in new[]
            {
                ("sfx_select.wav", "select", 31), ("sfx_powerup.wav", "powerup", 32),
                ("sfx_explosion.wav", "explosion", 33), ("sfx_lose.wav", "lose", 34),
                ("sfx_jump.wav", "jump", 35), ("sfx_shoot.wav", "shoot", 36),
            })
            {
                if (File.Exists(Path.Combine(root, name))) continue;
                Write(root, name, SfxrGenerator.Render(category, seed));
                extra.Add(name);
            }
            return [.. files, .. extra];
        }
        catch
        {
            return files;
        }
    }

    /// <summary>v2.9: Art.gd - AGENTERNAS RITBIBLIOTEK. Agenter ritar med
    /// nakna draw_rect/draw_circle (enklast) och resultatet ser ut som
    /// programmer-art fran 2007 (agarens "forsta Bloons-spelet"-skarmdump).
    /// Biblioteket ger produktionsprimitiver: skugga+kontur+ljushighlight,
    /// paneler, brickor med symbol, orbs/tokens, banor mellan punkter,
    /// gradient+vinjett-bakgrund. Regissorskriteriet + bildkritiken tvingar
    /// anvandningen for allt NYTT; golvkiten har egen intrimmad ritning.</summary>
    static string[] AppendArtLib(string root, string[] files)
    {
        try
        {
            Write(root, "Art.gd", GodotArtLib);
            return [.. files, "Art.gd"];
        }
        catch
        {
            return files;
        }
    }

    const string GodotArtLib = """
class_name Art
# Art.gd - produktionsritning for agentbyggda spel. ANVAND DESSA i stallet
# for nakna draw_rect/draw_circle: alla former far skugga, kontur och djup.
# Alla funktioner ar statiska: Art.panel(self, rect, fargen) fran valfri
# _draw(). BYT TEMA: skicka andra farger - formspraket ar konstant.

const SHADOW := Color(0.0, 0.0, 0.0, 0.28)
const OUTLINE_DARKEN := 0.45
const HILITE := Color(1.0, 1.0, 1.0, 0.35)

# Gradientbakgrund (vertikala band) med mork vinjett upptill/nedtill -
# aldrig en platt enfargsyta bakom spelet.
static func bg(c: CanvasItem, rect: Rect2, top: Color, bottom: Color) -> void:
    var bands := 24
    for i in range(bands):
        var t := float(i) / float(bands - 1)
        var r := Rect2(rect.position.x, rect.position.y + rect.size.y * float(i) / float(bands),
            rect.size.x, rect.size.y / float(bands) + 1.0)
        c.draw_rect(r, top.lerp(bottom, t))
    c.draw_rect(Rect2(rect.position, Vector2(rect.size.x, 46.0)), Color(0, 0, 0, 0.10))
    c.draw_rect(Rect2(rect.position + Vector2(0, rect.size.y - 46.0), Vector2(rect.size.x, 46.0)), Color(0, 0, 0, 0.14))

# Panel/kort: skugga bakom, fyllning, ljuskant upptill, kontur.
static func panel(c: CanvasItem, rect: Rect2, fill: Color) -> void:
    c.draw_rect(Rect2(rect.position + Vector2(0, 4), rect.size), SHADOW)
    c.draw_rect(rect, fill)
    c.draw_rect(Rect2(rect.position, Vector2(rect.size.x, maxf(3.0, rect.size.y * 0.14))), HILITE)
    c.draw_rect(rect, fill.darkened(OUTLINE_DARKEN), false, 2.0)

# Spelbricka/ruta med valfri symboltext i mitten.
static func tile(c: CanvasItem, rect: Rect2, fill: Color, symbol: String = "", symbol_col: Color = Color.WHITE) -> void:
    panel(c, rect, fill)
    if symbol != "":
        var fs := int(rect.size.y * 0.5)
        c.draw_string(ThemeDB.fallback_font,
            Vector2(rect.position.x, rect.position.y + rect.size.y * 0.5 + float(fs) * 0.36),
            symbol, HORIZONTAL_ALIGNMENT_CENTER, rect.size.x, fs, symbol_col)

# Orb: skuggad cirkel med ljushighlight och kontur - aldrig en platt cirkel.
static func orb(c: CanvasItem, pos: Vector2, r: float, col: Color) -> void:
    c.draw_circle(pos + Vector2(0, r * 0.18), r, SHADOW)
    c.draw_circle(pos, r, col)
    c.draw_circle(pos + Vector2(-r * 0.3, -r * 0.3), r * 0.35, HILITE)
    c.draw_arc(pos, r, 0.0, TAU, 24, col.darkened(OUTLINE_DARKEN), 2.0)

# Spelartoken: orb med vit ring sa den skiljer sig fran dekor.
static func token(c: CanvasItem, pos: Vector2, r: float, col: Color) -> void:
    orb(c, pos, r, col)
    c.draw_arc(pos, r + 2.5, 0.0, TAU, 24, Color(1, 1, 1, 0.9), 2.0)

# Bana/koppling mellan punkter (t.ex. bradrutor) - dubbellinje ger djup.
static func connect_path(c: CanvasItem, points: PackedVector2Array, col: Color, closed: bool = true) -> void:
    if points.size() < 2:
        return
    var n := points.size() if closed else points.size() - 1
    for i in range(n):
        var a := points[i]
        var b := points[(i + 1) % points.size()]
        c.draw_line(a, b, col.darkened(0.4), 5.0)
        c.draw_line(a, b, col, 2.5)

# Matare/progressbar med kontur.
static func bar(c: CanvasItem, rect: Rect2, frac: float, back: Color, fill: Color) -> void:
    c.draw_rect(Rect2(rect.position + Vector2(0, 2), rect.size), SHADOW)
    c.draw_rect(rect, back)
    c.draw_rect(Rect2(rect.position, Vector2(rect.size.x * clampf(frac, 0.0, 1.0), rect.size.y)), fill)
    c.draw_rect(rect, back.darkened(OUTLINE_DARKEN), false, 2.0)

# Rubriktext med mork kontur - lasbar mot alla bakgrunder.
static func label(c: CanvasItem, pos: Vector2, text: String, size: int, col: Color, width: float = 1152.0) -> void:
    var f := ThemeDB.fallback_font
    c.draw_string_outline(f, pos, text, HORIZONTAL_ALIGNMENT_CENTER, width, size, 4, Color(0, 0, 0, 0.55))
    c.draw_string(f, pos, text, HORIZONTAL_ALIGNMENT_CENTER, width, size, col)
""";

    /// <summary>v2.4: BAKGRUNDSMUSIK i varje Godot-kit. ChiptuneComposer har
    /// funnits sedan v1.36 men inget kit SPELADE musik - ljudbilden var bara
    /// effekter. En loopbar slinga per genre-stämning skrivs centralt här;
    /// kiten laddar music.wav och loopar den (finished->play, -14 dB).</summary>
    static string[] AppendMusic(string root, string prompt, string[] files)
    {
        try
        {
            var mood = DetectGenre(prompt) switch
            {
                "party" => "victory",
                "racing" or "shooter" or "fps" => "action",
                "artillery" => "action",
                "platformer" or "rpg" or "roguelike" => "exploration",
                "management" or "simulator" or "idle" => "calm",
                "puzzle" or "memory" or "quiz" => "ambient",
                _ => "calm",
            };
            Write(root, "music.wav", ChiptuneComposer.Render(mood, seed: 7));
            return [.. files, "music.wav"];
        }
        catch
        {
            return files; // musiken är en bonus, aldrig ett krav
        }
    }

    static string[] AppendMechanicsDoc(string root, string prompt, string[] files)
    {
        try
        {
            var genre = DetectGenre(prompt);
            string[] names = genre switch
            {
                "party" => ["countdown_timer", "score_popup", "powerup_timer"],
                "platformer" => ["double_jump", "checkpoint", "enemy_patrol", "score_popup"],
                "rpg" or "roguelike" or "shooter" => ["enemy_patrol", "health_bar", "damage_flash", "camera_follow"],
                "racing" => ["checkpoint", "camera_follow", "countdown_timer"],
                "management" or "simulator" or "idle" => ["shop", "score_popup"],
                "artillery" => ["damage_flash", "countdown_timer", "score_popup"],
                "puzzle" => ["score_popup", "countdown_timer"],
                _ => ["score_popup", "countdown_timer"],
            };
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# Mekanikbibliotek (fardiga, testade GDScript-snuttar)");
            sb.AppendLine();
            sb.AppendLine("Klistra in och ANPASSA i stallet for att skriva fran noll -");
            sb.AppendLine("snuttarna foljer kitets stil (engelsk spelartext, juice inbyggd).");
            foreach (var name in names)
            {
                if (GameMechanics.GameMechanicLibrary.Get(name) is not { } m) continue;
                sb.AppendLine();
                sb.AppendLine($"## {m.Name} - {m.Description}");
                sb.AppendLine("```gdscript");
                sb.AppendLine(m.GDScript.Trim());
                sb.AppendLine("```");
            }
            Write(root, "MECHANICS.md", sb.ToString());
            return [.. files, "MECHANICS.md"];
        }
        catch
        {
            return files; // biblioteket är en bonus, aldrig ett krav
        }
    }

    static string[] ScaffoldGodotCore(string root, string prompt)
    {
        // Genrekit även för Godot - tidigare fanns BARA plattformaren oavsett
        // prompt, så en fotbollsmanager startade som "Pixel Rush" och agenten
        // fick bygga 95% från noll (rotorsaken bakom spretiga motorspel).
        // Management/sim/idle får tycoon-grunden; rpg/roguelike/shooter får
        // top-down-grunden; övriga genrer behåller plattformaren tills fler
        // kit finns.
        // 3D far det dedikerade 3D-kitet (The Cube) fore genre-routningen - genren
        // ar 2D-orienterad, men "3d" ar en motor-/dimensionssignal.
        // Normalisera en gang: inline (prompt ?? "") lamnade prompt som "kanske
        // null" for de foljande anropen (CS8604) - reassignen gor den non-null.
        prompt ??= "";
        // v2.5: FPS ar 3D till sin natur - routas fore 3D-signalen sa bade
        // "fps" och "3d fps" far first person-golvet (inte samlarspelet).
        if (DetectGenre(prompt) == "fps")
            return ScaffoldGodotFps(root, prompt);
        // v2.26: 2.5D/isometri ar en 2D-TEKNIK (romb-tiles, y-sortering) -
        // routas FORE 3d-signalen sa "isometrisk 3d-vy" far iso-kitet.
        // Triggas av prompt-orden ELLER stilkortets hint fran WorkerRole.
        if (prompt.Contains("isometri", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("isometric", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("2.5d", StringComparison.OrdinalIgnoreCase)
            || prompt.Contains("2,5d", StringComparison.OrdinalIgnoreCase))
            return ScaffoldGodotIso(root, prompt);
        if (prompt.Contains("3d", StringComparison.OrdinalIgnoreCase))
        {
            // v2.3: "3d mario party" ska INTE bli samlarspelet Kuben -
            // party-genren har ett eget 3D-golv (flerfilskittet Board Bash
            // 3D: Main + Mg*.gd-minispel = utbyggnadskonventionen).
            if (DetectGenre(prompt) == "party")
                return ScaffoldGodotParty3D(root, prompt);
            return ScaffoldGodot3D(root, prompt);
        }

        var genre = DetectGenre(prompt);
        if (genre is "management" or "simulator")
            return ScaffoldGodotManagement(root, prompt);
        // v2.28: genren "rpg" tacker BADE top-down (The Glade: vagoverlevnad)
        // och riktig RPG (Hero's Quest: overworld/turbaserad strid/dialog).
        // "top-down"/"topdown" i prompten behaller The Glade; ren rpg/aventyr
        // far Hero's Quest. shooter gar till top-down som forut.
        if (genre == "rpg")
        {
            if (prompt.Contains("top-down", StringComparison.OrdinalIgnoreCase)
                || prompt.Contains("topdown", StringComparison.OrdinalIgnoreCase))
                return ScaffoldGodotTopDown(root, prompt);
            return ScaffoldGodotRpg(root, prompt);
        }
        if (genre == "shooter")
            return ScaffoldGodotTopDown(root, prompt);
        if (genre == "racing")
            return ScaffoldGodotRacing(root, prompt);
        if (genre == "puzzle")
            return ScaffoldGodotPuzzle(root, prompt);
        if (genre == "artillery")
            return ScaffoldGodotArtillery(root, prompt);
        // v2.28: dedicated kits for popular genres
        if (genre == "towerdefense")
            return ScaffoldGodotTowerDefense(root, prompt);
        if (genre == "snake")
            return ScaffoldGodotSnake(root, prompt);
        if (genre == "breakout")
            return ScaffoldGodotBreakout(root, prompt);
        if (genre == "quiz")
            return ScaffoldGodotQuiz(root, prompt);
        if (genre == "memory")
            return ScaffoldGodotMemory(root, prompt);
        if (genre == "minesweeper")
            return ScaffoldGodotMinesweeper(root, prompt);
        if (genre is "idle" or "clicker")
            return ScaffoldGodotIdle(root, prompt);
        if (genre == "blockpuzzle")
            return ScaffoldGodotBlockPuzzle(root, prompt);
        if (genre == "roguelike")
            return ScaffoldGodotRoguelike(root, prompt);
        if (genre == "party")
            return ScaffoldGodotParty(root, prompt);

        // Plattformaren (Pixel Rush) - sedan v1.85 ren GDScript som de andra
        // kiten, sa den headless-verifieras och har juice-passet. Det gamla
        // C#/mono-kittet kunde aldrig parse-verifieras utan mono-bygge.
        return ScaffoldGodotPlatformer(root, prompt);
    }

    /// <summary>Writes a minimal valid mono WAV (square wave) so the game has
    /// sound effects without downloading anything. 16-bit PCM, 8 kHz.</summary>
    static byte[] MakeWav(int freqHz, double seconds)
    {
        const int sampleRate = 8000;
        var n = (int)(sampleRate * seconds);
        var data = new byte[44 + n * 2];
        // RIFF header
        var header = System.Text.Encoding.ASCII.GetBytes("RIFF");
        Array.Copy(header, 0, data, 0, 4);
        BitConverter.GetBytes(36 + n * 2).CopyTo(data, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("WAVE"), 0, data, 8, 4);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("fmt "), 0, data, 12, 4);
        BitConverter.GetBytes(16).CopyTo(data, 16);
        BitConverter.GetBytes((short)1).CopyTo(data, 20);   // PCM
        BitConverter.GetBytes((short)1).CopyTo(data, 22);   // mono
        BitConverter.GetBytes(sampleRate).CopyTo(data, 24);
        BitConverter.GetBytes(sampleRate * 2).CopyTo(data, 28);
        BitConverter.GetBytes((short)2).CopyTo(data, 32);
        BitConverter.GetBytes((short)16).CopyTo(data, 34);
        Array.Copy(System.Text.Encoding.ASCII.GetBytes("data"), 0, data, 36, 4);
        BitConverter.GetBytes(n * 2).CopyTo(data, 40);
        for (var i = 0; i < n; i++)
        {
            var t = (double)i / sampleRate;
            var s = Math.Sign(Math.Sin(2 * Math.PI * freqHz * t));
            BitConverter.GetBytes((short)(s * 9000)).CopyTo(data, 44 + i * 2);
        }
        return data;
    }

    /// <summary>Writes a minimal valid 32x32 RGBA Windows .ico (no external
    /// art) so the exported game/EXE has its own icon - a step toward a
    /// publishable build (e.g. Steam).</summary>
    static byte[] MakeIco()
    {
        const int sz = 32;
        const int px = sz * sz;
        // Pixel data: BGRA, bottom-up.
        var bgra = new byte[px * 4];
        for (var y = 0; y < sz; y++)
            for (var x = 0; x < sz; x++)
            {
                var i = (y * sz + x) * 4;
                // Cornflower-blue rounded-ish square on transparent bg.
                var inside = x >= 3 && x < sz - 3 && y >= 3 && y < sz - 3;
                if (inside)
                {
                    bgra[i] = 0x87; bgra[i + 1] = 0x42; bgra[i + 2] = 0x2d; bgra[i + 3] = 0xff;
                }
                else { bgra[i] = 0; bgra[i + 1] = 0; bgra[i + 2] = 0; bgra[i + 3] = 0; }
            }

        var dib = new byte[40 + bgra.Length];
        BitConverter.GetBytes(40).CopyTo(dib, 0);                  // header size
        BitConverter.GetBytes((int)sz).CopyTo(dib, 4);             // width
        BitConverter.GetBytes(-sz).CopyTo(dib, 8);                 // height (neg = top-down)
        dib[12] = 1;                                              // planes
        dib[14] = 32;                                             // bpp
        BitConverter.GetBytes(bgra.Length).CopyTo(dib, 20);       // pixel data size
        Array.Copy(bgra, 0, dib, 40, bgra.Length);

        var outp = new System.IO.MemoryStream();
        using var bw = new System.IO.BinaryWriter(outp);
        // ICONDIR
        bw.Write((short)0);      // reserved
        bw.Write((short)1);      // type = icon
        bw.Write((short)1);      // count
        // ICONDIRENTRY
        bw.Write((byte)sz);      // width
        bw.Write((byte)sz);      // height
        bw.Write((byte)0);       // colors
        bw.Write((byte)0);       // reserved
        bw.Write((short)1);      // planes
        bw.Write((short)32);     // bpp
        bw.Write((int)dib.Length);
        bw.Write((int)(6 + 16)); // offset to DIB
        bw.Write(dib);
        return outp.ToArray();
    }
    static string[] ScaffoldHtml5(string root, string prompt)
    {
        // A single, self-contained, immediately-playable game - no build
        // step, no engine install: open index.html in any browser. The GENRE
        // is picked from the prompt (rpg / racing / puzzle / tower defense /
        // shooter, platformer as default), so "bygg ett racingspel" actually
        // yields a racing game instead of the platformer. Every template
        // ships at the production bar: title screen, pause, game over/win
        // overlays with restart, WebAudio SFX, animation and a persistent
        // highscore - the agent then extends it instead of starting from
        // nothing.
        var genre = DetectGenre(prompt);
        var (game, design) = genre switch
        {
            "rpg" => (Html5Rpg(prompt), Html5RpgDesignDoc(prompt)),
            "racing" => (Html5Racing(prompt), Html5RacingDesignDoc(prompt)),
            "puzzle" => (Html5Puzzle(prompt), Html5PuzzleDesignDoc(prompt)),
            "towerdefense" => (Html5TowerDefense(prompt), Html5TdDesignDoc(prompt)),
            "shooter" => (Html5Shooter(prompt), Html5ShooterDesignDoc(prompt)),
            "snake" => (Html5Snake(prompt), Html5SnakeDesignDoc(prompt)),
            "idle" => (Html5Idle(prompt), Html5IdleDesignDoc(prompt)),
            "breakout" => (Html5Breakout(prompt), Html5BreakoutDesignDoc(prompt)),
            "management" => (Html5Management(prompt), Html5ManagementDesignDoc(prompt)),
            "simulator" => (Html5Simulator(prompt), Html5SimulatorDesignDoc(prompt)),
            "roguelike" => (Html5Roguelike(prompt), Html5RoguelikeDesignDoc(prompt)),
            "memory" => (Html5Memory(prompt), Html5MemoryDesignDoc(prompt)),
            "minesweeper" => (Html5Minesweeper(prompt), Html5MinesweeperDesignDoc(prompt)),
            "quiz" => (Html5Quiz(prompt), Html5QuizDesignDoc(prompt)),
            "blockpuzzle" => (Html5BlockPuzzle(prompt), Html5BlockPuzzleDesignDoc(prompt)),
            _ => (Html5Game(), Html5DesignDoc(prompt))
        };
        Write(root, "index.html", game);
        Write(root, "DESIGN.md", design);
        Write(root, "README.md", Html5GenreReadme(genre));
        return new[] { "index.html", "DESIGN.md", "README.md" };
    }

    /// <summary>The agent's "plan" for the game, written as a real artefact
    /// (DESIGN.md) so the build is auditable and the user can see the design
    /// intent before/while the game is extended.</summary>
    static string Html5DesignDoc(string prompt)
    {
        var p = (prompt ?? "").Trim();
        if (string.IsNullOrWhiteSpace(p)) p = "ett 2d plattformspel";
        return
"# Speldesign: 2D Plattformspel (HTML5)\n\n" +
"## Koncept\n" +
"Byggt utifrån prompten: **" + p + "**\n\n" +
"Ett klassiskt sidoscrollande plattformspel i webbläsaren - ingen installation, inget bygge. " +
"Målet är att ta sig genom 3 nivåer, samla mynt och nå flaggan.\n\n" +
"## Målgrupp & känsla\n" +
"- Casual, snabbt att förstå, svårare för varje nivå.\n" +
"- Ren, färgglad pixel-stil; webbläsarspelet ska kännas som ett riktigt litet spel.\n\n" +
"## Spelmekanik\n" +
"- **Rörelse:** piltangenter / WASD. **Hoppa:** mellanslag / W / upp (endast från marken).\n" +
"- **Gravitation & kollision:** enklare AABB-fysik mot plattformar; spelaren fastnar inte.\n" +
"- **Mynt:** +10 poäng var, ljud vid uppplock. Försvinner när de tas.\n" +
"- **Fiender:** rör sig fram och tillbaka på sin plattform; beröring = -20 HP + knockback.\n" +
"- **HP:** 100. Vid 0 → Game Over. Vid fall utanför skärmen → Game Over.\n" +
"- **Flagga:** nå den för att klara nivån (+100 poäng).\n\n" +
"## Nivåer (progression är riktig, inte bara snabbare)\n" +
"1. **Intro** - få plattformar, 2 fiender. Lär ut rörelse & hopp.\n" +
"2. **Vertikalitet** - fler/högre plattformar, 3 fiender, mer luft.\n" +
"3. **Gauntlet** - trånga plattformar, 4 fiender, hög tempo. Avslutande nivå.\n" +
"Klarrar alla 3 → \"Du vann spelet!\".\n\n" +
"## Visuellt & ljud\n" +
"- Canvas-rendering, 2-frame sprite-bob för löpning (ingen extern asset).\n" +
"- Web Audio-SFX: hopp, mynt, träff, vinst. Ljud initieras först vid första input (autoplay-regler).\n" +
"- HUD: HP-bar, nivå, poäng och sparat rekord (localStorage).\n" +
"- Titelskärm före start, paus med Esc/P, game over/vinst-skärm med omstart.\n\n" +
"## Tekniska antaganden\n" +
"- Ett enda `index.html` (HTML+CSS+JS). Inget externt beroende, inget bygge.\n" +
"- Agenten (eller användaren) bygger vidare genom att redigera `index.html` - t.ex. fler nivåer " +
"(lägg till i `levels`-arrayen), nya fiendetyper, power-ups eller en timer.\n";
    }

    static string Html5Game()
    {
        // Single, self-contained, immediately-playable 2D platformer.
        // Rendered as a C# verbatim string (quotes doubled) so the
        // embedded HTML/JS keeps natural newlines. Real gravity/jump,
        // collectibles, enemies, HUD, 3 levels with genuine layout
        // progression, sprite-frame animation and Web Audio SFX.
        return @"<!DOCTYPE html>
<html lang=""sv"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<title>2D Plattformspel</title>
<style>
  html,body{margin:0;height:100%;background:#1a1a2e;display:flex;align-items:center;justify-content:center;font-family:system-ui,sans-serif;overflow:hidden}
  #wrap{position:relative}
  canvas{background:linear-gradient(#87ceeb,#cdeffd);border:3px solid #16213e;display:block;image-rendering:pixelated}
  #hud{position:absolute;top:10px;left:10px;color:#fff;text-shadow:2px 2px 0 #000;font-size:18px;pointer-events:none}
  .bar{width:200px;height:18px;border:2px solid #fff;border-radius:4px;overflow:hidden;margin-top:4px;background:#333}
  .bar>i{display:block;height:100%;background:#ff5b5b;transition:width .2s}
  #over{position:absolute;inset:0;display:none;align-items:center;justify-content:center;flex-direction:column;background:rgba(0,0,0,.82);color:#fff;text-align:center}
  #over h1{font-size:42px;margin:0}#over button{margin-top:18px;padding:10px 28px;font-size:16px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:6px}
  #pause{position:absolute;inset:0;display:none;align-items:center;justify-content:center;flex-direction:column;background:rgba(0,0,0,.7);color:#fff;text-align:center}
  #pause h1{font-size:42px;margin:0}
  #start{position:absolute;inset:0;display:flex;align-items:center;justify-content:center;flex-direction:column;background:rgba(10,14,22,.92);color:#fff;text-align:center}
  #start h1{font-size:46px;margin:0 0 6px}
  #start p{margin:4px;opacity:.8}
  #start button{margin-top:22px;padding:12px 36px;font-size:18px;cursor:pointer;background:#4caf50;color:#fff;border:0;border-radius:8px}
  #hint{position:absolute;bottom:10px;left:10px;color:#fff;text-shadow:1px 1px 0 #000;font-size:13px;pointer-events:none}
</style>
</head>
<body>
<div id=""wrap"">
  <canvas id=""game"" width=""800"" height=""480""></canvas>
  <div id=""hud"">HP <span id=""hp"">100</span><div class=""bar""><i id=""hpbar"" style=""width:100%""></i></div>Niva <span id=""lvl"">1</span> &middot; Poang <span id=""score"">0</span> &middot; Rekord <span id=""high"">0</span></div>
  <div id=""hint"">Piltangenter / WASD rorelse &middot; Mellanslag / W / Upp = hoppa &middot; Esc / P = paus</div>
  <div id=""over""><h1 id=""title"">Game Over</h1><button onclick=""location.reload()"">Spela igen</button></div>
  <div id=""pause""><h1>Paus</h1><p>Tryck Esc eller P for att fortsatta</p></div>
  <div id=""start""><h1>2D Plattformspel</h1><p>Samla mynt, undvik fiender, na flaggan for att vinna.</p><p>Piltangenter / WASD + Mellanslag for att hoppa. Esc / P pausar.</p><button id=""startBtn"">Starta spelet</button></div>
</div>
<script>
const cv=document.getElementById('game'),ctx=cv.getContext('2d');
const W=cv.width,H=cv.height,G=0.6;
const player={x:40,y:H-60,w:28,h:40,vx:0,vy:0,on:false,hp:100,face:1,frame:0,fps:0};
const keys={};
addEventListener('keydown',e=>{keys[e.key.toLowerCase()]=true;if([' ','arrowup','w'].includes(e.key.toLowerCase()))e.preventDefault();});
addEventListener('keyup',e=>keys[e.key.toLowerCase()]=false);

// 3 hand-authored levels: progression is real (new layouts), not just faster.
const levels=[
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:160,y:H-110,w:120,h:18},{x:340,y:H-170,w:120,h:18},{x:540,y:H-120,w:120,h:18},{x:680,y:H-200,w:120,h:18}],
    coins:[{x:200,y:H-140,r:9},{x:380,y:H-200,r:9},{x:580,y:H-150,r:9},{x:720,y:H-230,r:9}],
    enemies:[{x:400,y:H-58,w:30,h:30,vx:-1.2},{x:620,y:H-138,w:30,h:30,vx:1.2}],
    flag:{x:W-48,y:H-90,w:24,h:70} },
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:120,y:H-90,w:90,h:16},{x:260,y:H-150,w:90,h:16},{x:400,y:H-100,w:90,h:16},{x:540,y:H-180,w:90,h:16},{x:680,y:H-240,w:120,h:16}],
    coins:[{x:160,y:H-120,r:9},{x:300,y:H-180,r:9},{x:440,y:H-130,r:9},{x:580,y:H-210,r:9},{x:730,y:H-270,r:9}],
    enemies:[{x:300,y:H-58,w:30,h:30,vx:1.4},{x:460,y:H-128,w:30,h:30,vx:-1.4},{x:640,y:H-188,w:30,h:30,vx:1.6}],
    flag:{x:W-48,y:H-130,w:24,h:70} },
  { platforms:[{x:0,y:H-20,w:W,h:20},{x:100,y:H-110,w:70,h:14},{x:240,y:H-160,w:70,h:14},{x:380,y:H-120,w:70,h:14},{x:520,y:H-200,w:70,h:14},{x:660,y:H-150,w:70,h:14},{x:740,y:H-250,w:60,h:14}],
    coins:[{x:130,y:H-140,r:9},{x:270,y:H-190,r:9},{x:410,y:H-150,r:9},{x:550,y:H-230,r:9},{x:690,y:H-180,r:9},{x:765,y:H-280,r:9}],
    enemies:[{x:200,y:H-58,w:30,h:30,vx:1.8},{x:360,y:H-58,w:30,h:30,vx:-1.8},{x:520,y:H-138,w:30,h:30,vx:2},{x:700,y:H-88,w:30,h:30,vx:-2.2}],
    flag:{x:W-48,y:H-90,w:24,h:70} }
];
const FINAL_LEVEL=levels.length;
let platforms,coins,enemies,flag;
function loadLevel(n){const L=levels[Math.min(n,FINAL_LEVEL)-1];
  platforms=L.platforms.map(p=>({...p}));
  coins=L.coins.map(c=>({...c,got:false}));
  enemies=L.enemies.map(e=>({...e}));
  flag={...L.flag};}

let score=0,running=false,started=false,paused=false,level=1,animT=0;
let high=+(localStorage.getItem('platformer.high')||0);
document.getElementById('high').textContent=high;
document.getElementById('startBtn').onclick=()=>{document.getElementById('start').style.display='none';loadLevel(level);started=true;running=true;initAudio();};
addEventListener('keydown',e=>{const k=e.key.toLowerCase();
  if((k==='escape'||k==='p')&&started&&document.getElementById('over').style.display!=='flex'){
    paused=!paused;document.getElementById('pause').style.display=paused?'flex':'none';}});

// Web Audio SFX, guarded so a failed/blocked context can never throw in the loop.
let AC=null;const snd={};let audioOK=true;
function initAudio(){if(AC||!audioOK)return;try{AC=new (window.AudioContext||window.webkitAudioContext)();
  const mk=(f,d,type)=>{try{const o=AC.createOscillator(),g=AC.createGain();o.type=type;o.frequency.value=f;o.connect(g);g.connect(AC.destination);g.gain.setValueAtTime(.18,AC.currentTime);g.gain.exponentialRampToValueAtTime(.001,AC.currentTime+d);o.start();o.stop(AC.currentTime+d);}catch(e){}};
  snd.jump=()=>mk(420,.18,'square');snd.coin=()=>mk(880,.15,'triangle');snd.hit=()=>mk(140,.3,'sawtooth');
  snd.win=()=>[523,659,784,1047].forEach((f,i)=>setTimeout(()=>mk(f,.18,'triangle'),i*120));
}catch(e){audioOK=false;AC=null;}}
addEventListener('click',initAudio);addEventListener('keydown',initAudio);

function rects(a,b){return a.x<b.x+b.w&&a.x+a.w>b.x&&a.y<b.y+b.h&&a.y+a.h>b.y;}

function update(){if(!running||!started||paused)return;
  const sp=3.4;
  if(keys['arrowleft']||keys['a']){player.vx=-sp;player.face=-1;}
  else if(keys['arrowright']||keys['d']){player.vx=sp;player.face=1;}
  else player.vx*=0.8;
  if((keys[' ']||keys['w']||keys['arrowup'])&&player.on){player.vy=-11;player.on=false;snd.jump&&snd.jump();}
  player.vy+=G;player.x+=player.vx;player.y+=player.vy;
  player.x=Math.max(0,Math.min(W-player.w,player.x));

  player.on=false;
  for(const p of platforms){if(rects(player,p)){
    const ox=Math.min(player.x+p.w-p.x,p.x+p.w-player.x);
    const oy=Math.min(player.y+p.h-p.y,p.y+p.h-player.y);
    if(ox<oy){player.x+=player.x<p.x?-ox:ox;player.vx=0;}
    else{if(player.y<p.y){player.y=p.y-player.h;player.vy=0;player.on=true;}else{player.y=p.y+p.h;player.vy=0;}}
  }}
  for(const c of coins)if(!c.got&&rects(player,{x:c.x-c.r,y:c.y-c.r,w:c.r*2,h:c.r*2})){c.got=true;score+=10;snd.coin&&snd.coin();}
  for(const e of enemies){e.x+=e.vx;if(e.x<200||e.x>W-60)e.vx*=-1;e.y=platforms.find(p=>e.x>p.x-20&&e.x<p.x+p.w+20)?.y-e.h||e.y;
    if(rects(player,e)&&player.hp>0){player.hp-=20;snd.hit&&snd.hit();player.vy=-7;
      if(player.hp<=0){running=false;end(false);}}}
  if(player.y>H+80){player.hp=0;running=false;end(false);}
  if(rects(player,flag)){running=false;score+=100;end(true);}
  animT+=16;if(animT>120){animT=0;player.frame^=1;}
}
function end(win){const o=document.getElementById('over');o.style.display='flex';
  if(score>high){high=score;try{localStorage.setItem('platformer.high',high);}catch(e){}}
  document.getElementById('high').textContent=high;
  document.getElementById('title').textContent=win?(level>=FINAL_LEVEL?'Du vann spelet! ':'Du vann niva '+level+'! '):'Game Over';
  const btn=o.querySelector('button');
  if(win&&level<FINAL_LEVEL){btn.textContent='Nasta niva';btn.onclick=()=>nextLevel();}
  else{btn.textContent='Spela igen';btn.onclick=()=>location.reload();}
  snd.win&&snd.win();}
function nextLevel(){level++;document.getElementById('lvl').textContent=level;loadLevel(level);
  player.x=40;player.y=H-60;player.vx=0;player.vy=0;player.hp=100;
  const o=document.getElementById('over');o.style.display='none';running=true;}
function draw(){ctx.clearRect(0,0,W,H);
  for(const p of platforms){ctx.fillStyle='#5a3d2b';ctx.fillRect(p.x,p.y,p.w,p.h);ctx.fillStyle='#3fa34d';ctx.fillRect(p.x,p.y,p.w,6);}
  for(const c of coins)if(!c.got){ctx.fillStyle='#ffd23f';ctx.beginPath();ctx.arc(c.x,c.y,c.r,0,7);ctx.fill();ctx.strokeStyle='#b8860b';ctx.stroke();}
  for(const e of enemies){ctx.fillStyle='#c0392b';ctx.fillRect(e.x,e.y,e.w,e.h);ctx.fillStyle='#fff';ctx.fillRect(e.x+6,e.y+8,5,5);ctx.fillRect(e.x+18,e.y+8,5,5);}
  const px=player.x,py=player.y,bob=player.frame?2:0;
  ctx.fillStyle='#2d6cdf';ctx.fillRect(px,py+bob,player.w,player.h-10);
  ctx.fillStyle='#ffd9a0';ctx.fillRect(px+6,py-8+bob,16,14);
  ctx.fillStyle='#1b3b8b';ctx.fillRect(px+(player.face>0?player.w-8:2),py+4+bob,6,4);
  ctx.fillStyle='#15233f';
  if(player.frame){ctx.fillRect(px+4,py+player.h-6,8,6);ctx.fillRect(px+16,py+player.h-2,8,6);}
  else{ctx.fillRect(px+4,py+player.h-2,8,6);ctx.fillRect(px+16,py+player.h-6,8,6);}
  ctx.fillStyle='#444';ctx.fillRect(flag.x,flag.y,4,flag.h);
  ctx.fillStyle='#27ae60';ctx.beginPath();ctx.moveTo(flag.x+4,flag.y);ctx.lineTo(flag.x+28,flag.y+12);ctx.lineTo(flag.x+4,flag.y+24);ctx.fill();
  document.getElementById('hp').textContent=Math.max(0,player.hp);
  document.getElementById('hpbar').style.width=Math.max(0,player.hp)+'%';
  document.getElementById('score').textContent=score;
}
function loop(){try{update();draw();}catch(e){}requestAnimationFrame(loop);}
loop();
</script>
</body>
</html>";
    }
    static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    static void Write(string root, string rel, byte[] content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    static string Sln(string name) =>
        @"Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio Version 17
Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = """ + name + @""", """ + name + @"\" + name + @".csproj"", ""{11111111-1111-1111-1111-111111111111}""
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
EndGlobal
";

    static string Csproj(string name) =>
        @"<Project ToolsVersion=""Current"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
  <PropertyGroup>
    <RootNamespace>" + name + @"</RootNamespace>
    <AssemblyName>" + name + @"</AssemblyName>
    <LangVersion>latest</LangVersion>
    <TargetFramework>netstandard2.1</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include=""**/*.cs"" />
    <Reference Include=""UnityEngine"">
      <HintPath>UnityEngine.dll</HintPath>
    </Reference>
  </ItemGroup>
</Project>
";

    static string UnityPlayerController() => @"
using UnityEngine;

/// <summary>A ready-to-play 2D platformer character: gravity, run, jump,
/// landing detection, screen flip, coin pickup (score), enemy contact
/// (damage), and goal reach (level clear). Drop on a Sprite with a
/// Rigidbody2D + BoxCollider2D (the generated scene does exactly that).</summary>
[RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
public class PlayerController : MonoBehaviour
{
    [Header(""Movement"")]
    public float moveSpeed = 6f;
    public float jumpForce = 14f;
    public LayerMask groundLayer;

    [Header(""Audio"")]
    public AudioClip jumpSfx;
    public AudioClip coinSfx;
    public AudioClip hurtSfx;

    private Rigidbody2D _rb;
    private bool _grounded;
    private AudioSource _audio;

    void Awake()
    {
        _rb = GetComponent<Rigidbody2D>();
        _audio = GetComponent<AudioSource>();
        if (_audio == null) _audio = gameObject.AddComponent<AudioSource>();
    }

    void Update()
    {
        float x = Input.GetAxisRaw(""Horizontal"");
        _rb.linearVelocity = new Vector2(x * moveSpeed, _rb.linearVelocity.y);
        if (x != 0) transform.localScale = new Vector3(Mathf.Sign(x), 1, 1);

        if ((Input.GetButtonDown(""Jump"") || Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow))
            && _grounded)
        {
            _rb.linearVelocity = new Vector2(_rb.linearVelocity.x, jumpForce);
            if (jumpSfx && _audio) _audio.PlayOneShot(jumpSfx);
        }
    }

    void FixedUpdate()
    {
        var b = GetComponent<BoxCollider2D>();
        var origin = (Vector2)transform.position + Vector2.down * (b.bounds.extents.y + 0.05f);
        _grounded = Physics2D.Raycast(origin, Vector2.down, 0.1f, groundLayer);
    }

    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(""Coin""))
        {
            if (coinSfx && _audio) _audio.PlayOneShot(coinSfx);
            GameManager.Instance?.Collect(other.gameObject);
        }
        else if (other.CompareTag(""Enemy""))
        {
            if (hurtSfx && _audio) _audio.PlayOneShot(hurtSfx);
            GameManager.Instance?.Damage(1);
        }
        else if (other.CompareTag(""Goal""))
        {
            GameManager.Instance?.LevelCleared();
        }
    }
}
";

    static string UnityEnemy() => @"
using UnityEngine;

/// <summary>Patrols left/right between its start and start+patrolRange and
/// damages the player on contact (it is tagged ""Enemy"").</summary>
public class Enemy : MonoBehaviour
{
    [Header(""Patrol"")]
    public float patrolRange = 3f;
    public float speed = 2.5f;

    private float _minX;
    private int _dir = 1;

    void Awake() => _minX = transform.position.x;

    void Update()
    {
        if (transform.position.x <= _minX || transform.position.x >= _minX + patrolRange) _dir *= -1;
        transform.position += Vector3.right * (_dir * speed * Time.deltaTime);
        transform.localScale = new Vector3(_dir, 1, 1);
    }
}
";

    static string UnityCoin() => @"
using UnityEngine;

/// <summary>Collectible. On player contact it tells GameManager to add score
/// and removes itself. Tagged ""Coin"" so PlayerController recognizes it.</summary>
public class Coin : MonoBehaviour
{
    void OnTriggerEnter2D(Collider2D other)
    {
        if (other.CompareTag(""Player""))
            GameManager.Instance?.Collect(gameObject);
    }
}
";

    static string UnityGameManager() => @"
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>Score + HP + 3-level progression + win/lose + pause. Holds the
/// run state; the generated scene wires Coins, Enemies, a Goal and a UI that
/// UIManager updates from these values.</summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header(""Gameplay"")]
    public int totalLevels = 3;
    public int startHp = 5;
    public int coinsToWin = 3;

    public int Score { get; private set; }
    public int Hp { get; private set; }
    public int Level { get; private set; } = 1;

    // Survive the scene reload LevelCleared performs: instance fields reset
    // with every reload, so without these the game restarted at level 1 with
    // 0 points after every cleared level.
    private static int s_score;
    private static int s_level = 1;
    private static bool s_startedOnce;

    private Text _hud;
    private bool _paused;
    private bool _started;
    private bool _ended;
    private int _high;

    void Awake()
    {
        Instance = this;
        Hp = startHp;
        Score = s_score;
        Level = s_level;
        _high = PlayerPrefs.GetInt(""highscore"", 0);
        _hud = GameObject.Find(""UIManager"")?.GetComponent<UIManager>()?.HudText;
        if (s_startedOnce)
        {
            // Mid-run reload (next level): skip the title screen.
            _started = true;
            Time.timeScale = 1;
            UpdateHud();
        }
        else
        {
            // Title screen: frozen until the player presses Enter/Space.
            Time.timeScale = 0;
            if (_hud) _hud.text = $""PIXEL RUSH\nSamla mynt, undvik fiender, na malet.\nRekord: {_high}\n\nTryck ENTER eller SPACE for att starta"";
        }
    }

    void Update()
    {
        if (!_started)
        {
            if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            {
                _started = true;
                s_startedOnce = true;
                Time.timeScale = 1;
                UpdateHud();
            }
            return;
        }
        if (_ended)
        {
            if (Input.GetKeyDown(KeyCode.R))
            {
                s_score = 0; s_level = 1; s_startedOnce = false;
                Time.timeScale = 1;
                SceneManager.LoadScene(SceneManager.GetActiveScene().name);
            }
            return;
        }
        if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.P)) TogglePause();
    }

    public void Collect(GameObject coin)
    {
        Score += 10;
        Destroy(coin);
        UpdateHud();
        if (Score / 10 >= coinsToWin) LevelCleared();
    }

    public void Damage(int amount)
    {
        Hp -= amount;
        UpdateHud();
        if (Hp <= 0) GameOver();
    }

    public void LevelCleared()
    {
        if (Level >= totalLevels) { Win(); return; }
        s_level = Level + 1;
        s_score = Score;
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    private void Win()
    {
        _ended = true;
        Time.timeScale = 0;
        SaveHighscore();
        if (_hud) _hud.text = $""DU VANN! Poang: {Score} - Rekord: {_high}\nTryck R for att spela igen"";
        Debug.Log(""You win!"");
    }

    private void GameOver()
    {
        _ended = true;
        Time.timeScale = 0;
        SaveHighscore();
        if (_hud) _hud.text = $""GAME OVER - Poang: {Score} - Rekord: {_high}\nTryck R for att spela igen"";
        Debug.Log(""Game over."");
    }

    private void SaveHighscore()
    {
        if (Score <= _high) return;
        _high = Score;
        PlayerPrefs.SetInt(""highscore"", _high);
        PlayerPrefs.Save();
    }

    private void TogglePause()
    {
        _paused = !_paused;
        Time.timeScale = _paused ? 0 : 1;
    }

    private void UpdateHud()
    {
        if (_hud) _hud.text = $""Niva {Level}/{totalLevels}   Poang {Score}   HP {Hp}   Rekord {_high}"";
    }
}
";

    static string UnityUIManager() => @"
using UnityEngine;
using UnityEngine.UI;

/// <summary>Screen HUD: a single Text showing score / hp / level, updated by
/// GameManager. Place on a Canvas with a Text child named ""Hud"".</summary>
public class UIManager : MonoBehaviour
{
    public Text HudText;

    void Awake()
    {
        if (HudText == null) HudText = GetComponentInChildren<Text>();
    }
}
";

    static string UnityScene(string sceneGuid)
    {
        var ground = "a1b2c3d400000000000000000000001";
        var player = "a1b2c3d400000000000000000000002";
        var cam = "a1b2c3d400000000000000000000003";
        var light = "a1b2c3d400000000000000000000004";
        var coin1 = "a1b2c3d400000000000000000000005";
        var coin2 = "a1b2c3d400000000000000000000006";
        var coin3 = "a1b2c3d400000000000000000000007";
        var enemy1 = "a1b2c3d400000000000000000000008";
        var goal = "a1b2c3d400000000000000000000009";
        var canvas = "a1b2c3d40000000000000000000000a";
        var hud = "a1b2c3d40000000000000000000000b";
        return $@"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!29 &1
OcclusionCullingSettings:
  m_ObjectHideFlags: 0
  serializedVersion: 2
--- !u!1 &{ground}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Ground
  m_IsActive: 1
--- !u!61 &{ground}0
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {ground}}}
  m_Size: {{x: 20, y: 1}}
--- !u!1 &{player}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Player
  m_IsActive: 1
  m_TagString: Player
  m_Component:
  - component: {{fileID: {player}p}}
  - component: {{fileID: {player}r}}
  - component: {{fileID: {player}c}}
  - component: {{fileID: {player}a}}
  - component: {{fileID: {player}s}}
--- !u!212 &{player}p
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Color: {{r: 0.18, g: 0.42, b: 0.87, a: 1}}
--- !u!50 &{player}r
Rigidbody2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_BodyType: 0
  m_GravityScale: 2
--- !u!61 &{player}c
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Size: {{x: 1, y: 1.6}}
--- !u!82 &{player}a
AudioSource:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_PlayOnAwake: 0
--- !u!114 &{player}s
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {player}}}
  m_Name: PlayerController
--- !u!1 &{cam}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Main Camera
  m_IsActive: 1
--- !u!20 &{cam}c
Camera:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {cam}}}
  m_BackGroundColor: {{r: 0.53, g: 0.81, b: 0.99, a: 0}}
  m_projection: 1
  m_Size: 5
--- !u!1 &{light}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Directional Light
  m_IsActive: 1
--- !u!108 &{light}l
Light:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {light}}}
  m_Color: {{r: 1, g: 1, b: 1, a: 1}}
--- !u!1 &{coin1}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin1}c}}
  - component: {{fileID: {coin1}s}}
--- !u!61 &{coin1}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin1}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin1}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin1}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{coin2}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin2}c}}
  - component: {{fileID: {coin2}s}}
--- !u!61 &{coin2}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin2}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin2}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin2}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{coin3}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Coin
  m_IsActive: 1
  m_TagString: Coin
  m_Component:
  - component: {{fileID: {coin3}c}}
  - component: {{fileID: {coin3}s}}
--- !u!61 &{coin3}c
CircleCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin3}}}
  m_IsTrigger: 1
  m_Radius: 0.4
--- !u!212 &{coin3}s
SpriteRenderer:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {coin3}}}
  m_Color: {{r: 1, g: 0.82, b: 0.25, a: 1}}
--- !u!1 &{enemy1}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Enemy
  m_IsActive: 1
  m_TagString: Enemy
  m_Component:
  - component: {{fileID: {enemy1}c}}
  - component: {{fileID: {enemy1}s}}
--- !u!61 &{enemy1}c
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {enemy1}}}
  m_IsTrigger: 1
  m_Size: {{x: 1, y: 1}}
--- !u!114 &{enemy1}s
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {enemy1}}}
  m_Name: Enemy
--- !u!1 &{goal}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Goal
  m_IsActive: 1
  m_TagString: Goal
  m_Component:
  - component: {{fileID: {goal}c}}
--- !u!61 &{goal}c
BoxCollider2D:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {goal}}}
  m_IsTrigger: 1
  m_Size: {{x: 1, y: 3}}
--- !u!1 &{canvas}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: UIManager
  m_IsActive: 1
  m_Component:
  - component: {{fileID: {canvas}c}}
  - component: {{fileID: {canvas}h}}
--- !u!223 &{canvas}c
Canvas:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {canvas}}}
  m_RenderMode: 0
--- !u!114 &{canvas}h
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {canvas}}}
  m_Name: UIManager
--- !u!1 &{hud}
GameObject:
  m_ObjectHideFlags: 0
  m_Name: Hud
  m_IsActive: 1
  m_Parent: {{fileID: {canvas}}}
  m_Component:
  - component: {{fileID: {hud}t}}
--- !u!114 &{hud}t
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_GameObject: {{fileID: {hud}}}
  m_Name: Hud
  m_Text: Pixel Rush
  m_FontData:
    m_FontSize: 24
";
    }

    /// <summary>Windows Desktop export preset for Godot 4 - a standalone
    /// export_presets.cfg so `godot --export-release "Windows Desktop"` actually
    /// finds the preset (the project.godot [export] form is Godot 3 and is
    /// ignored by Godot 4, which would make the headless build error).</summary>
    static string GodotExportPresets() =>
        "[preset.0]\n" +
        "name=\"Windows Desktop\"\n" +
        "platform=\"Windows Desktop\"\n" +
        "runnable=true\n" +
        "advanced_options=false\n" +
        "dedicated_server=false\n" +
        "custom_features=\"\"\n" +
        "export_filter=\"all_resources\"\n" +
        "include_filter=\"\"\n" +
        "exclude_filter=\"\"\n" +
        "export_path=\"build/PixelRush.exe\"\n" +
        "patches=\"\"\n" +
        "encryption_include_filters=\"\"\n" +
        "encryption_exclude_filters=\"\"\n" +
        "seed=0\n" +
        "encrypt_pck=false\n" +
        "encrypt_directory=false\n" +
        "\n[preset.0.options]\n" +
        "custom_template/debug=\"\"\n" +
        "custom_template/release=\"\"\n" +
        "variant/extensions_support=false\n" +
        "variant/threads_support=true\n" +
        "architectures/architecture=\"x86_64\"\n" +
        "binary_format/embed_prefix=\"\"\n" +
        "binary_format/embed_suffix=\"\"\n" +
        "binary_format/embed_pck=false\n" +
        "codesign/enable=false\n" +
        "codesign/timestamp=true\n" +
        "codesign/timestamp_server_url=\"\"\n" +
        "codesign/digest_algorithm=\"SHA256\"\n" +
        "codesign/description=\"\"\n" +
        "application/modify_resources=false\n" +
        "application/icon=\"res://icon.ico\"\n" +
        "application/console_wrapper=2\n" +
        "application/embedding=PackedStringArray(\"\")\n" +
        "application/file_version=\"\"\n" +
        "application/product_version=\"\"\n" +
        "application/company_name=\"\"\n" +
        "application/product_name=\"Pixel Rush\"\n" +
        "application/file_description=\"\"\n" +
        "application/copyright=\"\"\n" +
        "application/trademarks=\"\"\n" +
        // Web-preset (HTML5/WASM): sa `godot --export-release "Web"` ger ett
        // webbspelbart bygge nar web-exportmallen finns provisionerad.
        "\n[preset.1]\n" +
        "name=\"Web\"\n" +
        "platform=\"Web\"\n" +
        "runnable=true\n" +
        "advanced_options=false\n" +
        "dedicated_server=false\n" +
        "custom_features=\"\"\n" +
        "export_filter=\"all_resources\"\n" +
        "include_filter=\"\"\n" +
        "exclude_filter=\"\"\n" +
        "export_path=\"build/web/index.html\"\n" +
        "patches=\"\"\n" +
        "encryption_include_filters=\"\"\n" +
        "encryption_exclude_filters=\"\"\n" +
        "seed=0\n" +
        "encrypt_pck=false\n" +
        "encrypt_directory=false\n" +
        "\n[preset.1.options]\n" +
        "custom_template/debug=\"\"\n" +
        "custom_template/release=\"\"\n" +
        "variant/extensions_support=false\n" +
        // v2.5: threads AV - traddade webbyggen kraver COOP/COEP-headers
        // (crossOriginIsolated) och blir svartruta i en vanlig iframe.
        // Utan trador funkar demon direkt i studioflikens live-vy.
        "variant/thread_support=false\n" +
        "vram_texture_compression/for_desktop=true\n" +
        "vram_texture_compression/for_mobile=false\n" +
        "html/export_icon=true\n" +
        "html/custom_html_shell=\"\"\n" +
        "html/head_include=\"\"\n" +
        "html/canvas_resize_policy=2\n" +
        "html/focus_canvas_on_start=true\n" +
        "html/experimental_virtual_keyboard=false\n" +
        "progressive_web_app/enabled=false\n";

}
