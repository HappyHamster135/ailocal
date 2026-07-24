namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29 etapp 1: husets ATERBRUKBARA 3D-karaktarssystem. Rig3D.gd skrivs till
/// varje Godot-projekt (som Art.gd och Shell.gd) och bygger en riggad humanoid
/// ur en Cast3D-spec - alltsa ur SAMMA CharacterSpec som ritar 2D-pixelgubben.
///
/// Teknik: ledhierarki av Node3D med BoxMesh-delar, inte Skeleton3D. Skinning
/// GAR att skriva fran kod, men felmoden ar en tyst totalkollaps (utan
/// reset_bone_poses blir figuren en 20 cm stump utan felmeddelande) och for
/// stela block koper skinning en deformation vi inte anvander. Ledtabellen
/// ligger som ren data sa en skinnad variant kan komma senare utan att nagot
/// kit ror sig.
/// </summary>
public partial class GameScaffoldService
{
    static string[] AppendRig3DLib(string root, string[] files)
    {
        try
        {
            Write(root, "Rig3D.gd", GodotRig3DLib);
            return [.. files, "Rig3D.gd"];
        }
        catch { return files; }
    }

    static string[] AppendCast3D(string root, string[] files)
    {
        try
        {
            return Cast3DScript.WriteInto(root) ? [.. files, "Cast3D.gd"] : files;
        }
        catch { return files; }
    }

