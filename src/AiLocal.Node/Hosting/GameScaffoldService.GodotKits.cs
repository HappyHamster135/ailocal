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

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true

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

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
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
    queue_redraw()

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

func _play(key: String) -> void:
    if snd.has(key) and snd[key].stream:
        snd[key].play()

func _clear_ui() -> void:
    for c in ui.get_children():
        c.queue_free()
    focus_pending = true

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

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(300, 46)
    b.position = Vector2(426, y)
    b.pressed.connect(cb)
    ui.add_child(b)
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
    queue_redraw()

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

func _button(txt: String, y: float, cb: Callable) -> void:
    var b := Button.new()
    b.text = txt
    b.size = Vector2(320, 46)
    b.position = Vector2(416, y)
    b.pressed.connect(cb)
    ui.add_child(b)
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
        "textures/vram_compression/import_etc2_astc=true\n";

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
	for key in ["click","coin","hurt","win"]:
		# Nullsakert: fore forsta importen (headless-parse) finns ingen
		# wav-resurs - spelet ska anda starta tyst, aldrig spamma fel.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
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
	label_into(box, "Esc in game: save and return here.", 12)

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

func _ready() -> void:
	randomize()
	for key in ["click","coin","hurt","win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
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
		c.texture = make_texture(7, Color(0.95, 0.8, 0.2), Color(1, 1, 0.7))
		c.scale = Vector2(2, 2)
		c.position = random_point()
		add_child(c)
		coins.append(c)

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
	draw_rect(ARENA, Color(0.10, 0.16, 0.10))
	draw_rect(ARENA, Color(0.4, 0.6, 0.4), false, 4.0)

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
	box.set_anchors_preset(Control.PRESET_CENTER)
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 42)
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	box.add_child(m)
	if with_buttons:
		var first := true
		for entry in [["Easy", 0], ["Normal", 1], ["Hard", 2]]:
			var b := Button.new()
			b.text = "Start: " + str(entry[0])
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
# Pixel Rush - komplett plattformargrund i ren GDScript: 3 nivaer, mynt,
# patrullerande fiender (stampbara), mal-flagga, HP, highscore och juice.
# BYT TEMA: farger/former i make_texture-anropen + texterna + nivadatan.

const SAVE_PATH := "user://pixelrush_highscore.save"
const FINAL_LEVEL := 3
const GRAVITY := 1400.0
const MOVE_SPEED := 300.0
const JUMP_VELOCITY := -620.0

var difficulty := 1
var state := "title" # title | playing | paused | over
var player: CharacterBody2D
var enemies: Array = []   # [{body, min_x, max_x, dir}]
var coins: Array = []
var platforms: Array = []
var goal: Sprite2D
var level := 0
var hp := 3
var score := 0
var invulnerable := 0.0
var shake := 0.0  # C1 juice: screenshake-magnitud (px), avtar mot 0
var coyote := 0.0
var jump_buffer := 0.0
var jump_was_down := false
var touch_jump := false   # HOPP-knappen pa touchskarmar (datorspel: alltid false)
var was_on_floor := false
var spawn_point := Vector2(60, 540)
var sky := Color(0.35, 0.55, 0.85)
var hud: CanvasLayer
var hud_label: Label
var overlay: Control
var snd := {}

