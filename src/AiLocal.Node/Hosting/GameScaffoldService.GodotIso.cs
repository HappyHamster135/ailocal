namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.26: 2.5D/ISO-KITET (Isle Raider) - det isometriska golvet som
/// composerns 2.5D-stilkort lovar. Romb-tiles (2:1 pixeldiamanter i kod),
/// y-sorterade pixelgubbar (PixelAnimator-sprites), PixelBackdrop-bakgrund
/// och klassisk samlarloop over tre oar. Triggas pa isometri/2.5d i
/// prompten ELLER stilkortets [STIL: iso] (WorkerRole skickar stilhinten
/// till scaffolden).
/// </summary>
public partial class GameScaffoldService
{
    internal static string[] ScaffoldGodotIso(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Isle Raider"));
        files.Add("project.godot");
        Write(root, "export_presets.cfg", GodotExportPresets());
        files.Add("export_presets.cfg");
        Write(root, "Main.tscn", GodotKitMainScene("Node2D"));
        files.Add("Main.tscn");
        Write(root, "Main.gd", GodotIsoMain);
        files.Add("Main.gd");
        // v2.26: forsta kitet som ANVANDER bakgrundsgeneratorn - on flyter
        // pa en PixelBackdrop-platta (tema fran prompten, meadow som default).
        Write(root, "background.png", PixelBackdrop.Build(prompt, 288, 162));
        files.Add("background.png");
        foreach (var (name, category, seed) in new[]
        {
            ("click.wav", "select", 31), ("coin.wav", "coin", 31),
            ("hurt.wav", "hurt", 31), ("win.wav", "win", 31),
            ("step.wav", "jump", 33),
        })
        {
            Write(root, name, SfxrGenerator.Render(category, seed));
            files.Add(name);
        }
        Write(root, "icon.ico", MakeIco());
        files.Add("icon.ico");
        Write(root, "DESIGN.md", GodotIsoDesignDoc(prompt));
        files.Add("DESIGN.md");
        Write(root, "README.md",
            "# Isle Raider - isometric 2.5D collect-adventure (Godot 4, GDScript)\n\n" +
            "Spelartext pa ENGELSKA (husregeln for alla kit sedan v1.99).\n\n" +
            "Innehall: 3 oar med romb-tiles (2:1 pixeldiamanter ritade i kod),\n" +
            "y-sorterade pixelgubbar (player_frames.tres), mynt, stenar,\n" +
            "vandrande fiender, tidsgrans, liv, poang + basta resultat,\n" +
            "PixelBackdrop-bakgrund (background.png).\n" +
            "Oppna i Godot 4 och tryck Play, eller exportera:\n" +
            "`godot --headless --export-release \"Windows Desktop\" build/spel.exe`\n\n" +
            "Styrning: pilar/WASD ror gubben i skarmens riktningar (iso-mappat);\n" +
            "Esc pausar. BYT TEMA: markfarger i ISLANDS, bakgrunden via\n" +
            "generate_asset type 'background' style 'pixelart', props i\n" +
            "_build_island. Fler oar = fler poster i ISLANDS.\n");
        files.Add("README.md");
        return [.. files];
    }

    static string GodotIsoDesignDoc(string prompt) =>
        "# Isometriskt 2.5D-aventyr (Godot 4, GDScript)\n\n## Koncept\nByggt fran: **" + (prompt ?? "").Trim() +
        "**\n\nIsometrisk samlarloop (Q*bert/Tunic-vinkeln): utforska romb-oar,\n" +
        "plocka alla mynt fore tidsgransen, undvik vakterna, na nasta o.\n\n" +
        "## Iso-tekniken (VIKTIG - foljs vid utbyggnad)\n" +
        "- Grid -> skarm: `_iso(gx, gy)` = ((gx-gy)*16, (gx+gy)*8) - 2:1-romber (TILE_W 32, TILE_H 16)\n" +
        "- Skarm -> grid (input): dgx = ix/TILE_W + iy/TILE_H; dgy = iy/TILE_H - ix/TILE_W\n" +
        "- Djup: actor_layer har y_sort_enabled - allt ritbart ar egna Node2D-barn,\n" +
        "  positionen ar FOTTERNA (sprite.offset lyfter kroppen)\n" +
        "- Tiles ritas i floor_layer (under alla aktorer), randtiles far sidor (raised)\n\n" +
        "## Mekanik\n- 3 oar (ISLANDS): storre radie, fler stenar/fiender, kortare tid\n" +
        "- Mynt +10 poang; alla mynt = nasta o + tidsbonus; fiendekontakt -1 liv (3 liv)\n" +
        "- Svarighet paverkar fiendefart och tid\n\n" +
        "## Produktion\n- Shell.startup + huvudmeny (Start x3/Options/Quit), paus (Esc), resultat, basta poang (user://)\n" +
        "- Juice: myntpartiklar, skarmskak vid traff, blink under ododlighet, ganganimation + flip\n" +
        "- Bakgrund: background.png ar en PixelBackdrop-platta - byt tema via generate_asset type 'background'\n\n" +
        "## Extension (tema-exempel)\n- Dungeon: byt oar mot rum, mynt mot nycklar, lagg dorrar\n" +
        "- Skattjakt: gravbara tiles, karta som visar X\n- Fler faror: patrullmonster, fallande block, is som glider\n";

