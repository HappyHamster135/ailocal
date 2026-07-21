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
        Write(root, "project.godot", GodotKitProject("Klubben"));
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
            "# Klubben - Management/Tycoon (Godot 4, GDScript)\n\n" +
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
        Write(root, "project.godot", GodotKitProject("Glantan"));
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
            "# Glantan - Top-down action (Godot 4, GDScript)\n\n" +
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

    /// <summary>Racing: top-down varvracer med bilfysik, oval bana, checkpoints
    /// i ordning, varv, varvtimer och basta tid. Fyller genreluckan dar racing-
    /// prompts tidigare fick plattformaren.</summary>
    internal static string[] ScaffoldGodotRacing(string root, string prompt)
    {
        var files = new List<string>();
        Write(root, "project.godot", GodotKitProject("Varvet"));
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
            "# Varvet - Top-down racing (Godot 4, GDScript)\n\n" +
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
# Varvet - top-down varvracer. Kor bilen runt banan, klara varven pa basta tid.
# UI byggs i kod (_show_title/_finish), banan + bilen ritas i _draw. BYT TEMA:
# farger/former i _draw och texterna i _show_title.

const SAVE_PATH := "user://varvet_best.txt"
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

func _ready() -> void:
    randomize()
    _build_checkpoints()
    _setup_audio()
    best = _load_best()
    ui = CanvasLayer.new()
    add_child(ui)
    _show_title()

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

func _show_title() -> void:
    state = "title"
    _clear_ui()
    _label("VARVET", 80, 72, Color(1, 0.85, 0.2))
    _label("Kor %d varv sa fort du kan - piltangenter: gas/broms/styr." % LAPS, 180, 22)
    _label("Basta tid: %s" % ("-" if best <= 0.0 else "%.2f s" % best), 216, 22, Color(0.7, 0.9, 1))
    var names := ["Latt", "Medel", "Svar"]
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
    car = nxt
    _progress()
    var hud := ui.get_node_or_null("Hud")
    if hud:
        hud.text = "Varv %d/%d   Tid %.2f s" % [min(lap + 1, LAPS), LAPS, t]
    queue_redraw()

func _progress() -> void:
    var target := checkpoints[(checkpoint + 1) % 4]
    if car.distance_to(target) < 95.0:
        checkpoint = (checkpoint + 1) % 4
        _play("coin")
        if checkpoint == 0:
            lap += 1
            if lap >= LAPS:
                _finish()

func _finish() -> void:
    state = "over"
    _play("win")
    var rec := best <= 0.0 or t < best
    if rec:
        best = t
        _save_best(t)
    _clear_ui()
    _label("MALGANG!", 200, 60, Color(1, 0.9, 0.3))
    _label("Tid: %.2f s%s" % [t, "   NYTT REKORD!" if rec else "   Rekord: %.2f s" % best], 280, 26)
    _button("Spela igen", 340, func(): _show_title())

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
        draw_colored_polygon(pts, Color(0.9, 0.2, 0.2))
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
        Write(root, "project.godot", GodotKitProject("Tvatusen"));
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
            "# Tvatusen - Slajd-pussel (Godot 4, GDScript)\n\n" +
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
# Tvatusen - slajd-pussel (2048): slajda med piltangenter, sla ihop lika brickor,
# na 2048. Rutnat + UI ritas i kod. BYT TEMA: farger i _tile_color, mal i TARGET.

const N := 4
const SAVE_PATH := "user://tvatusen_best.txt"
const TARGET := 2048

var grid: Array[int] = []
var state := "title"
var score := 0
var best := 0
var snd := {}
var ui: CanvasLayer

func _ready() -> void:
    randomize()
    _setup_audio()
    best = _load_best()
    ui = CanvasLayer.new()
    add_child(ui)
    _show_title()

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

func _show_title() -> void:
    state = "title"
    _clear_ui()
    _label("TVATUSEN", 90, 70, Color(0.95, 0.8, 0.3))
    _label("Slajda med piltangenterna. Sla ihop lika brickor och na %d." % TARGET, 200, 22)
    _label("Basta: %d" % best, 240, 22, Color(0.7, 0.9, 1))
    _button("Spela", 310, func(): _start())
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
    var hud := _label("Poang: 0", 20, 26)
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
            hud.text = "Poang: %d" % score
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
    _label("DU VANN!" if won else "SLUT - inga drag kvar", 200, 54, Color(0.95, 0.85, 0.3))
    _label("Poang: %d   Basta: %d" % [score, best], 280, 26)
    _button("Spela igen", 340, func(): _show_title())

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

    static string GodotKitProject(string name) =>
        "[application]\n" +
        $"config/name=\"{name}\"\n" +
        "config/icon=\"res://icon.ico\"\n" +
        "run/main_scene=\"res://Main.tscn\"\n" +
        "config/features=PackedStringArray(\"4.3\")\n" +
        "[display]\n" +
        "window/size/viewport_width=1152\n" +
        "window/size/viewport_height=648\n";

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
# Klubben - komplett management/tycoon-grund. Allt UI byggs i kod har.
# BYT TEMA: andra texter, NAMES/roller och siffror - strukturen bar allt.

const SAVE_PATH := "user://klubben_save.json"
const SEASON_LENGTH := 10
const NAMES := ["Alva","Bo","Cleo","Dag","Elin","Frans","Greta","Hugo","Ines","Jarl","Klara","Leo","Maja","Nils","Olga","Per"]
const ROLES := ["Anfall","Mitt","Forsvar","Malvakt"]
const RIVALS := ["Norrvik","Sodra IF","Ostkusten","Vastra BK","Bergslaget"]

var difficulty := 1
var week := 0
var budget := 0
var players: Array = []
var league: Array = []
var market: Array = []
var last_result := ""
var snd := {}

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
	league.append({"name": "Ditt lag", "strength": 0, "points": 0, "played": 0})
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
	last_result = "Omgang %d: Ditt lag - %s  %d-%d   (publik +%s kr, loner -%s kr)" % [week + 1, rival["name"], my_goals, their_goals, fmt(income), fmt(wage_bill())]
	week += 1
	refill_market()
	save_game()
	if budget < 0:
		show_end(false, "Konkursen ar ett faktum - kassan ar tom.")
	elif week >= SEASON_LENGTH:
		show_end(rank_of_me() == 1, "Sasongen ar slut - du slutade pa plats %d." % rank_of_me())
	else:
		show_hub()

func rank_of_me() -> int:
	var sorted := league.duplicate()
	sorted.sort_custom(func(a, b): return int(a["points"]) > int(b["points"]))
	for i in range(sorted.size()):
		if sorted[i]["name"] == "Ditt lag":
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
	return b

func fmt(n: int) -> String:
	return str(n)

# ---------- skarmar ----------
func show_title() -> void:
	var box := panel_root()
	label_into(box, "KLUBBEN", 44)
	label_into(box, "Ta ligans samsta lag till toppen pa %d omgangar." % SEASON_LENGTH, 18)
	label_into(box, "Valj svarighetsgrad:", 16)
	button_into(box, "Latt (900 000 kr i startkassa)", func(): new_game(0))
	button_into(box, "Medel (600 000 kr)", func(): new_game(1))
	button_into(box, "Svar (350 000 kr)", func(): new_game(2))
	if FileAccess.file_exists(SAVE_PATH):
		button_into(box, "Ladda sparat spel", func():
			if load_game():
				show_hub())
	label_into(box, "Esc i spelet: spara och ga hit.", 12)

func show_hub() -> void:
	var box := panel_root()
	label_into(box, "Omgang %d/%d   Kassa: %s kr   Lagstyrka: %d   Tabellplats: %d" % [week + 1, SEASON_LENGTH, fmt(budget), team_strength(), rank_of_me()], 18)
	if last_result != "":
		label_into(box, last_result, 14)
	var tabs := HBoxContainer.new()
	tabs.add_theme_constant_override("separation", 8)
	box.add_child(tabs)
	button_into(tabs, "Trupp", show_squad)
	button_into(tabs, "Marknad", show_market)
	button_into(tabs, "Tabell", show_table)
	button_into(tabs, "Spela omgang %d" % (week + 1), play_week)

func show_squad() -> void:
	var box := panel_root()
	label_into(box, "Truppen (%d) - loner %s kr/omgang" % [players.size(), fmt(wage_bill())], 20)
	for i in range(players.size()):
		var p: Dictionary = players[i]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		box.add_child(row)
		label_into(row, "%s  [%s]  betyg %d" % [p["name"], p["role"], int(p["rating"])], 14)
		if players.size() > 6:
			var index := i
			button_into(row, "Salj +%s kr" % fmt(price_of(p) / 2), func():
				budget += price_of(players[index]) / 2
				players.remove_at(index)
				play("coin")
				show_squad())
	button_into(box, "Tillbaka", show_hub)

func show_market() -> void:
	var box := panel_root()
	label_into(box, "Marknaden - kassa %s kr" % fmt(budget), 20)
	for i in range(market.size()):
		var p: Dictionary = market[i]
		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 8)
		box.add_child(row)
		label_into(row, "%s  [%s]  betyg %d" % [p["name"], p["role"], int(p["rating"])], 14)
		var index := i
		var b := button_into(row, "Kop %s kr" % fmt(price_of(p)), func():
			if budget >= price_of(market[index]) and players.size() < 12:
				budget -= price_of(market[index])
				players.append(market[index])
				market.remove_at(index)
				play("coin")
				show_market())
		if budget < price_of(p) or players.size() >= 12:
			b.disabled = true
	button_into(box, "Tillbaka", show_hub)

