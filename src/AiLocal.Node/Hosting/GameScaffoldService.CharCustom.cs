namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.30 punkt 3: KARAKTARSSKAPAREN (agarens Pummel Party-referens).
/// Bitarna fanns redan - CharacterSpec har kroppstyp, har, ansikte, marke och
/// fyra fargramper, och Shell har ett karaktarsval - men allt harleddes
/// automatiskt och spelaren kunde inte valja nagot.
///
/// Nyckeln som gor det mojligt: 2D-spriten ritas med EXAKTA palettfarger ur
/// specen, sa en pixelexakt fargersattning vid korning ger en ny figur utan
/// att nagot behover ritas om i C#. 3D-riggen byggs redan ur en Dictionary
/// och kan darfor byggas om live.
/// </summary>
public partial class GameScaffoldService
{
    static string[] AppendCharCustomLib(string root, string[] files)
    {
        try
        {
            Write(root, "CharCustom.gd", GodotCharCustomLib);
            return [.. files, "CharCustom.gd"];
        }
        catch { return files; }
    }

    const string GodotCharCustomLib = """
class_name CharCustom
# Karaktarsskaparen: later SPELAREN valja utseende, och sparar valet.
#
#   const CharCustom = preload("res://CharCustom.gd")
#   const Cast3D = preload("res://Cast3D.gd")
#
#   var val = CharCustom.load_choice()                  # sparat val
#   var frames = CharCustom.frames_for("player", val)   # 2D SpriteFrames
#   sprite.sprite_frames = frames
#   var spec = CharCustom.spec_for("player", val)       # 3D-spec till Rig3D
#
# Valet ar ett index per kategori - inte rafarger - sa en sparfil fran en
# aldre version aldrig kan ge en trasig palett.

const SHIRTS := [
	[Color8(196, 64, 58), Color8(226, 96, 84), Color8(255, 150, 130)],
	[Color8(40, 96, 176), Color8(70, 134, 214), Color8(120, 180, 245)],
	[Color8(38, 140, 78), Color8(64, 180, 104), Color8(120, 220, 150)],
	[Color8(150, 60, 170), Color8(186, 92, 206), Color8(222, 146, 236)],
	[Color8(214, 148, 32), Color8(240, 182, 60), Color8(255, 216, 120)],
	[Color8(38, 148, 158), Color8(64, 188, 196), Color8(126, 224, 228)],
	[Color8(90, 96, 112), Color8(126, 134, 152), Color8(176, 184, 200)],
	[Color8(190, 84, 130), Color8(222, 118, 164), Color8(248, 168, 200)],
]
const HAIRS := [
	[Color8(28, 24, 22), Color8(48, 40, 34), Color8(74, 62, 52)],
	[Color8(58, 38, 22), Color8(92, 62, 36), Color8(132, 96, 60)],
	[Color8(112, 62, 26), Color8(158, 92, 40), Color8(200, 132, 72)],
	[Color8(148, 118, 44), Color8(196, 166, 74), Color8(236, 212, 130)],
	[Color8(96, 96, 104), Color8(146, 146, 156), Color8(198, 198, 208)],
	[Color8(40, 60, 120), Color8(64, 92, 168), Color8(110, 142, 214)],
]
const SKINS := [
	[Color8(122, 78, 46), Color8(166, 110, 68), Color8(200, 146, 100)],
	[Color8(160, 108, 62), Color8(206, 150, 96), Color8(236, 190, 140)],
	[Color8(193, 151, 100), Color8(239, 197, 148), Color8(255, 233, 191)],
	[Color8(210, 172, 128), Color8(248, 214, 176), Color8(255, 240, 214)],
]
const BODIES := ["slim", "normal", "broad"]
const HAIRSTYLES := ["short", "long", "spiky", "ponytail", "bald"]

const KEY := "char_custom"

# ---------- valet ----------
static func default_choice() -> Dictionary:
	return {"shirt": 0, "hair": 1, "skin": 2, "body": 1, "style": 0}

static func clamp_choice(c: Dictionary) -> Dictionary:
	var d := default_choice()
	d["shirt"] = clampi(int(c.get("shirt", 0)), 0, SHIRTS.size() - 1)
	d["hair"] = clampi(int(c.get("hair", 1)), 0, HAIRS.size() - 1)
	d["skin"] = clampi(int(c.get("skin", 2)), 0, SKINS.size() - 1)
	d["body"] = clampi(int(c.get("body", 1)), 0, BODIES.size() - 1)
	d["style"] = clampi(int(c.get("style", 0)), 0, HAIRSTYLES.size() - 1)
	return d

static func cycle(c: Dictionary, field: String, dir: int) -> Dictionary:
	var d := clamp_choice(c)
	var n := 1
	match field:
		"shirt": n = SHIRTS.size()
		"hair": n = HAIRS.size()
		"skin": n = SKINS.size()
		"body": n = BODIES.size()
		"style": n = HAIRSTYLES.size()
	d[field] = (int(d[field]) + dir + n) % n
	return d

static func label(c: Dictionary, field: String) -> String:
	var d := clamp_choice(c)
	match field:
		"body": return BODIES[int(d["body"])].capitalize()
		"style": return HAIRSTYLES[int(d["style"])].capitalize()
		"shirt": return "Colour %d" % (int(d["shirt"]) + 1)
		"hair": return "Tone %d" % (int(d["hair"]) + 1)
		"skin": return "Tone %d" % (int(d["skin"]) + 1)
	return ""

# ---------- sparning (aker via Shell sa ovriga nycklar bevaras) ----------
static func save_choice(c: Dictionary) -> void:
	var s = load("res://Shell.gd")
	var data: Dictionary = s.load_settings()
	data[KEY] = clamp_choice(c)
	s.save_settings(data)

static func load_choice() -> Dictionary:
	var s = load("res://Shell.gd")
	var data: Dictionary = s.load_settings()
	if data.has(KEY) and data[KEY] is Dictionary:
		return clamp_choice(data[KEY])
	return default_choice()

# ---------- 3D: bygg om specen ----------
static func spec_for(slug: String, c: Dictionary) -> Dictionary:
	var d := clamp_choice(c)
	var base: Dictionary = load("res://Cast3D.gd").spec(slug).duplicate(true)
	base["shirt"] = SHIRTS[int(d["shirt"])]
	base["hair_col"] = HAIRS[int(d["hair"])]
	base["skin"] = SKINS[int(d["skin"])]
	base["body"] = BODIES[int(d["body"])]
	base["hair"] = HAIRSTYLES[int(d["style"])]
	return base

# ---------- 2D: pixelexakt palettbyte pa den bakade spriten ----------
# Spriten ritades med EXAKT specens palettfarger, sa en direkt
# fargersattning ger en korrekt ny figur - ingen omritning behovs.
static func frames_for(slug: String, c: Dictionary, frame := 24) -> SpriteFrames:
	var base_png := "res://player.png" if slug == "player" else "res://char_%s.png" % slug
	if not ResourceLoader.exists(base_png):
		base_png = "res://player.png"
	if not ResourceLoader.exists(base_png):
		return null
	var tex: Texture2D = load(base_png)
	var img := tex.get_image()
	img.convert(Image.FORMAT_RGBA8)

	var d := clamp_choice(c)
	var spec: Dictionary = load("res://Cast3D.gd").spec(slug)
	var mapping := {}
	_map_ramp(mapping, spec.get("shirt", []), SHIRTS[int(d["shirt"])])
	_map_ramp(mapping, spec.get("hair_col", []), HAIRS[int(d["hair"])])
	_map_ramp(mapping, spec.get("skin", []), SKINS[int(d["skin"])])

	for y in range(img.get_height()):
		for x in range(img.get_width()):
			var px := img.get_pixel(x, y)
			if px.a < 0.5:
				continue
			var k := _key(px)
			if mapping.has(k):
				img.set_pixel(x, y, mapping[k])
	return frames_from_image(img, frame)

static func _map_ramp(mapping: Dictionary, from_ramp, to_ramp: Array) -> void:
	if not (from_ramp is Array):
		return
	var n: int = mini(from_ramp.size(), to_ramp.size())
	for i in range(n):
		mapping[_key(from_ramp[i])] = to_ramp[i]

static func _key(c: Color) -> int:
	return (int(round(c.r * 255.0)) << 16) | (int(round(c.g * 255.0)) << 8) | int(round(c.b * 255.0))

# Bygger SpriteFrames ur en horisontell sheet-bild. Husets konvention:
# idle = ruta 0-1, walk = ruta 2-5.
static func frames_from_image(img: Image, frame := 24) -> SpriteFrames:
	var tex := ImageTexture.create_from_image(img)
	var count: int = maxi(1, img.get_width() / maxi(1, frame))
	var sf := SpriteFrames.new()
	sf.remove_animation("default")
	var clips := [["idle", 0, mini(2, count), 3.0], ["walk", 2, maxi(0, mini(4, count - 2)), 10.0]]
	for clip in clips:
		var name: String = clip[0]
		var start: int = clip[1]
		var n: int = clip[2]
		if n <= 0:
			continue
		sf.add_animation(name)
		sf.set_animation_speed(name, float(clip[3]))
		sf.set_animation_loop(name, true)
		for i in range(n):
			var at := AtlasTexture.new()
			at.atlas = tex
			at.region = Rect2((start + i) * frame, 0, frame, frame)
			sf.add_frame(name, at)
	return sf

# ---------- skaparen (UI) ----------
# Overlay med LEVANDE forhandsvisning: figuren ritas om for varje val, sa
# man ser exakt vad man far. Sparas i user:// via Shell nar man ar klar.
static func creator_panel(parent: Node, on_done: Callable, slug := "player") -> Control:
	var choice := load_choice()
	var overlay := Control.new()
	overlay.set_anchors_preset(Control.PRESET_FULL_RECT)
	parent.add_child(overlay)

	var box := VBoxContainer.new()
	box.set_anchors_preset(Control.PRESET_FULL_RECT)
	box.alignment = BoxContainer.ALIGNMENT_CENTER
	box.add_theme_constant_override("separation", 10)
	overlay.add_child(box)

	var title := Label.new()
	title.text = "CUSTOMISE YOUR CHARACTER"
	title.add_theme_font_size_override("font_size", 34)
	title.add_theme_color_override("font_outline_color", Color(0, 0, 0, 0.7))
	title.add_theme_constant_override("outline_size", 10)
	title.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	box.add_child(title)

	# Levande forhandsvisning
	# Hojden maste rymma figuren HELT: AnimatedSprite2D ritas fran sin mitt,
	# sa en 24 px sprite i skala 6 (=144 px) sticker ut 72 px at varje hall.
	# Med en for lag scen la sig figuren ovanpa forsta valraden.
	var stage := Control.new()
	stage.custom_minimum_size = Vector2(0, 200)
	stage.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	box.add_child(stage)
	var preview := AnimatedSprite2D.new()
	preview.position = Vector2(0, 104)
	preview.scale = Vector2(6, 6)
	preview.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
	stage.add_child(preview)

	var refresh := func() -> void:
		var sf := frames_for(slug, choice)
		if sf:
			preview.sprite_frames = sf
			if sf.has_animation("walk"):
				preview.play("walk")
			elif sf.has_animation("idle"):
				preview.play("idle")

	var rows := [
		["body", "Build"], ["style", "Hair"], ["hair", "Hair colour"],
		["skin", "Skin"], ["shirt", "Outfit"],
	]
	var value_labels := {}
	var first_btn: Button = null
	var last_btn: Button = null
	for r in rows:
		var field: String = r[0]
		var row := HBoxContainer.new()
		row.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
		row.add_theme_constant_override("separation", 8)
		box.add_child(row)

		var name_lbl := Label.new()
		name_lbl.text = str(r[1])
		name_lbl.custom_minimum_size = Vector2(150, 0)
		name_lbl.add_theme_font_size_override("font_size", 18)
		row.add_child(name_lbl)

		var left := Button.new()
		left.text = "<"
		left.custom_minimum_size = Vector2(48, 38)
		row.add_child(left)

		var val := Label.new()
		val.text = label(choice, field)
		val.custom_minimum_size = Vector2(150, 0)
		val.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		val.add_theme_font_size_override("font_size", 18)
		row.add_child(val)
		value_labels[field] = val

		var right := Button.new()
		right.text = ">"
		right.custom_minimum_size = Vector2(48, 38)
		row.add_child(right)

		var f := field
		left.pressed.connect(func() -> void:
			choice = cycle(choice, f, -1)
			value_labels[f].text = label(choice, f)
			refresh.call())
		right.pressed.connect(func() -> void:
			choice = cycle(choice, f, 1)
			value_labels[f].text = label(choice, f)
			refresh.call())
		if first_btn == null:
			first_btn = right
		# Fokuskedja sa piltangenter fungerar utan mus.
		if last_btn != null:
			last_btn.focus_neighbor_bottom = last_btn.get_path_to(right)
			right.focus_neighbor_top = right.get_path_to(last_btn)
		last_btn = right

	var done := Button.new()
	done.text = "Done"
	done.custom_minimum_size = Vector2(200, 46)
	done.size_flags_horizontal = Control.SIZE_SHRINK_CENTER
	box.add_child(done)
	if last_btn != null:
		last_btn.focus_neighbor_bottom = last_btn.get_path_to(done)
		done.focus_neighbor_top = done.get_path_to(last_btn)
	done.pressed.connect(func() -> void:
		save_choice(choice)
		overlay.queue_free()
		on_done.call(choice))

	refresh.call()
	if first_btn:
		first_btn.call_deferred("grab_focus")
	return overlay
""";
}