    // ---- Main.gd: Isle Raider (isometriskt golv) ---------------------------
    const string GodotIsoMain = """
extends Node2D
# Isle Raider - isometriskt 2.5D-samlaraventyr: romb-tiles, y-sortering,
# pixelgubbar. BYT TEMA: markfarger i ISLANDS, bakgrund via generate_asset
# type 'background', props/faror i _build_island. Spelartext pa ENGELSKA.

const SAVE_PATH := "user://isleraider_best.txt"
const TILE_W := 32.0
const TILE_H := 16.0

# Preload (inte class_name-globalen): kitet ska parsa aven fore forsta importen.
const Shell = preload("res://Shell.gd")
# v2.35: preload, INTE class_name - utan importpass finns ingen global
# klassregister-cache och `Art.` fallerar med "Identifier not declared".
const Art = preload("res://Art.gd")

# Oarna: radie i tiles, stenar, fiender, tid och markfarger (topp/sida/mork).
const ISLANDS: Array = [
    {"radius": 5.2, "rocks": 5, "enemies": 1, "time": 60.0,
     "top": Color8(120, 190, 110), "side": Color8(86, 130, 74), "dark": Color8(58, 96, 56)},
    {"radius": 6.0, "rocks": 8, "enemies": 2, "time": 55.0,
     "top": Color8(224, 196, 128), "side": Color8(176, 144, 88), "dark": Color8(128, 102, 62)},
    {"radius": 6.8, "rocks": 11, "enemies": 2, "time": 50.0,
     "top": Color8(232, 240, 248), "side": Color8(168, 190, 214), "dark": Color8(118, 140, 168)},
]

var state := "title"  # title | play | pause | between | results
var difficulty := 1
var island_idx := 0
var lives := 3
var score := 0
var time_left := 60.0
var best_score := 0
var invuln := 0.0
var shake := 0.0
var between_t := 0.0
var autopilot := false
var attract_t := 0.0

var world: Node2D
var floor_layer: Node2D
var actor_layer: Node2D
var player: AnimatedSprite2D
var pgx := 0.0
var pgy := 0.0
var grid_radius := 4.6
var coins: Array = []    # {gx, gy, node, t}
var rocks: Array = []    # Vector2i-lista over blockerade celler
var enemies: Array = []  # {gx, gy, tx, ty, node, speed}
var ui: CanvasLayer
var hud: Label
var focus_pending := true
var _last_button: Button = null
var snd := {}
var tile_flat: ImageTexture
var tile_raised: ImageTexture

func _ready() -> void:
    randomize()
    Shell.startup()
    autopilot = OS.get_environment("AILOCAL_AUTOPILOT") == "1"
    _setup_audio()
    best_score = _load_best()
    # Bakgrunden: PixelBackdrop-plattan fran scaffolden (NEAREST-uppskalad -
    # kitets default_texture_filter=0 haller pixlarna skarpa).
    var bg_tex = load("res://background.png")
    if bg_tex:
        var bg := Sprite2D.new()
        bg.texture = bg_tex
        bg.centered = false
        bg.scale = Vector2(1152.0 / float(bg_tex.get_width()), 648.0 / float(bg_tex.get_height()))
        add_child(bg)
    world = Node2D.new()
    world.position = Vector2(576, 270)
    # Heltalsskala (2x) fyller skarmen och haller pixlarna skarpa (NEAREST).
    world.scale = Vector2(2, 2)
    add_child(world)
    floor_layer = Node2D.new()
    world.add_child(floor_layer)
    actor_layer = Node2D.new()
    actor_layer.y_sort_enabled = true
    world.add_child(actor_layer)
    ui = CanvasLayer.new()
    add_child(ui)
    hud = Label.new()
    hud.position = Vector2(16, 10)
    hud.add_theme_font_size_override("font_size", 17)
    ui.add_child(hud)
    _show_title()

func _setup_audio() -> void:
    for key in ["click", "coin", "hurt", "win", "step"]:
        var s = load("res://%s.wav" % key)
        if s:
            var p := AudioStreamPlayer.new()
            p.stream = s
            add_child(p)
            snd[key] = p
    var music = load("res://music.wav")
    if music:
        var mp := AudioStreamPlayer.new()
        mp.stream = music
        mp.volume_db = -14.0
        mp.finished.connect(mp.play)
        add_child(mp)
        mp.play()

func _play(key: String) -> void:
    if snd.has(key):
        # Liten slumpad tonhojd sa upprepade ljud inte trottar orat.
        snd[key].pitch_scale = randf_range(0.97, 1.03) if key == "click" \
        	else randf_range(0.88, 1.13)
        snd[key].play()

# ---------- iso-matte ----------
func _iso(gx: float, gy: float) -> Vector2:
    return Vector2((gx - gy) * TILE_W * 0.5, (gx + gy) * TILE_H * 0.5)

func _on_island(gx: float, gy: float) -> bool:
    # Cirkel i GRID-rummet = 2:1-ellips pa SKARMEN (o-form). Diamantvillkoret
    # (|gx|+|gy|) blev en fyrkant pa skarmen - iso-mappningen roterar 45 grader.
    return gx * gx + gy * gy <= grid_radius * grid_radius

func _blocked(gx: float, gy: float) -> bool:
    if not _on_island(gx, gy):
        return true
    var cell := Vector2i(roundi(gx), roundi(gy))
    return rocks.has(cell)

# ---------- pixeldiamanter (ritade i kod - samma sprak som 2D-kiten) ----------
func _make_tile_tex(top: Color, side: Color, dark: Color, raised: bool) -> ImageTexture:
    var hgt := 24 if raised else 16
    var img := Image.create(32, hgt, false, Image.FORMAT_RGBA8)
    var outline := Color8(27, 22, 36)
    var light := top.lightened(0.22)
    for y in range(16):
        var half := (y + 1) * 2 if y < 8 else (16 - y) * 2
        for dx in range(-half, half):
            var x := 16 + dx
            if x < 0 or x > 31:
                continue
            var edge := dx == -half or dx == half - 1 or y == 0 or y == 15
            var c := top
            if edge:
                c = outline
            elif y <= 3:
                c = light
            img.set_pixel(x, y, c)
    if raised:
        # Sidorna: vanster halva i sidofarg, hoger i morkare - blocket far djup.
        for y in range(16, 24):
            var half2 := (24 - y) * 2
            for dx in range(-half2, half2):
                var x2 := 16 + dx
                if x2 < 0 or x2 > 31:
                    continue
                var edge2 := dx == -half2 or dx == half2 - 1 or y == 23
                img.set_pixel(x2, y, outline if edge2 else (side if dx < 0 else dark))
    return ImageTexture.create_from_image(img)

func _make_coin_tex() -> ImageTexture:
    var img := Image.create(10, 10, false, Image.FORMAT_RGBA8)
    var outline := Color8(27, 22, 36)
    var gold := Color8(240, 196, 60)
    var shine := Color8(255, 238, 150)
    for y in range(10):
        for x in range(10):
            var dx := x - 4.5
            var dy := y - 4.5
            var d := dx * dx + dy * dy
            if d <= 12.0:
                img.set_pixel(x, y, gold)
            if d > 12.0 and d <= 20.0:
                img.set_pixel(x, y, outline)
    img.set_pixel(3, 3, shine)
    img.set_pixel(4, 3, shine)
    img.set_pixel(3, 4, shine)
    return ImageTexture.create_from_image(img)

# ---------- varldsbygget ----------
func _build_island() -> void:
    for c in floor_layer.get_children():
        c.queue_free()
    for c in actor_layer.get_children():
        c.queue_free()
    coins = []
    rocks = []
    enemies = []
    var isl: Dictionary = ISLANDS[island_idx]
    grid_radius = float(isl["radius"])
    tile_flat = _make_tile_tex(isl["top"], isl["side"], isl["dark"], false)
    tile_raised = _make_tile_tex(isl["top"], isl["side"], isl["dark"], true)
    var r := int(ceil(grid_radius))
    var open_cells: Array = []
    for gy in range(-r, r + 1):
        for gx in range(-r, r + 1):
            if not _on_island(float(gx), float(gy)):
                continue
            var s := Sprite2D.new()
            # Randtiles far sidor (raised) sa on ser tjock ut mot havet.
            var rim := float(gx) * float(gx) + float(gy) * float(gy) > (grid_radius - 1.0) * (grid_radius - 1.0)
            s.texture = tile_raised if rim else tile_flat
            s.centered = true
            s.position = _iso(float(gx), float(gy)) + Vector2(0, 4 if rim else 0)
            floor_layer.add_child(s)
            if not rim:
                open_cells.append(Vector2i(gx, gy))
    open_cells.shuffle()
    # Stenar: forhojd bricka i mork ton + blockerad cell.
    var rock_tex := _make_tile_tex(isl["dark"], isl["dark"].darkened(0.2), isl["dark"].darkened(0.4), true)
    for i in range(mini(int(isl["rocks"]), open_cells.size())):
        var cell: Vector2i = open_cells.pop_front()
        if cell == Vector2i.ZERO:
            continue
        rocks.append(cell)
        var s2 := Sprite2D.new()
        s2.texture = rock_tex
        s2.position = _iso(float(cell.x), float(cell.y)) + Vector2(0, -3)
        actor_layer.add_child(s2)
    # Mynt pa oppna celler.
    var coin_tex := _make_coin_tex()
    var coin_count := 8 + island_idx * 2
    for i in range(mini(coin_count, open_cells.size())):
        var cell2: Vector2i = open_cells.pop_front()
        if cell2 == Vector2i.ZERO:
            continue
        var s3 := Sprite2D.new()
        s3.texture = coin_tex
        s3.scale = Vector2(2, 2)
        s3.position = _iso(float(cell2.x), float(cell2.y)) + Vector2(0, -8)
        actor_layer.add_child(s3)
        coins.append({"gx": float(cell2.x), "gy": float(cell2.y), "node": s3, "t": randf() * TAU})
    # Spelaren: pixelgubben fran scaffolden, fotterna pa positionen.
    player = AnimatedSprite2D.new()
    player.sprite_frames = load("res://player_frames.tres")
    player.scale = Vector2(2, 2)
    player.offset = Vector2(0, -11)
    player.play("idle")
    actor_layer.add_child(player)
    pgx = 0.0
    pgy = 0.0
    player.position = _iso(pgx, pgy)
    # Fiender: egen pixelgubbe (enemy_frames), vandrar mellan slumpmal.
    var espeed: Array = [1.4, 1.9, 2.4]
    for i in range(int(isl["enemies"])):
        var e := AnimatedSprite2D.new()
        e.sprite_frames = load("res://enemy_frames.tres")
        e.scale = Vector2(2, 2)
        e.offset = Vector2(0, -11)
        e.play("walk")
        actor_layer.add_child(e)
        var start := Vector2(grid_radius - 1.5, 0.0) if i % 2 == 0 else Vector2(0.0, grid_radius - 1.5)
        # Forsta malet slumpas (ALDRIG spegelpunkten - den vagen gar rakt
        # over spelarens startruta och gav traff inom tva sekunder).
        var a0 := randf() * TAU
        var r0 := randf_range(1.0, grid_radius - 1.2)
        enemies.append({"gx": start.x, "gy": start.y, "tx": cos(a0) * r0, "ty": sin(a0) * r0,
            "node": e, "speed": float(espeed[difficulty])})
        e.position = _iso(start.x, start.y)
    time_left = float(isl["time"]) + (10.0 if difficulty == 0 else 0.0) - (5.0 if difficulty == 2 else 0.0)
    # Startododlighet: spelaren ska hinna lasa on innan vakterna kan traffa.
    invuln = 2.0

# ---------- ui ----------
func _clear_ui() -> void:
    for c in ui.get_children():
        if c is Control and c != hud:
            c.queue_free()
    focus_pending = true
    _last_button = null

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
        b.call_deferred("grab_focus")
    if _last_button != null and is_instance_valid(_last_button):
        _last_button.focus_neighbor_bottom = _last_button.get_path_to(b)
        b.focus_neighbor_top = b.get_path_to(_last_button)
    _last_button = b
    return b

# ---------- flode ----------
func _show_title() -> void:
    state = "title"
    _clear_ui()
    hud.text = ""
    attract_t = 0.0
    _label_ui("ISLE RAIDER", 70, 64, Color(1, 0.86, 0.3))
    _label_ui("An isometric treasure hunt! Grab every coin before time runs out.", 158, 20)
    _label_ui("Dodge the guards - three hits and the raid is over. Best: %d" % best_score, 192, 16, Color(0.7, 0.9, 1))
    _button_ui("Start: Easy", 250, func() -> void: _start_game(0))
    _button_ui("Start: Normal", 308, func() -> void: _start_game(1))
    _button_ui("Start: Hard", 366, func() -> void: _start_game(2))
    _button_ui("Options", 424, func() -> void: _show_options())
    _button_ui("Quit", 482, func() -> void: get_tree().quit())
    _label_ui("Arrows/WASD move on screen (iso-mapped). Esc pauses.", 552, 14, Color(0.55, 0.55, 0.6))

func _show_options() -> void:
    _play("click")
    _clear_ui()
    Shell.options_panel(ui, func() -> void: _show_title())

func _start_game(diff: int) -> void:
    difficulty = diff
    _play("click")
    island_idx = 0
    lives = 3
    score = 0
    _clear_ui()
    _build_island()
    state = "play"
    _update_hud()

func _update_hud() -> void:
    hud.text = "Coins %d   Time %ds   Lives %d   Island %d/%d   Score %d   Best %d" % [
        coins.size(), int(ceil(time_left)), lives, island_idx + 1, ISLANDS.size(), score, best_score]

func _show_pause() -> void:
    state = "pause"
    _label_ui("PAUSED", 200, 48, Color(1, 0.9, 0.4))
    _button_ui("Resume", 290, func() -> void: _resume())
    _button_ui("Quit to Title", 348, func() -> void: _show_title())

func _resume() -> void:
    _play("click")
    _clear_ui()
    state = "play"

func _show_results(won: bool) -> void:
    state = "results"
    if score > best_score:
        best_score = score
        _save_best(best_score)
    _clear_ui()
    _label_ui("ALL ISLANDS RAIDED!" if won else "THE RAID IS OVER", 160, 44,
        Color(0.5, 1, 0.6) if won else Color(1, 0.5, 0.45))
    _label_ui("Score: %d    Best: %d" % [score, best_score], 230, 24)
    _button_ui("Play Again", 300, func() -> void: _start_game(difficulty))
    _button_ui("Back to Title", 358, func() -> void: _show_title())
    if won:
        _play("win")

func _input(event: InputEvent) -> void:
    if event.is_action_pressed("ui_cancel"):
        if state == "play":
            _show_pause()
        elif state == "pause":
            _resume()

func _physics_process(delta: float) -> void:
    if shake > 0.0:
        shake = move_toward(shake, 0.0, 2.2 * delta)
        world.position = Vector2(576, 270) + Vector2(randf_range(-shake, shake), randf_range(-shake, shake)) * 8.0
    if state == "title":
        # Attract-autopilot: efter 8s demonstrerar spelet sig sjalvt (sonden
        # nar spelloopen utan att kunna reglerna).
        attract_t += delta
        if autopilot and attract_t > 5.0:
            attract_t = 0.0
            _start_game(1)
        return
    if state != "play":
        if state == "between":
            between_t -= delta
            if between_t <= 0.0:
                _clear_ui()
                _build_island()
                state = "play"
        return

    # Tid och puls.
    time_left -= delta
    if time_left <= 0.0:
        _lose_life("Time ran out!")
        return

    # Input i SKARMENS riktningar - mappas till gridet (iso-inversen).
    var ix := Input.get_axis("ui_left", "ui_right")
    var iy := Input.get_axis("ui_up", "ui_down")
    if autopilot and coins.size() > 0:
        # Autopiloten gar mot narmsta mynt i grid-rummet.
        var best_d := 1e9
        var bgx := 0.0
        var bgy := 0.0
        for c in coins:
            var d := absf(float(c["gx"]) - pgx) + absf(float(c["gy"]) - pgy)
            if d < best_d:
                best_d = d
                bgx = float(c["gx"])
                bgy = float(c["gy"])
        var gdir := Vector2(bgx - pgx, bgy - pgy)
        if gdir.length() > 0.05:
            gdir = gdir.normalized()
            _move_player(gdir.x, gdir.y, delta)
    elif ix != 0.0 or iy != 0.0:
        var dgx := ix / TILE_W + iy / TILE_H
        var dgy := iy / TILE_H - ix / TILE_W
        var v := Vector2(dgx, dgy)
        if v.length() > 0.001:
            v = v.normalized()
            _move_player(v.x, v.y, delta)
        if ix != 0.0:
            player.flip_h = ix < 0.0
    else:
        if player.animation != "idle":
            player.play("idle")

    # Mynt: bob + plock.
    for c in coins.duplicate():
        c["t"] = float(c["t"]) + delta * 4.0
        var n: Sprite2D = c["node"]
        n.position = _iso(float(c["gx"]), float(c["gy"])) + Vector2(0, -8.0 + sin(float(c["t"])) * 2.0)
        if absf(float(c["gx"]) - pgx) < 0.55 and absf(float(c["gy"]) - pgy) < 0.55:
            score += 10
            _play("coin")
            _burst(n.position, Color(1, 0.85, 0.3))
            n.queue_free()
            coins.erase(c)
            if coins.size() == 0:
                score += int(time_left) * 2
                island_idx += 1
                if island_idx >= ISLANDS.size():
                    _show_results(true)
                else:
                    _play("win")
                    state = "between"
                    between_t = 1.6
                    _label_ui("Island cleared! +%d time bonus" % (int(time_left) * 2), 240, 32, Color(0.5, 1, 0.6))
                _update_hud()
                return

    # Fiender: vandra mot slumpmal, byt mal vid framme; kontakt = traff.
    for e in enemies:
        var egx := float(e["gx"])
        var egy := float(e["gy"])
        var dir := Vector2(float(e["tx"]) - egx, float(e["ty"]) - egy)
        if dir.length() < 0.2:
            var a := randf() * TAU
            var rr := randf_range(1.0, grid_radius - 1.2)
            e["tx"] = cos(a) * rr
            e["ty"] = sin(a) * rr
        else:
            dir = dir.normalized() * float(e["speed"]) * delta
            e["gx"] = egx + dir.x
            e["gy"] = egy + dir.y
            var en: AnimatedSprite2D = e["node"]
            en.position = _iso(float(e["gx"]), float(e["gy"]))
            en.flip_h = dir.x - dir.y < 0.0
        if invuln <= 0.0 and absf(float(e["gx"]) - pgx) < 0.5 and absf(float(e["gy"]) - pgy) < 0.5:
            _lose_life("A guard caught you!")
            return

    if invuln > 0.0:
        invuln -= delta
        player.modulate.a = 0.5 + 0.5 * sin(invuln * 20.0)
        if invuln <= 0.0:
            player.modulate.a = 1.0
    _update_hud()

func _move_player(dgx: float, dgy: float, delta: float) -> void:
    var speed := 3.4
    var nx := pgx + dgx * speed * delta
    var ny := pgy + dgy * speed * delta
    # Per-axel sa gubben glider langs stenar i stallet for att fastna.
    if not _blocked(nx, pgy):
        pgx = nx
    if not _blocked(pgx, ny):
        pgy = ny
    player.position = _iso(pgx, pgy)
    if player.animation != "walk":
        player.play("walk")

func _lose_life(reason: String) -> void:
    lives -= 1
    _play("hurt")
    shake = maxf(shake, 0.6)
    invuln = 1.4
    if lives <= 0:
        _show_results(false)
        return
    _label_ui(reason + "  (%d lives left)" % lives, 240, 26, Color(1, 0.6, 0.5))
    var lbl: Label = ui.get_children().back()
    get_tree().create_timer(1.4).timeout.connect(func() -> void:
        if is_instance_valid(lbl):
            lbl.queue_free())
    if time_left <= 0.0:
        time_left = float(ISLANDS[island_idx]["time"]) * 0.6
    pgx = 0.0
    pgy = 0.0
    player.position = _iso(pgx, pgy)
    _update_hud()

func _burst(pos: Vector2, col: Color) -> void:
    var p := CPUParticles2D.new()
    p.position = pos
    p.amount = 12
    p.one_shot = true
    p.explosiveness = 0.9
    p.lifetime = 0.5
    p.direction = Vector2(0, -1)
    p.spread = 70.0
    p.initial_velocity_min = 40.0
    p.initial_velocity_max = 110.0
    p.gravity = Vector2(0, 220)
    p.scale_amount_min = 2.0
    p.scale_amount_max = 3.5
    p.color = col
    actor_layer.add_child(p)
    p.emitting = true
    get_tree().create_timer(1.0).timeout.connect(p.queue_free)

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
}