func show_table() -> void:
	var box := panel_root()
	label_into(box, "Ligatabellen", 20)
	var sorted := league.duplicate()
	sorted.sort_custom(func(a, b): return int(a["points"]) > int(b["points"]))
	for i in range(sorted.size()):
		var t: Dictionary = sorted[i]
		var mark := "  <- du" if t["name"] == "Ditt lag" else ""
		label_into(box, "%d. %s   %d p (%d spelade)%s" % [i + 1, t["name"], int(t["points"]), int(t["played"]), mark], 15)
	button_into(box, "Tillbaka", show_hub)

func show_end(won: bool, message: String) -> void:
	var box := panel_root()
	if won:
		play("win")
	label_into(box, "SEGER!" if won else "SLUTET", 40)
	label_into(box, message, 18)
	label_into(box, "Slutkassa: %s kr   Lagstyrka: %d" % [fmt(budget), team_strength()], 15)
	button_into(box, "Spela igen", show_title)
""";

    // ---- Main.gd: top-down --------------------------------------------------
    const string GodotTopDownMain = """
extends Node2D
# Glantan - komplett top-down-grund: vagor, fiender, mynt, HP, highscore.
# BYT TEMA: farger/former i make_texture-anropen + texterna.

const SAVE_PATH := "user://glantan_highscore.save"
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
	show_title()

