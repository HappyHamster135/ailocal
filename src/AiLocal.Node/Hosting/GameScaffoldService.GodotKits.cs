namespace AiLocal.Node.Hosting;

/// <summary>
/// Godot genre kits in PURE GDScript (no mono/C# - models write far better
/// GDScript than Godot-C#, no csproj needed, and headless parses .gd
/// directly). Same philosophy as the 16 HTML5 kits: a COMPLETE, playable
/// game at the production bar (title screen, difficulty, sound, save/load,
/// pause, win/lose) that the agent RE-THEMES and extends - not a stub. UI is
/// built programmatically in _ready so the .tscn files stay tiny and the
/// static res:// check never has scene-reference drift to trip on.
/// </summary>
public partial class GameScaffoldService
{
    /// <summary>Management/tycoon: run an organization (team/studio/shop)
    /// from worst to best - budget, roster, market, simulated rounds against
    /// a league, three difficulties, save/load. The kit the football-manager
    /// and gamedev-tycoon prompts have been missing.</summary>
    internal static string[] ScaffoldGodotManagement(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Club Manager"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Control"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotManagementMain);
        files.Add("Main.gd");
        foreach (var (name, category) in new[] { ("click.wav", "jump"), ("coin.wav", "coin"), ("hurt.wav", "hurt"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotManagementDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Club Manager - Management/Tycoon (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbar managementgrund: budget, trupp (8 medlemmar), marknad,\n" +
            "simulerade omgangar mot en liga pa 6 lag, tre svarighetsgrader och\n" +
            "spara/ladda. Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "Allt UI byggs i kod i Main.gd - byt TEMA (lag/studio/butik) genom att\n" +
            "andra texter, namnlistor och siffror dar.\n");
        files.Add("README.md");
        return [.. files];
    }

    /// <summary>Top-down action/RPG: 8-way movement, chasing enemies, pickups,
    /// waves, HP, difficulty, highscore - the base for zelda-likes, arena
    /// games and survival prompts.</summary>
    internal static string[] ScaffoldGodotTopDown(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("The Glade"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotTopDownMain);
        files.Add("Main.gd");
        foreach (var (name, category) in new[] { ("click.wav", "jump"), ("coin.wav", "coin"), ("hurt.wav", "hurt"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        // Animerade karaktarer: flerbilds-sprites (gangcykel + idle) -> Godot
        // SpriteFrames, sa spelaren och fienderna GAR pa riktigt i stallet for
        // statiska fyrkanter (PixelAnimator + GodotSpriteFrames, deterministiskt).
        var playerSheet = PixelAnimator.Build(prompt);
        Write(root, "player.png", playerSheet.Png);
        Write(root, "player_frames.tres", GodotSpriteFrames.Build("player.png", playerSheet));
        files.Add("player.png"); files.Add("player_frames.tres");
        var enemySheet = PixelAnimator.Build(prompt + " fiende monster");
        Write(root, "enemy.png", enemySheet.Png);
        Write(root, "enemy_frames.tres", GodotSpriteFrames.Build("enemy.png", enemySheet));
        files.Add("enemy.png"); files.Add("enemy_frames.tres");
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotTopDownDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# The Glade - Top-down action (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbar top-down-grund: 8-vagars rorelse, jagande fiender,\n" +
            "mynt, 5 vagor, HP, tre svarighetsgrader, paus och highscore.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "Sprites genereras i kod (inga externa bilder) - byt tema via\n" +
            "farger/former i Main.gd.\n");
        files.Add("README.md");
        return [.. files];
    }

    /// <summary>Plattformaren (Pixel Rush) i ren GDScript - porterad fran det
    /// gamla C#/mono-kittet (v1.85) sa den kan headless-verifieras och fa
    /// juice-passet som de andra kiten: gravitation + coyotetid + hoppbuffert,
    /// 3 nivaer, mynt, patrullerande fiender med stamp, mal-flagga, HP,
    /// svarighetsgrader, highscore, screenshake och partiklar.</summary>
    internal static string[] ScaffoldGodotPlatformer(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Pixel Rush"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotPlatformerMain);
        files.Add("Main.gd");
        // click.wav ar hoppljudet (sfxr-kategorin "jump") - samma filnamn som
        // ovriga kit sa kvalitetskontrakten (click/coin/hurt/win) haller.
        foreach (var (name, category) in new[] { ("click.wav", "jump"), ("coin.wav", "coin"), ("hurt.wav", "hurt"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        var playerSheet = PixelAnimator.Build(prompt);
        Write(root, "player.png", playerSheet.Png);
        Write(root, "player_frames.tres", GodotSpriteFrames.Build("player.png", playerSheet));
        files.Add("player.png"); files.Add("player_frames.tres");
        var enemySheet = PixelAnimator.Build(prompt + " fiende monster");
        Write(root, "enemy.png", enemySheet.Png);
        Write(root, "enemy_frames.tres", GodotSpriteFrames.Build("enemy.png", enemySheet));
        files.Add("enemy.png"); files.Add("enemy_frames.tres");
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotPlatformerDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Pixel Rush - 2D Platformer (Godot 4, GDScript)\n\n" +
            "Komplett spelbar plattformare: 3 nivaer, mynt, patrullerande fiender\n" +
            "(hoppa pa dem!), mal-flagga, HP, tre svarighetsgrader, paus och highscore.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "Styrning: Pilar/A-D for rorelse, Space/W/Upp for att hoppa, Esc for paus,\n" +
            "R for omstart efter vinst/forlust.\n\n" +
            "Sprites genereras i kod (inga externa bilder) - byt tema via\n" +
            "farger/former i Main.gd.\n");
        files.Add("README.md");
        return [.. files];
    }

    /// <summary>Artilleri/duell (v1.98, ShellShock Live/Worms-klassen): tur-
    /// baserad ballistik mot AI-motstandare pa FORSTORBAR terrang - vind,
    /// kratrar, tre vapen, motstandarstege med stigande traffsakerhet.
    /// Fyller den storsta formluckan: alla tidigare kit var ensamspelare-
    /// progression; detta ar versus-golvet.</summary>
    internal static string[] ScaffoldGodotArtillery(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Cannonade"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotArtilleryMain);
        files.Add("Main.gd");
        // v2.27: scenisk bakgrund (PixelBackdrop) bakom den forstorbara
        // terrangen - temat foljer prompten (meadow som default).
        Write(root, "background.png", PixelBackdrop.Build(prompt, 288, 162));
        files.Add("background.png");
        foreach (var (name, category) in new[] { ("click.wav", "shoot"), ("coin.wav", "powerup"), ("hurt.wav", "explosion"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotArtilleryDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Cannonade - Artilleriduell (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbar artilleriduell i ShellShock Live/Worms-klassen:\n" +
            "turbaserad ballistik mot AI, FORSTORBAR terrang med kratrar, vind,\n" +
            "tre vapen med ammunition, motstandarstege med stigande traffsakerhet,\n" +
            "fallskada, highscore (segersvit), paus och omstart.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n\n" +
            "Styrning: Vanster/Hoger vinkel, Upp/Ned kraft, Space eldar,\n" +
            "1-3 byter vapen, Esc pausar. Grafiken ritas i kod - byt tema via\n" +
            "farger/varden i Main.gd.\n");
        files.Add("README.md");
        return [.. files];
    }

    /// <summary>Racing: top-down varvracer med bilfysik, oval bana, checkpoints
    /// i ordning, varv, varvtimer och basta tid. Fyller genreluckan dar racing-
    /// prompts tidigare fick plattformaren.</summary>
    internal static string[] ScaffoldGodotRacing(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("The Circuit"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotRacingMain);
        files.Add("Main.gd");
        foreach (var (name, category) in new[] { ("click.wav", "select"), ("coin.wav", "coin"), ("hurt.wav", "hurt"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotRacingDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# The Circuit - Top-down racing (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbar varvracer: bilfysik (gas/broms/styr), oval bana med\n" +
            "gras som bromsar, checkpoints i ordning, 3 varv, varvtimer och basta tid.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "Banan och bilen ritas i kod (_draw) - byt tema via farger/former i Main.gd.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotRacingDesignDoc(string prompt) =>
        "# Top-down racing (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nKor bilen runt banan och klara 3 varv pa basta tid. Passera checkpoints i\n" +
        "ordning (den glodande markoren visar nasta), hall dig pa asfalten - graset bromsar.\n\n" +
        "## Mekaniker (klara)\n- Bilfysik: gas/broms/styr med friktion, tre svarighetsgrader (toppfart)\n" +
        "- Oval bana med on-track-test (mellan yttre och inre ellips)\n- Checkpoints i ordning + varvrakning + varvtimer\n" +
        "- Basta tid sparas (user://), titel/vinst-overlay, ljud\n\n## Bygg vidare\n- AI-motstandare, fler banor, power-ups, drift/boost, minimap\n";

    // Ren GDScript, 4-space indentering (Godot 4.3 tar tabs ELLER spaces om
    // konsekvent). Toppniva pa kolumn 0, stangande """ pa kolumn 0 => raw-strangen
    // stripar inget och GDScript-indenteringen bevaras exakt.
    const string GodotRacingMain = """
extends Node2D
# The Circuit - top-down varvracer. Kor bilen runt banan pa basta tid.
# UI byggs i kod (_show_title/_finish), banan + bilen ritas i _draw. BYT TEMA:
# farger/former i _draw och texterna i _show_title. Spelartext pa ENGELSKA.

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://circuit_best.txt"
const LAPS := 3

var C := Vector2(576, 324)
var OUT := Vector2(470, 260)
var INN := Vector2(240, 120)

var state := "title"
var difficulty := 1
var car := Vector2.ZERO
var heading := 0.0
var vel := 0.0
var lap := 0
var checkpoint := 0
var t := 0.0
var best := 0.0
var checkpoints: Array[Vector2] = []
var snd := {}
var ui: CanvasLayer
var shake := 0.0  # C1 juice: screenshake-magnitud (px), avtar mot 0
var dot_tex: ImageTexture  # C1 juice: liten vit partikeltextur (utan blir de osynliga)
var car_flash := 0.0  # C1 juice: bilens egen vita blixt vid checkpoint (avtar)
var focus_pending := true

func _ready() -> void:
    randomize()
    Shell.startup()
    _build_checkpoints()
    _setup_audio()
    best = _load_best()
    var img := Image.create(6, 6, false, Image.FORMAT_RGBA8)
    img.fill(Color(1, 1, 1))
    dot_tex = ImageTexture.create_from_image(img)
    ui = CanvasLayer.new()
    add_child(ui)
    _setup_touch()
    _show_title()

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
    if not DisplayServer.is_touchscreen_available():
        return
    _touch_btn(Vector2(28, 536), "ui_left", "<")
    _touch_btn(Vector2(132, 536), "ui_right", ">")
    _touch_btn(Vector2(1036, 444), "ui_up", "GAS")
    _touch_btn(Vector2(1036, 536), "ui_down", "BRAKE")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
    var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
    img2.fill(Color(1, 1, 1, 0.20))
    for x in range(88):
        for y in range(88):
            if x < 3 or y < 3 or x > 84 or y > 84:
                img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
    var b := TouchScreenButton.new()
    b.texture_normal = ImageTexture.create_from_image(img2)
    b.position = pos
    if action != "":
        b.action = action
    ui.add_child(b)
    var t := Label.new()
    t.text = text
    t.add_theme_font_size_override("font_size", 24)
    t.position = pos
    t.size = Vector2(88, 88)
    t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    t.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(t)
    return b

# C1 juice: en engangs-partikelskur. UI ligger pa CanvasLayer och paverkas inte
# av att varldens Node2D (self) skakas.
func _burst(pos: Vector2, col: Color, count: int) -> void:
    var p := CPUParticles2D.new()
    p.position = pos
    p.amount = count
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.5
    p.spread = 180.0
    p.initial_velocity_min = 60.0
    p.initial_velocity_max = 160.0
    p.gravity = Vector2(0, 160)
    p.scale_amount_min = 2.0
    p.scale_amount_max = 4.0
    p.color = col
    p.texture = dot_tex
    add_child(p)
    p.emitting = true
    get_tree().create_timer(1.0).timeout.connect(p.queue_free)

func _build_checkpoints() -> void:
    var m := (OUT + INN) * 0.5
    checkpoints = [
        C + Vector2(0, m.y),
        C + Vector2(m.x, 0),
        C + Vector2(0, -m.y),
        C + Vector2(-m.x, 0),
    ]

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win"]:
        var p := AudioStreamPlayer.new()
        var s = load("res://%s.wav" % key)
        if s:
            p.stream = s
        add_child(p)
        snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune-slinga, lag volym sa
    # effekterna hors. finished->play = loop utan importinstallningar.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true
    _last_button = null

func _label(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

var _last_button: Button = null   # v2.22: fokuskedja for pilnavigering

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    # v2.22: fristaende knappar pa en CanvasLayer far inte palitliga
    # automatiska fokusgrannar - lanka pil-upp/ner-kedjan explicit.
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    # Forsta knappen pa varje skarm far fokus -> Enter/Space fungerar direkt
    # (tangentbordsspelbart, och kvalitetsgrindens sond kommer forbi titeln).
    if focus_pending:
        focus_pending = false
        b.grab_focus()

func _show_title() -> void:
    state = "title"
    position = Vector2.ZERO  # C1 juice: snappa tillbaka varlden
    shake = 0.0
    _clear_ui()
    _label("THE CIRCUIT", 80, 72, Color(1, 0.85, 0.2))
    _label("Race %d laps as fast as you can - arrow keys: gas/brake/steer." % LAPS, 180, 22)
    _label("Best time: %s" % ("-" if best <= 0.0 else "%.2f s" % best), 216, 22, Color(0.7, 0.9, 1))
    var names := ["Easy", "Normal", "Hard"]
    for i in range(3):
        var d := i
        _button(names[i], 290 + i * 58, func(): _start(d))
    # v2.22 spelskalet: Options + Quit pa titeln.
    _button("Options", 470, func(): _show_options())
    _button("Quit", 528, func(): get_tree().quit())
    queue_redraw()

func _show_options() -> void:
    _play("click")
    _clear_ui()
    Shell.options_panel(ui, func(): _show_title())

func _start(d: int) -> void:
    difficulty = d
    _play("click")
    lap = 0
    checkpoint = 0
    t = 0.0
    vel = 0.0
    car = checkpoints[0]
    heading = (checkpoints[1] - checkpoints[0]).angle()
    _clear_ui()
    var hud := _label("", 12, 24)
    hud.name = "Hud"
    state = "playing"
    queue_redraw()

func _on_track(p: Vector2) -> bool:
    var o := Vector2((p.x - C.x) / OUT.x, (p.y - C.y) / OUT.y)
    var i := Vector2((p.x - C.x) / INN.x, (p.y - C.y) / INN.y)
    return o.length_squared() <= 1.0 and i.length_squared() >= 1.0

func _physics_process(delta: float) -> void:
    if state != "playing":
        return
    # C1 juice: screenshake genom att flytta varldens Node2D en avtagande offset.
    if shake > 0.0:
        shake = move_toward(shake, 0.0, 30.0 * delta)
        position = Vector2(randf_range(-shake, shake), randf_range(-shake, shake))
    elif position != Vector2.ZERO:
        position = Vector2.ZERO
    if car_flash > 0.0:
        car_flash = move_toward(car_flash, 0.0, delta)
    t += delta
    var accel := Input.get_action_strength("ui_up") - Input.get_action_strength("ui_down")
    var steer := Input.get_action_strength("ui_right") - Input.get_action_strength("ui_left")
    var top: float = [260.0, 320.0, 380.0][difficulty]
    vel = clamp(vel + accel * 300.0 * delta, -120.0, top)
    vel = move_toward(vel, 0.0, 90.0 * delta)
    if abs(vel) > 5.0:
        heading += steer * 2.4 * delta * signf(vel)
    var dir := Vector2(cos(heading), sin(heading))
    var nxt := car + dir * vel * delta
    if not _on_track(nxt):
        vel = move_toward(vel, 0.0, 700.0 * delta)
        nxt = car + dir * vel * delta
        if abs(vel) > 20.0:
            shake = max(shake, 2.5)  # C1 juice: skrammel nar man kor av banan
    car = nxt
    _progress()
    var hud := ui.get_node_or_null("Hud")
    if hud:
        hud.text = "Lap %d/%d   Time %.2f s" % [min(lap + 1, LAPS), LAPS, t]
    queue_redraw()

func _progress() -> void:
    var target := checkpoints[(checkpoint + 1) % 4]
    if car.distance_to(target) < 95.0:
        checkpoint = (checkpoint + 1) % 4
        _play("coin")
        _burst(target, Color(1, 0.85, 0.2), 14)  # C1 juice: bekraftelse-skur
        shake = max(shake, 4.0)
        car_flash = 0.25  # C1 juice: bilen blinkar vitt
        if checkpoint == 0:
            lap += 1
            if lap >= LAPS:
                _finish()

func _finish() -> void:
    state = "over"
    position = Vector2.ZERO  # C1 juice: snappa tillbaka varlden
    shake = 0.0
    _play("win")
    # C1 juice: malskuren firas har - shake i _progress hann nollstallas av
    # denna funktion samma bildruta, sa den storsta stunden fick ingen effekt.
    _burst(checkpoints[0], Color(1, 0.9, 0.3), 40)
    var rec := best <= 0.0 or t < best
    if rec:
        best = t
        _save_best(t)
    _clear_ui()
    _label("FINISH!", 200, 60, Color(1, 0.9, 0.3))
    _label("Time: %.2f s%s" % [t, "   NEW RECORD!" if rec else "   Best: %.2f s" % best], 280, 26)
    _button("Play again", 340, func(): _show_title())

func _draw() -> void:
    draw_rect(Rect2(Vector2.ZERO, Vector2(1152, 648)), Color(0.12, 0.4, 0.16))
    _ellipse(C, OUT, Color(0.22, 0.22, 0.26))
    _ellipse(C, INN, Color(0.12, 0.4, 0.16))
    var s := checkpoints[0]
    draw_line(s + Vector2(-42, 0), s + Vector2(42, 0), Color.WHITE, 4)
    if state == "playing":
        var target := checkpoints[(checkpoint + 1) % 4]
        draw_circle(target, 15, Color(1, 0.9, 0.2, 0.5))
        var fwd := Vector2(cos(heading), sin(heading))
        var side := fwd.rotated(PI * 0.5)
        var pts := PackedVector2Array([car + fwd * 16, car + side * 9 - fwd * 12, car - side * 9 - fwd * 12])
        draw_colored_polygon(pts, Color(0.9, 0.2, 0.2).lerp(Color.WHITE, car_flash / 0.25))  # C1 juice: checkpoint-blixt
        draw_circle(car, 4, Color(1, 1, 0.85))

func _ellipse(c: Vector2, r: Vector2, col: Color) -> void:
    var pts := PackedVector2Array()
    for i in range(48):
        var a := TAU * i / 48.0
        pts.append(c + Vector2(cos(a) * r.x, sin(a) * r.y))
    draw_colored_polygon(pts, col)

func _load_best() -> float:
    if not FileAccess.file_exists(SAVE_PATH):
        return 0.0
    var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
    return float(f.get_as_text()) if f else 0.0

func _save_best(v: float) -> void:
    var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
    if f:
        f.store_string(str(v))
""";

    /// <summary>Pussel: slajd-pussel (2048) - slajda med piltangenter, sla ihop
    /// lika brickor, na malet. Fyller genreluckan dar pussel-prompts tidigare
    /// fick plattformaren.</summary>
    internal static string[] ScaffoldGodotPuzzle(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Twenty48"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotPuzzleMain);
        files.Add("Main.gd");
        foreach (var (name, category) in new[] { ("click.wav", "select"), ("coin.wav", "coin"), ("hurt.wav", "lose"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotPuzzleDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Twenty48 - Slajd-pussel (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbart slajd-pussel (2048): slajda med piltangenterna, sla\n" +
            "ihop lika brickor, na 2048. Poang, basta resultat, vinst/forlust.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "Rutnatet ritas i kod (_draw) - byt tema via farger i _tile_color och mal i TARGET.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotPuzzleDesignDoc(string prompt) =>
        "# Slajd-pussel (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nSlajda rutnatet med piltangenterna. Nar tva lika brickor krockar slas de ihop\n" +
        "till dubbla vardet. Na 2048 for att vinna; ta slut pa drag = forlust.\n\n" +
        "## Mekaniker (klara)\n- 4x4 rutnat, slajda i fyra riktningar (compress + merge)\n" +
        "- Ny bricka (2/4) spawnar efter varje drag\n- Poang, basta resultat sparas (user://)\n" +
        "- Vinst vid 2048, forlust nar inga drag finns kvar, titel/overlay, ljud\n\n" +
        "## Bygg vidare\n- Storre rutnat/andra mal, undo, animation pa forflyttning, dagliga utmaningar\n";

    const string GodotPuzzleMain = """
extends Node2D
# Twenty48 - slajd-pussel (2048): slajda med piltangenter, sla ihop lika brickor,
# na 2048. Rutnat + UI ritas i kod. BYT TEMA: farger i _tile_color, mal i TARGET.
# Spelartext pa ENGELSKA (husregeln).

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const N := 4
const SAVE_PATH := "user://twenty48_best.txt"
const TARGET := 2048

var grid: Array[int] = []
var state := "title"
var score := 0
var best := 0
var snd := {}
var ui: CanvasLayer
var focus_pending := true

func _ready() -> void:
    randomize()
    Shell.startup()
    _setup_audio()
    best = _load_best()
    ui = CanvasLayer.new()
    add_child(ui)
    _setup_touch()
    _show_title()

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
    if not DisplayServer.is_touchscreen_available():
        return
    _touch_btn(Vector2(28, 536), "ui_left", "<")
    _touch_btn(Vector2(132, 536), "ui_right", ">")
    _touch_btn(Vector2(1036, 444), "ui_up", "^")
    _touch_btn(Vector2(1036, 536), "ui_down", "v")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
    var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
    img2.fill(Color(1, 1, 1, 0.20))
    for x in range(88):
        for y in range(88):
            if x < 3 or y < 3 or x > 84 or y > 84:
                img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
    var b := TouchScreenButton.new()
    b.texture_normal = ImageTexture.create_from_image(img2)
    b.position = pos
    if action != "":
        b.action = action
    ui.add_child(b)
    var t := Label.new()
    t.text = text
    t.add_theme_font_size_override("font_size", 24)
    t.position = pos
    t.size = Vector2(88, 88)
    t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    t.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(t)
    return b

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win"]:
        var p := AudioStreamPlayer.new()
        var s = load("res://%s.wav" % key)
        if s:
            p.stream = s
        add_child(p)
        snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune-slinga, lag volym sa
    # effekterna hors. finished->play = loop utan importinstallningar.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true
    _last_button = null

func _label(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

var _last_button: Button = null   # v2.22: fokuskedja for pilnavigering

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(300, 46)
    b.position = Vector2(426, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    # v2.22: explicit fokuskedja (CanvasLayer-knappar saknar auto-grannar).
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    # Forsta knappen pa varje skarm far fokus -> Enter/Space fungerar direkt
    # (tangentbordsspelbart, och kvalitetsgrindens sond kommer forbi titeln).
    if focus_pending:
        focus_pending = false
        b.grab_focus()

func _show_title() -> void:
    state = "title"
    _clear_ui()
    _label("TWENTY48", 90, 70, Color(0.95, 0.8, 0.3))
    _label("Slide with the arrow keys. Merge equal tiles to reach %d." % TARGET, 200, 22)
    _label("Best: %d" % best, 240, 22, Color(0.7, 0.9, 1))
    _button("Play", 310, func(): _start())
    # v2.22 spelskalet: Options + Quit pa titeln.
    _button("Options", 368, func(): _show_options())
    _button("Quit", 426, func(): get_tree().quit())
    queue_redraw()

func _show_options() -> void:
    _play("click")
    _clear_ui()
    Shell.options_panel(ui, func(): _show_title())

func _start() -> void:
    _play("click")
    score = 0
    grid = []
    for i in range(N * N):
        grid.append(0)
    _spawn()
    _spawn()
    _clear_ui()
    var hud := _label("Score: 0", 20, 26)
    hud.name = "Hud"
    state = "playing"
    queue_redraw()

func _spawn() -> void:
    var empty: Array[int] = []
    for i in range(N * N):
        if grid[i] == 0:
            empty.append(i)
    if empty.is_empty():
        return
    grid[empty[randi() % empty.size()]] = 2 if randf() < 0.9 else 4

func _input(event: InputEvent) -> void:
    if state != "playing":
        return
    var dir := -1
    if event.is_action_pressed("ui_left"):
        dir = 0
    elif event.is_action_pressed("ui_right"):
        dir = 1
    elif event.is_action_pressed("ui_up"):
        dir = 2
    elif event.is_action_pressed("ui_down"):
        dir = 3
    if dir < 0:
        return
    if _move(dir):
        _play("click")
        _spawn()
        var hud := ui.get_node_or_null("Hud")
        if hud:
            hud.text = "Score: %d" % score
        queue_redraw()
        _check_end()

func _slide(line: Array[int]) -> Array[int]:
    var vals: Array[int] = []
    for v in line:
        if v != 0:
            vals.append(v)
    var out: Array[int] = []
    var i := 0
    while i < vals.size():
        if i + 1 < vals.size() and vals[i] == vals[i + 1]:
            var m := vals[i] * 2
            out.append(m)
            score += m
            i += 2
        else:
            out.append(vals[i])
            i += 1
    while out.size() < N:
        out.append(0)
    return out

func _idx(dir: int, k: int, j: int) -> int:
    match dir:
        0: return k * N + j
        1: return k * N + (N - 1 - j)
        2: return j * N + k
        3: return (N - 1 - j) * N + k
    return 0

func _move(dir: int) -> bool:
    var moved := false
    for k in range(N):
        var line: Array[int] = []
        for j in range(N):
            line.append(grid[_idx(dir, k, j)])
        var slid := _slide(line)
        for j in range(N):
            var at := _idx(dir, k, j)
            if grid[at] != slid[j]:
                moved = true
            grid[at] = slid[j]
    return moved

func _check_end() -> void:
    for v in grid:
        if v >= TARGET:
            _finish(true)
            return
    for i in range(N * N):
        if grid[i] == 0:
            return
    for r in range(N):
        for c in range(N):
            var v := grid[r * N + c]
            if c + 1 < N and grid[r * N + c + 1] == v:
                return
            if r + 1 < N and grid[(r + 1) * N + c] == v:
                return
    _finish(false)

func _finish(won: bool) -> void:
    state = "over"
    _play("win" if won else "hurt")
    if score > best:
        best = score
        _save_best(score)
    _clear_ui()
    _label("YOU WIN!" if won else "NO MOVES LEFT", 200, 54, Color(0.95, 0.85, 0.3))
    _label("Score: %d   Best: %d" % [score, best], 280, 26)
    _button("Play again", 340, func(): _show_title())

func _draw() -> void:
    draw_rect(Rect2(0, 0, 1152, 648), Color(0.16, 0.15, 0.13))
    if state == "title":
        return
    var sz := 118.0
    var gap := 12.0
    var total := N * sz + (N - 1) * gap
    var ox := 576.0 - total * 0.5
    var oy := 340.0 - total * 0.5
    var font := ThemeDB.fallback_font
    for r in range(N):
        for c in range(N):
            var v := grid[r * N + c]
            var x := ox + c * (sz + gap)
            var y := oy + r * (sz + gap)
            draw_rect(Rect2(x, y, sz, sz), _tile_color(v))
            if v > 0:
                var fs := 46 if v < 1000 else 34
                var tc := Color(0.15, 0.12, 0.1) if v <= 4 else Color.WHITE
                draw_string(font, Vector2(x, y + sz * 0.5 + fs * 0.35), str(v), HORIZONTAL_ALIGNMENT_CENTER, sz, fs, tc)

func _tile_color(v: int) -> Color:
    if v == 0:
        return Color(0.26, 0.24, 0.21)
    var t := clampf(log(float(v)) / log(2048.0), 0.0, 1.0)
    return Color(0.95, 0.78 - t * 0.5, 0.32 - t * 0.28)

func _load_best() -> int:
    if not FileAccess.file_exists(SAVE_PATH):
        return 0
    var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
    return int(f.get_as_text()) if f else 0

func _save_best(v: int) -> void:
    var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
    if f:
        f.store_string(str(v))
""";

    /// <summary>3D: ett komplett Godot 3D-samlarspel (CharacterBody3D, kamera,
    /// ljus, meshar - allt i kod). Fyller 3D-luckan dar 3D tidigare betydde
    /// otestad best-effort-Unity. Godot gor 3D bra och kan verifieras headless.</summary>
    internal static string[] ScaffoldGodot3D(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("The Cube"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node3D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotThreeDMain);
        files.Add("Main.gd");
        foreach (var (name, category) in new[] { ("click.wav", "select"), ("coin.wav", "coin"), ("hurt.wav", "lose"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotThreeDDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# The Cube - 3D-samlarspel (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbart 3D-spel: styr kuben (CharacterBody3D) pa en bana, samla\n" +
            "alla mynt innan tiden tar slut. Kamera, ljus, mark och meshar byggs i kod.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n" +
            "Webb (spela i webblasaren): `godot --headless --export-release \"Web\" build/web/index.html`\n\n" +
            "3D byggs helt i _build_world - byt tema via farger/former/mekanik dar.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotThreeDDesignDoc(string prompt) =>
        "# 3D-samlarspel (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nStyr kuben i 3D och samla alla mynt innan tiden tar slut. Hela scenen byggs\n" +
        "programmatiskt (mark, riktat ljus, foljande kamera, spelare, mynt).\n\n" +
        "## Mekaniker (klara)\n- CharacterBody3D med gravitation + move_and_slide, WASD/piltangenter\n" +
        "- Foljande Camera3D, DirectionalLight3D + Environment\n- Snurrande mynt, plock via avstand, poang\n" +
        "- Tre svarighetsgrader (tid + fart), tidsgrans, basta resultat sparas (user://)\n" +
        "- Titel/vinst/forlust-overlay, ljud\n\n## Bygg vidare\n- Hopp, hinder/fiender i 3D, plattformar, forsta-persons-kamera, fler banor\n";

    const string GodotThreeDMain = """
extends Node3D
# The Cube - ett litet 3D-samlarspel: styr kuben, samla alla mynt innan tiden
# tar slut. Hela scenen (mark/ljus/kamera/spelare/mynt) byggs i kod. BYT TEMA:
# farger/former i _build_world och mal/tid i konstanterna. Spelartext pa
# ENGELSKA (husregeln).

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://cube_best.txt"
const COINS := 8

var state := "title"
var difficulty := 1
var player: CharacterBody3D
var coins: Array[Node3D] = []
var collected := 0
var time_left := 0.0
var best := 0
var snd := {}
var ui: CanvasLayer
var cam: Camera3D
var shake := 0.0  # C1 juice: kamerashake-magnitud (varldsenheter), avtar mot 0
var focus_pending := true

func _ready() -> void:
    randomize()
    Shell.startup()
    _setup_audio()
    best = _load_best()
    _build_world()
    ui = CanvasLayer.new()
    add_child(ui)
    _setup_touch()
    _show_title()

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
    if not DisplayServer.is_touchscreen_available():
        return
    _touch_btn(Vector2(28, 536), "ui_left", "<")
    _touch_btn(Vector2(132, 536), "ui_right", ">")
    _touch_btn(Vector2(1036, 444), "ui_up", "^")
    _touch_btn(Vector2(1036, 536), "ui_down", "v")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
    var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
    img2.fill(Color(1, 1, 1, 0.20))
    for x in range(88):
        for y in range(88):
            if x < 3 or y < 3 or x > 84 or y > 84:
                img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
    var b := TouchScreenButton.new()
    b.texture_normal = ImageTexture.create_from_image(img2)
    b.position = pos
    if action != "":
        b.action = action
    ui.add_child(b)
    var t := Label.new()
    t.text = text
    t.add_theme_font_size_override("font_size", 24)
    t.position = pos
    t.size = Vector2(88, 88)
    t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    t.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(t)
    return b

# C1 juice: en engangs 3D-partikelskur vid myntplock. Meshens StandardMaterial3D
# barr fargen. VIKTIGT: scale_amount ar en MULTIPLIER pa mesh-storleken (default
# 1.0), sa 0.12*0.12 blev ~0.03 units = osynligt fran ~20-enheters kamera -
# 1.0-2.0 x mesh-radie 0.12 ger ~0.12-0.24 units = synlig skur.
func _burst3d(pos: Vector3, col: Color) -> void:
    var p := CPUParticles3D.new()
    p.position = pos
    p.amount = 16
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.6
    p.direction = Vector3(0, 1, 0)
    p.spread = 80.0
    p.initial_velocity_min = 2.5
    p.initial_velocity_max = 6.0
    p.gravity = Vector3(0, -12, 0)
    p.scale_amount_min = 1.0
    p.scale_amount_max = 2.0
    var m := SphereMesh.new()
    m.radius = 0.12
    m.height = 0.24
    var mat := StandardMaterial3D.new()
    mat.albedo_color = col
    m.material = mat
    p.mesh = m
    add_child(p)
    p.emitting = true
    get_tree().create_timer(1.4).timeout.connect(p.queue_free)

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win"]:
        var p := AudioStreamPlayer.new()
        var s = load("res://%s.wav" % key)
        if s:
            p.stream = s
        add_child(p)
        snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune-slinga, lag volym sa
    # effekterna hors. finished->play = loop utan importinstallningar.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

func _mat(c: Color) -> StandardMaterial3D:
    var m := StandardMaterial3D.new()
    m.albedo_color = c
    return m

func _build_world() -> void:
    var light := DirectionalLight3D.new()
    light.rotation_degrees = Vector3(-55, -30, 0)
    light.light_energy = 1.1
    add_child(light)
    var env := WorldEnvironment.new()
    var e := Environment.new()
    e.background_mode = Environment.BG_COLOR
    e.background_color = Color(0.14, 0.16, 0.24)
    e.ambient_light_color = Color(0.5, 0.5, 0.6)
    e.ambient_light_energy = 0.6
    env.environment = e
    add_child(env)
    var ground := MeshInstance3D.new()
    var gm := BoxMesh.new()
    gm.size = Vector3(40, 1, 40)
    ground.mesh = gm
    ground.position = Vector3(0, -0.5, 0)
    ground.material_override = _mat(Color(0.2, 0.45, 0.25))
    add_child(ground)
    var gbody := StaticBody3D.new()
    var gcol := CollisionShape3D.new()
    var gshape := BoxShape3D.new()
    gshape.size = Vector3(40, 1, 40)
    gcol.shape = gshape
    gbody.add_child(gcol)
    gbody.position = Vector3(0, -0.5, 0)
    add_child(gbody)
    cam = Camera3D.new()
    add_child(cam)
    player = CharacterBody3D.new()
    var pm := MeshInstance3D.new()
    var pbox := BoxMesh.new()
    pbox.size = Vector3(1, 1, 1)
    pm.mesh = pbox
    pm.material_override = _mat(Color(0.3, 0.6, 1.0))
    player.add_child(pm)
    var pcol := CollisionShape3D.new()
    var pshape := BoxShape3D.new()
    pshape.size = Vector3(1, 1, 1)
    pcol.shape = pshape
    player.add_child(pcol)
    player.position = Vector3(0, 1, 0)
    add_child(player)

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true
    _last_button = null

func _label(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

var _last_button: Button = null   # v2.22: fokuskedja for pilnavigering

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    # v2.22: explicit fokuskedja (CanvasLayer-knappar saknar auto-grannar).
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    # Forsta knappen pa varje skarm far fokus -> Enter/Space fungerar direkt
    # (tangentbordsspelbart, och kvalitetsgrindens sond kommer forbi titeln).
    if focus_pending:
        focus_pending = false
        b.grab_focus()

func _show_title() -> void:
    state = "title"
    _clear_ui()
    _label("THE CUBE", 80, 72, Color(0.5, 0.8, 1))
    _label("Collect all %d coins before time runs out. WASD / arrow keys." % COINS, 190, 22)
    _label("Best: %d coins" % best, 226, 22, Color(0.7, 0.9, 1))
    var names := ["Easy", "Normal", "Hard"]
    for i in range(3):
        var d := i
        _button(names[i], 290 + i * 58, func(): _start(d))
    # v2.22 spelskalet: Options + Quit pa titeln.
    _button("Options", 470, func(): _show_options())
    _button("Quit", 528, func(): get_tree().quit())

func _show_options() -> void:
    _play("click")
    _clear_ui()
    Shell.options_panel(ui, func(): _show_title())

func _start(d: int) -> void:
    difficulty = d
    _play("click")
    collected = 0
    var times: Array[float] = [60.0, 45.0, 30.0]
    time_left = times[d]
    player.position = Vector3(0, 1, 0)
    player.velocity = Vector3.ZERO
    _spawn_coins()
    _clear_ui()
    var hud := _label("", 12, 26)
    hud.name = "Hud"
    state = "playing"

func _spawn_coins() -> void:
    for c in coins:
        if is_instance_valid(c):
            c.queue_free()
    coins = []
    for i in range(COINS):
        var coin := MeshInstance3D.new()
        var cm := SphereMesh.new()
        cm.radius = 0.5
        cm.height = 1.0
        coin.mesh = cm
        coin.material_override = _mat(Color(1, 0.85, 0.2))
        coin.position = Vector3(randf_range(-17, 17), 1, randf_range(-17, 17))
        add_child(coin)
        coins.append(coin)

func _physics_process(delta: float) -> void:
    if state != "playing":
        return
    time_left -= delta
    var fz := Input.get_action_strength("ui_down") - Input.get_action_strength("ui_up")
    var fx := Input.get_action_strength("ui_right") - Input.get_action_strength("ui_left")
    var speeds: Array[float] = [7.0, 9.0, 11.0]
    var speed := speeds[difficulty]
    player.velocity.x = fx * speed
    player.velocity.z = fz * speed
    if player.is_on_floor():
        player.velocity.y = 0.0
    else:
        player.velocity.y -= 24.0 * delta
    player.move_and_slide()
    # C1 juice: kamerashake - lagg en avtagande slump-offset pa foljekameran.
    if shake > 0.0:
        shake = move_toward(shake, 0.0, 2.0 * delta)
    var sh := Vector3(randf_range(-shake, shake), randf_range(-shake, shake), 0.0)
    cam.position = player.position + Vector3(0, 14, 14) + sh
    cam.look_at(player.position, Vector3.UP)
    for coin in coins:
        if is_instance_valid(coin):
            coin.rotate_y(delta * 3.0)
            if player.position.distance_to(coin.position) < 1.3:
                _burst3d(coin.position, Color(1, 0.85, 0.2))  # C1 juice
                shake = max(shake, 0.4)
                coin.queue_free()
                collected += 1
                _play("coin")
    var hud := ui.get_node_or_null("Hud")
    if hud:
        hud.text = "Coins %d/%d   Time %.0f s" % [collected, COINS, max(0.0, time_left)]
    if collected >= COINS:
        _finish(true)
    elif time_left <= 0.0:
        _finish(false)

func _finish(won: bool) -> void:
    state = "over"
    _play("win" if won else "hurt")
    if collected > best:
        best = collected
        _save_best(collected)
    _clear_ui()
    _label("YOU WIN!" if won else "TIME'S UP", 200, 56, Color(0.6, 0.9, 0.4) if won else Color(1, 0.5, 0.4))
    _label("Coins: %d/%d   Best: %d" % [collected, COINS, best], 280, 26)
    _button("Play again", 340, func(): _show_title())

func _load_best() -> int:
    if not FileAccess.file_exists(SAVE_PATH):
        return 0
    var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
    return int(f.get_as_text()) if f else 0

func _save_best(v: int) -> void:
    var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
    if f:
        f.store_string(str(v))
""";

    static string GodotKitProject(string name) =>
        "[application]\n" +
        $"config/name=\"{name}\"\n" +
        "config/icon=\"res://icon.ico\"\n" +
        "run/main_scene=\"res://Main.tscn\"\n" +
        "config/features=PackedStringArray(\"4.3\")\n" +
        "[display]\n" +
        "window/size/viewport_width=1152\n" +
        "window/size/viewport_height=648\n" +
        // Stretch: 1:1 vid standardfonstret (datorspel EXAKT som forr - sond-
        // erna bevisar det) men layouten skalar ratt pa telefon-/surfplatte-
        // skarmar i stallet for att beskaras (v1.93, emulatorfyndet).
        "window/stretch/mode=\"canvas_items\"\n" +
        "window/stretch/aspect=\"keep\"\n" +
        // Android-export KRAVER etc2/astc-import - utan den faller Godots
        // projektvalidering med ett HELT TOMT felmeddelande (v1.90, verifierat
        // mot 4.3-kallkoden: should_import_etc2_astc -> valid=false utan text).
        // Ofarligt for desktop/webb - bara ett extra texturformat vid import.
        "[rendering]\n" +
        "textures/vram_compression/import_etc2_astc=true\n" +
        // v2.17: NEAREST som standardfilter - pixelart-sprites renderades
        // LINJART filtrerade (suddiga gubbar vid scale 2+) i alla kit; skarpa
        // pixlar ar sjalva stilen. Vektorritning i _draw paverkas inte.
        "textures/canvas_textures/default_texture_filter=0\n";

    static string GodotKitMainScene(string rootType) =>
        "[gd_scene load_steps=2 format=3 uid=\"uid://ailocalkitmain\"]\n\n" +
        "[ext_resource type=\"Script\" path=\"res://Main.gd\" id=\"1\"]\n\n" +
        $"[node name=\"Main\" type=\"{rootType}\"]\n" +
        "script = ExtResource(\"1\")\n";

    static string GodotManagementDesignDoc(string prompt) =>
        "# Management / Tycoon (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nDu tar over ligans samsta organisation och bygger den till den basta over en sasong pa 10 omgangar.\n\n" +
        "## Mekanik\n- **Trupp:** 8 medlemmar med namn, roll och betyg 1-99; snittet ar din styrka\n" +
        "- **Marknad:** kop (budget dras, betyg in) och salj (minst 6 kvar) - marknaden fylls pa varje omgang\n" +
        "- **Omgangssimulering:** styrka + form-slump mot ligans motstand; resultat, tabell och biljettintakter\n" +
        "- **Ekonomi:** budget i kr; intakter per omgang, loner dras; konkurs = forlust\n" +
        "- **Svarighetsgrader:** Latt/Medel/Svar paverkar startbudget och motstandarstyrka\n" +
        "- **Vinst:** ligaseger efter 10 omgangar; **Forlust:** konkurs\n\n" +
        "## Produktion\n- Titelskarm med svarighetsval + Ladda-knapp, hubb med Trupp/Marknad/Tabell, spara/ladda (user://)\n" +
        "- Ljud: klick, kop, forlust, seger (sfxr-wav, inga externa filer)\n- Esc gar till titeln (autosparar)\n\n" +
        "## Extension (tema-exempel)\n- Fotbollsmanager: medlemmar=spelare, omgang=match\n- Gamedev-tycoon: medlemmar=utvecklare, omgang=spelslapp\n- Fler ligor/divisioner, traning, sponsorer\n";

    static string GodotTopDownDesignDoc(string prompt) =>
        "# Top-down action (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nOverlev 5 vagor av jagande fiender och samla mynt i en oppen aren.\n\n" +
        "## Mekanik\n- **Rorelse:** 8 vagar (WASD/pilar), deltatid\n- **Fiender:** jagar spelaren; kontakt kostar HP (ododlighetsfonster efterat)\n" +
        "- **Mynt:** +poang; alla mynt plockade => nasta vag\n- **Vagor:** 5 st, fler och snabbare fiender per vag\n" +
        "- **Svarighetsgrader:** Latt/Medel/Svar paverkar HP och fiendefart\n- **Vinst:** klara vag 5; **Forlust:** 0 HP\n\n" +
        "## Produktion\n- Titelskarm med svarighetsval, paus (Esc), vinst/forlust-overlay med omstart, highscore (user://)\n" +
        "- Ljud: plock, traff, seger (sfxr-wav)\n- Sprites genereras i kod - inga externa bilder\n\n" +
        "## Extension\n- Vapen/attack, boss pa vag 5, fler fiendetyper, rum/dungeon i stallet for aren\n";

    // ---- Main.gd: management ------------------------------------------------
    const string GodotManagementMain = """
extends Control
# Club Manager - komplett management/tycoon-grund. Allt UI byggs i kod har.
# BYT TEMA: andra texter, NAMES/roller och siffror - strukturen bar allt.
# Spelartext pa ENGELSKA (husregeln).

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://clubmanager_save.json"
const SEASON_LENGTH := 10
const NAMES := ["Alva","Bo","Cleo","Dag","Elin","Frans","Greta","Hugo","Ines","Jarl","Klara","Leo","Maja","Nils","Olga","Per"]
const ROLES := ["Striker","Midfield","Defence","Keeper"]
const RIVALS := ["Norrvik","Sodra IF","Ostkusten","Vastra BK","Bergslaget"]

var difficulty := 1
var week := 0
var budget := 0
var players: Array = []
var league: Array = []
var market: Array = []
var last_result := ""
var snd := {}
var focus_pending := true

func _ready() -> void:
	Shell.startup()
	for key in ["click","coin","hurt","win"]:
		# Nullsakert: fore forsta importen (headless-parse) finns ingen
		# wav-resurs - spelet ska anda starta tyst, aldrig spamma fel.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
	# Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
	var music = load("res://music.wav")
	if music:
		var mp := AudioStreamPlayer.new()
		mp.stream = music
		mp.volume_db = -14.0
		mp.finished.connect(mp.play)
		add_child(mp)
		mp.play()
	show_title()

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel") and week > 0:
		save_game()
		show_title()

func play(key: String) -> void:
	if snd.has(key):
		snd[key].play()

# ---------- state ----------
func new_game(diff: int) -> void:
	difficulty = diff
	week = 0
	budget = [900000, 600000, 350000][diff]
	players = []
	for i in range(8):
		players.append(make_person(30 + randi() % 25))
	league = []
	for i in range(RIVALS.size()):
		league.append({"name": RIVALS[i], "strength": 55 + i * 6 + diff * 4, "points": 0, "played": 0})
	league.append({"name": "Your Team", "strength": 0, "points": 0, "played": 0})
	refill_market()
	last_result = ""
	show_hub()

func make_person(rating: int) -> Dictionary:
	return {"name": NAMES[randi() % NAMES.size()] + " " + char(65 + randi() % 26) + ".",
		"role": ROLES[randi() % ROLES.size()], "rating": clampi(rating, 20, 99)}

func refill_market() -> void:
	market = []
	for i in range(6):
		market.append(make_person(35 + randi() % 45))

func team_strength() -> int:
	if players.is_empty():
		return 0
	var total := 0
	for p in players:
		total += int(p["rating"])
	return total / players.size()

func price_of(p: Dictionary) -> int:
	return int(p["rating"]) * 3000

func wage_bill() -> int:
	var total := 0
	for p in players:
		total += int(p["rating"]) * 120
	return total

# ---------- simulering ----------
func play_week() -> void:
	var rival: Dictionary = league[week % RIVALS.size()]
	var mine := team_strength() + randi() % 20
	var theirs := int(rival["strength"]) + randi() % 20
	var my_goals := clampi((mine - theirs) / 8 + 1 + randi() % 2, 0, 6)
	var their_goals := clampi((theirs - mine) / 8 + 1 + randi() % 2, 0, 6)
	var me: Dictionary = league[league.size() - 1]
	me["played"] = int(me["played"]) + 1
	rival["played"] = int(rival["played"]) + 1
	if my_goals > their_goals:
		me["points"] = int(me["points"]) + 3
		play("coin")
	elif my_goals == their_goals:
		me["points"] = int(me["points"]) + 1
		rival["points"] = int(rival["points"]) + 1
	else:
		rival["points"] = int(rival["points"]) + 3
		play("hurt")
	# ovriga rivaler mots inbordes
	for i in range(RIVALS.size()):
		if i != week % RIVALS.size():
			var other: Dictionary = league[i]
			other["played"] = int(other["played"]) + 1
			other["points"] = int(other["points"]) + [0, 1, 3][randi() % 3]
	var income := 40000 + int(me["points"]) * 4000 + my_goals * 5000
	budget += income - wage_bill()
	last_result = "Round %d: Your Team - %s  %d-%d   (gate +%s, wages -%s)" % [week + 1, rival["name"], my_goals, their_goals, fmt(income), fmt(wage_bill())]
	week += 1
	refill_market()
	save_game()
	if budget < 0:
		show_end(false, "Bankrupt - the club treasury is empty.")
	elif week >= SEASON_LENGTH:
		show_end(rank_of_me() == 1, "The season is over - you finished in position %d." % rank_of_me())
	else:
		show_hub()

func rank_of_me() -> int:
	var sorted := league.duplicate()
	sorted.sort_custom(func(a, b): return int(a["points"]) > int(b["points"]))
	for i in range(sorted.size()):
		if sorted[i]["name"] == "Your Team":
			return i + 1
	return sorted.size()

# ---------- spara/ladda ----------
func save_game() -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify({"difficulty": difficulty, "week": week, "budget": budget,
			"players": players, "league": league, "market": market}))

func load_game() -> bool:
	if not FileAccess.file_exists(SAVE_PATH):
		return false
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	if f == null:
		return false
	var data: Variant = JSON.parse_string(f.get_as_text())
	if typeof(data) != TYPE_DICTIONARY:
		return false
	difficulty = int(data.get("difficulty", 1))
	week = int(data.get("week", 0))
	budget = int(data.get("budget", 0))
	players = data.get("players", [])
	league = data.get("league", [])
	market = data.get("market", [])
	last_result = ""
	return true

# ---------- ui-hjalpare ----------
func clear_ui() -> void:
	for child in get_children():
		if child is Control:
			child.queue_free()

func panel_root() -> VBoxContainer:
	clear_ui()
	focus_pending = true
	var bg := ColorRect.new()
	bg.color = Color(0.08, 0.1, 0.14)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(bg)
	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.offset_left = 40
	box.offset_top = 24
	box.offset_right = -40
	box.offset_bottom = -24
	box.add_theme_constant_override("separation", 10)
	add_child(box)
	return box

func label_into(parent: Control, text: String, size: int = 16) -> Label:
	var l := Label.new()
	l.text = text
	l.add_theme_font_size_override("font_size", size)
	parent.add_child(l)
	return l

func button_into(parent: Control, text: String, handler: Callable) -> Button:
	var b := Button.new()
	b.text = text
	b.pressed.connect(func():
		play("click")
		handler.call())
	parent.add_child(b)
	# Forsta knappen pa varje skarm far fokus -> Enter/Space fungerar direkt
	# (tangentbordsspelbart, och kvalitetsgrindens sond kommer forbi titeln).
	if focus_pending:
		focus_pending = false
		b.grab_focus()
	return b

func fmt(n: int) -> String:
	return str(n)

# ---------- skarmar ----------
func show_title() -> void:
	var box := panel_root()
	label_into(box, "CLUB MANAGER", 44)
	label_into(box, "Take the league's worst team to the top in %d rounds." % SEASON_LENGTH, 18)
	label_into(box, "Choose difficulty:", 16)
	button_into(box, "Easy (900 000 starting cash)", func(): new_game(0))
	button_into(box, "Normal (600 000)", func(): new_game(1))
	button_into(box, "Hard (350 000)", func(): new_game(2))
	if FileAccess.file_exists(SAVE_PATH):
		button_into(box, "Load saved game", func():
			if load_game():
				show_hub())
	# v2.22 spelskalet: Options + Quit pa titeln.
	button_into(box, "Options", show_options)
	button_into(box, "Quit", func(): get_tree().quit())
	label_into(box, "Esc in game: save and return here.", 12)

func show_options() -> void:
	clear_ui()
	focus_pending = true
	Shell.options_panel(self, show_title)

func show_hub() -> void:
	var box := panel_root()
	label_into(box, "Round %d/%d   Cash: %s   Team strength: %d   League position: %d" % [week + 1, SEASON_LENGTH, fmt(budget), team_strength(), rank_of_me()], 18)
	if last_result != "":
		label_into(box, last_result, 14)
	var tabs := HBoxContainer.new()
	tabs.add_theme_constant_override("separation", 8)
	box.add_child(tabs)
	button_into(tabs, "Squad", show_squad)
	button_into(tabs, "Market", show_market)
	button_into(tabs, "Table", show_table)
	button_into(tabs, "Play round %d" % (week + 1), play_week)

func show_squad() -> void:
	var box := panel_root()
	label_into(box, "Squad (%d) - wages %s/round" % [players.size(), fmt(wage_bill())], 20)
	for i in range(players.size()):
		var p: Dictionary = players[i]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		box.add_child(row)
		label_into(row, "%s  [%s]  rating %d" % [p["name"], p["role"], int(p["rating"])], 14)
		if players.size() > 6:
			var index := i
			button_into(row, "Sell +%s" % fmt(price_of(p) / 2), func():
				budget += price_of(players[index]) / 2
				players.remove_at(index)
				play("coin")
				show_squad())
	button_into(box, "Back", show_hub)

func show_market() -> void:
	var box := panel_root()
	label_into(box, "Market - cash %s" % fmt(budget), 20)
	for i in range(market.size()):
		var p: Dictionary = market[i]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		box.add_child(row)
		label_into(row, "%s  [%s]  rating %d" % [p["name"], p["role"], int(p["rating"])], 14)
		var index := i
		var b := button_into(row, "Buy %s" % fmt(price_of(p)), func():
			if budget >= price_of(market[index]) and players.size() < 12:
				budget -= price_of(market[index])
				players.append(market[index])
				market.remove_at(index)
				play("coin")
				show_market())
		if budget < price_of(p) or players.size() >= 12:
			b.disabled = true
	button_into(box, "Back", show_hub)

func show_table() -> void:
	var box := panel_root()
	label_into(box, "League table", 20)
	var sorted := league.duplicate()
	sorted.sort_custom(func(a, b): return int(a["points"]) > int(b["points"]))
	for i in range(sorted.size()):
		var t: Dictionary = sorted[i]
		var mark := "  <- you" if t["name"] == "Your Team" else ""
		label_into(box, "%d. %s   %d pts (%d played)%s" % [i + 1, t["name"], int(t["points"]), int(t["played"]), mark], 15)
	button_into(box, "Back", show_hub)

func show_end(won: bool, message: String) -> void:
	var box := panel_root()
	if won:
		play("win")
	label_into(box, "CHAMPIONS!" if won else "THE END", 40)
	label_into(box, message, 18)
	label_into(box, "Final cash: %s   Team strength: %d" % [fmt(budget), team_strength()], 15)
	button_into(box, "Play again", show_title)
""";

    // ---- Main.gd: top-down --------------------------------------------------
    const string GodotTopDownMain = """
extends Node2D
# The Glade - komplett top-down-grund: vagor, fiender, mynt, HP, highscore.
# BYT TEMA: farger/former i make_texture-anropen + texterna. Spelartext pa
# ENGELSKA (husregeln).

# Preload (inte class_name-globalen): kitet ska parsa AVEN fore forsta
# importen, och class_name-registret finns forst efter import.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://glade_highscore.save"
const ARENA := Rect2(60, 60, 1032, 528)
const FINAL_WAVE := 5

var difficulty := 1
var state := "title" # title | playing | paused | over
var player: CharacterBody2D
var enemies: Array = []
var coins: Array = []
var hp := 3
var score := 0
var wave := 0
var invulnerable := 0.0
var shake := 0.0  # C1 juice: screenshake-magnitud (px), avtar mot 0
var hud: CanvasLayer
var hud_label: Label
var overlay: Control
var snd := {}
# v2.20 pixelspraket: gras-tiles i tva varianter + dekor (blommor/stenar)
# pa deterministiska platser - aldrig en platt enfargad yta.
var grass_a: ImageTexture
var grass_b: ImageTexture
var decor: Array = []   # {pos, tex}

func _ready() -> void:
	randomize()
	# Spelskalet: sparade installningar (volym/mute/fullskarm) galler direkt.
	Shell.startup()
	# Pixelart = skarpa pixlar aven i gamla projekt utan mallens filterrad.
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	grass_a = _make_grass_tex(0)
	grass_b = _make_grass_tex(1)
	var decor_seed := 12345
	for i in range(14):
		decor_seed = (decor_seed * 1103515245 + 12345) % 2147483647
		var dx := ARENA.position.x + 24.0 + float(decor_seed % 984)
		decor_seed = (decor_seed * 1103515245 + 12345) % 2147483647
		var dy := ARENA.position.y + 24.0 + float(decor_seed % 480)
		decor.append({"pos": Vector2(dx, dy), "tex": _make_decor_tex(i % 3)})
	for key in ["click","coin","hurt","win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
	# Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
	var music = load("res://music.wav")
	if music:
		var mp := AudioStreamPlayer.new()
		mp.stream = music
		mp.volume_db = -14.0
		mp.finished.connect(mp.play)
		add_child(mp)
		mp.play()
	hud = CanvasLayer.new()
	add_child(hud)
	hud_label = Label.new()
	hud_label.position = Vector2(70, 20)
	hud_label.add_theme_font_size_override("font_size", 18)
	hud.add_child(hud_label)
	_setup_touch()
	show_title()

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
	if not DisplayServer.is_touchscreen_available():
		return
	_touch_btn(Vector2(28, 536), "ui_left", "<")
	_touch_btn(Vector2(132, 536), "ui_right", ">")
	_touch_btn(Vector2(1036, 444), "ui_up", "^")
	_touch_btn(Vector2(1036, 536), "ui_down", "v")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
	var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
	img2.fill(Color(1, 1, 1, 0.20))
	for x in range(88):
		for y in range(88):
			if x < 3 or y < 3 or x > 84 or y > 84:
				img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
	var b := TouchScreenButton.new()
	b.texture_normal = ImageTexture.create_from_image(img2)
	b.position = pos
	if action != "":
		b.action = action
	hud.add_child(b)
	var t := Label.new()
	t.text = text
	t.add_theme_font_size_override("font_size", 24)
	t.position = pos
	t.size = Vector2(88, 88)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(t)
	return b

func play_sound(key: String) -> void:
	if snd.has(key):
		snd[key].play()

# C1 juice: en engangs-partikelskur. HUD/overlay ligger pa en CanvasLayer och
# paverkas inte av att varldens Node2D (self) skakas.
func spawn_burst(pos: Vector2, col: Color, count: int) -> void:
	var p := CPUParticles2D.new()
	p.position = pos
	p.amount = count
	p.one_shot = true
	p.explosiveness = 0.9
	p.lifetime = 0.5
	p.spread = 180.0
	p.initial_velocity_min = 70.0
	p.initial_velocity_max = 180.0
	p.gravity = Vector2(0, 200)
	p.scale_amount_min = 2.0
	p.scale_amount_max = 4.0
	p.color = col
	# C1: utan textur blir partiklarna en 1x1-quad (~2-4px) och nastan osynliga.
	# Vit 6px-textur x scale 2-4 = 12-24px, och p.color tonar den till ratt farg.
	p.texture = make_texture(6, Color(1, 1, 1), Color(1, 1, 1))
	add_child(p)
	p.emitting = true
	get_tree().create_timer(1.0).timeout.connect(p.queue_free)

func make_texture(size: int, base: Color, accent: Color) -> ImageTexture:
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	img.fill(base)
	for x in range(size):
		for y in range(size):
			if x == 0 or y == 0 or x == size - 1 or y == size - 1:
				img.set_pixel(x, y, accent)
	img.set_pixel(size / 2, size / 3, accent)
	img.set_pixel(size / 2 - 1, size / 3, accent)
	return ImageTexture.create_from_image(img)

func make_sprite_body(frames_path: String, size: int) -> CharacterBody2D:
	var body := CharacterBody2D.new()
	var spr := AnimatedSprite2D.new()
	spr.name = "Anim"
	spr.sprite_frames = load(frames_path)
	spr.scale = Vector2(2, 2)
	spr.play("idle")
	body.add_child(spr)
	var shape := CollisionShape2D.new()
	var rect := RectangleShape2D.new()
	rect.size = Vector2(size * 2, size * 2)
	shape.shape = rect
	body.add_child(shape)
	return body

# ---------- flode ----------
func new_game(diff: int) -> void:
	difficulty = diff
	hp = [5, 3, 2][diff]
	score = 0
	wave = 0
	clear_entities()
	if player:
		player.queue_free()
	player = make_sprite_body("res://player_frames.tres", 12)
	player.position = ARENA.get_center()
	add_child(player)
	state = "playing"
	close_overlay()
	next_wave()

func clear_entities() -> void:
	for e in enemies:
		e.queue_free()
	for c in coins:
		c.queue_free()
	enemies = []
	coins = []

func next_wave() -> void:
	wave += 1
	if wave > FINAL_WAVE:
		finish(true)
		return
	clear_entities()
	for i in range(2 + wave + difficulty):
		var e := make_sprite_body("res://enemy_frames.tres", 11)
		e.get_node("Anim").play("walk")
		e.position = random_edge_point()
		add_child(e)
		enemies.append(e)
	for i in range(4 + wave):
		var c := Sprite2D.new()
		c.texture = _make_coin_tex()
		c.scale = Vector2(2, 2)
		c.position = random_point()
		add_child(c)
		coins.append(c)

func _make_coin_tex() -> ImageTexture:
	# v2.20: riktigt pixelmynt - kontur, guldkropp, mork undersida, glans.
	var img := Image.create(10, 10, false, Image.FORMAT_RGBA8)
	var outline := Color8(27, 22, 36)
	var gold := Color8(240, 196, 60)
	var dark := Color8(190, 140, 40)
	var shine := Color8(255, 246, 190)
	for y in range(10):
		for x in range(10):
			var dx := x - 4.5
			var dy := y - 4.5
			var d2 := dx * dx + dy * dy
			if d2 <= 12.5:
				img.set_pixel(x, y, gold if y < 6 else dark)
			elif d2 <= 20.0:
				img.set_pixel(x, y, outline)
	img.set_pixel(3, 3, shine)
	img.set_pixel(4, 2, shine)
	return ImageTexture.create_from_image(img)

func random_point() -> Vector2:
	return Vector2(ARENA.position.x + randf() * ARENA.size.x, ARENA.position.y + randf() * ARENA.size.y)

func random_edge_point() -> Vector2:
	var p := random_point()
	if randi() % 2 == 0:
		p.x = ARENA.position.x if randi() % 2 == 0 else ARENA.end.x
	else:
		p.y = ARENA.position.y if randi() % 2 == 0 else ARENA.end.y
	return p

func finish(won: bool) -> void:
	state = "over"
	if won:
		play_sound("win")
	var best := load_highscore()
	if score > best:
		save_highscore(score)
		best = score
	show_overlay("YOU WIN!" if won else "GAME OVER", "Score: %d   Best: %d" % [score, best], true)

# ---------- highscore ----------
func load_highscore() -> int:
	if not FileAccess.file_exists(SAVE_PATH):
		return 0
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	return int(f.get_as_text()) if f else 0

func save_highscore(value: int) -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(str(value))

# ---------- loop ----------
func _physics_process(delta: float) -> void:
	if state != "playing" or player == null:
		return
	# C1 juice: screenshake genom att flytta varldens Node2D en liten slump-
	# forskjutning som avtar. Snapper tillbaka till noll nar den slocknat.
	if shake > 0.0:
		shake = move_toward(shake, 0.0, 32.0 * delta)
		position = Vector2(randf_range(-shake, shake), randf_range(-shake, shake))
	elif position != Vector2.ZERO:
		position = Vector2.ZERO
	var dir := Vector2(
		Input.get_axis("ui_left", "ui_right"),
		Input.get_axis("ui_up", "ui_down"))
	if dir.length() > 0:
		dir = dir.normalized()
	player.velocity = dir * 260.0
	var anim := player.get_node("Anim") as AnimatedSprite2D
	if anim:
		if dir.length() > 0.0:
			anim.play("walk")
			anim.flip_h = dir.x < 0.0
		else:
			anim.play("idle")
	player.move_and_slide()
	player.position = player.position.clamp(ARENA.position, ARENA.end)
	if invulnerable > 0.0:
		invulnerable -= delta
		# C1 juice: spelaren blinkar + tonar tillbaka fran rott medan
		# ododlighetsfonstret varar - visar ocksa att i-frames ar aktiva.
		if anim:
			anim.visible = fmod(invulnerable, 0.2) < 0.12
			anim.modulate = anim.modulate.lerp(Color.WHITE, 6.0 * delta)
			if invulnerable <= 0.0:
				anim.visible = true
				anim.modulate = Color.WHITE
	var speed := 60.0 + wave * 14.0 + difficulty * 12.0
	for e in enemies:
		e.position += (player.position - e.position).normalized() * speed * delta
		if invulnerable <= 0.0 and e.position.distance_to(player.position) < 30.0:
			hp -= 1
			invulnerable = 1.2
			play_sound("hurt")
			shake = 9.0  # C1 juice: kannbar traff
			spawn_burst(player.position, Color(1, 0.3, 0.3), 18)
			if anim:
				anim.modulate = Color(1, 0.35, 0.35)  # C1 juice: spelaren blinkar rott
			if hp <= 0:
				finish(false)
				return
	var remaining: Array = []
	for c in coins:
		if c.position.distance_to(player.position) < 32.0:
			score += 10
			play_sound("coin")
			spawn_burst(c.position, Color(1, 0.9, 0.3), 12)  # C1 juice
			shake = max(shake, 3.0)
			c.queue_free()
		else:
			remaining.append(c)
	coins = remaining
	if coins.is_empty():
		score += 25
		next_wave()
	hud_label.text = "HP: %d   Score: %d   Wave: %d/%d" % [hp, score, mini(wave, FINAL_WAVE), FINAL_WAVE]

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if state == "playing":
			state = "paused"
			show_overlay("PAUSED", "Esc: resume", false)
		elif state == "paused":
			state = "playing"
			close_overlay()

func _draw() -> void:
	# v2.20 pixelspraket: schackrutigt pixelgras i tva nyanser, mork kontur
	# runt gladan och pixeldekor - samma formsprak som gubbarna/brickorna.
	var ts := 48.0
	var cols := int(ARENA.size.x / ts) + 1
	var rows := int(ARENA.size.y / ts) + 1
	for r in range(rows):
		for c in range(cols):
			var px := ARENA.position.x + c * ts
			var py := ARENA.position.y + r * ts
			var w := minf(ts, ARENA.end.x - px)
			var h := minf(ts, ARENA.end.y - py)
			if w <= 0.0 or h <= 0.0:
				continue
			var tex := grass_a if (r + c) % 2 == 0 else grass_b
			draw_texture_rect(tex, Rect2(px, py, w, h), false)
	for d in decor:
		draw_texture_rect(d["tex"], Rect2(d["pos"] - Vector2(12, 12), Vector2(24, 24)), false)
	draw_rect(Rect2(ARENA.position - Vector2(4, 4), ARENA.size + Vector2(8, 8)), Color(0.09, 0.07, 0.12), false, 4.0)
	draw_rect(ARENA, Color(0.32, 0.48, 0.28), false, 2.0)

func _make_grass_tex(variant: int) -> ImageTexture:
	var img := Image.create(16, 16, false, Image.FORMAT_RGBA8)
	var base := Color8(44, 74, 42) if variant == 0 else Color8(40, 68, 38)
	img.fill(base)
	# morka jordflackar + ljusa grasstran i fasta monster (deterministiskt)
	var dark := base.darkened(0.22)
	var light := base.lightened(0.28)
	for i in range(6):
		var fx := (i * 5 + variant * 3) % 14 + 1
		var fy := (i * 7 + variant * 5) % 14 + 1
		img.set_pixel(fx, fy, dark)
	for i in range(4):
		var sx := (i * 4 + variant * 6 + 2) % 14 + 1
		var sy := (i * 9 + variant * 2 + 3) % 12 + 2
		img.set_pixel(sx, sy, light)
		img.set_pixel(sx, sy - 1, light)
	return ImageTexture.create_from_image(img)

func _make_decor_tex(kind: int) -> ImageTexture:
	var img := Image.create(12, 12, false, Image.FORMAT_RGBA8)
	var outline := Color8(27, 22, 36)
	if kind == 0:
		# blomma: gul mitt + vita kronblad + kontur
		img.set_pixel(5, 5, Color8(240, 200, 60))
		img.set_pixel(6, 5, Color8(240, 200, 60))
		img.set_pixel(5, 6, Color8(240, 200, 60))
		img.set_pixel(6, 6, Color8(240, 200, 60))
		for p in [Vector2i(5, 3), Vector2i(6, 3), Vector2i(3, 5), Vector2i(3, 6), Vector2i(8, 5), Vector2i(8, 6), Vector2i(5, 8), Vector2i(6, 8)]:
			img.set_pixel(p.x, p.y, Color8(245, 240, 235))
	elif kind == 1:
		# sten: gra klump med ljus topp och kontur
		for y in range(5, 10):
			for x in range(3, 9):
				img.set_pixel(x, y, Color8(120, 118, 132))
		for x in range(4, 8):
			img.set_pixel(x, 4, Color8(120, 118, 132))
			img.set_pixel(x, 5, Color8(158, 156, 170))
		for x in range(3, 9):
			img.set_pixel(x, 10, outline)
		img.set_pixel(2, 9, outline)
		img.set_pixel(9, 9, outline)
	else:
		# buske: tva grona klumpar med mork bas
		for y in range(5, 10):
			for x in range(2, 10):
				img.set_pixel(x, y, Color8(52, 96, 48))
		for x in range(3, 9):
			img.set_pixel(x, 4, Color8(52, 96, 48))
			img.set_pixel(x, 5, Color8(74, 128, 64))
		for x in range(2, 10):
			img.set_pixel(x, 10, outline)
	return ImageTexture.create_from_image(img)

# ---------- overlays ----------
func show_title() -> void:
	state = "title"
	queue_redraw()
	show_overlay("THE GLADE", "Survive %d waves. WASD/arrow keys to move.\nBest: %d" % [FINAL_WAVE, load_highscore()], true)

func show_overlay(title: String, message: String, with_buttons: bool) -> void:
	position = Vector2.ZERO  # C1 juice: snappa tillbaka varlden nar en overlay visas
	shake = 0.0
	close_overlay()
	overlay = Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	hud.add_child(overlay)
	var bg := ColorRect.new()
	bg.color = Color(0, 0, 0, 0.65)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.add_child(bg)
	var box := VBoxContainer.new()
	# FULL_RECT + centrerad alignment - PRESET_CENTER satter bara pivoten
	# och hogerforskjuter innehallet (v2.12-fyndet, fixat har v2.20).
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 42)
	t.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.7))
	t.add_theme_constant_override("outline_size", 10)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	m.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(m)
	if with_buttons:
		var first := true
		for entry in [["Easy", 0], ["Normal", 1], ["Hard", 2]]:
			var b := Button.new()
			b.text = "Start: " + str(entry[0])
			b.custom_minimum_size = Vector2(320, 46)
			b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
			var diff: int = entry[1]
			b.pressed.connect(func():
				play_sound("click")
				new_game(diff))
			box.add_child(b)
			# Forsta knappen far fokus -> Enter startar (tangentbords-
			# spelbart, och grindens sond kommer forbi titelskarmen).
			if first:
				first = false
				b.grab_focus()
	# v2.22 spelskalet: Options (volym/mute/fullskarm, sparas) + Quit -
	# bara pa titeln, aldrig pa paus/game over-overlays.
	if with_buttons and state == "title":
		var ob := Button.new()
		ob.text = "Options"
		ob.custom_minimum_size = Vector2(320, 46)
		ob.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		ob.pressed.connect(func():
			play_sound("click")
			open_options())
		box.add_child(ob)
		var qb := Button.new()
		qb.text = "Quit"
		qb.custom_minimum_size = Vector2(320, 46)
		qb.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		qb.pressed.connect(func(): get_tree().quit())
		box.add_child(qb)

func open_options() -> void:
	close_overlay()
	overlay = Shell.options_panel(hud, func():
		play_sound("click")
		show_title())

func close_overlay() -> void:
	if overlay:
		overlay.queue_free()
		overlay = null
""";

    static string GodotPlatformerDesignDoc(string prompt) =>
        "# 2D Plattformare (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nHoppa, stampa och samla dig genom 3 nivaer till mal-flaggan.\n\n" +
        "## Mekanik\n- **Rorelse:** vanster/hoger + hopp med gravitation, coyotetid och hoppbuffert (tight kansla)\n" +
        "- **Variabelt hopp:** slapp knappen tidigt = kort hopp\n" +
        "- **Fiender:** patrullerar; kontakt kostar HP (ododlighetsfonster efterat) - hoppa PA dem for att stampa (+20, studs)\n" +
        "- **Mynt:** +10; **Mal-flaggan:** klarar nivan (+50)\n- **Fall:** utanfor skarmen kostar HP och ateruppstar vid nivastart\n" +
        "- **Nivaer:** 3 st med okande layoutsvarighet och fiendefart (niva 2 har ett dodligt gap)\n" +
        "- **Svarighetsgrader:** Latt/Medel/Svar paverkar HP och fiendefart\n- **Vinst:** klara niva 3; **Forlust:** 0 HP\n\n" +
        "## Produktion\n- Titelskarm med svarighetsval, paus (Esc), vinst/forlust-overlay med omstart (R), highscore (user://)\n" +
        "- Juice: screenshake, partikelskurar (hopp/landning/mynt/stamp/traff/mal), traff-blink och i-frames\n" +
        "- Ljud: hopp, mynt, traff, seger (sfxr-wav, inga externa filer)\n- Sprites genereras i kod - inga externa bilder\n\n" +
        "## Extension (tema-exempel)\n- Fler nivaer, rorliga plattformar, power-ups (dubbelhopp), boss pa niva 3, scrollande varld med Camera2D\n";

    // ---- Main.gd: plattformare (Pixel Rush) --------------------------------
    const string GodotPlatformerMain = """
extends Node2D
# Pixel Rush PREMIUM - arkadgolvet pa riktigt: lagrad bakgrund (kullar/moln),
# 5 designade banor (lar -> testa -> vrid), vittrande plattformar, studs-
# plattor, medaljer (guld/silver/brons), overgangar, volymkontroll, coyote-
# tid + hoppbuffert + variabel hopphojd + acceleration, landnings-squash.
# BYT TEMA: farger/paletter i level_data + make_texture-anropen + texterna.

# Preload (inte class_name-globalen): kitet ska parsa AVEN fore forsta
# importen, och class_name-registret finns forst efter import.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://pixelrush_highscore.save"
const MEDALS_PATH := "user://pixelrush_medals.save"
const FINAL_LEVEL := 5
const GRAVITY := 1400.0
const MOVE_SPEED := 320.0
const GROUND_ACCEL := 2600.0
const AIR_ACCEL := 1700.0
const JUMP_VELOCITY := -620.0
const SPRING_VELOCITY := -940.0

var difficulty := 1
var state := "title" # title | playing | paused | over
var player: CharacterBody2D
var enemies: Array = []   # [{body, min_x, max_x, dir}]
var coins: Array = []
var platforms: Array = []
var crumblers: Array = [] # [{body, sprite, shape, state, t, rect}]
var springs: Array = []   # [{pos, t}]
var goal: Sprite2D
var level := 0
var hp := 3
var score := 0
var invulnerable := 0.0
var shake := 0.0
var coyote := 0.0
var jump_buffer := 0.0
var jump_was_down := false
var touch_jump := false
var was_on_floor := false
var spawn_point := Vector2(60, 540)
var sky_top := Color(0.30, 0.52, 0.86)
var sky_bottom := Color(0.66, 0.82, 0.95)
var hill_far := Color(0.42, 0.60, 0.80)
var hill_near := Color(0.32, 0.50, 0.68)
var clouds: Array = []    # [{x, y, speed, s}]
var t_global := 0.0
var fade := 1.0           # 1 = svart, 0 = klart; tonas i _process
var fade_target := 0.0
var level_card := 0.0     # "LEVEL N"-kortets kvarvarande tid
var muted := false
var volume_db := 0.0
# Medaljer: {"1": "gold" | "silver" | "bronze"} - villkor spars per bana.
var medals := {}
var level_damage := 0
var level_coins_total := 0
var level_coins_taken := 0
var hud: CanvasLayer
var hud_label: Label
var fade_rect: ColorRect
var card_label: Label
var overlay: Control
var snd := {}
# Autopilot (AILOCAL_AUTOPILOT=1): kvalitetsgrindens demospelare - startar
# fran titeln och spelar sjalv sa sondens dumpar visar riktigt spelande.
# Vanliga spelare har aldrig variabeln satt och markar ingenting.
var autopilot := false
var auto_t := 0.0
var cam: Camera2D

func _ready() -> void:
	randomize()
	autopilot = OS.get_environment("AILOCAL_AUTOPILOT") == "1"
	# Spelskalet: sparade installningar (volym/mute/fullskarm) galler direkt.
	Shell.startup()
	# Pixelart = skarpa pixlar (nearest aven i gamla projekt utan mallraden).
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	# Kameran finns BARA for screenshake (offset ror renderingen, aldrig
	# fysiken). Fixed-top-left vid (0,0) = exakt samma vy som utan kamera.
	cam = Camera2D.new()
	cam.anchor_mode = Camera2D.ANCHOR_MODE_FIXED_TOP_LEFT
	add_child(cam)
	cam.make_current()
	for key in ["click","coin","hurt","win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
	# Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
	var music = load("res://music.wav")
	if music:
		var mp := AudioStreamPlayer.new()
		mp.stream = music
		mp.volume_db = -14.0
		mp.finished.connect(mp.play)
		add_child(mp)
		mp.play()
	for i in range(7):
		clouds.append({"x": randf() * 1300.0 - 80.0, "y": 40.0 + randf() * 190.0,
			"speed": 6.0 + randf() * 14.0, "s": 0.7 + randf() * 0.9})
	hud = CanvasLayer.new()
	add_child(hud)
	hud_label = Label.new()
	hud_label.position = Vector2(20, 12)
	hud_label.add_theme_font_size_override("font_size", 19)
	hud_label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.7))
	hud_label.add_theme_constant_override("outline_size", 8)
	hud.add_child(hud_label)
	# Overgangslagret: svart fade + "LEVEL N"-kort, over allt annat.
	fade_rect = ColorRect.new()
	fade_rect.color = Color(0.06, 0.05, 0.10, 1.0)
	fade_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
	fade_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(fade_rect)
	card_label = Label.new()
	card_label.add_theme_font_size_override("font_size", 52)
	card_label.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.8))
	card_label.add_theme_constant_override("outline_size", 12)
	card_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	card_label.size = Vector2(1152, 70)
	card_label.position = Vector2(0, 280)
	card_label.visible = false
	hud.add_child(card_label)
	_load_medals()
	_setup_touch()
	show_title()

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
	if not DisplayServer.is_touchscreen_available():
		return
	_touch_btn(Vector2(28, 536), "ui_left", "<")
	_touch_btn(Vector2(132, 536), "ui_right", ">")
	var jump := _touch_btn(Vector2(1036, 536), "", "JUMP")
	jump.pressed.connect(func(): touch_jump = true)
	jump.released.connect(func(): touch_jump = false)

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
	var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
	img2.fill(Color(1, 1, 1, 0.20))
	for x in range(88):
		for y in range(88):
			if x < 3 or y < 3 or x > 84 or y > 84:
				img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
	var b := TouchScreenButton.new()
	b.texture_normal = ImageTexture.create_from_image(img2)
	b.position = pos
	if action != "":
		b.action = action
	hud.add_child(b)
	var t := Label.new()
	t.text = text
	t.add_theme_font_size_override("font_size", 24)
	t.position = pos
	t.size = Vector2(88, 88)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(t)
	return b

func play_sound(key: String) -> void:
	if snd.has(key):
		snd[key].play()

# ---------- ljudkontroll (volym + mute - arkadribban kraver den) ----------
func _apply_volume() -> void:
	var bus := AudioServer.get_bus_index("Master")
	AudioServer.set_bus_mute(bus, muted)
	AudioServer.set_bus_volume_db(bus, volume_db)

# C1 juice: en engangs-partikelskur. HUD/overlay ligger pa en CanvasLayer och
# paverkas inte av att varldens Node2D (self) skakas.
func spawn_burst(pos: Vector2, col: Color, count: int) -> void:
	var p := CPUParticles2D.new()
	p.position = pos
	p.amount = count
	p.one_shot = true
	p.explosiveness = 0.9
	p.lifetime = 0.5
	p.spread = 180.0
	p.initial_velocity_min = 70.0
	p.initial_velocity_max = 180.0
	p.gravity = Vector2(0, 300)
	p.scale_amount_min = 2.0
	p.scale_amount_max = 4.0
	p.color = col
	p.texture = make_texture(6, Color(1, 1, 1), Color(1, 1, 1))
	add_child(p)
	p.emitting = true
	get_tree().create_timer(1.0).timeout.connect(p.queue_free)

func make_texture(size: int, base: Color, accent: Color) -> ImageTexture:
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	img.fill(base)
	for x in range(size):
		for y in range(size):
			if x == 0 or y == 0 or x == size - 1 or y == size - 1:
				img.set_pixel(x, y, accent)
	return ImageTexture.create_from_image(img)

func make_player() -> CharacterBody2D:
	var body := CharacterBody2D.new()
	var spr := AnimatedSprite2D.new()
	spr.name = "Anim"
	spr.sprite_frames = load("res://player_frames.tres")
	spr.scale = Vector2(2, 2)
	spr.play("idle")
	body.add_child(spr)
	var shape := CollisionShape2D.new()
	var rect := RectangleShape2D.new()
	rect.size = Vector2(24, 24)
	shape.shape = rect
	body.add_child(shape)
	return body

# Fiender ar rena Node2D (ingen fysikkropp) - de blockerar aldrig spelaren;
# all interaktion (traff/stamp) gors med avstandskontroller i loopen.
func make_enemy() -> Node2D:
	var n := Node2D.new()
	var spr := AnimatedSprite2D.new()
	spr.name = "Anim"
	spr.sprite_frames = load("res://enemy_frames.tres")
	spr.scale = Vector2(2, 2)
	spr.play("walk")
	n.add_child(spr)
	return n

func make_platform(r: Rect2) -> StaticBody2D:
	var body := StaticBody2D.new()
	body.position = r.get_center()
	var shape := CollisionShape2D.new()
	var rect := RectangleShape2D.new()
	rect.size = r.size
	shape.shape = rect
	# Genomhoppbar underifran (klassisk plattformare) - MEN BARA svavande
	# plattor: one-way pa den tjocka MARKEN lat spelaren falla rakt igenom
	# varlden nar golvkontakten tappades en frame (v2.13-autopilotfyndet).
	shape.one_way_collision = r.position.y < 560.0
	body.add_child(shape)
	var spr := Sprite2D.new()
	spr.texture = make_texture(8, Color(0.35, 0.25, 0.2), Color(0.5, 0.8, 0.3))
	spr.scale = Vector2(r.size.x / 8.0, r.size.y / 8.0)
	body.add_child(spr)
	add_child(body)
	return body

func make_crumbler(r: Rect2) -> void:
	var body := StaticBody2D.new()
	body.position = r.get_center()
	var shape := CollisionShape2D.new()
	var rect := RectangleShape2D.new()
	rect.size = r.size
	shape.shape = rect
	shape.one_way_collision = true
	body.add_child(shape)
	var spr := Sprite2D.new()
	spr.texture = make_texture(8, Color(0.55, 0.45, 0.30), Color(0.85, 0.75, 0.45))
	spr.scale = Vector2(r.size.x / 8.0, r.size.y / 8.0)
	body.add_child(spr)
	add_child(body)
	crumblers.append({"body": body, "sprite": spr, "shape": shape, "state": "idle", "t": 0.0, "rect": r})

# ---------- nivadata: lar -> testa -> vrid ----------
# Hopphojden ar ~137 px - alla steg <= 120 px sa varje bana ar bevisat klarbar.
# Varje bana har egen palett (sky/hills) och EN ny ide:
#  1 LAR: hoppa + mynt, en langsam fiende.  2 TESTA: gap + tva fiender.
#  3 VRID: vittrande plattformar.  4 VRID: studsplattor + hojd.
#  5 FINAL: allt kombinerat, tight rutt.
func level_data(n: int) -> Dictionary:
	if n == 1:
		return {
			"plats": [Rect2(0, 600, 1152, 48), Rect2(180, 490, 200, 24), Rect2(470, 400, 190, 24), Rect2(760, 490, 200, 24)],
			"crumble": [], "springs": [],
			"coins": [Vector2(260, 454), Vector2(560, 364), Vector2(850, 454), Vector2(400, 564), Vector2(700, 564)],
			"enemies": [[Vector2(560, 576), 430.0, 700.0]],
			"goal": Vector2(1080, 560), "spawn": Vector2(60, 540),
			"sky_top": Color(0.30, 0.52, 0.86), "sky_bottom": Color(0.70, 0.85, 0.95),
			"hill_far": Color(0.45, 0.62, 0.82), "hill_near": Color(0.34, 0.52, 0.70)
		}
	if n == 2:
		return {
			"plats": [Rect2(0, 600, 360, 48), Rect2(470, 600, 220, 48), Rect2(800, 600, 352, 48), Rect2(300, 480, 150, 24), Rect2(600, 470, 150, 24), Rect2(900, 460, 150, 24)],
			"crumble": [], "springs": [],
			"coins": [Vector2(370, 444), Vector2(670, 434), Vector2(970, 424), Vector2(560, 564), Vector2(880, 564), Vector2(150, 564)],
			"enemies": [[Vector2(150, 576), 40.0, 320.0], [Vector2(900, 576), 820.0, 1110.0]],
			"goal": Vector2(1090, 420), "spawn": Vector2(50, 540),
			"sky_top": Color(0.86, 0.58, 0.38), "sky_bottom": Color(0.96, 0.82, 0.60),
			"hill_far": Color(0.80, 0.55, 0.45), "hill_near": Color(0.62, 0.42, 0.38)
		}
	if n == 3:
		return {
			"plats": [Rect2(0, 600, 300, 48), Rect2(852, 600, 300, 48), Rect2(180, 470, 140, 24), Rect2(850, 470, 140, 24)],
			"crumble": [Rect2(380, 520, 110, 20), Rect2(540, 450, 110, 20), Rect2(700, 520, 110, 20), Rect2(470, 340, 110, 20), Rect2(640, 340, 110, 20)],
			"springs": [],
			"coins": [Vector2(430, 484), Vector2(590, 414), Vector2(750, 484), Vector2(520, 304), Vector2(690, 304), Vector2(240, 434)],
			"enemies": [[Vector2(920, 576), 870.0, 1100.0]],
			"goal": Vector2(920, 430), "spawn": Vector2(50, 540),
			"sky_top": Color(0.26, 0.30, 0.52), "sky_bottom": Color(0.55, 0.50, 0.72),
			"hill_far": Color(0.40, 0.40, 0.62), "hill_near": Color(0.30, 0.30, 0.50)
		}
	if n == 4:
		return {
			"plats": [Rect2(0, 600, 1152, 48), Rect2(200, 470, 150, 24), Rect2(520, 360, 150, 24), Rect2(840, 250, 150, 24)],
			"crumble": [Rect2(380, 250, 100, 20)],
			"springs": [Vector2(430, 580), Vector2(740, 580)],
			"coins": [Vector2(270, 434), Vector2(590, 324), Vector2(910, 214), Vector2(430, 480), Vector2(740, 440), Vector2(430, 214)],
			"enemies": [[Vector2(300, 576), 120.0, 560.0], [Vector2(900, 576), 700.0, 1080.0]],
			"goal": Vector2(1080, 210), "spawn": Vector2(60, 540),
			"sky_top": Color(0.15, 0.42, 0.48), "sky_bottom": Color(0.55, 0.80, 0.75),
			"hill_far": Color(0.30, 0.55, 0.55), "hill_near": Color(0.22, 0.44, 0.44)
		}
	return {
		"plats": [Rect2(0, 600, 260, 48), Rect2(892, 600, 260, 48), Rect2(220, 480, 130, 24), Rect2(800, 480, 130, 24)],
		"crumble": [Rect2(400, 540, 100, 20), Rect2(560, 470, 100, 20), Rect2(720, 540, 100, 20), Rect2(500, 340, 100, 20)],
		"springs": [Vector2(300, 580), Vector2(850, 460)],
		"coins": [Vector2(450, 504), Vector2(610, 434), Vector2(770, 504), Vector2(550, 304), Vector2(300, 434), Vector2(950, 344), Vector2(120, 564), Vector2(1030, 564)],
		"enemies": [[Vector2(140, 576), 40.0, 240.0], [Vector2(1000, 576), 910.0, 1120.0], [Vector2(860, 456), 810.0, 920.0]],
		"goal": Vector2(1000, 300), "spawn": Vector2(50, 540),
		"sky_top": Color(0.42, 0.20, 0.44), "sky_bottom": Color(0.90, 0.55, 0.45),
		"hill_far": Color(0.60, 0.35, 0.48), "hill_near": Color(0.45, 0.26, 0.40)
	}

# ---------- flode ----------
func new_game(diff: int) -> void:
	difficulty = diff
	hp = [5, 3, 2][diff]
	score = 0
	level = 0
	state = "playing"
	close_overlay()
	next_level()

func clear_entities() -> void:
	for e in enemies:
		e["body"].queue_free()
	for c in coins:
		c.queue_free()
	for p in platforms:
		p.queue_free()
	for cr in crumblers:
		cr["body"].queue_free()
	if goal:
		goal.queue_free()
		goal = null
	enemies = []
	coins = []
	platforms = []
	crumblers = []
	springs = []

func next_level() -> void:
	# Medalj for banan som just klarades (inte vid forsta anropet).
	if level >= 1:
		_award_medal()
	level += 1
	if level > FINAL_LEVEL:
		finish(true)
		return
	clear_entities()
	var data := level_data(level)
	sky_top = data["sky_top"]
	sky_bottom = data["sky_bottom"]
	hill_far = data["hill_far"]
	hill_near = data["hill_near"]
	queue_redraw()
	for r in data["plats"]:
		platforms.append(make_platform(r))
	for cr in data["crumble"]:
		make_crumbler(cr)
	for sp in data["springs"]:
		springs.append({"pos": sp, "t": 0.0})
	for cpos in data["coins"]:
		var c := Sprite2D.new()
		c.texture = make_texture(7, Color(0.95, 0.8, 0.2), Color(1, 1, 0.7))
		c.scale = Vector2(2, 2)
		c.position = cpos
		c.set_meta("base_y", cpos.y)
		add_child(c)
		coins.append(c)
	for def in data["enemies"]:
		var body := make_enemy()
		var pos: Vector2 = def[0]
		body.position = pos
		add_child(body)
		enemies.append({"body": body, "min_x": def[1], "max_x": def[2], "dir": 1.0})
	goal = Sprite2D.new()
	goal.texture = make_texture(8, Color(0.2, 0.85, 0.4), Color(0.9, 1, 0.9))
	goal.scale = Vector2(3, 5)
	goal.position = data["goal"]
	add_child(goal)
	spawn_point = data["spawn"]
	if player == null:
		player = make_player()
		add_child(player)
	player.position = spawn_point
	player.velocity = Vector2.ZERO
	invulnerable = 0.0
	level_damage = 0
	level_coins_total = data["coins"].size()
	level_coins_taken = 0
	var panim := player.get_node("Anim") as AnimatedSprite2D
	if panim:
		panim.visible = true
		panim.modulate = Color.WHITE
	# Overgang: snabb svartton + "LEVEL N"-kort.
	fade = 1.0
	fade_target = 0.0
	level_card = 1.1
	card_label.text = "LEVEL %d" % level
	card_label.visible = true
	play_sound("click")

func _award_medal() -> void:
	var tier := "bronze"
	if level_coins_taken >= level_coins_total and level_damage == 0:
		tier = "gold"
	elif level_coins_taken >= level_coins_total:
		tier = "silver"
	var key := str(level)
	var rank := {"bronze": 1, "silver": 2, "gold": 3}
	var old: String = medals.get(key, "")
	if old == "" or int(rank.get(tier, 0)) > int(rank.get(old, 0)):
		medals[key] = tier
		_save_medals()

func finish(won: bool) -> void:
	if won:
		_award_medal()
	state = "over"
	if won:
		play_sound("win")
	var best := load_highscore()
	if score > best:
		save_highscore(score)
		best = score
	show_overlay("YOU WIN!" if won else "GAME OVER", "Score: %d   Best: %d\nR: play again" % [score, best], true)

# ---------- highscore + medaljer ----------
func load_highscore() -> int:
	if not FileAccess.file_exists(SAVE_PATH):
		return 0
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	return int(f.get_as_text()) if f else 0

func save_highscore(value: int) -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(str(value))

func _load_medals() -> void:
	medals = {}
	if not FileAccess.file_exists(MEDALS_PATH):
		return
	var f := FileAccess.open(MEDALS_PATH, FileAccess.READ)
	if f:
		var data: Variant = JSON.parse_string(f.get_as_text())
		if typeof(data) == TYPE_DICTIONARY:
			medals = data

func _save_medals() -> void:
	var f := FileAccess.open(MEDALS_PATH, FileAccess.WRITE)
	if f:
		f.store_string(JSON.stringify(medals))

# ---------- loop ----------
func _process(delta: float) -> void:
	t_global += delta
	# Moln driver alltid - varlden lever aven pa titeln.
	for c in clouds:
		c["x"] = float(c["x"]) + float(c["speed"]) * delta
		if float(c["x"]) > 1260.0:
			c["x"] = -140.0
	# Overgangar: fade + nivakort.
	fade = move_toward(fade, fade_target, 2.6 * delta)
	fade_rect.color.a = fade
	if level_card > 0.0:
		level_card -= delta
		if level_card <= 0.0:
			card_label.visible = false
	# Autopilot: vanta forbi sondens titeldump (~3 s), starta sedan sjalv;
	# efter game over startas en ny runda sa demon aldrig fastnar.
	if autopilot:
		if state == "playing":
			auto_t = 0.0
		else:
			auto_t += delta
		if state == "title" and auto_t > 5.0:
			auto_t = 0.0
			new_game(1)
		elif state == "over" and auto_t > 2.5:
			auto_t = 0.0
			new_game(difficulty)
	queue_redraw()

func _physics_process(delta: float) -> void:
	if state != "playing" or player == null:
		return
	# Screenshake pa KAMERAN, aldrig pa rot-noden: rotflytt teleporterar
	# fysikkropparna varje frame och slog spelaren ur golvkontakt
	# (v2.13-autopilotfyndet - genomfall genom marken vid varje skak).
	if shake > 0.0:
		shake = move_toward(shake, 0.0, 32.0 * delta)
		cam.offset = Vector2(randf_range(-shake, shake), randf_range(-shake, shake))
	elif cam.offset != Vector2.ZERO:
		cam.offset = Vector2.ZERO
	# ---- rorelse: acceleration ger tyngd, coyote + buffert ger valvilja ----
	var ax := Input.get_axis("ui_left", "ui_right")
	if ax == 0.0:
		if Input.is_physical_key_pressed(KEY_A):
			ax = -1.0
		elif Input.is_physical_key_pressed(KEY_D):
			ax = 1.0
	# Autopilot-styrningen: spring mot flaggan; strala fram/ner fran kanten -
	# gap eller vagg (eller fastnad) => hoppa. Hallt hopp under stigningen
	# sa variabla hopphojden inte kapar spranget vid -220.
	var auto_jump := false
	if autopilot and player.is_on_floor():
		var dirx := 1.0
		if goal != null and absf(goal.position.x - player.position.x) > 24.0:
			dirx = signf(goal.position.x - player.position.x)
		ax = dirx
		var space := get_world_2d().direct_space_state
		var edge := player.position + Vector2(dirx * 56.0, -4.0)
		var gq := PhysicsRayQueryParameters2D.create(edge, edge + Vector2(0, 120.0))
		gq.exclude = [player.get_rid()]
		var wq := PhysicsRayQueryParameters2D.create(player.position + Vector2(0, -6.0),
			player.position + Vector2(dirx * 46.0, -6.0))
		wq.exclude = [player.get_rid()]
		auto_jump = space.intersect_ray(gq).is_empty() \
			or not space.intersect_ray(wq).is_empty() \
			or absf(player.velocity.x) < 30.0
	elif autopilot:
		ax = 1.0 if goal == null else signf(goal.position.x - player.position.x)
		auto_jump = player.velocity.y < -60.0
	var accel := GROUND_ACCEL if player.is_on_floor() else AIR_ACCEL
	player.velocity.x = move_toward(player.velocity.x, ax * MOVE_SPEED, accel * delta)
	player.velocity.y = minf(player.velocity.y + GRAVITY * delta, 980.0)
	var jump_down := Input.is_physical_key_pressed(KEY_SPACE) \
		or Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP) \
		or touch_jump or auto_jump
	if jump_down and not jump_was_down:
		jump_buffer = 0.12
	if not jump_down and jump_was_down and player.velocity.y < -220.0:
		player.velocity.y = -220.0  # slappt tidigt = kort hopp (variabel hojd)
	jump_was_down = jump_down
	coyote = 0.14 if player.is_on_floor() else maxf(coyote - delta, 0.0)
	jump_buffer = maxf(jump_buffer - delta, 0.0)
	if jump_buffer > 0.0 and coyote > 0.0:
		player.velocity.y = JUMP_VELOCITY
		jump_buffer = 0.0
		coyote = 0.0
		play_sound("click")
		spawn_burst(player.position + Vector2(0, 20), Color(0.9, 0.9, 0.85), 6)
	player.move_and_slide()
	var on_floor := player.is_on_floor()
	var anim := player.get_node("Anim") as AnimatedSprite2D
	if on_floor and not was_on_floor:
		spawn_burst(player.position + Vector2(0, 20), Color(0.8, 0.75, 0.7), 8)
		shake = maxf(shake, 2.0)
		if anim:
			anim.scale = Vector2(2.5, 1.6)  # landnings-squash
	was_on_floor = on_floor
	if anim:
		anim.scale = anim.scale.lerp(Vector2(2, 2), 12.0 * delta)
		if absf(player.velocity.x) > 10.0:
			anim.play("walk")
			anim.flip_h = player.velocity.x < 0.0
		else:
			anim.play("idle")
	if invulnerable > 0.0:
		invulnerable -= delta
		if anim:
			anim.visible = fmod(invulnerable, 0.2) < 0.12
			anim.modulate = anim.modulate.lerp(Color.WHITE, 6.0 * delta)
			if invulnerable <= 0.0:
				anim.visible = true
				anim.modulate = Color.WHITE
	# ---- vittrande plattformar: sta pa -> skakar -> faller -> ater ----
	for cr in crumblers:
		var st: String = cr["state"]
		var body2: StaticBody2D = cr["body"]
		var rct: Rect2 = cr["rect"]
		if st == "idle":
			if on_floor and absf(player.position.x - rct.get_center().x) < rct.size.x * 0.5 + 12.0 \
				and absf((player.position.y + 12.0) - rct.position.y) < 14.0:
				cr["state"] = "shaking"
				cr["t"] = 0.45
		elif st == "shaking":
			cr["t"] = float(cr["t"]) - delta
			(cr["sprite"] as Sprite2D).offset = Vector2(randf_range(-2, 2), 0)
			if float(cr["t"]) <= 0.0:
				cr["state"] = "gone"
				cr["t"] = 2.6
				body2.visible = false
				(cr["shape"] as CollisionShape2D).set_deferred("disabled", true)
				spawn_burst(rct.get_center(), Color(0.7, 0.6, 0.4), 10)
				play_sound("hurt")
		elif st == "gone":
			cr["t"] = float(cr["t"]) - delta
			if float(cr["t"]) <= 0.0:
				cr["state"] = "idle"
				body2.visible = true
				(cr["sprite"] as Sprite2D).offset = Vector2.ZERO
				(cr["shape"] as CollisionShape2D).set_deferred("disabled", false)
	# ---- studsplattor ----
	for sp in springs:
		sp["t"] = maxf(float(sp["t"]) - delta, 0.0)
		var spos: Vector2 = sp["pos"]
		if player.velocity.y > 40.0 and player.position.distance_to(spos + Vector2(0, -14)) < 30.0:
			player.velocity.y = SPRING_VELOCITY
			sp["t"] = 0.25
			play_sound("coin")
			shake = maxf(shake, 4.0)
			spawn_burst(spos, Color(0.5, 1.0, 0.9), 14)
	# ---- fiender: patrull + stamp/traff ----
	var speed := 46.0 + level * 9.0 + difficulty * 14.0
	var alive: Array = []
	for e in enemies:
		var body: Node2D = e["body"]
		body.position.x += e["dir"] * speed * delta
		if body.position.x < e["min_x"]:
			e["dir"] = 1.0
		elif body.position.x > e["max_x"]:
			e["dir"] = -1.0
		var ea := body.get_node("Anim") as AnimatedSprite2D
		if ea:
			ea.flip_h = e["dir"] < 0.0
		var to_player := player.position - body.position
		var stomped := false
		if absf(to_player.x) < 26.0 and absf(to_player.y) < 32.0:
			if player.velocity.y > 120.0 and to_player.y < -8.0:
				stomped = true
				score += 20
				player.velocity.y = -430.0
				play_sound("coin")
				shake = maxf(shake, 5.0)
				spawn_burst(body.position, Color(0.9, 0.5, 0.9), 14)
				body.queue_free()
			elif invulnerable <= 0.0:
				damage()
				if state != "playing":
					return
		if not stomped:
			alive.append(e)
	enemies = alive
	# ---- mynt (bobbar i luften) ----
	var remaining: Array = []
	for c in coins:
		c.position.y = float(c.get_meta("base_y")) + sin(t_global * 3.0 + c.position.x * 0.05) * 3.0
		if c.position.distance_to(player.position) < 30.0:
			score += 10
			level_coins_taken += 1
			play_sound("coin")
			spawn_burst(c.position, Color(1, 0.9, 0.3), 12)
			shake = maxf(shake, 3.0)
			c.queue_free()
		else:
			remaining.append(c)
	coins = remaining
	# ---- mal-flaggan (vajar) ----
	if goal:
		goal.scale.x = 3.0 + sin(t_global * 4.0) * 0.35
		if player.position.distance_to(goal.position) < 40.0:
			score += 50
			play_sound("coin")
			shake = maxf(shake, 6.0)
			spawn_burst(goal.position, Color(0.4, 1, 0.6), 20)
			next_level()
			return
	# ---- fall utanfor skarmen ----
	if player.position.y > 720.0:
		damage()
		if state != "playing":
			return
		player.position = spawn_point
		player.velocity = Vector2.ZERO
	hud_label.text = "HP: %d   Score: %d   Level: %d/%d" % [hp, score, mini(level, FINAL_LEVEL), FINAL_LEVEL]

func damage() -> void:
	hp -= 1
	level_damage += 1
	invulnerable = 1.2
	play_sound("hurt")
	shake = 9.0
	spawn_burst(player.position, Color(1, 0.3, 0.3), 18)
	var anim := player.get_node("Anim") as AnimatedSprite2D
	if anim:
		anim.modulate = Color(1, 0.35, 0.35)
	if hp <= 0:
		finish(false)

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if state == "playing":
			state = "paused"
			show_overlay("PAUSED", "Esc: resume", false)
		elif state == "paused":
			state = "playing"
			close_overlay()
	if event is InputEventKey and event.pressed and not event.echo:
		if event.keycode == KEY_R and state == "over":
			new_game(difficulty)
		elif event.keycode == KEY_M:
			muted = not muted
			_apply_volume()
		elif event.keycode == KEY_MINUS:
			volume_db = maxf(volume_db - 3.0, -24.0)
			_apply_volume()
		elif event.keycode == KEY_EQUAL:
			volume_db = minf(volume_db + 3.0, 6.0)
			_apply_volume()

# ---------- ritning: lagrad varld i stallet for platt himmel ----------
func _draw() -> void:
	# Himmelsgradient (band) + sol med glod.
	var bands := 18
	for i in range(bands):
		var tt := float(i) / float(bands - 1)
		draw_rect(Rect2(0, 648.0 * float(i) / float(bands), 1152, 648.0 / float(bands) + 1.0),
			sky_top.lerp(sky_bottom, tt))
	draw_circle(Vector2(1010, 90), 58.0, Color(1, 0.95, 0.75, 0.25))
	draw_circle(Vector2(1010, 90), 42.0, Color(1, 0.92, 0.6))
	# Moln (driver i _process).
	for c in clouds:
		var cx: float = c["x"]
		var cy: float = c["y"]
		var cs: float = c["s"]
		draw_circle(Vector2(cx, cy), 26.0 * cs, Color(1, 1, 1, 0.55))
		draw_circle(Vector2(cx + 24.0 * cs, cy + 6.0 * cs), 20.0 * cs, Color(1, 1, 1, 0.50))
		draw_circle(Vector2(cx - 24.0 * cs, cy + 7.0 * cs), 18.0 * cs, Color(1, 1, 1, 0.50))
	# Kullar i tva lager - djup utan kamera.
	var far := PackedVector2Array()
	far.append(Vector2(0, 648))
	for i in range(9):
		var x := float(i) * 144.0
		far.append(Vector2(x, 470.0 + sin(float(i) * 1.7 + 0.8) * 55.0))
	far.append(Vector2(1152, 648))
	draw_colored_polygon(far, hill_far)
	var near := PackedVector2Array()
	near.append(Vector2(0, 648))
	for i in range(7):
		var x2 := float(i) * 192.0
		near.append(Vector2(x2, 540.0 + sin(float(i) * 2.3) * 45.0))
	near.append(Vector2(1152, 648))
	draw_colored_polygon(near, hill_near)
	# Studsplattor ritas i varlden (fjaderputs som trycks ihop).
	for sp in springs:
		var spos: Vector2 = sp["pos"]
		var squish: float = 1.0 - float(sp["t"]) * 1.6
		draw_rect(Rect2(spos.x - 22, spos.y - 8.0 * squish, 44, 8.0 * squish + 4.0), Color(0.20, 0.75, 0.65))
		draw_rect(Rect2(spos.x - 22, spos.y - 8.0 * squish, 44, 4), Color(0.65, 1.0, 0.9))

# ---------- overlays ----------
func show_title() -> void:
	state = "title"
	sky_top = Color(0.30, 0.52, 0.86)
	sky_bottom = Color(0.70, 0.85, 0.95)
	hill_far = Color(0.45, 0.62, 0.82)
	hill_near = Color(0.34, 0.52, 0.70)
	fade = 0.0
	fade_target = 0.0
	queue_redraw()
	show_overlay("PIXEL RUSH", "Reach the flag across %d levels. Arrows/A-D: move, Space/W: jump.\nStomp enemies, ride the springs, mind the crumbling ledges.\nBest: %d      M: mute   -/+: volume" % [FINAL_LEVEL, load_highscore()], true)

func show_overlay(title: String, message: String, with_buttons: bool) -> void:
	if cam:
		cam.offset = Vector2.ZERO
	shake = 0.0
	close_overlay()
	overlay = Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	hud.add_child(overlay)
	var bg := ColorRect.new()
	bg.color = Color(0.03, 0.04, 0.10, 0.55)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.add_child(bg)
	var box := VBoxContainer.new()
	# FULL_RECT + centrerad alignment i stallet for PRESET_CENTER: center-
	# preseten satter bara PIVOTEN i mitten sa innehallet vaxer at hoger -
	# layouten blev hogerforskjuten (sags i skarp korning).
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	# Logotypkansla: stor rubrik med kraftig kontur + skuggrad under.
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 64)
	t.add_theme_color_override("font_color", Color(1.0, 0.85, 0.25))
	t.add_theme_color_override("font_outline_color", Color(0.25, 0.12, 0.05))
	t.add_theme_constant_override("outline_size", 16)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	m.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.6))
	m.add_theme_constant_override("outline_size", 6)
	m.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(m)
	if with_buttons and state == "title":
		var medal_row := Label.new()
		medal_row.text = _medal_row_text()
		medal_row.add_theme_font_size_override("font_size", 15)
		medal_row.add_theme_color_override("font_color", Color(1.0, 0.9, 0.6))
		medal_row.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.6))
		medal_row.add_theme_constant_override("outline_size", 6)
		medal_row.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		box.add_child(medal_row)
	if with_buttons:
		var first := true
		for entry in [["Easy", 0], ["Normal", 1], ["Hard", 2]]:
			var b := Button.new()
			b.text = "Start: " + str(entry[0])
			b.custom_minimum_size = Vector2(320, 46)
			b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
			var diff: int = entry[1]
			b.pressed.connect(func():
				play_sound("click")
				new_game(diff))
			box.add_child(b)
			if first:
				first = false
				b.grab_focus()
	# v2.15 spelskalet: Options (volym/mute/fullskarm, sparas) + Quit -
	# bara pa titeln, aldrig pa paus/game over-overlays.
	if with_buttons and state == "title":
		var ob := Button.new()
		ob.text = "Options"
		ob.custom_minimum_size = Vector2(320, 46)
		ob.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		ob.pressed.connect(func():
			play_sound("click")
			open_options())
		box.add_child(ob)
		var qb := Button.new()
		qb.text = "Quit"
		qb.custom_minimum_size = Vector2(320, 46)
		qb.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		qb.pressed.connect(func(): get_tree().quit())
		box.add_child(qb)

func open_options() -> void:
	close_overlay()
	overlay = Shell.options_panel(hud, func():
		play_sound("click")
		show_title())

func _medal_row_text() -> String:
	var icons := {"gold": "[G]", "silver": "[S]", "bronze": "[b]"}
	var parts: Array = []
	for i in range(1, FINAL_LEVEL + 1):
		var m: String = medals.get(str(i), "")
		parts.append("L%d %s" % [i, icons.get(m, "--")])
	return "Medals:  " + "   ".join(parts)

func close_overlay() -> void:
	if overlay:
		overlay.queue_free()
		overlay = null
""";

    static string GodotArtilleryDesignDoc(string prompt) =>
        "# Artilleriduell (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nTurbaserad artilleriduell i ShellShock Live/Worms-klassen: sikta med vinkel+kraft,\n" +
        "kompensera for vinden, sprang kratrar i den FORSTORBARA terrangen och besegra\n" +
        "en stege av allt traffsakrare AI-motstandare.\n\n" +
        "## Mekanik\n- **Turordning:** du skjuter, sedan AI:n - vinden slumpas om varje tur och visas i HUD:en\n" +
        "- **Ballistik:** projektiler med gravitation + vindavdrift; siktlinje visar de forsta metrarna\n" +
        "- **Forstorbar terrang:** varje traff spranger en krater (pixelbaserad terrang); tankar faller nar marken forsvinner (fallskada!)\n" +
        "- **Tre vapen:** Granat (standard), Storbomb (storre krater, begransad ammo), Trippel (tre spridda skott) - byt med 1-3\n" +
        "- **Motstandarstege:** Rekryten -> Kaptenen -> Generalen, stigande traffsakerhet och HP; besegra alla tre for att vinna\n" +
        "- **AI:** provskjuter kandidater mot din position och lar sig mellan skotten; siktfel krymper per niva\n" +
        "- **Vinst/forlust:** motstandarens HP 0 = nasta duell; din HP 0 = game over; highscore = langsta segersvit (user://)\n\n" +
        "## Produktion\n- Titelskarm med instruktioner, paus (Esc), game over med omstart (R), segersvit sparad\n" +
        "- Juice: screenshake skalad efter smallen, kraterpartiklar, mynningsflamma, rekyl, projektilspar, HP-blink\n" +
        "- Ljud: skott, explosion, vapenbyte, seger (sfxr-wav)\n- Touchkontroller (runtime-gatade): vinkel/kraft/ELD/VAPEN\n- All grafik ritas i kod - inga externa bilder\n\n" +
        "## Extension (tema-exempel)\n- Upplasbara vapen mellan dueller, terrang-teman per motstandare, skoldar,\n  bransle for att flytta tanken, 2 spelare hotseat, rikoschett-vapen\n";

    // ---- Main.gd: artilleri (Cannonade) ------------------------------------
    const string GodotArtilleryMain = """
extends Node2D
# Cannonade - turbaserad artilleriduell pa forstorbar terrang (ShellShock
# Live/Worms-klassen). BYT TEMA: farger, terranggenerering och vapenlistan.
# Spelartext pa ENGELSKA (husregeln).

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://cannonade_streak.save"
const W := 1152
const H := 648
const GRAVITY := 240.0
const WIND_DRIFT := 0.55

var state := "title" # title | aim | fly | over
var terrain_img: Image
var terrain_tex: ImageTexture
var terrain_sprite: Sprite2D
var wind := 0.0
var opponent_index := 0
var streak := 0
var shake := 0.0

# Tank-data: [x, y, hp, max_hp]-ordbocker - ritas i _draw, ingen fysikkropp.
var player := {}
var enemy := {}
var player_turn := true
var aim_angle := 45.0
var aim_power := 62.0
var player_aim := Vector2(45.0, 62.0)
var aim_grace := 0.0
var recoil := 0.0

# Projektil under flygning (tom = ingen). Trippel skjuter en kedja.
var shots_queue: Array = []
var proj := {}
var trail: Array = []

var weapons: Array = [
	{"namn": "Grenade", "radie": 26.0, "dmg": 45.0, "ammo": -1},
	{"namn": "Big Bomb", "radie": 44.0, "dmg": 62.0, "ammo": 2},
	{"namn": "Triple", "radie": 18.0, "dmg": 26.0, "ammo": 3},
]
var weapon_index := 0
var touch_fire := false
var next_duel_pending := false

# AI-minne: basta (vinkel, kraft) hittills mot spelaren, per duell.
var ai_best := Vector2(135.0, 60.0)
const OPPONENTS: Array = [
	{"namn": "The Recruit", "hp": 100.0, "fel": 14.0},
	{"namn": "The Captain", "hp": 120.0, "fel": 7.0},
	{"namn": "The General", "hp": 140.0, "fel": 3.0},
]

var hud: CanvasLayer
var hud_label: Label
var overlay: Control
var snd := {}
# v2.27: scenisk pixelbakgrund (PixelBackdrop-plattan fran scaffolden) -
# ritas bakom den forstorbara terrangen; platt himmel + sol ar fallback.
var backdrop: Texture2D = null

func _ready() -> void:
	randomize()
	Shell.startup()
	backdrop = load("res://background.png") as Texture2D
	for key in ["click", "coin", "hurt", "win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
	# Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
	var music = load("res://music.wav")
	if music:
		var mp := AudioStreamPlayer.new()
		mp.stream = music
		mp.volume_db = -14.0
		mp.finished.connect(mp.play)
		add_child(mp)
		mp.play()
	terrain_sprite = Sprite2D.new()
	terrain_sprite.centered = false
	add_child(terrain_sprite)
	hud = CanvasLayer.new()
	add_child(hud)
	hud_label = Label.new()
	hud_label.position = Vector2(16, 10)
	hud_label.add_theme_font_size_override("font_size", 17)
	hud.add_child(hud_label)
	_setup_touch()
	show_title()

func play_sound(key: String) -> void:
	if snd.has(key):
		snd[key].play()

# ---------- terrang (pixelbaserad, forstorbar) ----------
func generate_terrain() -> void:
	terrain_img = Image.create(W, H, false, Image.FORMAT_RGBA8)
	terrain_img.fill(Color(0, 0, 0, 0))
	var base := randf_range(400.0, 470.0)
	var a1 := randf_range(30.0, 70.0)
	var a2 := randf_range(12.0, 30.0)
	var f1 := randf_range(0.004, 0.008)
	var f2 := randf_range(0.015, 0.03)
	var ph1 := randf() * 6.28
	var ph2 := randf() * 6.28
	for x in range(W):
		var h := base + sin(x * f1 + ph1) * a1 + sin(x * f2 + ph2) * a2
		var top := clampi(int(h), 120, H - 40)
		terrain_img.fill_rect(Rect2i(x, top, 1, H - top), Color(0.42, 0.30, 0.20))
		terrain_img.fill_rect(Rect2i(x, top, 1, 6), Color(0.35, 0.62, 0.30))
	terrain_tex = ImageTexture.create_from_image(terrain_img)
	terrain_sprite.texture = terrain_tex

func solid(x: int, y: int) -> bool:
	if x < 0 or x >= W or y < 0 or y >= H:
		return false
	return terrain_img.get_pixel(x, y).a > 0.5

# from_y: skanna nedat fran tankens hojd - annars teleporterar ett krater-
# overhang OVANFOR tanken upp den (Worms-fallan).
func ground_y(x: int, from_y: int = 0) -> int:
	for y in range(maxi(0, from_y), H):
		if solid(x, y):
			return y
	return H - 1

func crater(pos: Vector2, radius: float) -> void:
	var r := int(radius)
	var cx := int(pos.x)
	var cy := int(pos.y)
	for dx in range(-r, r + 1):
		for dy in range(-r, r + 1):
			if dx * dx + dy * dy <= r * r:
				var px := cx + dx
				var py := cy + dy
				if px >= 0 and px < W and py >= 0 and py < H:
					terrain_img.set_pixel(px, py, Color(0, 0, 0, 0))
	terrain_tex.update(terrain_img)

# ---------- flode ----------
func new_game() -> void:
	streak = 0
	opponent_index = 0
	next_duel_pending = false
	start_duel()

func start_duel() -> void:
	generate_terrain()
	var opp: Dictionary = OPPONENTS[opponent_index]
	player = {"x": 150.0, "y": 0.0, "hp": 100.0, "max_hp": 100.0}
	enemy = {"x": 1000.0, "y": 0.0, "hp": opp["hp"], "max_hp": opp["hp"]}
	settle_tank(player)
	settle_tank(enemy)
	for w in weapons:
		if w["namn"] == "Storbomb":
			w["ammo"] = 2
		elif w["namn"] == "Trippel":
			w["ammo"] = 3
	weapon_index = 0
	ai_best = Vector2(135.0, 60.0)
	player_turn = true
	aim_angle = 45.0
	aim_power = 62.0
	player_aim = Vector2(45.0, 62.0)
	# Titelns Enter/Space ar SAMMA ui_accept som eldar - utan fristen
	# avfyrades forsta skottet i samma tryck som startade duellen.
	aim_grace = 0.3
	new_wind()
	state = "aim"
	close_overlay()
	queue_redraw()

func settle_tank(t: Dictionary) -> void:
	var gy := ground_y(int(float(t["x"])), int(float(t["y"])) - 6)
	t["y"] = float(gy)

func new_wind() -> void:
	wind = randf_range(-42.0, 42.0)

func fire() -> void:
	var w: Dictionary = weapons[weapon_index]
	if int(w["ammo"]) == 0:
		play_sound("coin")
		return
	if int(w["ammo"]) > 0:
		w["ammo"] = int(w["ammo"]) - 1
	if player_turn:
		player_aim = Vector2(aim_angle, aim_power)
	play_sound("click")
	recoil = 7.0
	shake = maxf(shake, 2.5)
	var shooter := player if player_turn else enemy
	var muzzle := Vector2(float(shooter["x"]), float(shooter["y"]) - 14.0)
	spawn_burst(muzzle, Color(1.0, 0.85, 0.4), 8)
	shots_queue = []
	var count := 3 if w["namn"] == "Trippel" else 1
	for i in range(count):
		var spread := float(i - 1) * 4.0 if count == 3 else 0.0
		shots_queue.append({"angle": aim_angle + spread, "power": aim_power, "radie": w["radie"], "dmg": w["dmg"]})
	launch_next()
	state = "fly"

func launch_next() -> void:
	var s: Dictionary = shots_queue.pop_front()
	var shooter := player if player_turn else enemy
	var rad: float = deg_to_rad(float(s["angle"]))
	var dir := Vector2(cos(rad), -sin(rad))
	proj = {
		"pos": Vector2(float(shooter["x"]), float(shooter["y"]) - 16.0) + dir * 20.0,
		"vel": dir * (float(s["power"]) * 7.2),
		"radie": s["radie"],
		"dmg": s["dmg"],
	}
	trail = []

func explode(at: Vector2, radius: float, dmg: float) -> void:
	play_sound("hurt")
	shake = maxf(shake, 4.0 + radius * 0.22)
	spawn_burst(at, Color(0.55, 0.4, 0.25), int(radius))
	spawn_burst(at, Color(1.0, 0.6, 0.2), 12)
	crater(at, radius)
	for t in [player, enemy]:
		var d := at.distance_to(Vector2(float(t["x"]), float(t["y"]) - 8.0))
		if d < radius + 14.0:
			var hit := maxf(6.0, dmg * (1.0 - d / (radius + 14.0)))
			t["hp"] = float(t["hp"]) - hit
	# Marken kan ha forsvunnit - tankar faller, langa fall gor ont.
	for t in [player, enemy]:
		var before := float(t["y"])
		settle_tank(t)
		var fall := float(t["y"]) - before
		if fall > 60.0:
			t["hp"] = float(t["hp"]) - fall * 0.12
			spawn_burst(Vector2(float(t["x"]), float(t["y"])), Color(0.8, 0.75, 0.7), 8)

func end_of_shot() -> void:
	proj = {}
	if float(enemy["hp"]) <= 0.0:
		streak += 1
		play_sound("win")
		if opponent_index >= OPPONENTS.size() - 1:
			finish(true)
		else:
			opponent_index += 1
			next_duel_pending = true
			show_overlay("VICTORY!", "%s defeated. Next up: %s\nWin streak: %d" % [
				str(OPPONENTS[opponent_index - 1]["namn"]), str(OPPONENTS[opponent_index]["namn"]), streak], ["Next duel"])
			state = "over"
		return
	if float(player["hp"]) <= 0.0:
		finish(false)
		return
	if not shots_queue.is_empty():
		launch_next()
		return
	player_turn = not player_turn
	new_wind()
	state = "aim"
	if player_turn:
		# Aterstall spelarens EGET sikte - aim_angle bar nyss AI:ns varden.
		aim_angle = player_aim.x
		aim_power = player_aim.y
		aim_grace = 0.2
	else:
		ai_take_aim()

# ---------- AI ----------
func simulate_shot(angle: float, power: float, from: Vector2) -> Vector2:
	var rad := deg_to_rad(angle)
	var pos := from + Vector2(cos(rad), -sin(rad)) * 20.0
	var vel := Vector2(cos(rad), -sin(rad)) * (power * 7.2)
	for i in range(900):
		var dt := 1.0 / 60.0
		vel.y += GRAVITY * dt
		vel.x += wind * WIND_DRIFT * dt
		pos += vel * dt
		if pos.x < -50.0 or pos.x > W + 50.0 or pos.y > H:
			return pos
		if solid(int(pos.x), int(pos.y)):
			return pos
	return pos

func ai_take_aim() -> void:
	var opp: Dictionary = OPPONENTS[opponent_index]
	var from := Vector2(float(enemy["x"]), float(enemy["y"]) - 16.0)
	var target := Vector2(float(player["x"]), float(player["y"]))
	var best := ai_best
	var best_d := simulate_shot(best.x, best.y, from).distance_to(target)
	for i in range(14):
		var cand := Vector2(
			clampf(best.x + randf_range(-22.0, 22.0), 95.0, 175.0),
			clampf(best.y + randf_range(-18.0, 18.0), 25.0, 100.0))
		var d := simulate_shot(cand.x, cand.y, from).distance_to(target)
		if d < best_d:
			best_d = d
			best = cand
	ai_best = best
	var err: float = opp["fel"]
	aim_angle = clampf(best.x + randf_range(-err, err), 95.0, 175.0)
	aim_power = clampf(best.y + randf_range(-err, err), 25.0, 100.0)
	weapon_index = 0
	# Kort paus sa spelaren hinner se AI:ns sikte - sedan eld.
	get_tree().create_timer(0.9).timeout.connect(func():
		if state == "aim" and not player_turn:
			fire())

func finish(won: bool) -> void:
	state = "over"
	if won:
		play_sound("win")
	var best := load_streak()
	if streak > best:
		save_streak(streak)
		best = streak
	show_overlay("LADDER CLEARED!" if won else "DEFEATED",
		"Win streak: %d   Best: %d\nR: play again" % [streak, best], ["Play again"])

# ---------- highscore ----------
func load_streak() -> int:
	if not FileAccess.file_exists(SAVE_PATH):
		return 0
	var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
	return int(f.get_as_text()) if f else 0

func save_streak(value: int) -> void:
	var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
	if f:
		f.store_string(str(value))

# ---------- loop ----------
func _physics_process(delta: float) -> void:
	if state == "title" or state == "over":
		return
	if shake > 0.0:
		shake = move_toward(shake, 0.0, 30.0 * delta)
		position = Vector2(randf_range(-shake, shake), randf_range(-shake, shake))
	elif position != Vector2.ZERO:
		position = Vector2.ZERO
	recoil = move_toward(recoil, 0.0, 24.0 * delta)

	if state == "aim" and player_turn:
		var da := Input.get_axis("ui_left", "ui_right")
		var dp := Input.get_axis("ui_down", "ui_up")
		aim_angle = clampf(aim_angle + da * 40.0 * delta, 5.0, 175.0)
		aim_power = clampf(aim_power + dp * 30.0 * delta, 10.0, 100.0)
		aim_grace = maxf(0.0, aim_grace - delta)
		# just_pressed - hallen Space far inte autoskjuta nasta tur.
		if aim_grace <= 0.0 and (Input.is_action_just_pressed("ui_accept") or touch_fire):
			touch_fire = false
			fire()
	elif state == "fly" and not proj.is_empty():
		var dt := delta
		var vel: Vector2 = proj["vel"]
		var pos: Vector2 = proj["pos"]
		vel.y += GRAVITY * dt
		vel.x += wind * WIND_DRIFT * dt
		pos += vel * dt
		proj["vel"] = vel
		proj["pos"] = pos
		trail.append(pos)
		if trail.size() > 40:
			trail.pop_front()
		var hit_tank := false
		for t in [player, enemy]:
			if pos.distance_to(Vector2(float(t["x"]), float(t["y"]) - 8.0)) < 16.0:
				hit_tank = true
		if hit_tank or solid(int(pos.x), int(pos.y)):
			explode(pos, float(proj["radie"]), float(proj["dmg"]))
			end_of_shot()
		elif pos.x < -60.0 or pos.x > W + 60.0 or pos.y > H + 40.0:
			end_of_shot()

	var opp: Dictionary = OPPONENTS[opponent_index]
	var wind_txt := (">> %d" % int(absf(wind))) if wind >= 0.0 else ("<< %d" % int(absf(wind)))
	var w: Dictionary = weapons[weapon_index]
	var ammo_txt := "" if int(w["ammo"]) < 0 else " x%d" % int(w["ammo"])
	hud_label.text = "You: %d HP   %s: %d HP   Wind: %s   Angle: %d   Power: %d   Weapon: %s%s [1-3]" % [
		int(maxf(0.0, float(player["hp"]))), str(opp["namn"]), int(maxf(0.0, float(enemy["hp"]))),
		wind_txt, int(aim_angle), int(aim_power), str(w["namn"]), ammo_txt]
	queue_redraw()

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if state == "aim":
			state = "paused"
			show_overlay("PAUSED", "Esc: resume", [])
		elif state == "paused":
			state = "aim"
			close_overlay()
			# Pausades AI:ns siktepaus bort? Ge den en ny - annars softlock.
			if not player_turn and proj.is_empty() and shots_queue.is_empty():
				ai_take_aim()
		return
	if event is InputEventKey and event.pressed:
		if state == "over" and event.keycode == KEY_R:
			new_game()
		elif state == "title" and (event.keycode == KEY_ENTER or event.keycode == KEY_SPACE):
			new_game()
		elif state == "aim" and player_turn:
			if event.keycode == KEY_1:
				weapon_index = 0
				play_sound("coin")
			elif event.keycode == KEY_2:
				weapon_index = 1
				play_sound("coin")
			elif event.keycode == KEY_3:
				weapon_index = 2
				play_sound("coin")

# ---------- ritning ----------
func _draw() -> void:
	if backdrop != null:
		# Scenplattan (NEAREST-uppskalad 4x) - dess avlagsna kullar sticker
		# upp bakom den forstorbara terrangen och ger gratis parallaxdjup.
		draw_texture_rect(backdrop, Rect2(0, 0, W, H), false)
	else:
		draw_rect(Rect2(0, 0, W, H), Color(0.45, 0.65, 0.85))
		draw_circle(Vector2(1010, 90), 42.0, Color(1, 0.92, 0.6))
	if state == "title":
		return
	# Vindpil
	var wl := clampf(absf(wind) * 1.6, 8.0, 70.0)
	var wdir := 1.0 if wind >= 0.0 else -1.0
	draw_line(Vector2(576 - wl * wdir, 40), Vector2(576 + wl * wdir, 40), Color(1, 1, 1, 0.8), 3.0)
	draw_line(Vector2(576 + wl * wdir, 40), Vector2(576 + wl * wdir - 8.0 * wdir, 33), Color(1, 1, 1, 0.8), 3.0)
	draw_line(Vector2(576 + wl * wdir, 40), Vector2(576 + wl * wdir - 8.0 * wdir, 47), Color(1, 1, 1, 0.8), 3.0)
	# Tankar
	draw_tank(player, Color(0.25, 0.55, 0.9), true)
	draw_tank(enemy, Color(0.85, 0.3, 0.25), false)
	# Siktlinje (forsta biten av banan) for spelaren
	if state == "aim" and player_turn:
		var from := Vector2(float(player["x"]), float(player["y"]) - 16.0)
		var rad := deg_to_rad(aim_angle)
		var pos := from + Vector2(cos(rad), -sin(rad)) * 20.0
		var vel := Vector2(cos(rad), -sin(rad)) * (aim_power * 7.2)
		for i in range(11):
			var dt := 1.0 / 30.0
			vel.y += GRAVITY * dt
			vel.x += wind * WIND_DRIFT * dt
			pos += vel * dt
			draw_circle(pos, 2.5, Color(1, 1, 1, 0.55 - float(i) * 0.045))
	# Projektil + spar
	if state == "fly" and not proj.is_empty():
		for i in range(trail.size()):
			draw_circle(trail[i], 2.0, Color(1, 0.8, 0.4, float(i) / float(trail.size()) * 0.6))
		draw_circle(proj["pos"], 5.0, Color(0.15, 0.15, 0.15))

func draw_tank(t: Dictionary, col: Color, facing_right: bool) -> void:
	var x := float(t["x"])
	var y := float(t["y"])
	var kick := recoil if (t == player) == player_turn else 0.0
	var body := Rect2(x - 16.0, y - 12.0, 32.0, 12.0)
	draw_rect(body, col)
	draw_rect(Rect2(x - 12.0, y - 4.0, 24.0, 5.0), col.darkened(0.35))
	# Eldror: SAMMA vinkelrymd for bada (aim_angle 5-175, AI siktar 95-175 =
	# vansterut) - roret foljer siktet pa den vars tur det ar, viloriktning annars.
	var ang := 45.0 if t == player else 135.0
	if (t == player) == player_turn and state != "title":
		ang = aim_angle
	var rad := deg_to_rad(ang)
	var dir := Vector2(cos(rad), -sin(rad))
	var pivot := Vector2(x, y - 12.0)
	draw_line(pivot, pivot + dir * (26.0 - kick), col.darkened(0.2), 5.0)
	# HP-stapel
	var frac := clampf(float(t["hp"]) / float(t["max_hp"]), 0.0, 1.0)
	draw_rect(Rect2(x - 18.0, y - 26.0, 36.0, 5.0), Color(0, 0, 0, 0.4))
	draw_rect(Rect2(x - 18.0, y - 26.0, 36.0 * frac, 5.0), Color(0.3, 0.9, 0.3).lerp(Color(0.9, 0.25, 0.2), 1.0 - frac))

# ---------- juice ----------
func spawn_burst(pos: Vector2, col: Color, count: int) -> void:
	var p := CPUParticles2D.new()
	p.position = pos
	p.amount = count
	p.one_shot = true
	p.explosiveness = 0.9
	p.lifetime = 0.55
	p.spread = 180.0
	p.initial_velocity_min = 80.0
	p.initial_velocity_max = 220.0
	p.gravity = Vector2(0, 260)
	p.scale_amount_min = 2.0
	p.scale_amount_max = 4.0
	p.color = col
	p.texture = make_texture(6, Color(1, 1, 1), Color(1, 1, 1))
	add_child(p)
	p.emitting = true
	get_tree().create_timer(1.0).timeout.connect(p.queue_free)

func make_texture(size: int, base: Color, accent: Color) -> ImageTexture:
	var img := Image.create(size, size, false, Image.FORMAT_RGBA8)
	img.fill(base)
	for x in range(size):
		for y in range(size):
			if x == 0 or y == 0 or x == size - 1 or y == size - 1:
				img.set_pixel(x, y, accent)
	return ImageTexture.create_from_image(img)

# ---------- touch (aktiveras BARA pa touchskarm - datorspel oforandrade) ----------
func _setup_touch() -> void:
	if not DisplayServer.is_touchscreen_available():
		return
	_touch_btn(Vector2(28, 536), "ui_left", "<")
	_touch_btn(Vector2(132, 536), "ui_right", ">")
	_touch_btn(Vector2(932, 444), "ui_up", "^")
	_touch_btn(Vector2(932, 536), "ui_down", "v")
	var fire_btn := _touch_btn(Vector2(1036, 536), "", "FIRE")
	fire_btn.pressed.connect(func(): touch_fire = true)
	var weapon_btn := _touch_btn(Vector2(1036, 444), "", "WEAPON")
	weapon_btn.pressed.connect(func():
		weapon_index = (weapon_index + 1) % weapons.size()
		play_sound("coin"))

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
	var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
	img2.fill(Color(1, 1, 1, 0.20))
	for x in range(88):
		for y in range(88):
			if x < 3 or y < 3 or x > 84 or y > 84:
				img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
	var b := TouchScreenButton.new()
	b.texture_normal = ImageTexture.create_from_image(img2)
	b.position = pos
	if action != "":
		b.action = action
	hud.add_child(b)
	var t := Label.new()
	t.text = text
	t.add_theme_font_size_override("font_size", 22)
	t.position = pos
	t.size = Vector2(88, 88)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	t.mouse_filter = Control.MOUSE_FILTER_IGNORE
	hud.add_child(t)
	return b

# ---------- overlays ----------
func show_title() -> void:
	state = "title"
	queue_redraw()
	show_overlay("CANNONADE",
		"Turn-based artillery duel on destructible terrain.\nLeft/Right: angle   Up/Down: power   Space: FIRE   1-3: weapons\nMind the wind. Defeat the Recruit, the Captain and the General.\nBest streak: %d" % load_streak(),
		["Start the duel"])

func show_overlay(title: String, message: String, buttons: Array) -> void:
	position = Vector2.ZERO
	shake = 0.0
	close_overlay()
	overlay = Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	hud.add_child(overlay)
	var bg := ColorRect.new()
	bg.color = Color(0, 0, 0, 0.65)
	bg.set_anchors_preset(Control.PRESET_FULL_RECT)
	overlay.add_child(bg)
	var box := VBoxContainer.new()
	# FULL_RECT + centrerad alignment - PRESET_CENTER satter bara pivoten
	# och hogerforskjuter innehallet (v2.12-fyndet, fixat har v2.22).
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 42)
	t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	m.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(m)
	var first := true
	for label in buttons:
		var b := Button.new()
		b.text = str(label)
		b.custom_minimum_size = Vector2(320, 46)
		b.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		b.pressed.connect(func():
			play_sound("coin")
			if next_duel_pending:
				next_duel_pending = false
				start_duel()
			else:
				new_game())
		box.add_child(b)
		# Forsta knappen far fokus -> Enter fungerar direkt (tangentbords-
		# spelbart, och grindens sond kommer forbi titelskarmen).
		if first:
			first = false
			b.grab_focus()
	# v2.22 spelskalet: Options + Quit - bara pa titeln.
	if state == "title" and buttons.size() > 0:
		var ob := Button.new()
		ob.text = "Options"
		ob.custom_minimum_size = Vector2(320, 46)
		ob.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		ob.pressed.connect(func():
			play_sound("click")
			open_options())
		box.add_child(ob)
		var qb := Button.new()
		qb.text = "Quit"
		qb.custom_minimum_size = Vector2(320, 46)
		qb.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		qb.pressed.connect(func(): get_tree().quit())
		box.add_child(qb)

func open_options() -> void:
	close_overlay()
	overlay = Shell.options_panel(hud, func():
		play_sound("click")
		show_title())

func close_overlay() -> void:
	if overlay:
		overlay.queue_free()
		overlay = null
""";


    /// <summary>Party/bradspel (v2.1.0, Board Bash): Mario Party-klassen -
    /// sammansatt spel med bradlage (tarning, turer, 4 spelare, ekonomi) +
    /// flera fristaende minispel (tap race, dodge, memory) + 2 bradlayouter.
    /// Allt i EN Main.gd med tydlig sektionering - spelflodesvaxlare mellan
    /// titel/brade/minispel/resultat utan att ladda om scenen.</summary>
    internal static string[] ScaffoldGodotParty(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Board Bash"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotPartyMain);
        files.Add("Main.gd");
        // v2.27: en scenisk PixelBackdrop-platta per brade. Temana ar FASTA
        // (bradesknapparna heter night sky/deep sea/candy dusk) - anvandar-
        // prompten utelamnas medvetet sa ett anvandartema ("rymdparty") inte
        // kapar alla tre till samma bild.
        Write(root, "bg_night.png", PixelBackdrop.Build("calm night sky over rolling hills", 288, 162));
        files.Add("bg_night.png");
        Write(root, "bg_sea.png", PixelBackdrop.Build("deep sea underwater ocean floor", 288, 162));
        files.Add("bg_sea.png");
        Write(root, "bg_dusk.png", PixelBackdrop.Build("candy sunset dusk sky", 288, 162));
        files.Add("bg_dusk.png");
        foreach (var (name, category) in new[] { ("click.wav", "jump"), ("coin.wav", "coin"), ("hurt.wav", "hurt"), ("win.wav", "win") })
        {
            Write(root, name, SfxrGenerator.Render(category, seed: 7));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotPartyDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Board Bash - Party Board Game (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbart party-bradspel i Mario Party-klassen: bradlage med\n" +
            "tarning, turer, 4 spelare (1 mansklig + 3 AI), 24 rutor med olika\n" +
            "effekter (mynt, stjarnor, minispel, dueller, warpar), 5 minispels-\n" +
            "typer med introkort (Tap Race, Dodge, Memory, Coin Grab, Quick\n" +
            "Draw), 3 bradlayouter med egna teman (Ring / Serpentine / Spiral),\n" +
            "HOTSEAT for 1-4 manskliga spelare, bonusrundor, 6 rundor,\n" +
            "3 svarighetsgrader, partiklar, screenshake och touchkontroller.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n\n" +
            "Styrning: Space/Enter rullar tarningen; piltangenter + Space i\n" +
            "minispelen. All grafik ritas i kod (_draw) - byt tema via farger\n" +
            "och spelarnamn i Main.gd.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotPartyDesignDoc(string prompt) =>
        "# Party Board Game - Mario Party style (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEtt turbaserat bradspel for 4 spelare (1 mansklig + 3 AI) dar man rullar\n" +
        "tarning, flyttar runt en brada med 24 rutor, samlar mynt och stjarnor,\n" +
        "och tacklar minispel efter varje runda.\n\n" +
        "## Mekaniker (klara)\n- Bradlage: 24 rutor i loop, 3 layouter med egna teman (Ring / Serpentine / Spiral)\n" +
        "- 4 spelare: tarning (1-6), turordning, AI med svarighetsbaserad logik\n" +
        "- Rutor: bla (+3 mynt), rod (-2 mynt), stjarna (kop for 15 mynt), minispel\n" +
        "- 5 minispel: Tap Race (masha), Dodge (undvik block), Memory (Simon), Coin Grab (samla mynt), Quick Draw (reaktionsduell)\n" +
        "- 6 rundor, 3 svarighetsgrader, poang med stjarnor + mynt\n" +
        "- Titel/vinst-overlay, ljud, partiklar (CPUParticles2D), screenshake, touch\n\n" +
        "## Bygg vidare\n- Fler minispel: lagg till i _start_minigame och _minigame_process\n" +
        "- Fler bradlayouter: lagg till i _build_board under else-grenen\n" +
        "- Fler spelartyper: duellrutor, olyckskastor, butiker, teleportrar\n" +
        "- Kalenderhandelser: var 3:e runda = bonusrunda med dubbelvarde\n" +
        "- Teamlaget: varje minispel = egen worktree via TeamBuild\n";

    const string GodotPartyMain = """
extends Node2D
# Board Bash - Mario Party-like board game with minigames.
# 4 players (1 human + 3 AI), dice, board tiles, 5 minigame types, 3 themed board layouts.
# UI built in code. CHANGE THEME: colors in _draw, player names in PLAYERS.
# Player text in ENGLISH (house rule since v1.99).

# Preload (inte class_name-globalen): kitet ska parsa AVEN fore forsta
# importen, och class_name-registret finns forst efter import.
const Shell = preload("res://Shell.gd")

const BOARD_RING := 0
const BOARD_SERPENTINE := 1
const BOARD_SPIRAL := 2
const ROUNDS := 6
const TILE_COUNT := 24
const STAR_COST := 15

# Tile types
const TILE_BLUE := 0   # +3 coins
const TILE_RED := 1    # -2 coins
const TILE_STAR := 2   # buy star (15 coins)
const TILE_MG := 3     # minigame trigger
const TILE_DUEL := 4   # v2.20: duell - bada rullar, vinnaren tar 5 mynt
const TILE_WARP := 5   # v2.20: teleport 5-9 rutor framat

var PLAYERS: Array[Dictionary] = [
    {"name":"You","col":Color(0.3,0.7,1), "ai":false},
    {"name":"Bot A","col":Color(1,0.4,0.4), "ai":true},
    {"name":"Bot B","col":Color(0.4,1,0.4), "ai":true},
    {"name":"Bot C","col":Color(1,0.9,0.3), "ai":true},
]

# Valbara karaktarer (namn + farg) - spelarens val sparas via Shell-settings.
const CHARACTERS := [
    ["Bubble", Color(0.3, 0.7, 1.0)],
    ["Berry", Color(1.0, 0.4, 0.4)],
    ["Lime", Color(0.4, 1.0, 0.4)],
    ["Lemon", Color(1.0, 0.9, 0.3)],
    ["Grape", Color(0.75, 0.5, 1.0)],
    ["Peach", Color(1.0, 0.62, 0.45)],
]
var character_idx := 0
var practice_mode := false   # minigame startat fran menyn - resultat gar till titeln
var practice_pick := -1      # tvingat minigameval fran menyn (-1 = slumpa)
# v2.21 HOTSEAT: 1-4 manskliga spelare vid samma tangentbord. Bradet ar
# turbaserat (samma Space for alla); minispelen har egna tangenter per
# spelare (visas pa introkortet).
var human_count := 1
const MASH_KEYS := [KEY_SPACE, KEY_W, KEY_U, KEY_O]          # mash/reaktion per spelare
const MOVE_KEYS := [[KEY_LEFT, KEY_RIGHT], [KEY_A, KEY_D], [KEY_J, KEY_L], [KEY_F, KEY_H]]
var mg_intro_t := 0.0        # v2.21: introkortets nedrakning fore varje minispel
# Autopilot (AILOCAL_AUTOPILOT=1): kvalitetsgrindens demospelare - startar
# fran titeln och haller partyt rullande. Vanliga spelare markar ingenting.
var autopilot := false
var auto_t := 0.0

var state := "title"
var difficulty := 1        # 0=easy 1=normal 2=hard
var board_layout := BOARD_RING
var round := 0
var turn_idx := 0          # which player's turn (0-3)
var turn_phase := ""       # "rolling" "moving" "resolving" "done"
var dice_value := 0
var move_steps := 0
var move_timer := 0.0
var round_minigame := false
var minigame_type := 0
var board_tiles: Array[int] = []
var star_positions: Array[int] = []

# Player state: each entry is {tile, coins, stars}
var pstate: Array[Dictionary] = []

var snd: Dictionary = {}
var ui: CanvasLayer
var focus_pending := true
var shake := 0.0
var dot_tex: ImageTexture
var flash_t := 0.0
var ai_roll_timer := 0.0
var resolve_timer := 0.0
var attract_t := 0.0   # auto-rull efter 8s idle - spelet demonstrerar sig sjalvt
var event_text := ""   # v2.20: duell-/warphandelser som visas i HUD-raden
# v2.16 levande varld: riktiga animerade gubbar (AnimatedSprite2D fran
# player_frames.tres, fargade per spelare) + konfettibakgrund i djuplager.
var tokens: Array = []        # AnimatedSprite2D per spelare (tom = cirkel-fallback)
var token_pos: Array = []     # mjuk visningsposition per spelare
var confetti: Array = []      # {x, y, s, sp, col, layer}
# v2.19: tema per brade - varje layout har egen bakgrundston.
var board_bg_top := Color(0.10, 0.08, 0.20)
var board_bg_bottom := Color(0.17, 0.11, 0.27)
# v2.27: scenisk PixelBackdrop-platta per brade (dampad sa brickorna ar
# lasbara) - gradientbanden ar fallback fore forsta importen.
var board_backdrops := {}

# --- Minigame state ---
var mg_timer := 0.0
var mg_rankings: Array[int] = []
var mg_player_progress: Array[float] = []
var mg_alive: Array[bool] = []

# Minigame 1 (Tap Race) state
var mg_tap_fill: Array[float] = []

# Minigame 2 (Dodge) state
var mg_dodge_blocks: Array = []
var mg_dodge_player_x: Array[float] = []
var mg_dodge_spawn_timer := 0.0

# Minigame 3 (Memory) state
var mg_mem_sequence: Array[int] = []
var mg_mem_player_idx := 0
var mg_mem_step := 0
var mg_mem_input_phase := false
var mg_mem_flash_t := 0.0
var mg_mem_lengths: Array[int] = []
var mg_mem_ai_timer := 0.0

# Minigame 4 (Coin Grab, v2.19) state - Dodges positiva spegel: SAMLA det
# som faller i stallet for att undvika det.
var mg_coin_items: Array = []          # {x, y, speed}
var mg_coin_score: Array[int] = []
var mg_coin_spawn_timer := 0.0

# Minigame 5 (Quick Draw, v2.19) state - reaktionsduell i 3 omgangar:
# vanta pa GRONT, forst att trycka vinner rundan; for tidigt = last runda.
var mg_qd_phase := "wait"              # wait | go | between
var mg_qd_timer := 0.0
var mg_qd_round := 0
var mg_qd_wins: Array[int] = []
var mg_qd_locked: Array[bool] = []
var mg_qd_ai_react: Array = []         # planerad reaktionstid per AI denna runda
var mg_qd_round_done := false

# Board tile positions (populated in _build_board)
var tile_positions: Array[Vector2] = []

# ---------- LIFECYCLE ----------

func _ready() -> void:
    randomize()
    _setup_audio()
    # Spelskalet: sparade installningar (volym/mute/fullskarm) galler fran
    # forsta rutan, och spelarens valda karaktar laddas.
    var saved := Shell.startup()
    _apply_character(int(saved.get("bb_character", 0)))
    autopilot = OS.get_environment("AILOCAL_AUTOPILOT") == "1"
    # Pixelart = skarpa pixlar: nearest-filter pa allt ritat/alla barn
    # (gamla projekt utan mallens default_texture_filter far det anda).
    texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
    # v2.27: bradenas scenplattor (null-sakert fore forsta importen).
    for pair in [[BOARD_RING, "bg_night"], [BOARD_SERPENTINE, "bg_sea"], [BOARD_SPIRAL, "bg_dusk"]]:
        var tex = load("res://%s.png" % pair[1])
        if tex:
            board_backdrops[pair[0]] = tex
    var img := Image.create(6, 6, false, Image.FORMAT_RGBA8)
    img.fill(Color(1, 1, 1))
    dot_tex = ImageTexture.create_from_image(img)
    var conf_cols := [Color(1, 0.5, 0.7, 0.5), Color(0.5, 0.8, 1, 0.5), Color(1, 0.9, 0.4, 0.5), Color(0.6, 1, 0.6, 0.5)]
    for i in range(26):
        confetti.append({"x": randf() * 1152.0, "y": randf() * 648.0,
            "s": 2.0 + randf() * 4.0, "sp": 4.0 + randf() * 10.0,
            "col": conf_cols[i % 4], "layer": 0 if i % 3 == 0 else 1})
    ui = CanvasLayer.new()
    add_child(ui)
    _setup_touch()
    _show_title()

func _ensure_tokens() -> void:
    for t in tokens:
        if is_instance_valid(t):
            t.queue_free()
    tokens = []
    token_pos = []
    # Nullsakert fore forsta importen - da ritar cirkel-fallbacken i _draw.
    var frames: SpriteFrames = load("res://player_frames.tres") as SpriteFrames
    if frames == null:
        return
    for i in range(4):
        var spr := AnimatedSprite2D.new()
        spr.sprite_frames = frames
        # lerp mot vitt behaller gubbens ljus nar spelarfarger multipliceras in
        spr.modulate = Color(PLAYERS[i]["col"]).lerp(Color.WHITE, 0.35)
        spr.scale = Vector2(2.2, 2.2)
        spr.play("idle")
        add_child(spr)
        tokens.append(spr)
        token_pos.append(Vector2(576.0, 324.0))

func _update_tokens(delta: float) -> void:
    if tokens.is_empty():
        return
    var offsets := [Vector2(-12, -14), Vector2(12, -14), Vector2(-12, 8), Vector2(12, 8)]
    var show := state in ["playing_board", "results"] and pstate.size() >= 4
    for i in range(4):
        var t: AnimatedSprite2D = tokens[i]
        if not is_instance_valid(t):
            continue
        t.visible = show
        if not show:
            continue
        var ps := pstate[i]
        var target := Vector2(576.0, 324.0)
        if int(ps["tile"]) < tile_positions.size():
            target = tile_positions[int(ps["tile"])] + offsets[i]
        var prev: Vector2 = token_pos[i]
        token_pos[i] = prev.lerp(target, minf(1.0, 10.0 * delta))
        var moving := prev.distance_to(target) > 3.0
        # hopp-bob nar den aktiva gubben flyttar; alla svavar latt over rutan
        var bob := 0.0
        if moving and i == turn_idx:
            bob = absf(sin(Time.get_ticks_msec() * 0.02)) * 6.0
        t.position = token_pos[i] + Vector2(0, -10.0 - bob)
        if absf(target.x - prev.x) > 1.0:
            t.flip_h = target.x < prev.x
        var want_walk := moving and i == turn_idx and turn_phase == "moving"
        if want_walk and t.animation != "walk":
            t.play("walk")
        elif not want_walk and t.animation != "idle":
            t.play("idle")

func _apply_character(i: int) -> void:
    character_idx = clampi(i, 0, CHARACTERS.size() - 1)
    PLAYERS[0]["name"] = str(CHARACTERS[character_idx][0])
    PLAYERS[0]["col"] = CHARACTERS[character_idx][1]
    # Botarna far aldrig samma farg som spelarens val - ta lediga ur listan.
    var used: Array = [character_idx]
    for b in range(1, 4):
        var pick := (character_idx + b) % CHARACTERS.size()
        while pick in used:
            pick = (pick + 1) % CHARACTERS.size()
        used.append(pick)
        PLAYERS[b]["col"] = CHARACTERS[pick][1]

# ---------- TOUCH ----------

func _setup_touch() -> void:
    if not DisplayServer.is_touchscreen_available():
        return
    _touch_btn(Vector2(1036, 500), "ui_accept", "ROLL")
    _touch_btn(Vector2(28, 536), "ui_left", "<")
    _touch_btn(Vector2(132, 536), "ui_right", ">")
    _touch_btn(Vector2(80, 444), "ui_up", "^")
    _touch_btn(Vector2(80, 536), "ui_down", "v")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
    var img2 := Image.create(88, 88, false, Image.FORMAT_RGBA8)
    img2.fill(Color(1, 1, 1, 0.20))
    for x in range(88):
        for y in range(88):
            if x < 3 or y < 3 or x > 84 or y > 84:
                img2.set_pixel(x, y, Color(1, 1, 1, 0.55))
    var b := TouchScreenButton.new()
    b.texture_normal = ImageTexture.create_from_image(img2)
    b.position = pos
    if action != "":
        b.action = action
    ui.add_child(b)
    var t := Label.new()
    t.text = text
    t.add_theme_font_size_override("font_size", 24)
    t.position = pos
    t.size = Vector2(88, 88)
    t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    t.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(t)
    return b

# ---------- AUDIO ----------

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win"]:
        var p := AudioStreamPlayer.new()
        var s = load("res://%s.wav" % key)
        if s:
            p.stream = s
        add_child(p)
        snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune-slinga, lag volym sa
    # effekterna hors. finished->play = loop utan importinstallningar.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

# ---------- UI HELPERS ----------

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true
    _last_button = null

func _label(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

var _last_button: Button = null   # v2.19: fokuskedja for pilnavigering

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(300, 46)
    b.position = Vector2(426, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    # v2.19 (skarpt verifieringsfynd): FRISTAENDE knappar pa en CanvasLayer
    # far inte palitliga automatiska fokusgrannar - pil-upp/ner gjorde
    # ingenting i setup-/practice-menyerna. Lanka kedjan explicit.
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    if focus_pending:
        focus_pending = false
        b.grab_focus()

# ---------- PARTICLES / JUICE ----------

func _burst(pos: Vector2, col: Color, count: int) -> void:
    var p := CPUParticles2D.new()
    p.position = pos
    p.amount = count
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.5
    p.spread = 180.0
    p.initial_velocity_min = 60.0
    p.initial_velocity_max = 160.0
    p.gravity = Vector2(0, 160)
    p.scale_amount_min = 2.0
    p.scale_amount_max = 4.0
    p.color = col
    p.texture = dot_tex
    add_child(p)
    p.emitting = true
    get_tree().create_timer(1.0).timeout.connect(p.queue_free)

# ---------- TITLE SCREEN ----------

func _show_title() -> void:
    # v2.15 SPELSKALET: riktig huvudmeny (Play/karaktar/minigames/options/
    # quit) i stallet for instruktionstext + direktstart - det som skiljer
    # "demo" fran "spel". Play gar till setup-skarmen (brade + svarighet).
    state = "title"
    position = Vector2.ZERO
    shake = 0.0
    practice_mode = false
    auto_t = 0.0
    _clear_ui()
    _label("BOARD BASH", 60, 72, Color(1, 0.75, 0.2))
    _label("A party board game! Roll dice, collect coins, buy stars, win minigames.", 150, 20)
    _label("Playing as: %s" % str(PLAYERS[0]["name"]), 182, 18, Color(PLAYERS[0]["col"]))
    Shell.menu(ui, [
        ["Play", func(): _show_setup()],
        ["Choose Character", func(): _show_character_select()],
        ["Minigames", func(): _show_minigame_menu()],
        ["Options", func(): _show_options()],
        ["Quit", func(): get_tree().quit()],
    ], 70.0)
    queue_redraw()

func _show_setup() -> void:
    _play("click")
    _clear_ui()
    _label("GAME SETUP", 70, 52, Color(1, 0.75, 0.2))
    _label("First to the most stars after %d rounds wins. Space/Enter rolls the dice." % ROUNDS, 150, 18)
    _label("Board and players:", 200, 18)
    _button("Ring (night sky)", 224, func(): board_layout = BOARD_RING; _play("click"))
    _button("Serpentine (deep sea)", 278, func(): board_layout = BOARD_SERPENTINE; _play("click"))
    _button("Spiral (candy dusk)", 332, func(): board_layout = BOARD_SPIRAL; _play("click"))
    _button("Humans: %d (press to change)" % human_count, 386, func():
        human_count = human_count % 4 + 1
        _play("click")
        _show_setup())
    var diffs := ["Start: Easy", "Start: Normal", "Start: Hard"]
    for i in range(3):
        var d := i
        _button(diffs[i], 448 + i * 52, func(): _start_game(d, board_layout))
    _button("Back", 600, func(): _show_title())
    # Fokus pa START (Easy) - Enter startar direkt; bradval nas med pil-upp.
    for c in ui.get_children():
        if c is Button and c.text == "Start: Easy":
            c.grab_focus()
    queue_redraw()

func _show_character_select() -> void:
    _play("click")
    _clear_ui()
    var names_arr: Array = []
    var cols: Array = []
    for cdef in CHARACTERS:
        names_arr.append(cdef[0])
        cols.append(cdef[1])
    Shell.character_select(ui, names_arr, cols, character_idx, func(i: int):
        _apply_character(i)
        var s := Shell.load_settings()
        s["bb_character"] = i
        Shell.save_settings(s)
        _play("coin")
        _show_title())
    queue_redraw()

func _show_minigame_menu() -> void:
    # Fritt lage: ova pa ett valfritt minigame utan bradspelet runtomkring.
    _play("click")
    _clear_ui()
    _label("MINIGAMES - practice any of them", 70, 40, Color(1, 0.75, 0.2))
    var mgs := [["Tap Race - mash to fill first", 0], ["Dodge - last one standing", 1], ["Memory - repeat the pattern", 2], ["Coin Grab - catch the most coins", 3], ["Quick Draw - react on green", 4]]
    for m in mgs:
        var mtype: int = m[1]
        _button(str(m[0]), 190 + mtype * 60, func(): _start_practice(mtype))
    _button("Back", 510, func(): _show_title())
    for c in ui.get_children():
        if c is Button and str(c.text).begins_with("Tap"):
            c.grab_focus()
    queue_redraw()

func _show_options() -> void:
    _play("click")
    _clear_ui()
    Shell.options_panel(ui, func(): _show_title())
    queue_redraw()

func _start_practice(mtype: int) -> void:
    practice_mode = true
    practice_pick = mtype
    if pstate.is_empty():
        _init_players()
    _play("click")
    _start_minigame()

# ---------- GAME START ----------

func _start_game(d: int, layout: int) -> void:
    difficulty = d
    board_layout = layout
    # Tema per brade: natt-lila ring, djuphavs-teal serpentin, candy-rosa spiral.
    match board_layout:
        BOARD_SERPENTINE:
            board_bg_top = Color(0.05, 0.12, 0.15)
            board_bg_bottom = Color(0.09, 0.19, 0.22)
        BOARD_SPIRAL:
            board_bg_top = Color(0.15, 0.07, 0.13)
            board_bg_bottom = Color(0.23, 0.11, 0.19)
        _:
            board_bg_top = Color(0.10, 0.08, 0.20)
            board_bg_bottom = Color(0.17, 0.11, 0.27)
    # v2.21 hotseat: spelare 0..human_count-1 ar manniskor; ovriga botar.
    for i in range(4):
        PLAYERS[i]["ai"] = i >= human_count
        if i > 0:
            PLAYERS[i]["name"] = ("P%d" % (i + 1)) if i < human_count else ["Bot A", "Bot B", "Bot C"][i - 1]
    _play("click")
    round = 1
    turn_idx = 0
    round_minigame = false
    ai_roll_timer = 0.0
    resolve_timer = 0.0
    _build_board()
    _init_players()
    _ensure_tokens()
    _clear_ui()
    state = "playing_board"
    turn_phase = "rolling"
    _update_hud()
    queue_redraw()

func _build_board() -> void:
    board_tiles.clear()
    star_positions.clear()
    tile_positions.clear()
    # Generate tile types: mixed with guaranteed minimums
    for i in range(TILE_COUNT):
        var r := randf()
        if r < 0.15:
            board_tiles.append(TILE_RED)
        elif r < 0.25:
            board_tiles.append(TILE_STAR)
            star_positions.append(i)
        elif r < 0.38:
            board_tiles.append(TILE_MG)
        else:
            board_tiles.append(TILE_BLUE)
    # Guarantee minimum counts
    var stars_have := star_positions.size()
    var mg_count := 0
    for i in range(TILE_COUNT):
        if board_tiles[i] == TILE_MG:
            mg_count += 1
    for i in range(TILE_COUNT):
        if stars_have < 2 and board_tiles[i] == TILE_BLUE:
            board_tiles[i] = TILE_STAR
            star_positions.append(i)
            stars_have += 1
        if mg_count < 3 and board_tiles[i] == TILE_BLUE:
            board_tiles[i] = TILE_MG
            mg_count += 1
    # v2.20: duell- och warprutor - 2 av varje, spridda pa jamna/udda index.
    var duels := 0
    var warps := 0
    for i in range(TILE_COUNT):
        if board_tiles[i] != TILE_BLUE:
            continue
        if duels < 2 and i % 2 == 0:
            board_tiles[i] = TILE_DUEL
            duels += 1
        elif warps < 2 and i % 2 == 1:
            board_tiles[i] = TILE_WARP
            warps += 1
    # Compute tile positions
    var cx := 576.0
    var cy := 324.0
    if board_layout == BOARD_RING:
        var rx := 380.0
        var ry := 240.0
        for i in range(TILE_COUNT):
            var a := TAU * i / TILE_COUNT - TAU * 0.25
            tile_positions.append(Vector2(cx + cos(a) * rx, cy + sin(a) * ry))
    elif board_layout == BOARD_SPIRAL:
        # v2.19: spiralen - 24 rutor som vandrar inat i drygt tva varv;
        # slutrutan hamnar nara mitten (kansla av mal).
        for i in range(TILE_COUNT):
            var t := float(i) / float(TILE_COUNT)
            var a := TAU * 2.3 * t - TAU * 0.25
            var rr := 1.0 - t * 0.70
            tile_positions.append(Vector2(cx + cos(a) * 390.0 * rr, cy + sin(a) * 245.0 * rr))
    else:
        # Serpentine: 4 rows of 6, snaking back and forth
        var rows := 4
        var cols := 6
        var ox := 180.0
        var oy := 100.0
        var dx := 140.0
        var dy := 140.0
        for r in range(rows):
            var row_tiles: Array[int] = []
            for c in range(cols):
                row_tiles.append(r * cols + c)
            if r % 2 == 1:
                row_tiles.reverse()
            for c in range(cols):
                var ti := row_tiles[c]
                if ti < TILE_COUNT:
                    tile_positions.append(Vector2(ox + c * dx, oy + r * dy))

func _init_players() -> void:
    pstate.clear()
    for i in range(4):
        var start_tile := i * (TILE_COUNT / 4)
        pstate.append({"tile": start_tile, "coins": 10, "stars": 0})

func _update_hud() -> void:
    # Remove old HUD labels
    for c in ui.get_children():
        if c.has_meta("hud"):
            c.queue_free()
    var y := 10.0
    for i in range(4):
        var ps := pstate[i]
        var pn := PLAYERS[i]
        var l := _label("%s  S:%d  C:%d" % [pn["name"], ps["stars"], ps["coins"]], y, 16, pn["col"])
        l.set_meta("hud", true)
        l.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
        l.position.x = 20
        l.size.x = 400
        y += 20
    var phase_text: String
    if turn_phase == "rolling":
        phase_text = ("Your turn - roll!" if turn_idx == 0 else PLAYERS[turn_idx]["name"] + "'s turn - roll!")
    elif turn_phase == "moving":
        phase_text = "Moving... (rolled " + str(dice_value) + ")"
    else:
        phase_text = PLAYERS[turn_idx]["name"] + " resolving..."
    if event_text != "":
        phase_text = event_text
    var bonus_tag := "  BONUS x2!" if _is_bonus() else ""
    var rd := _label("Round %d/%d%s   %s" % [round, ROUNDS, bonus_tag, phase_text], 10, 18, Color(1, 0.9, 0.5))
    rd.set_meta("hud", true)
    rd.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
    rd.position.x = 752
    rd.size.x = 380

# ---------- BOARD TURN LOGIC ----------

func _input(event: InputEvent) -> void:
    if state == "playing_board" and turn_phase == "rolling":
        if event.is_action_pressed("ui_accept") and not PLAYERS[turn_idx]["ai"]:
            _do_roll()
    elif state == "playing_minigame":
        _minigame_input(event)

func _physics_process(delta: float) -> void:
    # C1 juice: screenshake
    if shake > 0.0:
        shake = move_toward(shake, 0.0, 30.0 * delta)
        position = Vector2(randf_range(-shake, shake), randf_range(-shake, shake))
    elif position != Vector2.ZERO:
        position = Vector2.ZERO

    # Autopilot: vanta forbi sondens titeldump (~3 s), starta sedan partiet;
    # slutskarmen gar tillbaka till titeln sa demon aldrig stannar.
    if autopilot:
        if state == "title":
            auto_t += delta
            if auto_t > 5.0:
                auto_t = 0.0
                _start_game(1, board_layout)
        elif state == "results":
            auto_t += delta
            if auto_t > 4.0:
                auto_t = 0.0
                _show_title()
        else:
            auto_t = 0.0

    # Levande varld: gubbarna foljer sina rutor, konfettin driver.
    _update_tokens(delta)
    for c in confetti:
        c["y"] = float(c["y"]) + float(c["sp"]) * delta * (0.5 if c["layer"] == 0 else 1.0)
        c["x"] = float(c["x"]) + sin(Time.get_ticks_msec() * 0.001 + float(c["y"]) * 0.01) * 0.3
        if float(c["y"]) > 660.0:
            c["y"] = -12.0
            c["x"] = randf() * 1152.0

    if state == "mg_intro":
        # v2.21: introkortets nedrakning - tickar sjalv (autopilot/attract
        # behover aldrig trycka nagot).
        mg_intro_t -= delta
        for c in ui.get_children():
            if c is Label and c.has_meta("countdown"):
                c.text = str(maxi(1, int(ceil(mg_intro_t))))
        if mg_intro_t <= 0.0:
            _begin_minigame()
    elif state == "playing_board":
        if turn_phase == "rolling" and PLAYERS[turn_idx]["ai"]:
            ai_roll_timer -= delta
            if ai_roll_timer <= 0.0:
                _do_roll()
        elif turn_phase == "rolling":
            # Attract-autopilot: efter 8s utan input rullar spelet sjalvt -
            # partyt stannar aldrig, och grindens sond nar hela loopen.
            # I autopilotlaget rullas snabbare sa dumparna visar mittspel.
            attract_t += delta
            if attract_t > (2.0 if autopilot else 8.0):
                attract_t = 0.0
                _do_roll()
        elif turn_phase == "moving":
            move_timer -= delta
            if move_timer <= 0.0:
                move_steps -= 1
                if move_steps > 0:
                    _step_move()
                else:
                    _resolve_tile()
        elif turn_phase == "resolving":
            resolve_timer -= delta
            if resolve_timer <= 0.0:
                _next_player()
    elif state == "playing_minigame":
        _minigame_process(delta)

    queue_redraw()

func _do_roll() -> void:
    attract_t = 0.0
    _play("click")
    dice_value = randi() % 6 + 1
    move_steps = dice_value
    turn_phase = "moving"
    _update_hud()
    _step_move()

func _step_move() -> void:
    var ps := pstate[turn_idx]
    ps["tile"] = (ps["tile"] + 1) % TILE_COUNT
    _play("coin")
    _burst(tile_positions[ps["tile"]], PLAYERS[turn_idx]["col"], 5)
    shake = maxf(shake, 1.5)
    move_timer = 0.12
    _update_hud()

func _is_bonus() -> bool:
    # v2.20: var tredje runda ar BONUSRUNDA - dubbla mynt pa bla rutor
    # och dubbla minispelsbeloningar. Aldrig i practice-laget.
    return not practice_mode and round > 0 and round % 3 == 0

func _resolve_tile() -> void:
    var ps := pstate[turn_idx]
    var tile_type := board_tiles[ps["tile"]]
    var pos: Vector2 = tile_positions[ps["tile"]]

    if tile_type == TILE_BLUE:
        var gain := 3 * (2 if _is_bonus() else 1)
        ps["coins"] += gain
        _play("coin")
        _burst(pos, Color(0.3, 0.7, 1), 12 * (2 if _is_bonus() else 1))
    elif tile_type == TILE_RED:
        ps["coins"] = max(0, ps["coins"] - 2)
        _play("hurt")
        shake = maxf(shake, 4.0)
        _burst(pos, Color(1, 0.3, 0.3), 8)
    elif tile_type == TILE_STAR and ps["coins"] >= STAR_COST:
        ps["coins"] -= STAR_COST
        ps["stars"] += 1
        _play("win")
        _burst(pos, Color(1, 0.85, 0.2), 25)
    elif tile_type == TILE_MG:
        round_minigame = true
    elif tile_type == TILE_DUEL:
        # v2.20 DUELLEN: slumpad motstandare, bada rullar tarning -
        # vinnaren tar upp till 5 mynt av forloraren. Resultatet visas
        # i HUD-raden (event_text) och rundan pausar lite langre.
        var foe := (turn_idx + 1 + randi() % 3) % 4
        var my_roll := randi() % 6 + 1
        var foe_roll := randi() % 6 + 1
        while foe_roll == my_roll:
            foe_roll = randi() % 6 + 1
        var winner := turn_idx if my_roll > foe_roll else foe
        var loser := foe if winner == turn_idx else turn_idx
        var pot: int = mini(5, int(pstate[loser]["coins"]))
        pstate[loser]["coins"] = int(pstate[loser]["coins"]) - pot
        pstate[winner]["coins"] = int(pstate[winner]["coins"]) + pot
        event_text = "DUEL! %s rolls %d, %s rolls %d - %s takes %d coins!" % [
            PLAYERS[turn_idx]["name"], my_roll, PLAYERS[foe]["name"], foe_roll, PLAYERS[winner]["name"], pot]
        _play("win" if winner == turn_idx else "hurt")
        shake = maxf(shake, 5.0)
        _burst(pos, Color(1, 0.6, 0.2), 18)
    elif tile_type == TILE_WARP:
        # v2.20 WARPEN: teleport 5-9 rutor framat med dubbel burst.
        var jump := 5 + randi() % 5
        _burst(pos, Color(0.3, 0.9, 0.9), 14)
        ps["tile"] = (int(ps["tile"]) + jump) % TILE_COUNT
        event_text = "%s warps %d tiles ahead!" % [PLAYERS[turn_idx]["name"], jump]
        _play("click")
        _burst(tile_positions[int(ps["tile"])], Color(0.3, 0.9, 0.9), 14)
    # else: TILE_STAR without enough coins = no effect

    turn_phase = "resolving"
    resolve_timer = 1.4 if event_text != "" else 0.6
    _update_hud()

func _next_player() -> void:
    event_text = ""
    turn_idx += 1
    if turn_idx >= 4:
        turn_idx = 0
        if round_minigame:
            round_minigame = false
            _start_minigame()
            return
        round += 1
        if round > ROUNDS:
            _show_results()
            return
    turn_phase = "rolling"
    ai_roll_timer = 1.0 + randf() * 0.5
    _update_hud()

# ---------- MINIGAME DISPATCH ----------

func _start_minigame() -> void:
    # v2.21 INTROKORTET: namn, regel och kontroller + nedrakning innan
    # spelet startar - forsta motet med ett minispel ska vara begripligt.
    state = "mg_intro"
    _clear_ui()

    # Pick minigame (different from last time); practice-menyn TVINGAR valet.
    var last := minigame_type
    if practice_pick >= 0:
        minigame_type = practice_pick
        practice_pick = -1
    else:
        minigame_type = (last + 1 + randi() % 4) % 5  # guaranteed different

    _label("PRACTICE!" if practice_mode else "MINIGAME!", 60, 44, Color(1, 0.85, 0.2))
    var mg_names := ["TAP RACE", "DODGE", "MEMORY", "COIN GRAB", "QUICK DRAW"]
    var mg_rules := [
        "Mash your button to fill the bar first! 15 seconds.",
        "Avoid the falling blocks - last one standing wins!",
        "Watch the arrow sequence, then repeat it. Longest run wins!",
        "Catch the falling coins - most coins in 18 seconds wins!",
        "Wait for GREEN, then hit your button first! Too early = locked out. 3 rounds.",
    ]
    _label(mg_names[minigame_type], 130, 40)
    _label(mg_rules[minigame_type], 190, 20)
    var mash_names := ["Space", "W", "U", "O"]
    var move_names := ["Arrows", "A/D", "J/L", "F/H"]
    var ctrl := ""
    for i in range(human_count):
        if minigame_type == 0 or minigame_type == 4:
            ctrl += "P%d: %s    " % [i + 1, mash_names[i]]
        elif minigame_type == 1 or minigame_type == 3:
            ctrl += "P%d: %s    " % [i + 1, move_names[i]]
        else:
            ctrl += "P%d: arrows on your turn    " % (i + 1)
    _label(ctrl.strip_edges(), 232, 18, Color(0.6, 0.85, 1))
    var cd := _label("3", 330, 76, Color(1, 0.85, 0.2))
    cd.set_meta("countdown", true)
    mg_intro_t = 3.2
    _play("click")
    queue_redraw()

func _begin_minigame() -> void:
    state = "playing_minigame"
    _clear_ui()
    _label("PRACTICE!" if practice_mode else "MINIGAME!", 40, 48, Color(1, 0.85, 0.2))

    mg_rankings.clear()
    mg_player_progress.clear()
    mg_alive.clear()
    for i in range(4):
        mg_player_progress.append(0.0)
        mg_alive.append(true)
    mg_timer = 0.0

    if minigame_type == 0:
        _label("TAP RACE - Mash Space/Enter to fill your bar!", 100, 24)
        _label("First to fill wins! You have 15 seconds.", 130, 18)
        mg_tap_fill.clear()
        for i in range(4):
            mg_tap_fill.append(0.0)
    elif minigame_type == 1:
        _label("DODGE - Avoid the falling red blocks!", 100, 24)
        _label("Left/Right arrows to move. Don't get hit!", 130, 18)
        mg_dodge_blocks.clear()
        mg_dodge_player_x.clear()
        var cx := 576.0
        for i in range(4):
            mg_dodge_player_x.append(cx - 180 + i * 120)
        mg_dodge_spawn_timer = 0.0
    elif minigame_type == 2:
        _label("MEMORY - Repeat the arrow sequence!", 100, 24)
        _label("Watch the arrows flash, then press them in order.", 130, 18)
        mg_mem_sequence.clear()
        mg_mem_player_idx = 0
        mg_mem_lengths.clear()
        for i in range(4):
            mg_mem_lengths.append(0)
        mg_mem_step = 0
        mg_mem_input_phase = false
        mg_mem_flash_t = 0.0
        mg_mem_ai_timer = 0.0
        _mem_generate_sequence()
    elif minigame_type == 3:
        _label("COIN GRAB - Catch the falling coins!", 100, 24)
        _label("Left/Right arrows to move. Most coins in 18 seconds wins!", 130, 18)
        mg_coin_items.clear()
        mg_coin_score.clear()
        mg_dodge_player_x.clear()
        var ccx := 576.0
        for i in range(4):
            mg_coin_score.append(0)
            mg_dodge_player_x.append(ccx - 180 + i * 120)
        mg_coin_spawn_timer = 0.0
    else:
        _label("QUICK DRAW - Wait for GREEN, then hit Space/Enter!", 100, 24)
        _label("Too early = locked out. First of 3 rounds wins each point.", 130, 18)
        mg_qd_round = 0
        mg_qd_wins.clear()
        mg_qd_locked.clear()
        for i in range(4):
            mg_qd_wins.append(0)
            mg_qd_locked.append(false)
        _qd_new_round()

    queue_redraw()

func _qd_new_round() -> void:
    mg_qd_phase = "wait"
    mg_qd_timer = 1.0 + randf() * 2.2
    mg_qd_round_done = false
    mg_qd_ai_react = []
    # AI-reaktionstider: snabbare pa hogre svarighet, med slump per runda.
    var base_react: float = [0.55, 0.40, 0.28][difficulty]
    for i in range(4):
        mg_qd_locked[i] = false
        mg_qd_ai_react.append(base_react + randf() * 0.35)

func _qd_react(i: int) -> void:
    if mg_qd_round_done or mg_qd_locked[i]:
        return
    if mg_qd_phase == "wait":
        # For tidigt - last resten av rundan.
        mg_qd_locked[i] = true
        if i == 0:
            _play("hurt")
        return
    if mg_qd_phase == "go":
        mg_qd_round_done = true
        mg_qd_wins[i] += 1
        _play("coin")
        shake = maxf(shake, 4.0)
        _burst(Vector2(576, 360), PLAYERS[i]["col"], 20)
        mg_qd_phase = "between"
        mg_qd_timer = 1.4

func _minigame_input(event: InputEvent) -> void:
    if state != "playing_minigame":
        return
    if minigame_type == 0:  # Tap Race - v2.21 hotseat: egen mash-tangent per spelare
        if event is InputEventKey and event.pressed and not event.echo:
            for i in range(human_count):
                if mg_alive[i] and event.keycode == MASH_KEYS[i]:
                    mg_tap_fill[i] = minf(1.0, mg_tap_fill[i] + 0.08)
                    if i == 0:
                        _play("click")
    elif minigame_type == 2:  # Memory - pilarna galler den manskliga spelare vars tur det ar
        if mg_mem_input_phase and mg_mem_player_idx < 4 \
            and not PLAYERS[mg_mem_player_idx]["ai"] and mg_alive[mg_mem_player_idx]:
            var d := -1
            if event.is_action_pressed("ui_left"): d = 0
            elif event.is_action_pressed("ui_right"): d = 1
            elif event.is_action_pressed("ui_up"): d = 2
            elif event.is_action_pressed("ui_down"): d = 3
            if d >= 0:
                _mem_check_input(mg_mem_player_idx, d)
    elif minigame_type == 4:  # Quick Draw - v2.21 hotseat: egen tangent per spelare
        if event is InputEventKey and event.pressed and not event.echo:
            for i in range(human_count):
                if event.keycode == MASH_KEYS[i]:
                    _qd_react(i)

func _minigame_process(delta: float) -> void:
    mg_timer += delta

    if minigame_type == 0:  # Tap Race
        # AI fill rates (difficulty-dependent) - bara for botar (v2.21 hotseat)
        var ai_rates := [0.025, 0.038, 0.052]
        var rate: float = ai_rates[difficulty]
        for i in range(1, 4):
            if mg_alive[i] and PLAYERS[i]["ai"]:
                mg_tap_fill[i] = minf(1.0, mg_tap_fill[i] + rate * delta * 60.0)
        # Check for finishers
        var all_done := true
        for i in range(4):
            if mg_alive[i] and mg_tap_fill[i] >= 1.0:
                mg_rankings.append(i)
                mg_alive[i] = false
            if mg_alive[i]:
                all_done = false
        if all_done or mg_timer > 16.0:
            _end_minigame()

    elif minigame_type == 1:  # Dodge
        # Spawn blocks
        mg_dodge_spawn_timer -= delta
        if mg_dodge_spawn_timer <= 0.0:
            mg_dodge_spawn_timer = 0.4 - difficulty * 0.08
            var alive_indices: Array[int] = []
            for i in range(4):
                if mg_alive[i]:
                    alive_indices.append(i)
            if alive_indices.size() > 0:
                var target := alive_indices[randi() % alive_indices.size()]
                var bx := mg_dodge_player_x[target] + randf_range(-40, 40)
                mg_dodge_blocks.append({"x": bx, "y": -20.0, "speed": 160.0 + difficulty * 30.0})
        # Move blocks and check collisions
        var to_remove: Array = []
        for b in mg_dodge_blocks:
            b["y"] += b["speed"] * delta
            if b["y"] > 700:
                to_remove.append(b)
            else:
                for i in range(4):
                    if mg_alive[i] and abs(b["x"] - mg_dodge_player_x[i]) < 30 and b["y"] > 575 and b["y"] < 625:
                        mg_alive[i] = false
                        mg_rankings.append(i)
                        _play("hurt")
                        shake = maxf(shake, 5.0)
                        _burst(Vector2(mg_dodge_player_x[i], 600), Color(1, 0.3, 0.3), 15)
        for b in to_remove:
            mg_dodge_blocks.erase(b)
        # AI movement - bara for botar (v2.21 hotseat)
        var dodge_chance: float = [0.75, 0.55, 0.35][difficulty]
        for i in range(1, 4):
            if not mg_alive[i] or not PLAYERS[i]["ai"]:
                continue
            var danger := false
            var best_dir := 0
            for b in mg_dodge_blocks:
                if abs(b["x"] - mg_dodge_player_x[i]) < 45 and b["y"] > 380 and b["y"] < 620:
                    danger = true
                    best_dir = -1 if b["x"] > mg_dodge_player_x[i] else 1
            if danger and randf() < dodge_chance:
                mg_dodge_player_x[i] = clamp(mg_dodge_player_x[i] + best_dir * 180 * delta, 100, 1050)
            else:
                mg_dodge_player_x[i] = clamp(mg_dodge_player_x[i] + (randf() - 0.5) * 100 * delta, 100, 1050)
        # Human movement - v2.21 hotseat: egna tangenter per manniska
        for i in range(human_count):
            if not mg_alive[i]:
                continue
            if Input.is_physical_key_pressed(MOVE_KEYS[i][0]):
                mg_dodge_player_x[i] -= 280 * delta
            if Input.is_physical_key_pressed(MOVE_KEYS[i][1]):
                mg_dodge_player_x[i] += 280 * delta
            mg_dodge_player_x[i] = clamp(mg_dodge_player_x[i], 100, 1050)
        # End condition
        var alive_count := 0
        for i in range(4):
            if mg_alive[i]:
                alive_count += 1
                mg_player_progress[i] = mg_timer
        if alive_count <= 1 or mg_timer > 22.0:
            _end_minigame()

    elif minigame_type == 2:  # Memory
        if mg_mem_input_phase:
            # Sequence display phase - show arrows
            mg_mem_flash_t -= delta
            if mg_mem_flash_t <= 0.0:
                if mg_mem_step < mg_mem_sequence.size():
                    mg_mem_step += 1
                    mg_mem_flash_t = 0.5
                else:
                    mg_mem_input_phase = false
                    mg_mem_step = 0
                    mg_mem_flash_t = 0.0
            # AI input (only when it's their turn to respond, not during display)
            if mg_mem_player_idx < 4 and PLAYERS[mg_mem_player_idx]["ai"] and mg_alive[mg_mem_player_idx]:
                mg_mem_ai_timer -= delta
                if mg_mem_ai_timer <= 0.0:
                    mg_mem_ai_timer = 0.3 + randf() * 0.3
                    var pidx := mg_mem_player_idx
                    var ai_perfect: float = [1.0, 0.7, 0.4][difficulty]
                    var d: int
                    if randf() < ai_perfect:
                        d = mg_mem_sequence[mg_mem_step]
                    else:
                        # Wrong answer - pick a random different direction
                        d = mg_mem_sequence[mg_mem_step]
                        while d == mg_mem_sequence[mg_mem_step]:
                            d = randi() % 4
                    _mem_check_input(pidx, d)
        else:
            # Show sequence before input
            mg_mem_flash_t -= delta
            if mg_mem_flash_t <= 0.0:
                mg_mem_input_phase = true
                mg_mem_step = 0
                mg_mem_flash_t = 1.0
                mg_mem_ai_timer = 0.5
        # End condition
        var all_done := true
        for i in range(4):
            if mg_alive[i]:
                all_done = false
        if all_done or mg_timer > 30.0:
            _end_minigame()

    elif minigame_type == 3:  # Coin Grab (v2.19)
        mg_coin_spawn_timer -= delta
        if mg_coin_spawn_timer <= 0.0:
            mg_coin_spawn_timer = 0.32 - difficulty * 0.04
            mg_coin_items.append({"x": randf_range(120.0, 1030.0), "y": -16.0,
                "speed": 170.0 + difficulty * 30.0 + randf() * 60.0})
        var caught: Array = []
        for c in mg_coin_items:
            c["y"] = float(c["y"]) + float(c["speed"]) * delta
            if float(c["y"]) > 700.0:
                caught.append(c)
                continue
            for i in range(4):
                if absf(float(c["x"]) - mg_dodge_player_x[i]) < 34.0 and float(c["y"]) > 575.0 and float(c["y"]) < 625.0:
                    mg_coin_score[i] += 1
                    caught.append(c)
                    if i == 0:
                        _play("coin")
                    _burst(Vector2(float(c["x"]), 600), Color(1, 0.9, 0.3), 8)
                    break
        for c in caught:
            mg_coin_items.erase(c)
        # AI: jaga narmsta fallande mynt - bara botar (v2.21 hotseat).
        var chase_speed: float = [140.0, 190.0, 240.0][difficulty]
        for i in range(1, 4):
            if not PLAYERS[i]["ai"]:
                continue
            var best_x := -1.0
            var best_d := 99999.0
            for c in mg_coin_items:
                if float(c["y"]) < 560.0:
                    var dxc := absf(float(c["x"]) - mg_dodge_player_x[i])
                    var eta := (600.0 - float(c["y"])) / float(c["speed"])
                    var score := dxc + eta * 60.0
                    if score < best_d:
                        best_d = score
                        best_x = float(c["x"])
            if best_x >= 0.0:
                var dir := signf(best_x - mg_dodge_player_x[i])
                mg_dodge_player_x[i] = clampf(mg_dodge_player_x[i] + dir * chase_speed * delta, 100.0, 1050.0)
        # Manskliga spelare: egna tangenter (v2.21 hotseat).
        for i in range(human_count):
            if Input.is_physical_key_pressed(MOVE_KEYS[i][0]):
                mg_dodge_player_x[i] -= 280 * delta
            if Input.is_physical_key_pressed(MOVE_KEYS[i][1]):
                mg_dodge_player_x[i] += 280 * delta
            mg_dodge_player_x[i] = clampf(mg_dodge_player_x[i], 100.0, 1050.0)
        if mg_timer > 18.0:
            _end_minigame()

    elif minigame_type == 4:  # Quick Draw (v2.19)
        mg_qd_timer -= delta
        if mg_qd_phase == "wait" and mg_qd_timer <= 0.0:
            mg_qd_phase = "go"
            mg_qd_timer = 0.0
            _play("click")
        elif mg_qd_phase == "go":
            mg_qd_timer += delta  # tid sedan GRONT - AI reagerar pa sina tider
            for i in range(1, 4):
                if PLAYERS[i]["ai"] and not mg_qd_round_done and not mg_qd_locked[i] and mg_qd_timer >= float(mg_qd_ai_react[i]):
                    _qd_react(i)
            # Ingen kvar som kan reagera (alla for tidiga) => ny runda.
            var anyone := false
            for i in range(4):
                if not mg_qd_locked[i]:
                    anyone = true
            if not anyone:
                mg_qd_phase = "between"
                mg_qd_timer = 1.0
        elif mg_qd_phase == "between" and mg_qd_timer <= 0.0:
            mg_qd_round += 1
            if mg_qd_round >= 3:
                _end_minigame()
            else:
                _qd_new_round()

func _mem_generate_sequence() -> void:
    mg_mem_sequence.clear()
    var length := 3 + mg_mem_lengths[0]  # base 3, grows per round
    for i in range(length):
        mg_mem_sequence.append(randi() % 4)

func _mem_check_input(player_idx: int, direction: int) -> void:
    if not mg_alive[player_idx]:
        return
    if direction == mg_mem_sequence[mg_mem_step]:
        _play("coin")
        mg_mem_step += 1
        if mg_mem_step >= mg_mem_sequence.size():
            mg_mem_lengths[player_idx] += 1
            mg_mem_step = 0
            mg_mem_input_phase = false
            mg_mem_flash_t = 1.0
            _mem_generate_sequence()
    else:
        _play("hurt")
        mg_alive[player_idx] = false
        mg_player_progress[player_idx] = float(mg_mem_lengths[player_idx])
        mg_rankings.append(player_idx)
        if player_idx == 0:
            _mem_next_player()
        else:
            # AI elimination handled in _minigame_process
            pass

func _mem_next_player() -> void:
    mg_mem_player_idx += 1
    while mg_mem_player_idx < 4 and not mg_alive[mg_mem_player_idx]:
        mg_mem_player_idx += 1
    if mg_mem_player_idx >= 4:
        _end_minigame()
    else:
        mg_mem_step = 0
        mg_mem_input_phase = false
        mg_mem_flash_t = 1.0
        mg_mem_ai_timer = 0.5

func _end_minigame() -> void:
    # Build final rankings
    var final_rankings: Array[int] = []
    if minigame_type == 0:  # Tap Race: best fill first
        var scored: Array[Dictionary] = []
        for i in range(4):
            scored.append({"idx": i, "score": mg_tap_fill[i]})
        scored.sort_custom(func(a, b): return a["score"] > b["score"])
        for s in scored:
            final_rankings.append(s["idx"])
    elif minigame_type == 1:  # Dodge: reverse elimination order (last standing = best)
        final_rankings = mg_rankings.duplicate()
        for i in range(4):
            if mg_alive[i] and not i in final_rankings:
                final_rankings.append(i)
        final_rankings.reverse()
    elif minigame_type == 2:  # Memory: longest sequence first
        var scored2: Array[Dictionary] = []
        for i in range(4):
            scored2.append({"idx": i, "score": float(mg_mem_lengths[i])})
        scored2.sort_custom(func(a, b): return a["score"] > b["score"])
        for s in scored2:
            final_rankings.append(s["idx"])
    elif minigame_type == 3:  # Coin Grab: most coins first
        var scored3: Array[Dictionary] = []
        for i in range(4):
            scored3.append({"idx": i, "score": float(mg_coin_score[i])})
        scored3.sort_custom(func(a, b): return a["score"] > b["score"])
        for s in scored3:
            final_rankings.append(s["idx"])
    else:  # Quick Draw: most round wins first
        var scored4: Array[Dictionary] = []
        for i in range(4):
            scored4.append({"idx": i, "score": float(mg_qd_wins[i])})
        scored4.sort_custom(func(a, b): return a["score"] > b["score"])
        for s in scored4:
            final_rankings.append(s["idx"])
    mg_rankings = final_rankings

    # Award coins
    var awards := [10, 5, 2, 0]
    _play("win")
    _clear_ui()
    _label("MINIGAME RESULTS", 40, 48, Color(1, 0.85, 0.2))
    var y := 110.0
    for rank in range(4):
        var pi: int = mg_rankings[rank]
        var award: int = awards[rank] * (2 if _is_bonus() else 1)
        pstate[pi]["coins"] += award
        var txt := "%d. %s  +%d coins" % [rank + 1, PLAYERS[pi]["name"], award]
        _label(txt, y, 22, PLAYERS[pi]["col"])
        y += 30
    if _is_bonus():
        _label("BONUS ROUND - double rewards!", y + 4, 18, Color(1, 0.85, 0.2))
        y += 26
    # Practice-lage: tillbaka till menyn, inte in i ett bradspel som inte pagar.
    if practice_mode:
        _button("Back to menu", y + 20, func(): _show_title())
    else:
        _button("Continue", y + 20, func(): _return_to_board())
    queue_redraw()

func _return_to_board() -> void:
    state = "playing_board"
    round += 1
    turn_idx = 0
    turn_phase = "rolling"
    ai_roll_timer = 1.0 + randf() * 0.5
    _clear_ui()
    if round > ROUNDS:
        _show_results()
    else:
        _update_hud()
    queue_redraw()

# ---------- GAME RESULTS ----------

func _show_results() -> void:
    state = "results"
    position = Vector2.ZERO
    shake = 0.0
    _play("win")
    _clear_ui()
    _label("GAME OVER!", 60, 60, Color(1, 0.85, 0.2))
    # Sort players by stars (desc), then coins (desc)
    var ranked: Array[Dictionary] = []
    for i in range(4):
        ranked.append({"idx": i, "stars": pstate[i]["stars"], "coins": pstate[i]["coins"]})
    ranked.sort_custom(func(a, b):
        if a["stars"] != b["stars"]:
            return a["stars"] > b["stars"]
        return a["coins"] > b["coins"])
    var y := 160.0
    var labels := ["1st", "2nd", "3rd", "4th"]
    for rank in range(4):
        var pi: int = ranked[rank]["idx"]
        var ps := pstate[pi]
        var pn := PLAYERS[pi]
        var txt := "%s  %s  %d stars  %d coins" % [labels[rank], pn["name"], ps["stars"], ps["coins"]]
        _label(txt, y, 24, pn["col"])
        y += 36
        if rank == 0:
            _burst(Vector2(576, y - 18), pn["col"], 40)
    _button("Play Again", y + 20, func(): _show_title())
    queue_redraw()

# ---------- DRAW ----------

func _draw() -> void:
    # Levande bakgrund: scenplatta per brade (v2.27, dampad for lasbarhet)
    # med gradientband som fallback + konfetti i tva djuplager + vinjett -
    # aldrig en platt yta med "bollar som bara flyter" (agarens dom v2.16).
    if board_backdrops.has(board_layout):
        draw_texture_rect(board_backdrops[board_layout], Rect2(0, 0, 1152, 648), false,
            Color(0.52, 0.52, 0.62))
    else:
        var bands := 14
        for i in range(bands):
            var bt := float(i) / float(bands - 1)
            draw_rect(Rect2(0, 648.0 * float(i) / float(bands), 1152, 648.0 / float(bands) + 1.0),
                board_bg_top.lerp(board_bg_bottom, bt))
    for c in confetti:
        var cs: float = float(c["s"]) * (0.7 if c["layer"] == 0 else 1.0)
        var cc: Color = c["col"]
        if c["layer"] == 0:
            cc.a = 0.22
        draw_circle(Vector2(float(c["x"]), float(c["y"])), cs, cc)
    draw_rect(Rect2(0, 0, 1152, 64), Color(0, 0, 0, 0.16))
    draw_rect(Rect2(0, 584, 1152, 64), Color(0, 0, 0, 0.16))

    if state in ["playing_board", "results"]:
        # Draw board tiles
        for i in range(TILE_COUNT):
            if i < tile_positions.size() and i < board_tiles.size():
                _draw_tile(tile_positions[i], board_tiles[i])
        # Draw connections between tiles
        for i in range(TILE_COUNT):
            var j := (i + 1) % TILE_COUNT
            if i < tile_positions.size() and j < tile_positions.size():
                draw_line(tile_positions[i], tile_positions[j], Color(0.25, 0.25, 0.35), 2)
        # Spelartokens: riktiga AnimatedSprite2D-gubbar (_update_tokens) med
        # cirkel-fallback fore forsta importen. Pulsringen markerar alltid
        # den aktiva spelaren.
        var offsets := [Vector2(-10, -10), Vector2(10, -10), Vector2(-10, 10), Vector2(10, 10)]
        for i in range(4):
            var ps := pstate[i]
            if ps["tile"] < tile_positions.size():
                var pos: Vector2 = tile_positions[ps["tile"]] + offsets[i]
                if tokens.is_empty():
                    var pn := PLAYERS[i]
                    draw_circle(pos, 8, pn["col"])
                    draw_circle(pos, 8, Color.WHITE, false, 2)
                # Highlight current player with pulsing ring
                if i == turn_idx and state == "playing_board":
                    var pulse := 1.0 + sin(Time.get_ticks_msec() * 0.005) * 0.3
                    draw_circle(pos, 12 * pulse, Color(1, 1, 1, 0.4), false, 2)

    elif state == "playing_minigame":
        _draw_minigame()

# v2.17: PIXELART-BRICKOR - rundad kvadrat med kontur, ljus topp, mork
# botten och pixelsymbol (samma formsprak som gubbarna) i stallet for
# nakna cirklar. Byggs EN gang per typ (ImageTexture, nearest-skalad).
var tile_texs: Dictionary = {}

func _make_tile_tex(ttype: int) -> ImageTexture:
    var base: Color
    match ttype:
        TILE_BLUE: base = Color8(58, 112, 200)
        TILE_RED: base = Color8(198, 62, 62)
        TILE_STAR: base = Color8(228, 186, 56)
        TILE_DUEL: base = Color8(224, 122, 46)
        TILE_WARP: base = Color8(48, 176, 170)
        _: base = Color8(152, 64, 192)
    var light := base.lightened(0.30)
    var dark := base.darkened(0.30)
    var outline := Color8(27, 22, 36)
    var img := Image.create(16, 16, false, Image.FORMAT_RGBA8)
    for y in range(16):
        for x in range(16):
            if (x == 0 or x == 15) and (y == 0 or y == 15):
                continue  # rundade horn
            if x == 0 or x == 15 or y == 0 or y == 15:
                img.set_pixel(x, y, outline)
            elif y <= 2:
                img.set_pixel(x, y, light)
            elif y >= 13:
                img.set_pixel(x, y, dark)
            else:
                img.set_pixel(x, y, base)
    var sym := Color(1, 1, 1, 0.95)
    match ttype:
        TILE_BLUE:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
                img.set_pixel(7, 5 + i, sym)
        TILE_RED:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
        TILE_STAR:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
                img.set_pixel(7, 5 + i, sym)
            img.set_pixel(5, 5, sym)
            img.set_pixel(9, 5, sym)
            img.set_pixel(5, 9, sym)
            img.set_pixel(9, 9, sym)
        TILE_DUEL:
            # korsade svard: tva diagonaler
            for i in range(5):
                img.set_pixel(5 + i, 5 + i, sym)
                img.set_pixel(9 - i, 5 + i, sym)
        TILE_WARP:
            # virvel: ring av pixlar + centrum
            img.set_pixel(7, 4, sym)
            img.set_pixel(9, 5, sym)
            img.set_pixel(10, 7, sym)
            img.set_pixel(9, 9, sym)
            img.set_pixel(7, 10, sym)
            img.set_pixel(5, 9, sym)
            img.set_pixel(4, 7, sym)
            img.set_pixel(5, 5, sym)
            img.set_pixel(7, 7, sym)
        _:
            img.set_pixel(6, 6, sym)
            img.set_pixel(9, 6, sym)
            img.set_pixel(6, 9, sym)
            img.set_pixel(9, 9, sym)
    return ImageTexture.create_from_image(img)

func _draw_tile(pos: Vector2, ttype: int) -> void:
    if not tile_texs.has(ttype):
        tile_texs[ttype] = _make_tile_tex(ttype)
    draw_texture_rect(tile_texs[ttype], Rect2(pos - Vector2(16, 16), Vector2(32, 32)), false)

func _draw_minigame() -> void:
    if minigame_type == 0:  # Tap Race
        var bar_w := 200.0
        var bar_h := 24.0
        var start_x := 576.0 - bar_w * 2 - 60
        for i in range(4):
            var x := start_x + i * (bar_w + 40)
            var y := 300.0
            var pn := PLAYERS[i]
            # Background
            draw_rect(Rect2(x, y, bar_w, bar_h), Color(0.2, 0.2, 0.2))
            # Fill
            var fill := mg_tap_fill[i] if i < mg_tap_fill.size() else 0.0
            draw_rect(Rect2(x, y, bar_w * fill, bar_h), pn["col"])
            # Border
            draw_rect(Rect2(x, y, bar_w, bar_h), Color.WHITE, false, 1)

    elif minigame_type == 1:  # Dodge
        # Falling blocks
        for b in mg_dodge_blocks:
            draw_rect(Rect2(b["x"] - 15, b["y"] - 15, 30, 30), Color(0.9, 0.3, 0.3))
        # Players at bottom
        for i in range(4):
            if not mg_alive[i]:
                continue
            var px := mg_dodge_player_x[i] if i < mg_dodge_player_x.size() else 576.0
            var pn := PLAYERS[i]
            draw_rect(Rect2(px - 12, 585, 24, 24), pn["col"])
        # Floor line
        draw_line(Vector2(0, 610), Vector2(1152, 610), Color(0.5, 0.5, 0.5), 2)

    elif minigame_type == 2:  # Memory
        # Current sequence display
        var arrow_colors := [Color(0.4, 0.6, 1), Color(1, 0.4, 0.4), Color(0.4, 0.9, 0.4), Color(1, 0.9, 0.3)]
        var arrow_shapes: Array[PackedVector2Array] = [
            PackedVector2Array([Vector2(10, 0), Vector2(-10, -6), Vector2(-10, 6)]),  # left
            PackedVector2Array([Vector2(-10, 0), Vector2(10, -6), Vector2(10, 6)]),   # right
            PackedVector2Array([Vector2(0, -10), Vector2(-6, 10), Vector2(6, 10)]),   # up
            PackedVector2Array([Vector2(0, 10), Vector2(-6, -10), Vector2(6, -10)]),  # down
        ]
        if mg_mem_input_phase and mg_mem_step < mg_mem_sequence.size():
            var d := mg_mem_sequence[mg_mem_step]
            # PackedVector2Array kan inte skalas/flyttas med operatorer -
            # transformera punktvis (samma klass av fel som fangades i v1.96).
            var pts := PackedVector2Array()
            for v in arrow_shapes[d]:
                pts.append(v * 3.0 + Vector2(576, 350))
            draw_colored_polygon(pts, arrow_colors[d])

    elif minigame_type == 3:  # Coin Grab (v2.19)
        for c in mg_coin_items:
            var cpos := Vector2(float(c["x"]), float(c["y"]))
            draw_circle(cpos, 11, Color(0.65, 0.5, 0.1))
            draw_circle(cpos, 9, Color(1, 0.85, 0.25))
            draw_circle(cpos + Vector2(-3, -3), 3, Color(1, 0.97, 0.7))
        for i in range(4):
            var px := mg_dodge_player_x[i] if i < mg_dodge_player_x.size() else 576.0
            var pn := PLAYERS[i]
            draw_rect(Rect2(px - 14, 583, 28, 8), Color(0.2, 0.16, 0.28))
            draw_rect(Rect2(px - 12, 585, 24, 24), pn["col"])
            var sc := mg_coin_score[i] if i < mg_coin_score.size() else 0
            for s in range(mini(sc, 12)):
                draw_circle(Vector2(px - 12 + (s % 6) * 5, 622 + (s / 6) * 7), 2.2, Color(1, 0.85, 0.25))
        draw_line(Vector2(0, 610), Vector2(1152, 610), Color(0.5, 0.5, 0.5), 2)

    else:  # Quick Draw (v2.19)
        var sig_col := Color(0.8, 0.25, 0.25)
        if mg_qd_phase == "go":
            sig_col = Color(0.3, 0.95, 0.4)
        elif mg_qd_phase == "between":
            sig_col = Color(0.5, 0.5, 0.55)
        draw_circle(Vector2(576, 330), 74, Color(0.1, 0.09, 0.16))
        draw_circle(Vector2(576, 330), 64, sig_col)
        for i in range(4):
            var bx := 366.0 + i * 140.0
            var pn := PLAYERS[i]
            var col: Color = pn["col"]
            if mg_qd_locked[i]:
                col.a = 0.35
            draw_rect(Rect2(bx - 20, 470, 40, 40), col)
            for w in range(mg_qd_wins[i] if i < mg_qd_wins.size() else 0):
                draw_circle(Vector2(bx - 12 + w * 12, 530), 4, Color(1, 0.85, 0.25))

""";

    /// <summary>v2.5: Strike Arena - FPS-golvet (first person, 3D). Utan det
    /// foll fps-prompts till top-down-2D eller samlarspelet. Musfangst +
    /// piltangent-titt (sonden kan spela), matematisk siktkontroll (ingen
    /// fysik-raycast = deterministiskt), vagor, ammo, HP, 3D-juice.</summary>
    internal static string[] ScaffoldGodotFps(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Strike Arena"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node3D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotFpsMain);
        files.Add("Main.gd");
        foreach (var (name, category, seed) in new[]
        {
            ("click.wav", "shoot", 7), ("coin.wav", "powerup", 7),
            ("hurt.wav", "hurt", 7), ("win.wav", "win", 7),
        })
        {
            Write(root, name, SfxrGenerator.Render(category, seed));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotFpsDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Strike Arena - First person shooter (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "Komplett spelbar FPS-grund: first person-kamera med musfangst OCH\n" +
            "piltangent-titt, vagbaserade fiender som jagar, matematisk sikt-\n" +
            "kontroll (ingen fysik-raycast), ammo/HP-pickups, 5 vagor, tre\n" +
            "svarigheter, highscore, paus (slapper musen), 3D-partiklar, musik.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n\n" +
            "Styrning: WASD ror, mus ELLER piltangenter tittar, vansterklick/\n" +
            "Space skjuter, Esc pausar. All grafik ar kodbyggda meshar - byt\n" +
            "tema via material/farger i _build_world och fiendernas varden.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotFpsDesignDoc(string prompt) =>
        "# First person shooter (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nEn arena-FPS: overlev 5 vagor av jagande fiender fran forstapersons-\nperspektiv. Sikta, skjut, plocka ammo och HP, overlev.\n\n" +
        "## Mekanik\n- **Kamera:** first person (musfangst) + piltangent-titt som fallback\n" +
        "- **Skott:** matematisk siktkontroll (vinkel + rackvidd mot fiender) - ingen fysik-raycast, deterministiskt och robust\n" +
        "- **Fiender:** kapslar som jagar spelaren; 2 traffar; kontakt kostar HP (i-frames)\n" +
        "- **Vagor:** 5 st med fler/snabbare fiender; ammo- och HP-pickups spawnar mellan vagor\n" +
        "- **Svarighetsgrader:** Easy/Normal/Hard paverkar HP, ammo och fiendefart\n" +
        "- **Vinst:** klara vag 5; **Forlust:** 0 HP. Poang + highscore (user://)\n\n" +
        "## Produktion\n- Titel med instruktioner (fokus pa Easy - Enter startar), paus slapper musen, game over/vinst med omstart\n" +
        "- Juice: skottrekyl pa kameran, mynningsblixt (HUD-flash), traffpartiklar i 3D, skarmskak vid skada\n" +
        "- Ljud: skott/traff/pickup/seger + actionmusik (loopad)\n- Crosshair och HUD i CanvasLayer\n\n" +
        "## Extension (tema-exempel)\n- Fler vapentyper (spridning/automatisk), granater, boss pa vag 5,\n  korridorbana i stallet for arena, huvudskott-bonus, vapenupplasningar\n";

    // ---- Main.gd: Strike Arena (FPS) ---------------------------------------
    const string GodotFpsMain = """
extends Node3D
# Strike Arena - first person arena shooter. BYT TEMA: farger/material i
# _build_world, fiendernas varden i WAVES-logiken. Spelartext pa ENGELSKA.

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

const SAVE_PATH := "user://strikearena_best.txt"
const FINAL_WAVE := 5
const ARENA_R := 17.0

var state := "title" # title | playing | paused | over
var difficulty := 1
var hp := 100
var ammo := 24
var score := 0
var wave := 0
var best := 0
var invuln := 0.0
var recoil := 0.0
var flash := 0.0
var yaw := 0.0
var pitch := 0.0

var player: Node3D
var cam: Camera3D
var enemies: Array = []   # {node, hp, speed}
var pickups: Array = []   # {node, kind}
var ui: CanvasLayer
var hud: Label
var crosshair: Label
var flash_rect: ColorRect
var focus_pending := true
var snd := {}

func _ready() -> void:
    randomize()
    Shell.startup()
    for key in ["click", "coin", "hurt", "win"]:
        var s = load("res://%s.wav" % key)
        if s:
            var p := AudioStreamPlayer.new()
            p.stream = s
            add_child(p)
            snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()
    best = _load_best()
    _build_world()
    ui = CanvasLayer.new()
    add_child(ui)
    hud = Label.new()
    hud.position = Vector2(16, 10)
    hud.add_theme_font_size_override("font_size", 18)
    ui.add_child(hud)
    crosshair = Label.new()
    crosshair.text = "+"
    crosshair.add_theme_font_size_override("font_size", 26)
    crosshair.position = Vector2(566, 306)
    ui.add_child(crosshair)
    flash_rect = ColorRect.new()
    flash_rect.color = Color(1, 0.9, 0.5, 0.0)
    flash_rect.set_anchors_preset(Control.PRESET_FULL_RECT)
    flash_rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(flash_rect)
    _show_title()

func play_sound(key: String) -> void:
    if snd.has(key):
        snd[key].play()

func _mat(c: Color) -> StandardMaterial3D:
    var m := StandardMaterial3D.new()
    m.albedo_color = c
    return m

func _build_world() -> void:
    var light := DirectionalLight3D.new()
    light.rotation_degrees = Vector3(-55, -40, 0)
    light.light_energy = 1.1
    add_child(light)
    var env := WorldEnvironment.new()
    var e := Environment.new()
    e.background_mode = Environment.BG_COLOR
    e.background_color = Color(0.07, 0.08, 0.12)
    e.ambient_light_color = Color(0.5, 0.5, 0.6)
    e.ambient_light_energy = 0.65
    env.environment = e
    add_child(env)
    var ground := MeshInstance3D.new()
    var gm := CylinderMesh.new()
    gm.top_radius = ARENA_R + 1.0
    gm.bottom_radius = ARENA_R + 1.0
    gm.height = 0.6
    ground.mesh = gm
    ground.position = Vector3(0, -0.3, 0)
    ground.material_override = _mat(Color(0.22, 0.22, 0.28))
    add_child(ground)
    # Vaggring av pelare - ger djupkansla och riktmarken nar man snurrar.
    for i in range(14):
        var ang := TAU * float(i) / 14.0
        var pillar := MeshInstance3D.new()
        var bm := BoxMesh.new()
        bm.size = Vector3(1.4, 4.0, 1.4)
        pillar.mesh = bm
        pillar.position = Vector3(cos(ang) * (ARENA_R + 0.4), 2.0, sin(ang) * (ARENA_R + 0.4))
        pillar.material_override = _mat(Color(0.32, 0.34, 0.45) if i % 2 == 0 else Color(0.26, 0.28, 0.38))
        add_child(pillar)
    player = Node3D.new()
    player.position = Vector3(0, 1.6, 0)
    add_child(player)
    cam = Camera3D.new()
    player.add_child(cam)

func _make_enemy() -> Dictionary:
    var node := MeshInstance3D.new()
    var cm := CapsuleMesh.new()
    cm.radius = 0.5
    cm.height = 1.8
    node.mesh = cm
    node.material_override = _mat(Color(0.85, 0.3, 0.3))
    var ang := randf() * TAU
    node.position = Vector3(cos(ang) * (ARENA_R - 2.0), 1.0, sin(ang) * (ARENA_R - 2.0))
    add_child(node)
    return {"node": node, "hp": 2, "speed": 2.2 + wave * 0.35 + float(difficulty) * 0.4}

func _spawn_pickup(kind: String) -> void:
    var node := MeshInstance3D.new()
    var bm := BoxMesh.new()
    bm.size = Vector3(0.7, 0.7, 0.7)
    node.mesh = bm
    node.material_override = _mat(Color(0.3, 0.8, 1.0) if kind == "ammo" else Color(0.3, 0.9, 0.4))
    node.position = Vector3(randf_range(-10.0, 10.0), 0.6, randf_range(-10.0, 10.0))
    add_child(node)
    pickups.append({"node": node, "kind": kind})

func _burst3d(pos: Vector3, col: Color) -> void:
    var p := CPUParticles3D.new()
    p.position = pos
    p.amount = 14
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.5
    p.direction = Vector3(0, 1, 0)
    p.spread = 85.0
    p.initial_velocity_min = 2.5
    p.initial_velocity_max = 6.5
    p.gravity = Vector3(0, -12, 0)
    p.scale_amount_min = 1.0
    p.scale_amount_max = 2.0
    var m := SphereMesh.new()
    m.radius = 0.1
    m.height = 0.2
    var mat := StandardMaterial3D.new()
    mat.albedo_color = col
    m.material = mat
    p.mesh = m
    add_child(p)
    p.emitting = true
    get_tree().create_timer(1.0).timeout.connect(p.queue_free)

# ---------- ui ----------
func _clear_overlay() -> void:
    for c in ui.get_children():
        if c is Control and c != hud and c != crosshair and c != flash_rect:
            c.queue_free()
    focus_pending = true

func _label_ui(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

var _last_button: Button = null   # v2.22: fokuskedja for pilnavigering

func _button_ui(txt: String, y: float, cb: Callable) -> Button:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    # v2.22: explicit fokuskedja (CanvasLayer-knappar saknar auto-grannar).
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    if focus_pending:
        focus_pending = false
        b.grab_focus()
    return b

# ---------- flode ----------
func _show_title() -> void:
    state = "title"
    Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
    _clear_overlay()
    _last_button = null
    hud.text = ""
    crosshair.visible = false
    _label_ui("STRIKE ARENA", 80, 64, Color(1, 0.5, 0.35))
    _label_ui("First person arena shooter. Survive %d waves." % FINAL_WAVE, 170, 20)
    _label_ui("WASD: move   Mouse or arrow keys: look   Click/Space: fire   Esc: pause", 204, 16)
    _label_ui("Best score: %d" % best, 238, 16, Color(0.7, 0.9, 1))
    _button_ui("Start: Easy", 300, func(): _start(0))
    _button_ui("Start: Normal", 358, func(): _start(1))
    _button_ui("Start: Hard", 416, func(): _start(2))
    # v2.22 spelskalet: Options + Quit pa titeln.
    _button_ui("Options", 474, func(): _show_options())
    _button_ui("Quit", 532, func(): get_tree().quit())

func _show_options() -> void:
    play_sound("click")
    _clear_overlay()
    _last_button = null
    Shell.options_panel(ui, func(): _show_title())

func _start(diff: int) -> void:
    difficulty = diff
    play_sound("coin")
    hp = [140, 100, 70][diff]
    ammo = [36, 24, 18][diff]
    score = 0
    wave = 0
    yaw = 0.0
    pitch = 0.0
    player.position = Vector3(0, 1.6, 0)
    for e in enemies:
        if is_instance_valid(e["node"]):
            e["node"].queue_free()
    enemies = []
    for pk in pickups:
        if is_instance_valid(pk["node"]):
            pk["node"].queue_free()
    pickups = []
    _clear_overlay()
    crosshair.visible = true
    state = "playing"
    Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
    _next_wave()

func _next_wave() -> void:
    wave += 1
    if wave > FINAL_WAVE:
        _finish(true)
        return
    for i in range(2 + wave + difficulty):
        enemies.append(_make_enemy())
    _spawn_pickup("ammo")
    if wave >= 2:
        _spawn_pickup("hp")

func _finish(won: bool) -> void:
    state = "over"
    Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
    crosshair.visible = false
    if won:
        play_sound("win")
    if score > best:
        best = score
        _save_best(score)
    _clear_overlay()
    _label_ui("YOU WIN!" if won else "GAME OVER", 180, 56, Color(0.6, 0.9, 0.4) if won else Color(1, 0.5, 0.4))
    _label_ui("Score: %d   Best: %d" % [score, best], 260, 24)
    _button_ui("Play again", 330, func(): _show_title())

# ---------- input ----------
func _unhandled_input(event: InputEvent) -> void:
    if event is InputEventMouseMotion and state == "playing":
        yaw -= event.relative.x * 0.0028
        pitch = clampf(pitch - event.relative.y * 0.0028, -1.2, 1.2)
    if event.is_action_pressed("ui_cancel"):
        if state == "playing":
            state = "paused"
            Input.mouse_mode = Input.MOUSE_MODE_VISIBLE
            _label_ui("PAUSED", 240, 44)
            _label_ui("Esc: resume", 300, 18)
        elif state == "paused":
            state = "playing"
            Input.mouse_mode = Input.MOUSE_MODE_CAPTURED
            _clear_overlay()
    if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_LEFT and state == "playing":
        _fire()

func _fire() -> void:
    if ammo <= 0:
        play_sound("coin")
        return
    ammo -= 1
    play_sound("click")
    recoil = 0.06
    flash = 0.15
    var fwd := -cam.global_transform.basis.z
    var origin := cam.global_position
    # Matematisk siktkontroll: narmaste fiende inom siktvinkel + rackvidd.
    var best_e := -1
    var best_d := 999.0
    for i in range(enemies.size()):
        var en: Dictionary = enemies[i]
        if not is_instance_valid(en["node"]):
            continue
        var to: Vector3 = en["node"].global_position + Vector3(0, 0.9, 0) - origin
        var d := to.length()
        if d > 40.0:
            continue
        var ang := fwd.angle_to(to.normalized())
        if ang < 0.06 + 0.9 / maxf(d, 1.0) * 0.06 and d < best_d:
            best_d = d
            best_e = i
    if best_e >= 0:
        var en2: Dictionary = enemies[best_e]
        en2["hp"] = int(en2["hp"]) - 1
        _burst3d(en2["node"].global_position + Vector3(0, 1.0, 0), Color(1, 0.7, 0.3))
        if int(en2["hp"]) <= 0:
            score += 25
            _burst3d(en2["node"].global_position, Color(0.9, 0.3, 0.3))
            en2["node"].queue_free()
            enemies.remove_at(best_e)
            play_sound("coin")

# ---------- loop ----------
func _physics_process(delta: float) -> void:
    if state != "playing":
        return
    recoil = move_toward(recoil, 0.0, 0.3 * delta)
    flash = move_toward(flash, 0.0, 1.2 * delta)
    flash_rect.color.a = flash
    # Piltangent-titt (sondens vag in - och laptop utan mus).
    yaw -= Input.get_axis("ui_left", "ui_right") * 1.8 * delta
    pitch = clampf(pitch - Input.get_axis("ui_down", "ui_up") * -1.2 * delta, -1.2, 1.2)
    player.rotation.y = yaw
    cam.rotation.x = pitch + recoil
    # WASD-rorelse relativt blickriktningen.
    var mv := Vector2.ZERO
    if Input.is_physical_key_pressed(KEY_W):
        mv.y -= 1.0
    if Input.is_physical_key_pressed(KEY_S):
        mv.y += 1.0
    if Input.is_physical_key_pressed(KEY_A):
        mv.x -= 1.0
    if Input.is_physical_key_pressed(KEY_D):
        mv.x += 1.0
    if mv.length() > 0.0:
        mv = mv.normalized()
        var fwd2 := Vector3(sin(yaw), 0, cos(yaw))
        var right := Vector3(cos(yaw), 0, -sin(yaw))
        player.position += (fwd2 * mv.y + right * mv.x) * 6.0 * delta
        var flat := Vector2(player.position.x, player.position.z)
        if flat.length() > ARENA_R - 1.0:
            flat = flat.normalized() * (ARENA_R - 1.0)
            player.position.x = flat.x
            player.position.y = 1.6
            player.position.z = flat.y
    if Input.is_action_just_pressed("ui_accept"):
        _fire()
    if invuln > 0.0:
        invuln -= delta
    # Fiender jagar; kontakt kostar HP.
    for en in enemies:
        if not is_instance_valid(en["node"]):
            continue
        var node: MeshInstance3D = en["node"]
        var to_p := player.position - node.position
        to_p.y = 0.0
        node.position += to_p.normalized() * float(en["speed"]) * delta
        if invuln <= 0.0 and to_p.length() < 1.2:
            hp -= 12
            invuln = 0.9
            play_sound("hurt")
            flash = 0.25
            flash_rect.color = Color(1, 0.2, 0.2, flash)
            if hp <= 0:
                _finish(false)
                return
    flash_rect.color = Color(1, 0.9, 0.5, flash) if invuln <= 0.0 else Color(1, 0.2, 0.2, flash)
    # Pickups (snurrar + plockas pa avstand).
    var kept: Array = []
    for pk in pickups:
        if not is_instance_valid(pk["node"]):
            continue
        pk["node"].rotate_y(delta * 2.5)
        if player.position.distance_to(pk["node"].position) < 1.4:
            if str(pk["kind"]) == "ammo":
                ammo += 12
            else:
                hp = mini(hp + 25, 150)
            score += 5
            play_sound("coin")
            _burst3d(pk["node"].position, Color(0.4, 0.9, 1.0))
            pk["node"].queue_free()
        else:
            kept.append(pk)
    pickups = kept
    var alive := 0
    for en2 in enemies:
        if is_instance_valid(en2["node"]):
            alive += 1
    if alive == 0:
        score += 50
        _next_wave()
    hud.text = "HP: %d   Ammo: %d   Score: %d   Wave: %d/%d" % [maxi(hp, 0), ammo, score, mini(wave, FINAL_WAVE), FINAL_WAVE]

# ---------- highscore ----------
func _load_best() -> int:
    if not FileAccess.file_exists(SAVE_PATH):
        return 0
    var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
    return int(f.get_as_text()) if f else 0

func _save_best(v: int) -> void:
    var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
    if f:
        f.store_string(str(v))
""";

    /// <summary>v2.3: Board Bash 3D - partygenrens 3D-golv och det FORSTA
    /// flerfilskittet: Main.gd (brada/flode) + tre fristaende Mg*.gd-minispel.
    /// Filkonventionen AR utbyggnadsvagen: fler minispel = fler Mg*.gd-filer
    /// (GenreContracts.CountMinigames raknar dem, teamspar bygger dem
    /// parallellt). 3D-modeller byggs i kod (meshar/material/ljus) och varje
    /// minispel har EGET ljud (olika sfxr-kategori+seed).</summary>
    internal static string[] ScaffoldGodotParty3D(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Board Bash 3D"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node3D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotParty3DMain);
        files.Add("Main.gd");
        // v2.24 pixellyftet: samma pixelgubbe som 2D-partyt, som billboard-
        // sprite i 3D-varlden (SNES-Mario-Party-looken) - fargas per spelare
        // via modulate i make_actor.
        var tokenSheet = PixelAnimator.Build(prompt);
        Write(root, "player.png", tokenSheet.Png);
        Write(root, "player_frames.tres", GodotSpriteFrames.Build("player.png", tokenSheet));
        files.Add("player.png"); files.Add("player_frames.tres");
        Write(root, "MgRace3D.gd", GodotParty3DMgRace);
        files.Add("MgRace3D.gd");
        Write(root, "MgFall3D.gd", GodotParty3DMgFall);
        files.Add("MgFall3D.gd");
        Write(root, "MgCollect3D.gd", GodotParty3DMgCollect);
        files.Add("MgCollect3D.gd");
        // OLIKA ljudeffekter: basljuden + ETT eget ljud per minispel
        // (distinkt sfxr-kategori och seed - inte samma pip overallt).
        foreach (var (name, category, seed) in new[]
        {
            ("click.wav", "select", 7), ("coin.wav", "coin", 7),
            ("hurt.wav", "hurt", 7), ("win.wav", "win", 7),
            ("dice.wav", "select", 21), ("star.wav", "powerup", 9),
            ("mg_race.wav", "jump", 13), ("mg_fall.wav", "explosion", 17),
            ("mg_collect.wav", "coin", 23),
        })
        {
            Write(root, name, SfxrGenerator.Render(category, seed));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotParty3DDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Board Bash 3D - 3D party board game (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n" +
            "FLERFILSKIT - utbyggnadskonventionen: VARJE minispel ar en egen\n" +
            "Mg*.gd-fil (extends Node3D, func setup(main), anropar\n" +
            "main.minigame_done(rankings) nar det ar klart) och registreras i\n" +
            "MINIGAMES-listan i Main.gd. Fler minispel = fler Mg*.gd-filer -\n" +
            "kvalitetsgrinden RAKNAR dem mot promptens begarda antal.\n\n" +
            "Innehall: 3D-brada (24 rutor i ring), tarning, 4 spelare (du + 3\n" +
            "AI), mynt/stjarnor, 3 st 3D-minispel (Race, Falling Floor,\n" +
            "Coin Rush) med EGNA ljud, 6 ronder, resultatskarm.\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n\n" +
            "Styrning: Space/Enter rullar tarningen; pilar/WASD i minispelen;\n" +
            "Esc pausar. Grafiken ar pixelbrickor (_make_tile_tex) + animerade\n" +
            "pixelgubbar som billboard-sprites (make_actor, player_frames.tres)\n" +
            "i en kodbyggd 3D-varld - byt tema via _build_world, _make_tile_tex\n" +
            "och minispelens filer.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotParty3DDesignDoc(string prompt) =>
        "# 3D Party / bradspel med minispel (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nMario Party/Pummel Party-klassen i 3D: rulla tarningen pa en 3D-brada,\n" +
        "samla mynt, kop stjarnor, och avgor ronderna i 3D-minispel.\n\n" +
        "## Arkitektur (VIKTIG - foljs vid utbyggnad)\n" +
        "- `Main.gd`: spelflode (titel -> brada -> minispel -> resultat), delat spelartillstand, ljud\n" +
        "- `Mg*.gd`: ETT minispel per fil. Kontrakt: `extends Node3D`, `func setup(main)`,\n" +
        "  kor sitt lopp och anropar `main.minigame_done(rankings)` (Array[int], basta forst)\n" +
        "- Nytt minispel = ny Mg*.gd + en rad i MINIGAMES-listan + eget ljud (sfxr-wav)\n" +
        "- Kvalitetsgrinden raknar Mg*.gd-filer mot promptens begarda antal (\"15 minigames\")\n\n" +
        "## Mekanik\n- Brada: 24 rutor i ring (bla +3 mynt, roda -3, gula = minispelsbonus, lila = stjarnruta 20 mynt)\n" +
        "- Tarning 1-6 (Space), token hoppar ruta for ruta; AI rullar sjalv\n" +
        "- Efter varje rond (alla 4 flyttat): ett slumpat minispel; vinnaren far mest mynt\n" +
        "- 6 ronder; flest stjarnor vinner (mynt avgor lika)\n\n" +
        "## Minispel (golvets tre - LAGG FLER)\n- MgRace3D: springlopp till mallinjen (pilar; AI-fart per svarighet)\n" +
        "- MgFall3D: golvplattor faller - overlev langst (flytta dig fran rott)\n- MgCollect3D: plocka flest mynt pa 15 s\n\n" +
        "## Produktion\n- Titel med bradval/svarighet (fokus pa Easy - Enter startar), paus, resultat, highscore (user://)\n" +
        "- Juice: kamerashake, partiklar (3D), hoppanimation, rutpuls\n- Ljud: EGNA ljud per minispel + tarning/stjarna (olika sfxr-kategorier/seeds)\n\n" +
        "## Extension (tema-exempel)\n- Pummel Party-elak: sabotage-items, stold av mynt\n- Lego-tema: bygg-minispel (stapla block)\n- Fler brador, items/foremÃ¥l, boss-minispel, 2-4 manniskor hotseat\n";

    // ---- Main.gd: Board Bash 3D (flerfilskittets nav) ----------------------
    const string GodotParty3DMain = """
extends Node3D
# Board Bash 3D - 3D-partybrada med minispel i EGNA filer (Mg*.gd).
# BYT TEMA: farger/material i _build_world, ruttyper i TILE_*, minispel
# registreras i MINIGAMES. Spelartext pa ENGELSKA (husregeln).

const SAVE_PATH := "user://boardbash3d_best.txt"
const TILE_COUNT := 24
const ROUNDS := 6
const TILE_BLUE := 0
const TILE_RED := 1
const TILE_BONUS := 2
const TILE_STAR := 3

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")

# Minispelsregistret - utbyggnadskonventionen: en Mg*.gd-fil per minispel.
const MINIGAMES: Array = [
    {"scene": "res://MgRace3D.gd", "name": "Foot Race", "sound": "mg_race"},
    {"scene": "res://MgFall3D.gd", "name": "Falling Floor", "sound": "mg_fall"},
    {"scene": "res://MgCollect3D.gd", "name": "Coin Rush", "sound": "mg_collect"},
]

var state := "title" # title | board | minigame | results
var difficulty := 1
var round_no := 1
var turn_idx := 0
var rolling := false
var moving_steps := 0
var move_timer := 0.0
var ai_timer := 0.0
var attract_t := 0.0   # auto-rull efter 8s idle - spelet demonstrerar sig sjalvt
var shake := 0.0
var best_stars := 0

# Spelare: {name, color, tile, coins, stars, node}
var players: Array = []
var tiles: Array = []        # {type, pos, node}
var tile_nodes: Array = []
var mode_node: Node3D = null
var cam: Camera3D
var ui: CanvasLayer
var hud: Label
var phase_label: Label
var focus_pending := true
var snd := {}

func _ready() -> void:
    randomize()
    Shell.startup()
    _setup_audio()
    best_stars = _load_best()
    _build_world()
    ui = CanvasLayer.new()
    add_child(ui)
    hud = Label.new()
    hud.position = Vector2(16, 10)
    hud.add_theme_font_size_override("font_size", 17)
    ui.add_child(hud)
    phase_label = Label.new()
    phase_label.position = Vector2(700, 10)
    phase_label.add_theme_font_size_override("font_size", 20)
    phase_label.add_theme_color_override("font_color", Color(1, 0.9, 0.4))
    ui.add_child(phase_label)
    _setup_touch()
    _show_title()

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win", "dice", "star", "mg_race", "mg_fall", "mg_collect"]:
        var s = load("res://%s.wav" % key)
        if s:
            var p := AudioStreamPlayer.new()
            p.stream = s
            add_child(p)
            snd[key] = p
    # Bakgrundsmusik (v2.4): loopbar chiptune, lag volym under effekterna.
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func play_sound(key: String) -> void:
    if snd.has(key):
        snd[key].play()

# ---------- touch (aktiveras BARA pa touchskarm) ----------
func _setup_touch() -> void:
    if not DisplayServer.is_touchscreen_available():
        return
    _touch_btn(Vector2(28, 536), "ui_left", "<")
    _touch_btn(Vector2(132, 536), "ui_right", ">")
    _touch_btn(Vector2(1036, 444), "ui_up", "^")
    _touch_btn(Vector2(1036, 536), "ui_accept", "GO")

func _touch_btn(pos: Vector2, action: String, text: String) -> TouchScreenButton:
    var img := Image.create(88, 88, false, Image.FORMAT_RGBA8)
    img.fill(Color(1, 1, 1, 0.20))
    for x in range(88):
        for y in range(88):
            if x < 3 or y < 3 or x > 84 or y > 84:
                img.set_pixel(x, y, Color(1, 1, 1, 0.55))
    var b := TouchScreenButton.new()
    b.texture_normal = ImageTexture.create_from_image(img)
    b.position = pos
    if action != "":
        b.action = action
    ui.add_child(b)
    var t := Label.new()
    t.text = text
    t.add_theme_font_size_override("font_size", 22)
    t.position = pos
    t.size = Vector2(88, 88)
    t.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    t.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
    t.mouse_filter = Control.MOUSE_FILTER_IGNORE
    ui.add_child(t)
    return b

# ---------- 3D-varlden (allt i kod - inga externa modeller) ----------
func _mat(c: Color) -> StandardMaterial3D:
    var m := StandardMaterial3D.new()
    m.albedo_color = c
    return m

# v2.24 pixellyftet: rutorna ar pixelbrickor (16x16-textur med kontur,
# ljus topp, mork botten och symbol - samma sprak som 2D-partyt) och
# NEAREST-filter sa pixlarna star skarpa aven i 3D.
func _make_tile_tex(t: int) -> ImageTexture:
    var base: Color
    match t:
        TILE_RED: base = Color8(198, 62, 62)
        TILE_BONUS: base = Color8(228, 186, 56)
        TILE_STAR: base = Color8(152, 64, 192)
        _: base = Color8(58, 112, 200)
    var light := base.lightened(0.30)
    var dark := base.darkened(0.30)
    var outline := Color8(27, 22, 36)
    var img := Image.create(16, 16, false, Image.FORMAT_RGBA8)
    for y in range(16):
        for x in range(16):
            if (x == 0 or x == 15) and (y == 0 or y == 15):
                img.set_pixel(x, y, outline)  # 3D-brickan har inga genomskinliga horn
            elif x == 0 or x == 15 or y == 0 or y == 15:
                img.set_pixel(x, y, outline)
            elif y <= 2:
                img.set_pixel(x, y, light)
            elif y >= 13:
                img.set_pixel(x, y, dark)
            else:
                img.set_pixel(x, y, base)
    var sym := Color(1, 1, 1, 0.95)
    match t:
        TILE_BLUE:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
                img.set_pixel(7, 5 + i, sym)
        TILE_RED:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
        TILE_STAR:
            for i in range(5):
                img.set_pixel(5 + i, 7, sym)
                img.set_pixel(7, 5 + i, sym)
            img.set_pixel(5, 5, sym)
            img.set_pixel(9, 5, sym)
            img.set_pixel(5, 9, sym)
            img.set_pixel(9, 9, sym)
        _:
            img.set_pixel(6, 6, sym)
            img.set_pixel(9, 6, sym)
            img.set_pixel(6, 9, sym)
            img.set_pixel(9, 9, sym)
    return ImageTexture.create_from_image(img)

func _tile_mat(t: int) -> StandardMaterial3D:
    var m := StandardMaterial3D.new()
    m.albedo_texture = _make_tile_tex(t)
    m.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
    return m

# Pixelgubben som billboard-sprite (delas med minispelen via main.make_actor):
# samma animerade figur som 2D-partyt, fargad per spelare. Fallback till
# kapsel om player_frames.tres saknas i en aldre projektkopia.
func make_actor(col: Color) -> Node3D:
    if ResourceLoader.exists("res://player_frames.tres"):
        var s := AnimatedSprite3D.new()
        s.sprite_frames = load("res://player_frames.tres")
        s.modulate = col.lerp(Color.WHITE, 0.35)
        s.billboard = BaseMaterial3D.BILLBOARD_ENABLED
        s.texture_filter = BaseMaterial3D.TEXTURE_FILTER_NEAREST
        s.alpha_cut = SpriteBase3D.ALPHA_CUT_DISCARD
        s.pixel_size = 0.07
        s.play("idle")
        return s
    var n := MeshInstance3D.new()
    var m := CapsuleMesh.new()
    m.radius = 0.42
    m.height = 1.5
    n.mesh = m
    n.material_override = _mat(col)
    return n

func set_actor_anim(node: Node3D, anim: String) -> void:
    if node is AnimatedSprite3D:
        node.play(anim)

func _build_world() -> void:
    var light := DirectionalLight3D.new()
    light.rotation_degrees = Vector3(-58, -35, 0)
    light.light_energy = 1.2
    add_child(light)
    var env := WorldEnvironment.new()
    var e := Environment.new()
    e.background_mode = Environment.BG_COLOR
    e.background_color = Color(0.10, 0.09, 0.16)
    e.ambient_light_color = Color(0.55, 0.5, 0.65)
    e.ambient_light_energy = 0.7
    env.environment = e
    add_child(env)
    var ground := MeshInstance3D.new()
    var gm := CylinderMesh.new()
    gm.top_radius = 15.0
    gm.bottom_radius = 15.0
    gm.height = 0.8
    ground.mesh = gm
    ground.position = Vector3(0, -0.4, 0)
    ground.material_override = _mat(Color(0.16, 0.3, 0.2))
    add_child(ground)
    cam = Camera3D.new()
    cam.position = Vector3(0, 16, 15)
    cam.rotation_degrees = Vector3(-48, 0, 0)
    add_child(cam)
    # Bradan: 24 rutor i en ring av pixelbrickor (v2.24: alla ruttyper ar
    # boxar med pixeltextur - stjarnrutan hojs och far storre bricka i
    # stallet for den gamla sfaren, sa pixelspraket haller ihop).
    for i in range(TILE_COUNT):
        var ang := TAU * float(i) / float(TILE_COUNT)
        var pos := Vector3(cos(ang) * 11.0, 0.15, sin(ang) * 11.0)
        var t := TILE_BLUE
        if i % 6 == 3:
            t = TILE_RED
        elif i % 8 == 5:
            t = TILE_BONUS
        elif i % 12 == 7:
            t = TILE_STAR
        var node := MeshInstance3D.new()
        var bm := BoxMesh.new()
        bm.size = Vector3(1.9, 0.5, 1.9) if t == TILE_STAR else Vector3(1.6, 0.3, 1.6)
        node.mesh = bm
        node.material_override = _tile_mat(t)
        node.position = pos + (Vector3(0, 0.1, 0) if t == TILE_STAR else Vector3.ZERO)
        add_child(node)
        tiles.append({"type": t, "pos": pos})
        tile_nodes.append(node)

func _make_player_token(col: Color) -> Node3D:
    var n := make_actor(col)
    add_child(n)
    return n

func _burst3d(pos: Vector3, col: Color) -> void:
    var p := CPUParticles3D.new()
    p.position = pos
    p.amount = 14
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.6
    p.direction = Vector3(0, 1, 0)
    p.spread = 75.0
    p.initial_velocity_min = 2.5
    p.initial_velocity_max = 6.0
    p.gravity = Vector3(0, -12, 0)
    p.scale_amount_min = 1.0
    p.scale_amount_max = 2.0
    var m := SphereMesh.new()
    m.radius = 0.12
    m.height = 0.24
    var mat := StandardMaterial3D.new()
    mat.albedo_color = col
    m.material = mat
    p.mesh = m
    add_child(p)
    p.emitting = true
    get_tree().create_timer(1.2).timeout.connect(p.queue_free)

# ---------- ui-hjalpare ----------
func _clear_overlay() -> void:
    for c in ui.get_children():
        if c is Control and c != hud and c != phase_label:
            c.queue_free()
    focus_pending = true

func _label_ui(txt: String, y: float, fsize: int, col := Color.WHITE) -> Label:
    var l := Label.new()
    l.text = txt
    l.add_theme_font_size_override("font_size", fsize)
    l.add_theme_color_override("font_color", col)
    l.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
    l.size = Vector2(1152, fsize + 10)
    l.position = Vector2(0, y)
    ui.add_child(l)
    return l

func _button_ui(txt: String, y: float, cb: Callable) -> Button:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
    if focus_pending:
        focus_pending = false
        b.grab_focus()
    return b

# ---------- flode ----------
func _show_title() -> void:
    state = "title"
    _clear_overlay()
    hud.text = ""
    phase_label.text = ""
    _label_ui("BOARD BASH 3D", 70, 64, Color(1, 0.82, 0.25))
    _label_ui("A 3D party board game! Roll dice, grab coins, buy stars, win minigames.", 160, 20)
    _label_ui("Most stars after %d rounds wins. Best so far: %d stars." % [ROUNDS, best_stars], 194, 16, Color(0.7, 0.9, 1))
    _button_ui("Start: Easy", 260, func(): _start_game(0))
    _button_ui("Start: Normal", 318, func(): _start_game(1))
    _button_ui("Start: Hard", 376, func(): _start_game(2))
    # v2.22 spelskalet: Options + Quit pa titeln.
    _button_ui("Options", 434, func(): _show_options())
    _button_ui("Quit", 492, func(): get_tree().quit())
    _label_ui("Space/Enter rolls the dice on your turn. Arrows/WASD in minigames.", 560, 14, Color(0.55, 0.55, 0.6))

func _show_options() -> void:
    play_sound("click")
    _clear_overlay()
    Shell.options_panel(ui, func(): _show_title())

func _start_game(diff: int) -> void:
    difficulty = diff
    play_sound("click")
    round_no = 1
    turn_idx = 0
    for p in players:
        if p.has("node") and is_instance_valid(p["node"]):
            p["node"].queue_free()
    players = []
    var defs := [
        {"name": "You", "color": Color(0.3, 0.7, 1.0)},
        {"name": "Bot A", "color": Color(0.95, 0.4, 0.35)},
        {"name": "Bot B", "color": Color(0.4, 0.9, 0.45)},
        {"name": "Bot C", "color": Color(0.95, 0.75, 0.3)},
    ]
    for i in range(4):
        var d: Dictionary = defs[i]
        var tok := _make_player_token(d["color"])
        players.append({"name": d["name"], "color": d["color"], "tile": 0, "coins": 10, "stars": 0, "node": tok})
    _place_tokens()
    _clear_overlay()
    state = "board"
    rolling = false
    _update_hud()

func _place_tokens() -> void:
    for i in range(players.size()):
        var p: Dictionary = players[i]
        var base: Vector3 = tiles[p["tile"]]["pos"]
        var off := Vector3(float(i % 2) * 0.7 - 0.35, 1.0, float(i / 2) * 0.7 - 0.35)
        p["node"].position = base + off

func _update_hud() -> void:
    var parts: Array = []
    for p in players:
        parts.append("%s S:%d C:%d" % [p["name"], p["stars"], p["coins"]])
    hud.text = "  ".join(parts)
    if state == "board":
        phase_label.text = "Round %d/%d   %s" % [round_no, ROUNDS,
            ("Your turn - roll!" if turn_idx == 0 else str(players[turn_idx]["name"]) + " rolls...")]

func _physics_process(delta: float) -> void:
    if shake > 0.0:
        shake = move_toward(shake, 0.0, 2.5 * delta)
        cam.position = Vector3(0, 16, 15) + Vector3(randf_range(-shake, shake), randf_range(-shake, shake), 0)
    if state != "board":
        return
    if moving_steps > 0:
        move_timer -= delta
        if move_timer <= 0.0:
            move_timer = 0.22
            var p: Dictionary = players[turn_idx]
            p["tile"] = (int(p["tile"]) + 1) % TILE_COUNT
            _place_tokens()
            play_sound("click")
            moving_steps -= 1
            if moving_steps == 0:
                set_actor_anim(p["node"], "idle")
                _resolve_tile()
        return
    if turn_idx == 0:
        if not rolling:
            # Attract-autopilot: efter 8s utan input rullar spelet sjalvt -
            # partyt stannar aldrig, och kvalitetsgrindens sond nar hela
            # loopen (brada -> minispel) utan att kunna reglerna.
            attract_t += delta
            if Input.is_action_just_pressed("ui_accept") or attract_t > 8.0:
                attract_t = 0.0
                _roll()
    else:
        ai_timer -= delta
        if ai_timer <= 0.0:
            _roll()

func _roll() -> void:
    rolling = true
    play_sound("dice")
    moving_steps = 1 + randi() % 6
    move_timer = 0.35
    set_actor_anim(players[turn_idx]["node"], "walk")
    phase_label.text = "Round %d/%d   %s rolled %d!" % [round_no, ROUNDS, ("You" if turn_idx == 0 else str(players[turn_idx]["name"])), moving_steps]

func _resolve_tile() -> void:
    var p: Dictionary = players[turn_idx]
    var tile: Dictionary = tiles[p["tile"]]
    var t: int = tile["type"]
    var pos: Vector3 = tile["pos"]
    if t == TILE_BLUE:
        p["coins"] = int(p["coins"]) + 3
        play_sound("coin")
        _burst3d(pos, Color(0.4, 0.7, 1.0))
    elif t == TILE_RED:
        p["coins"] = maxi(0, int(p["coins"]) - 3)
        play_sound("hurt")
        shake = maxf(shake, 0.5)
        _burst3d(pos, Color(0.9, 0.3, 0.3))
    elif t == TILE_BONUS:
        p["coins"] = int(p["coins"]) + 6
        play_sound("coin")
        _burst3d(pos, Color(1, 0.9, 0.4))
    elif t == TILE_STAR:
        if int(p["coins"]) >= 20:
            p["coins"] = int(p["coins"]) - 20
            p["stars"] = int(p["stars"]) + 1
            play_sound("star")
            _burst3d(pos + Vector3(0, 1, 0), Color(0.85, 0.5, 1.0))
    _update_hud()
    rolling = false
    turn_idx += 1
    ai_timer = 0.8
    if turn_idx >= players.size():
        turn_idx = 0
        _start_minigame()
    else:
        _update_hud()

# ---------- minispel (flerfilskonventionen) ----------
func _start_minigame() -> void:
    state = "minigame"
    var mg: Dictionary = MINIGAMES[randi() % MINIGAMES.size()]
    phase_label.text = "Minigame: " + str(mg["name"])
    play_sound(str(mg["sound"]))
    var script_res = load(str(mg["scene"]))
    if script_res == null:
        _after_minigame([0, 1, 2, 3])
        return
    mode_node = Node3D.new()
    mode_node.set_script(script_res)
    add_child(mode_node)
    if mode_node.has_method("setup"):
        mode_node.call("setup", self)

func minigame_done(rankings: Array) -> void:
    var awards := [10, 6, 3, 1]
    for rank in range(mini(rankings.size(), 4)):
        var pi: int = rankings[rank]
        var award: int = awards[rank]
        players[pi]["coins"] = int(players[pi]["coins"]) + award
    play_sound("win")
    _after_minigame(rankings)

func _after_minigame(_rankings: Array) -> void:
    if mode_node and is_instance_valid(mode_node):
        mode_node.queue_free()
    mode_node = null
    _place_tokens()
    _update_hud()
    if round_no >= ROUNDS:
        _show_results()
        return
    round_no += 1
    state = "board"
    _update_hud()

func _show_results() -> void:
    state = "results"
    var ranked := players.duplicate()
    ranked.sort_custom(func(a, b):
        if int(a["stars"]) != int(b["stars"]):
            return int(a["stars"]) > int(b["stars"])
        return int(a["coins"]) > int(b["coins"]))
    var winner: Dictionary = ranked[0]
    if int(winner["stars"]) > best_stars and str(winner["name"]) == "You":
        best_stars = int(winner["stars"])
        _save_best(best_stars)
    play_sound("win")
    _clear_overlay()
    _label_ui("RESULTS", 90, 54, Color(1, 0.85, 0.3))
    for i in range(ranked.size()):
        var p: Dictionary = ranked[i]
        _label_ui("%d. %s - %d stars, %d coins" % [i + 1, p["name"], int(p["stars"]), int(p["coins"])], 180 + i * 34, 22, p["color"])
    _label_ui(("You win the party!" if str(winner["name"]) == "You" else str(winner["name"]) + " wins the party!"), 340, 26)
    _button_ui("Play again", 400, func(): _show_title())

func _unhandled_input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel") and state == "board":
        _show_title()

# ---------- highscore ----------
func _load_best() -> int:
    if not FileAccess.file_exists(SAVE_PATH):
        return 0
    var f := FileAccess.open(SAVE_PATH, FileAccess.READ)
    return int(f.get_as_text()) if f else 0

func _save_best(v: int) -> void:
    var f := FileAccess.open(SAVE_PATH, FileAccess.WRITE)
    if f:
        f.store_string(str(v))
""";

    // ---- MgRace3D.gd: springlopp (Minigame 1) ------------------------------
    const string GodotParty3DMgRace = """
extends Node3D
# Minigame 1 (Foot Race): forsta over mallinjen. Pilar/WASD = spring.
# Kontraktet for ALLA minispel: setup(main) -> spela -> main.minigame_done(rankings).

var main: Node3D = null
var runners: Array = []
var t := 0.0
var countdown := 3.0
var done := false
var finished: Array = []

func setup(m: Node3D) -> void:
    main = m
    for i in range(4):
        # v2.24: pixelgubben fran main (billboard-sprite) i stallet for kapsel.
        var body: Node3D = main.make_actor(main.players[i]["color"])
        body.position = Vector3(-9.0, 1.0, float(i) * 1.6 - 2.4)
        add_child(body)
        main.set_actor_anim(body, "walk")
        runners.append({"idx": i, "node": body, "x": -9.0, "speed": 0.0})
    var goal := MeshInstance3D.new()
    var gm := BoxMesh.new()
    gm.size = Vector3(0.4, 2.2, 8.0)
    goal.mesh = gm
    var gmat := StandardMaterial3D.new()
    gmat.albedo_color = Color(1, 1, 1, 0.8)
    goal.material_override = gmat
    goal.position = Vector3(9.0, 1.0, 0)
    add_child(goal)

func _physics_process(delta: float) -> void:
    if main == null or done:
        return
    if countdown > 0.0:
        countdown -= delta
        main.phase_label.text = "Foot Race - GO in %d..." % int(ceil(countdown))
        return
    t += delta
    var ai_speed: Array = [3.2, 3.9, 4.6]
    for r in runners:
        var i: int = r["idx"]
        if finished.has(i):
            continue
        if i == 0:
            var dir := Input.get_axis("ui_left", "ui_right")
            var fwd := 1.0 if Input.is_action_pressed("ui_up") else 0.0
            r["x"] = float(r["x"]) + (maxf(dir, fwd)) * 5.2 * delta
        else:
            var sp: float = ai_speed[main.difficulty]
            r["x"] = float(r["x"]) + (sp + randf_range(-0.6, 0.6)) * delta
        r["node"].position.x = float(r["x"])
        if float(r["x"]) >= 9.0:
            finished.append(i)
            main.play_sound("mg_race")
    main.phase_label.text = "Foot Race - run right! (%.0fs)" % t
    if finished.size() >= 3 or t > 25.0:
        done = true
        for r in runners:
            var i2: int = r["idx"]
            if not finished.has(i2):
                finished.append(i2)
        main.minigame_done(finished)
""";

    // ---- MgFall3D.gd: fallande golv (Minigame 2) ---------------------------
    const string GodotParty3DMgFall = """
extends Node3D
# Minigame 2 (Falling Floor): plattor blir roda och faller - overlev langst.
# Pilar/WASD flyttar dig mellan plattorna.

var main: Node3D = null
var pads: Array = []
var actors: Array = []
var countdown := 3.0
var t := 0.0
var next_drop := 1.2
var done := false
var eliminated: Array = []

func setup(m: Node3D) -> void:
    main = m
    for gx in range(4):
        for gz in range(4):
            var pad := MeshInstance3D.new()
            var bm := BoxMesh.new()
            bm.size = Vector3(2.4, 0.3, 2.4)
            pad.mesh = bm
            var mat := StandardMaterial3D.new()
            mat.albedo_color = Color(0.35, 0.55, 0.8)
            pad.material_override = mat
            pad.position = Vector3(float(gx) * 2.7 - 4.05, 0.2, float(gz) * 2.7 - 4.05)
            add_child(pad)
            pads.append({"node": pad, "alive": true, "warn": 0.0})
    for i in range(4):
        # v2.24: pixelgubben fran main (billboard-sprite) i stallet for kapsel.
        var body: Node3D = main.make_actor(main.players[i]["color"])
        body.position = Vector3(float(i % 2) * 2.7 - 1.35, 1.2, float(i / 2) * 2.7 - 1.35)
        add_child(body)
        main.set_actor_anim(body, "walk")
        actors.append({"idx": i, "node": body, "alive": true})

func _pad_at(pos: Vector3) -> int:
    for pi in range(pads.size()):
        var pd: Dictionary = pads[pi]
        if not pd["alive"]:
            continue
        var pp: Vector3 = pd["node"].position
        if absf(pos.x - pp.x) < 1.35 and absf(pos.z - pp.z) < 1.35:
            return pi
    return -1

func _physics_process(delta: float) -> void:
    if main == null or done:
        return
    if countdown > 0.0:
        countdown -= delta
        main.phase_label.text = "Falling Floor - survive! %d..." % int(ceil(countdown))
        return
    t += delta
    next_drop -= delta
    if next_drop <= 0.0:
        next_drop = maxf(0.45, 1.2 - t * 0.03)
        var alive_pads: Array = []
        for pi in range(pads.size()):
            if pads[pi]["alive"] and float(pads[pi]["warn"]) <= 0.0:
                alive_pads.append(pi)
        if alive_pads.size() > 3:
            var pick: int = alive_pads[randi() % alive_pads.size()]
            pads[pick]["warn"] = 0.9
            pads[pick]["node"].material_override.albedo_color = Color(0.9, 0.3, 0.25)
    for pd in pads:
        if float(pd["warn"]) > 0.0:
            pd["warn"] = float(pd["warn"]) - delta
            if float(pd["warn"]) <= 0.0 and pd["alive"]:
                pd["alive"] = false
                pd["node"].visible = false
                main.play_sound("mg_fall")
    for a in actors:
        if not a["alive"]:
            continue
        var i: int = a["idx"]
        var node: MeshInstance3D = a["node"]
        if i == 0:
            var dx := Input.get_axis("ui_left", "ui_right")
            var dz := Input.get_axis("ui_up", "ui_down")
            node.position.x += dx * 4.5 * delta
            node.position.z += dz * 4.5 * delta
        else:
            var here := _pad_at(node.position)
            if here < 0 or float(pads[here]["warn"]) > 0.0:
                var target := -1
                for pi in range(pads.size()):
                    if pads[pi]["alive"] and float(pads[pi]["warn"]) <= 0.0:
                        target = pi
                        break
                if target >= 0:
                    var tp: Vector3 = pads[target]["node"].position
                    var err := 1.0 - float(main.difficulty) * 0.35
                    node.position.x = move_toward(node.position.x, tp.x + randf_range(-err, err), 3.6 * delta)
                    node.position.z = move_toward(node.position.z, tp.z + randf_range(-err, err), 3.6 * delta)
        if _pad_at(node.position) < 0:
            a["alive"] = false
            node.visible = false
            eliminated.append(i)
            main.play_sound("hurt")
    var alive_count := 0
    for a2 in actors:
        if a2["alive"]:
            alive_count += 1
    main.phase_label.text = "Falling Floor - last one standing! (%.0fs)" % t
    if alive_count <= 1 or t > 30.0:
        done = true
        var rankings: Array = []
        for a3 in actors:
            if a3["alive"]:
                rankings.append(a3["idx"])
        for j in range(eliminated.size() - 1, -1, -1):
            rankings.append(eliminated[j])
        main.minigame_done(rankings)
""";

    // ---- MgCollect3D.gd: myntrush (Minigame 3) -----------------------------
    const string GodotParty3DMgCollect = """
extends Node3D
# Minigame 3 (Coin Rush): plocka flest mynt pa 15 sekunder. Pilar/WASD.

var main: Node3D = null
var actors: Array = []
var coins: Array = []
var scores: Array = [0, 0, 0, 0]
var countdown := 3.0
var time_left := 15.0
var done := false

func setup(m: Node3D) -> void:
    main = m
    for i in range(4):
        # v2.24: pixelgubben fran main (billboard-sprite) i stallet for kapsel.
        var body: Node3D = main.make_actor(main.players[i]["color"])
        body.position = Vector3(float(i % 2) * 4.0 - 2.0, 1.0, float(i / 2) * 4.0 - 2.0)
        add_child(body)
        main.set_actor_anim(body, "walk")
        actors.append({"idx": i, "node": body})
    for c in range(14):
        _spawn_coin()

func _spawn_coin() -> void:
    var coin := MeshInstance3D.new()
    var sm := SphereMesh.new()
    sm.radius = 0.35
    sm.height = 0.7
    coin.mesh = sm
    var mat := StandardMaterial3D.new()
    mat.albedo_color = Color(1, 0.85, 0.2)
    coin.material_override = mat
    coin.position = Vector3(randf_range(-8.0, 8.0), 0.6, randf_range(-8.0, 8.0))
    add_child(coin)
    coins.append(coin)

func _physics_process(delta: float) -> void:
    if main == null or done:
        return
    if countdown > 0.0:
        countdown -= delta
        main.phase_label.text = "Coin Rush - grab coins! %d..." % int(ceil(countdown))
        return
    time_left -= delta
    var ai_speed: Array = [3.4, 4.1, 4.8]
    for a in actors:
        var i: int = a["idx"]
        var node: MeshInstance3D = a["node"]
        if i == 0:
            var dx := Input.get_axis("ui_left", "ui_right")
            var dz := Input.get_axis("ui_up", "ui_down")
            node.position.x = clampf(node.position.x + dx * 5.2 * delta, -9.0, 9.0)
            node.position.z = clampf(node.position.z + dz * 5.2 * delta, -9.0, 9.0)
        else:
            var nearest: MeshInstance3D = null
            var best_d := 999.0
            for c in coins:
                if is_instance_valid(c):
                    var d := node.position.distance_to(c.position)
                    if d < best_d:
                        best_d = d
                        nearest = c
            if nearest:
                var sp: float = ai_speed[main.difficulty]
                var dirv := (nearest.position - node.position).normalized()
                node.position += dirv * sp * delta
        # Plocka forst, spawna EFTER loopen - att appenda till "coins" mitt
        # under iterationen over samma array ar odefinierat i GDScript.
        var remaining: Array = []
        var respawn := 0
        for c2 in coins:
            if is_instance_valid(c2) and node.position.distance_to(c2.position) < 0.9:
                scores[i] += 1
                main.play_sound("mg_collect")
                c2.queue_free()
                respawn += 1
            elif is_instance_valid(c2):
                remaining.append(c2)
        coins = remaining
        for s in range(respawn):
            _spawn_coin()
    main.phase_label.text = "Coin Rush - %ds left  (You: %d)" % [int(ceil(time_left)), scores[0]]
    if time_left <= 0.0:
        done = true
        var order: Array = [0, 1, 2, 3]
        order.sort_custom(func(a2, b2): return scores[a2] > scores[b2])
        main.minigame_done(order)
""";
}