func _ready() -> void:
	randomize()
	for key in ["click","coin","hurt","win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
	hud = CanvasLayer.new()
	add_child(hud)
	hud_label = Label.new()
	hud_label.position = Vector2(20, 14)
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
	# C1: utan textur blir partiklarna en 1x1-quad och nastan osynliga.
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
	# Genomhoppbar underifran (klassisk plattformare) - upptackt i skarpt
	# Android-speltest v1.93: solida plattformar gav huvuddunk vid hopp
	# under dem. Marken ligger langst ner sa one-way ar ofarlig aven dar.
	shape.one_way_collision = true
	body.add_child(shape)
	var spr := Sprite2D.new()
	spr.texture = make_texture(8, Color(0.35, 0.25, 0.2), Color(0.5, 0.8, 0.3))
	spr.scale = Vector2(r.size.x / 8.0, r.size.y / 8.0)
	body.add_child(spr)
	add_child(body)
	return body

# ---------- nivadata ----------
# Hopphojden ar ~137 px (v^2/2g) - alla plattformssteg ligger pa <= 120 px
# sa varje niva ar bevisat klarbar. BYT TEMA/BANOR har.
func level_data(n: int) -> Dictionary:
	if n == 1:
		return {
			"plats": [Rect2(0, 600, 1152, 48), Rect2(150, 480, 220, 24), Rect2(450, 380, 200, 24), Rect2(760, 300, 200, 24), Rect2(950, 460, 180, 24)],
			"coins": [Vector2(220, 444), Vector2(540, 344), Vector2(850, 264), Vector2(1030, 424), Vector2(640, 564)],
			"enemies": [[Vector2(520, 576), 400.0, 720.0]],
			"goal": Vector2(1090, 560),
			"spawn": Vector2(60, 540),
			"sky": Color(0.35, 0.55, 0.85)
		}
	if n == 2:
		return {
			"plats": [Rect2(0, 600, 400, 48), Rect2(752, 600, 400, 48), Rect2(430, 520, 140, 24), Rect2(600, 440, 140, 24), Rect2(430, 360, 140, 24), Rect2(600, 280, 140, 24), Rect2(850, 220, 200, 24)],
			"coins": [Vector2(490, 484), Vector2(660, 404), Vector2(490, 324), Vector2(660, 244), Vector2(200, 564), Vector2(940, 564), Vector2(900, 184)],
			"enemies": [[Vector2(200, 576), 60.0, 340.0], [Vector2(900, 576), 800.0, 1090.0]],
			"goal": Vector2(1010, 180),
			"spawn": Vector2(60, 540),
			"sky": Color(0.85, 0.6, 0.4)
		}
	return {
		"plats": [Rect2(0, 600, 1152, 48), Rect2(200, 490, 150, 24), Rect2(430, 490, 150, 24), Rect2(660, 490, 150, 24), Rect2(890, 490, 150, 24), Rect2(320, 380, 150, 24), Rect2(550, 380, 150, 24), Rect2(780, 380, 150, 24), Rect2(500, 270, 180, 24)],
		"coins": [Vector2(270, 454), Vector2(500, 454), Vector2(730, 454), Vector2(960, 454), Vector2(390, 344), Vector2(620, 344), Vector2(850, 344), Vector2(590, 234)],
		"enemies": [[Vector2(300, 576), 100.0, 500.0], [Vector2(800, 576), 600.0, 1050.0], [Vector2(620, 356), 560.0, 690.0]],
		"goal": Vector2(580, 234),
		"spawn": Vector2(60, 540),
		"sky": Color(0.3, 0.3, 0.5)
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
	if goal:
		goal.queue_free()
		goal = null
	enemies = []
	coins = []
	platforms = []

func next_level() -> void:
	level += 1
	if level > FINAL_LEVEL:
		finish(true)
		return
	clear_entities()
	var data := level_data(level)
	sky = data["sky"]
	queue_redraw()
	for r in data["plats"]:
		platforms.append(make_platform(r))
	for cpos in data["coins"]:
		var c := Sprite2D.new()
		c.texture = make_texture(7, Color(0.95, 0.8, 0.2), Color(1, 1, 0.7))
		c.scale = Vector2(2, 2)
		c.position = cpos
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
	var panim := player.get_node("Anim") as AnimatedSprite2D
	if panim:
		panim.visible = true
		panim.modulate = Color.WHITE

func finish(won: bool) -> void:
	state = "over"
	if won:
		play_sound("win")
	var best := load_highscore()
	if score > best:
		save_highscore(score)
		best = score
	show_overlay("YOU WIN!" if won else "GAME OVER", "Score: %d   Best: %d\nR: play again" % [score, best], true)

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
	# ---- spelarfysik: gravitation, coyotetid, hoppbuffert, variabelt hopp ----
	var ax := Input.get_axis("ui_left", "ui_right")
	if ax == 0.0:
		if Input.is_physical_key_pressed(KEY_A):
			ax = -1.0
		elif Input.is_physical_key_pressed(KEY_D):
			ax = 1.0
	player.velocity.x = ax * MOVE_SPEED
	player.velocity.y = minf(player.velocity.y + GRAVITY * delta, 980.0)
	var jump_down := Input.is_physical_key_pressed(KEY_SPACE) \
		or Input.is_physical_key_pressed(KEY_W) or Input.is_physical_key_pressed(KEY_UP) \
		or touch_jump
	if jump_down and not jump_was_down:
		jump_buffer = 0.12
	if not jump_down and jump_was_down and player.velocity.y < -220.0:
		player.velocity.y = -220.0  # C1: slappt tidigt = kort hopp (variabel hojd)
	jump_was_down = jump_down
	coyote = 0.14 if player.is_on_floor() else maxf(coyote - delta, 0.0)
	jump_buffer = maxf(jump_buffer - delta, 0.0)
	if jump_buffer > 0.0 and coyote > 0.0:
		player.velocity.y = JUMP_VELOCITY
		jump_buffer = 0.0
		coyote = 0.0
		play_sound("click")
		spawn_burst(player.position + Vector2(0, 20), Color(0.9, 0.9, 0.85), 6)  # C1: hoppdamm
	player.move_and_slide()
	var on_floor := player.is_on_floor()
	if on_floor and not was_on_floor:
		spawn_burst(player.position + Vector2(0, 20), Color(0.8, 0.75, 0.7), 8)  # C1: landningsdamm
		shake = maxf(shake, 2.0)
	was_on_floor = on_floor
	var anim := player.get_node("Anim") as AnimatedSprite2D
	if anim:
		if absf(player.velocity.x) > 10.0:
			anim.play("walk")
			anim.flip_h = player.velocity.x < 0.0
		else:
			anim.play("idle")
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
	# ---- fiender: patrull + stamp/traff ----
	var speed := 46.0 + level * 10.0 + difficulty * 14.0
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
				stomped = true  # spelaren faller ovanifran -> stamp
				score += 20
				player.velocity.y = -430.0  # studsen ar belonningen
				play_sound("coin")
				shake = maxf(shake, 5.0)  # C1 juice
				spawn_burst(body.position, Color(0.9, 0.5, 0.9), 14)
				body.queue_free()
			elif invulnerable <= 0.0:
				damage()
				if state != "playing":
					return
		if not stomped:
			alive.append(e)
	enemies = alive
	# ---- mynt ----
	var remaining: Array = []
	for c in coins:
		if c.position.distance_to(player.position) < 30.0:
			score += 10
			play_sound("coin")
			spawn_burst(c.position, Color(1, 0.9, 0.3), 12)  # C1 juice
			shake = maxf(shake, 3.0)
			c.queue_free()
		else:
			remaining.append(c)
	coins = remaining
	# ---- mal-flaggan ----
	if goal and player.position.distance_to(goal.position) < 40.0:
		score += 50
		play_sound("coin")
		shake = maxf(shake, 6.0)
		spawn_burst(goal.position, Color(0.4, 1, 0.6), 20)  # C1 juice
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
	invulnerable = 1.2
	play_sound("hurt")
	shake = 9.0  # C1 juice: kannbar traff
	spawn_burst(player.position, Color(1, 0.3, 0.3), 18)
	var anim := player.get_node("Anim") as AnimatedSprite2D
	if anim:
		anim.modulate = Color(1, 0.35, 0.35)  # C1 juice: spelaren blinkar rott
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
	if event is InputEventKey and event.pressed and event.keycode == KEY_R and state == "over":
		new_game(difficulty)

func _draw() -> void:
	draw_rect(Rect2(0, 0, 1152, 648), sky)
	draw_circle(Vector2(1010, 90), 42.0, Color(1, 0.92, 0.6))

# ---------- overlays ----------
func show_title() -> void:
	state = "title"
	queue_redraw()
	show_overlay("PIXEL RUSH", "Reach the flag across %d levels. Arrows/A-D: move, Space/W: jump.\nJump ON enemies to stomp them. Best: %d" % [FINAL_LEVEL, load_highscore()], true)

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
	box.set_anchors_preset(Control.PRESET_CENTER)
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 42)
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	box.add_child(m)
	if with_buttons:
		var first := true
		for entry in [["Easy", 0], ["Normal", 1], ["Hard", 2]]:
			var b := Button.new()
			b.text = "Start: " + str(entry[0])
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

func _ready() -> void:
	randomize()
	for key in ["click", "coin", "hurt", "win"]:
		# Nullsakert fore forsta importen - se management-kitets kommentar.
		var stream: AudioStream = load("res://" + key + ".wav") as AudioStream
		if stream:
			var p := AudioStreamPlayer.new()
			p.stream = stream
			add_child(p)
			snd[key] = p
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
	box.set_anchors_preset(Control.PRESET_CENTER)
	box.add_theme_constant_override("separation", 12)
	overlay.add_child(box)
	var t := Label.new()
	t.text = title
	t.add_theme_font_size_override("font_size", 42)
	box.add_child(t)
	var m := Label.new()
	m.text = message
	m.add_theme_font_size_override("font_size", 16)
	box.add_child(m)
	var first := true
	for label in buttons:
		var b := Button.new()
		b.text = str(label)
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

func close_overlay() -> void:
	if overlay:
		overlay.queue_free()
		overlay = null
""";
}