func play_sound(key: String) -> void:
	if snd.has(key):
		snd[key].play()

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
	show_overlay("DU VANN!" if won else "SLUTET", "Poang: %d   Rekord: %d" % [score, best], true)

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
	var speed := 60.0 + wave * 14.0 + difficulty * 12.0
	for e in enemies:
		e.position += (player.position - e.position).normalized() * speed * delta
		if invulnerable <= 0.0 and e.position.distance_to(player.position) < 30.0:
			hp -= 1
			invulnerable = 1.2
			play_sound("hurt")
			if hp <= 0:
				finish(false)
				return
	var remaining: Array = []
	for c in coins:
		if c.position.distance_to(player.position) < 32.0:
			score += 10
			play_sound("coin")
			c.queue_free()
		else:
			remaining.append(c)
	coins = remaining
	if coins.is_empty():
		score += 25
		next_wave()
	hud_label.text = "HP: %d   Poang: %d   Vag: %d/%d" % [hp, score, mini(wave, FINAL_WAVE), FINAL_WAVE]

func _unhandled_input(event: InputEvent) -> void:
	if event.is_action_pressed("ui_cancel"):
		if state == "playing":
			state = "paused"
			show_overlay("PAUS", "Esc: fortsatt", false)
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
	show_overlay("GLANTAN", "Overlev %d vagor. WASD/pilar for att rora dig.\nRekord: %d" % [FINAL_WAVE, load_highscore()], true)

func show_overlay(title: String, message: String, with_buttons: bool) -> void:
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
		for entry in [["Latt", 0], ["Medel", 1], ["Svar", 2]]:
			var b := Button.new()
			b.text = "Starta: " + str(entry[0])
			var diff: int = entry[1]
			b.pressed.connect(func():
				play_sound("click")
				new_game(diff))
			box.add_child(b)

func close_overlay() -> void:
	if overlay:
		overlay.queue_free()
		overlay = null
""";
}