    const string GodotRig3DLib = """
class_name Rig3D
extends Node3D
# Husets 3D-karaktar: en riggad humanoid byggd av namngivna LEDER.
# Byggs ur en Cast3D-spec, sa samma figur som 2D-pixelgubben - samma
# kroppstyp, samma palett.
#
# ANVANDNING (kit och agentbyggen):
#   const Rig3D = preload("res://Rig3D.gd")
#   const Cast3D = preload("res://Cast3D.gd")
#   var hero = Rig3D.actor(Cast3D.spec("player"))
#   add_child(hero)
#   hero.play("walk"); hero.set_speed(0.8)
#   hero.face_dir(dir, delta)
#   var vapen = hero.socket("hand_r")   # hang saker pa figuren
#
# Fotterna ligger pa y = 0 i riggens lokala rum: satt bara position till
# markpunkten, ingen halva-hojden-korrigering behovs.
# BYGG ALDRIG egna kapsel-/boxgubbar - anvand den har.

const CLIPS := ["idle", "walk", "run", "jump", "fall", "attack", "hit", "cheer", "down"]

var spec := {}
var m := {}
var joints := {}
var sockets := {}
var clip := "idle"
var clip_speed := 1.0
var move01 := 0.0
var phase := 0.0
var clip_t := 0.0
var _mats := {}

# ---------- fabrik ----------
# OBS: refererar MEDVETET inte "Rig3D" har inne. class_name registreras forst
# vid importen, och ett kit som kors utan foregaende import (t.ex. den gated
# motorsonden) faller da pa "Identifier not found: Rig3D" i sin egen fil.
# Samma skal som gor att kiten preloadar Shell.gd i stallet for class_name.
static func actor(character_spec: Dictionary, tint := Color(0, 0, 0, 0)) -> Node3D:
	var r = load("res://Rig3D.gd").new()
	r._build(character_spec, tint)
	return r

# Ren funktion: matten utan att bygga en nod (for kit som bara behover
# ogonhojd eller kapselmatt).
static func metrics_of(character_spec: Dictionary) -> Dictionary:
	if character_spec.has("metrics"):
		return character_spec["metrics"]
	return {}

# ---------- matt ----------
func height() -> float: return float(m.get("height", 1.7))
func eye_height() -> float: return float(m.get("eye_y", 1.5))
func chest_height() -> float: return float(m.get("chest_y", 1.1))
func hip_height() -> float: return float(m.get("hip_y", 0.77))
func capsule_radius() -> float: return float(m.get("cap_r", 0.22))
func capsule_height() -> float: return float(m.get("cap_h", 1.7))

func joint(n: String) -> Node3D:
	return joints.get(n, null)

func socket(n: String) -> Node3D:
	return sockets.get(n, null)

# ---------- bygge ----------
func _build(s: Dictionary, tint: Color) -> void:
	spec = s
	m = s.get("metrics", {})
	if m.is_empty():
		m = {"height": 1.7, "upper_leg": 0.42, "lower_leg": 0.34, "torso_h": 0.54,
			"head_h": 0.39, "upper_arm": 0.28, "lower_arm": 0.26, "shoulder_x": 0.23,
			"hip_x": 0.10, "body_w": 0.39, "body_d": 0.24, "limb_w": 0.12,
			"head_w": 0.29, "hip_y": 0.76, "shoulder_y": 1.30, "eye_y": 1.53,
			"chest_y": 1.08, "cap_r": 0.21, "cap_h": 1.7}

	var skin: Array = s.get("skin", [Color8(239, 197, 148), Color8(239, 197, 148), Color8(255, 233, 191)])
	var shirt: Array = s.get("shirt", [Color8(120, 60, 60), Color8(190, 90, 80), Color8(230, 140, 120)])
	var pants: Array = s.get("pants", [Color8(40, 44, 62), Color8(58, 62, 86), Color8(80, 86, 112)])
	var haircol: Array = s.get("hair_col", [Color8(40, 30, 24), Color8(70, 52, 38), Color8(104, 80, 56)])
	var shoe: Color = s.get("shoe", Color8(38, 32, 44))
	var eyec: Color = s.get("eye", Color8(24, 22, 32))

	# Tint (partyt fargar spelare 1-4): blandas in i trojan sa figuren gar
	# att skilja utan att hela paletten kastas bort.
	if tint.a > 0.0:
		shirt = [shirt[0].lerp(tint, 0.55), shirt[1].lerp(tint, 0.65), shirt[2].lerp(tint, 0.5)]

	var ul := float(m["upper_leg"])
	var ll := float(m["lower_leg"])
	var th := float(m["torso_h"])
	var hh := float(m["head_h"])
	var ua := float(m["upper_arm"])
	var la := float(m["lower_arm"])
	var sx := float(m["shoulder_x"])
	var hx := float(m["hip_x"])
	var bw := float(m["body_w"])
	var bd := float(m["body_d"])
	var lw := float(m["limb_w"])
	var hw := float(m["head_w"])

	# --- hofter (ankaret; fotterna hamnar pa y=0) ---
	var hips := _jnt("hips", self, Vector3(0, ul + ll, 0))
	_box(hips, Vector3(bw * 0.92, th * 0.30, bd), Vector3(0, th * 0.13, 0), pants[1])

	# --- bal + axlar ---
	var spine := _jnt("spine", hips, Vector3(0, th * 0.26, 0))
	_box(spine, Vector3(bw, th * 0.74, bd), Vector3(0, th * 0.37, 0), shirt[1])
	var chest := _jnt("chest", spine, Vector3(0, th * 0.74, 0))

	# --- huvud ---
	var head := _jnt("head", chest, Vector3(0, 0, 0))
	_box(head, Vector3(hw, hh * 0.82, hw * 0.92), Vector3(0, hh * 0.41, 0), skin[1])
	# ogon pa framsidan (z+) - liten men avgorande lasbarhet
	var ez := hw * 0.47
	_box(head, Vector3(hw * 0.16, hh * 0.13, 0.02), Vector3(-hw * 0.20, hh * 0.48, ez), eyec)
	_box(head, Vector3(hw * 0.16, hh * 0.13, 0.02), Vector3(hw * 0.20, hh * 0.48, ez), eyec)
	var hair_style: String = s.get("hair", "short")
	if hair_style != "bald":
		_box(head, Vector3(hw * 1.04, hh * 0.20, hw * 0.96), Vector3(0, hh * 0.76, 0), haircol[1])
		if hair_style == "long":
			_box(head, Vector3(hw * 1.04, hh * 0.34, hw * 0.22), Vector3(0, hh * 0.46, -hw * 0.42), haircol[0])
		elif hair_style == "ponytail":
			_box(head, Vector3(hw * 0.26, hh * 0.42, hw * 0.26), Vector3(0, hh * 0.60, -hw * 0.56), haircol[0])
		elif hair_style == "spiky":
			_box(head, Vector3(hw * 0.16, hh * 0.16, hw * 0.16), Vector3(-hw * 0.24, hh * 0.92, 0), haircol[1])
			_box(head, Vector3(hw * 0.16, hh * 0.20, hw * 0.16), Vector3(0, hh * 0.96, 0), haircol[1])
			_box(head, Vector3(hw * 0.16, hh * 0.16, hw * 0.16), Vector3(hw * 0.24, hh * 0.92, 0), haircol[1])
	if s.get("face", "plain") == "beard":
		_box(head, Vector3(hw * 0.72, hh * 0.20, hw * 0.30), Vector3(0, hh * 0.16, ez * 0.72), haircol[0])
	if s.get("mark", "none") == "horns":
		_box(head, Vector3(hw * 0.14, hh * 0.26, hw * 0.14), Vector3(-hw * 0.40, hh * 0.92, 0), skin[0])
		_box(head, Vector3(hw * 0.14, hh * 0.26, hw * 0.14), Vector3(hw * 0.40, hh * 0.92, 0), skin[0])
	elif s.get("mark", "none") == "ears":
		_box(head, Vector3(hw * 0.14, hh * 0.18, hw * 0.14), Vector3(-hw * 0.56, hh * 0.44, 0), skin[1])
		_box(head, Vector3(hw * 0.14, hh * 0.18, hw * 0.14), Vector3(hw * 0.56, hh * 0.44, 0), skin[1])

	# --- armar ---
	for side in [-1, 1]:
		var sn: String = "l" if side < 0 else "r"
		var arm := _jnt("arm_" + sn, chest, Vector3(side * sx, -th * 0.06, 0))
		# Basfargen, inte den morka rampen: med shirt[0] lastes armen som en
		# skugga i stallet for en arm (3D-ljuset skuggar redan sidorna).
		_box(arm, Vector3(lw, ua, lw), Vector3(0, -ua * 0.5, 0), shirt[1])
		var fore := _jnt("forearm_" + sn, arm, Vector3(0, -ua, 0))
		_box(fore, Vector3(lw * 0.92, la, lw * 0.92), Vector3(0, -la * 0.5, 0), skin[1])
		_mk_socket("hand_" + sn, fore, Vector3(0, -la, 0))

	# --- ben ---
	for side2 in [-1, 1]:
		var sn2: String = "l" if side2 < 0 else "r"
		var leg := _jnt("leg_" + sn2, hips, Vector3(side2 * hx, 0, 0))
		_box(leg, Vector3(lw * 1.06, ul, lw * 1.06), Vector3(0, -ul * 0.5, 0), pants[1])
		var shin := _jnt("shin_" + sn2, leg, Vector3(0, -ul, 0))
		_box(shin, Vector3(lw, ll, lw), Vector3(0, -ll * 0.5, 0), pants[0])
		var foot := _jnt("foot_" + sn2, shin, Vector3(0, -ll, 0))
		_box(foot, Vector3(lw * 1.1, ll * 0.16, lw * 1.7), Vector3(0, ll * 0.08, lw * 0.30), shoe)

	# --- fastpunkter ---
	_mk_socket("head_top", head, Vector3(0, hh * 0.92, 0))
	_mk_socket("chest", chest, Vector3(0, -th * 0.20, bd * 0.5))
	_mk_socket("back", chest, Vector3(0, -th * 0.20, -bd * 0.5))
	_mk_socket("aim", head, Vector3(0, hh * 0.45, hw * 0.5))

func _jnt(n: String, parent: Node3D, pos: Vector3) -> Node3D:
	var j := Node3D.new()
	j.name = n
	j.position = pos
	parent.add_child(j)
	joints[n] = j
	return j

func _mk_socket(n: String, parent: Node3D, pos: Vector3) -> Node3D:
	var s := Node3D.new()
	s.name = "socket_" + n
	s.position = pos
	parent.add_child(s)
	sockets[n] = s
	return s

func _box(parent: Node3D, size: Vector3, offset: Vector3, col: Color) -> MeshInstance3D:
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	mi.position = offset
	mi.material_override = _mat(col)
	parent.add_child(mi)
	return mi

# Materialcache: en instans per unik farg (annars far en figur 20+ material
# och en partyskarm hundratals).
func _mat(col: Color) -> StandardMaterial3D:
	var key := col.to_html(false)
	if _mats.has(key):
		return _mats[key]
	var mat := StandardMaterial3D.new()
	mat.albedo_color = col
	mat.roughness = 0.9
	_mats[key] = mat
	return mat

# ---------- korning ----------
func play(new_clip: String, speed := 1.0) -> void:
	if not CLIPS.has(new_clip):
		new_clip = "idle"
	if clip != new_clip:
		clip_t = 0.0
	clip = new_clip
	clip_speed = maxf(0.05, speed)

# 0..1 -> stegtakt OCH amplitud, sa en figur som smyger inte stampar.
func set_speed(v01: float) -> void:
	move01 = clampf(v01, 0.0, 1.0)

func face_dir(dir: Vector3, delta: float) -> void:
	var flat := Vector3(dir.x, 0.0, dir.z)
	if flat.length() < 0.001:
		return
	var want := atan2(flat.x, flat.z)
	rotation.y = lerp_angle(rotation.y, want, clampf(delta * 12.0, 0.0, 1.0))

func _process(delta: float) -> void:
	clip_t += delta
	var cadence := 1.0
	if clip == "walk":
		cadence = 6.0 * (0.55 + move01 * 0.75)
	elif clip == "run":
		cadence = 10.0
	elif clip == "idle":
		cadence = 1.8
	else:
		cadence = 3.0
	phase += delta * cadence * clip_speed
	_apply()

func _apply() -> void:
	var s := sin(phase)
	var c := cos(phase)
	var amp := 1.0
	if clip == "walk":
		amp = 0.45 + move01 * 0.75
	_rot("hips", Vector3.ZERO)
	_rot("spine", Vector3.ZERO)
	_rot("head", Vector3.ZERO)
	match clip:
		"idle":
			_lift(sin(phase) * 0.006)
			_rot("spine", Vector3(0, 0, 0))
			_rot("arm_l", Vector3(0, 0, -4 - s * 1.5))
			_rot("arm_r", Vector3(0, 0, 4 + s * 1.5))
			_rot("head", Vector3(s * 1.5, c * 2.0, 0))
			_rot("leg_l", Vector3.ZERO)
			_rot("leg_r", Vector3.ZERO)
			_rot("shin_l", Vector3.ZERO)
			_rot("shin_r", Vector3.ZERO)
		"walk", "run":
			var sw := (32.0 if clip == "walk" else 48.0) * amp
			_lift(absf(s) * 0.02 * amp)
			_rot("leg_l", Vector3(s * sw, 0, 0))
			_rot("leg_r", Vector3(-s * sw, 0, 0))
			_rot("shin_l", Vector3(maxf(0.0, -s) * sw * 1.25, 0, 0))
			_rot("shin_r", Vector3(maxf(0.0, s) * sw * 1.25, 0, 0))
			_rot("foot_l", Vector3(maxf(0.0, s) * 12.0, 0, 0))
			_rot("foot_r", Vector3(maxf(0.0, -s) * 12.0, 0, 0))
			_rot("arm_l", Vector3(-s * sw * 0.7, 0, -5))
			_rot("arm_r", Vector3(s * sw * 0.7, 0, 5))
			_rot("forearm_l", Vector3(-maxf(0.0, s) * 22.0, 0, 0))
			_rot("forearm_r", Vector3(-maxf(0.0, -s) * 22.0, 0, 0))
			_rot("spine", Vector3(6.0 if clip == "run" else 2.0, c * 3.0, 0))
		"jump":
			_rot("leg_l", Vector3(-28, 0, 0)); _rot("leg_r", Vector3(-14, 0, 0))
			_rot("shin_l", Vector3(52, 0, 0)); _rot("shin_r", Vector3(28, 0, 0))
			_rot("arm_l", Vector3(-125, 0, -12)); _rot("arm_r", Vector3(-125, 0, 12))
			_rot("spine", Vector3(-6, 0, 0))
		"fall":
			_rot("leg_l", Vector3(16, 0, 0)); _rot("leg_r", Vector3(-16, 0, 0))
			_rot("shin_l", Vector3(18, 0, 0)); _rot("shin_r", Vector3(18, 0, 0))
			_rot("arm_l", Vector3(-96, 0, -26)); _rot("arm_r", Vector3(-96, 0, 26))
		"attack":
			var a := clampf(clip_t * 6.0, 0.0, 1.0)
			var swing := sin(a * PI) * 118.0
			_rot("arm_r", Vector3(-swing, 0, 8))
			_rot("forearm_r", Vector3(-swing * 0.35, 0, 0))
			_rot("arm_l", Vector3(swing * 0.22, 0, -8))
			_rot("spine", Vector3(0, -swing * 0.16, 0))
		"hit":
			var k := maxf(0.0, 1.0 - clip_t * 3.5)
			_rot("spine", Vector3(-22.0 * k, 0, 0))
			_rot("head", Vector3(-16.0 * k, 0, 0))
			_rot("arm_l", Vector3(-24.0 * k, 0, -18)); _rot("arm_r", Vector3(-24.0 * k, 0, 18))
		"cheer":
			_lift(absf(s) * 0.05)
			_rot("arm_l", Vector3(-160, 0, -18 - s * 10.0))
			_rot("arm_r", Vector3(-160, 0, 18 + s * 10.0))
			_rot("head", Vector3(-8, 0, 0))
		"down":
			rotation.x = deg_to_rad(-82)
			_lift(-hip_height() * 0.55)
			_rot("arm_l", Vector3(0, 0, -70)); _rot("arm_r", Vector3(0, 0, 70))

func _lift(dy: float) -> void:
	var h: Node3D = joints.get("hips", null)
	if h:
		h.position.y = float(m.get("hip_y", 0.76)) + dy

func _rot(n: String, deg: Vector3) -> void:
	var j: Node3D = joints.get(n, null)
	if j == null:
		return
	j.rotation = Vector3(deg_to_rad(deg.x), deg_to_rad(deg.y), deg_to_rad(deg.z))

# ---------- sjalvvakt ----------
# Returnerar "" nar riggen ar sund. Anropas DIREKT efter actor(), fore
# forsta play() - mitt i ett klipp kan en fot dippa och ge falsklarm.
func self_check() -> String:
	for need in ["hips", "spine", "chest", "head", "arm_l", "arm_r", "leg_l", "leg_r", "foot_l", "foot_r"]:
		if not joints.has(need):
			return "saknar led: " + need
	for need2 in ["hand_l", "hand_r", "head_top", "chest", "back", "aim"]:
		if not sockets.has(need2):
			return "saknar fastpunkt: " + need2
	var fl: Node3D = joints["foot_l"]
	var fy := fl.global_position.y - global_position.y
	if absf(fy) > 0.02:
		return "foten star inte pa y=0 (fick %.3f)" % fy
	var hd: Node3D = joints["head"]
	var hy := hd.global_position.y - global_position.y
	var want := float(m.get("shoulder_y", 1.3))
	if absf(hy - want) > 0.03:
		return "huvudleden pa fel hojd (%.3f, vantade %.3f)" % [hy, want]
	return ""
""";
}
