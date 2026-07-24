namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.30: FYSIKGOLVET. Matningen over alla 21 kit visade noll forekomster av
/// RigidBody, Area och PhysicsMaterial - all rorelse var kinematisk och varje
/// "traff" en manuell avstandsberakning i _physics_process. Det ar bade
/// fattigt (inget gar att knuffa, inget studsar) och skort (samma manuella
/// loopar som tyst gick sonder i 3D-partyt).
///
/// Phys.gd ger de tre saker som saknades, som fardiga byggstenar:
/// knuffbara props (RigidBody), triggers som Godot sjalv sköter (Area) och
/// studsmaterial. Skickas med varje Godot-scaffold precis som Art.gd.
/// </summary>
public partial class GameScaffoldService
{
    static string[] AppendPhysicsLib(string root, string[] files)
    {
        try
        {
            Write(root, "Phys.gd", GodotPhysicsLib);
            return [.. files, "Phys.gd"];
        }
        catch { return files; }
    }

    const string GodotPhysicsLib = """
class_name Phys
# Husets FYSIKGOLV. Anvand detta i stallet for att rakna avstand for hand i
# _physics_process - Godot har en fysikmotor, den ar snabbare och den gar
# inte sonder tyst nar nagon andrar en typ.
#
#   const Phys = preload("res://Phys.gd")
#
#   # knuffbar lada (3D)
#   var lada = Phys.prop3d(Vector3(0.8, 0.8, 0.8), Color8(150, 110, 70))
#   lada.position = Vector3(3, 2, 0)
#   add_child(lada)
#
#   # trigger i stallet for manuell avstandskoll
#   var zon = Phys.trigger3d(1.2)
#   zon.body_entered.connect(func(b): print("nagon kom in"))
#   mynt.add_child(zon)
#
#   # knuff fran en explosion
#   Phys.blast3d(self, traffpunkt, 9.0, 4.0)

# ---------- material ----------
static func bouncy(bounce := 0.7, friction := 0.35) -> PhysicsMaterial:
	var m := PhysicsMaterial.new()
	m.bounce = clampf(bounce, 0.0, 1.0)
	m.friction = clampf(friction, 0.0, 1.0)
	return m

# ---------- knuffbara props ----------
# En RigidBody3D med lada-mesh, kollision och material i ETT anrop.
static func prop3d(size: Vector3, col: Color, mass := 1.2, bounce := 0.15) -> RigidBody3D:
	var body := RigidBody3D.new()
	body.mass = maxf(0.05, mass)
	body.physics_material_override = bouncy(bounce, 0.6)
	var mi := MeshInstance3D.new()
	var bm := BoxMesh.new()
	bm.size = size
	mi.mesh = bm
	var mat := StandardMaterial3D.new()
	mat.albedo_color = col
	mat.roughness = 0.85
	mi.material_override = mat
	body.add_child(mi)
	var col_shape := CollisionShape3D.new()
	var box := BoxShape3D.new()
	box.size = size
	col_shape.shape = box
	body.add_child(col_shape)
	return body

static func ball3d(radius: float, col: Color, mass := 0.8, bounce := 0.8) -> RigidBody3D:
	var body := RigidBody3D.new()
	body.mass = maxf(0.05, mass)
	body.physics_material_override = bouncy(bounce, 0.25)
	var mi := MeshInstance3D.new()
	var sm := SphereMesh.new()
	sm.radius = radius
	sm.height = radius * 2.0
	mi.mesh = sm
	var mat := StandardMaterial3D.new()
	mat.albedo_color = col
	mi.material_override = mat
	body.add_child(mi)
	var cs := CollisionShape3D.new()
	var sp := SphereShape3D.new()
	sp.radius = radius
	cs.shape = sp
	body.add_child(cs)
	return body

static func prop2d(size: Vector2, col: Color, mass := 1.0, bounce := 0.2) -> RigidBody2D:
	var body := RigidBody2D.new()
	body.mass = maxf(0.05, mass)
	body.physics_material_override = bouncy(bounce, 0.6)
	var rect := ColorRect.new()
	rect.color = col
	rect.size = size
	rect.position = -size * 0.5
	rect.mouse_filter = Control.MOUSE_FILTER_IGNORE
	body.add_child(rect)
	var cs := CollisionShape2D.new()
	var box := RectangleShape2D.new()
	box.size = size
	cs.shape = box
	body.add_child(cs)
	return body

# ---------- triggers (ersatter manuell avstandskoll) ----------
# Godot sköter overlappen sjalv: koppla body_entered/area_entered i stallet
# for att loopa alla objekt varje bildruta.
static func trigger3d(radius: float, height := 0.0) -> Area3D:
	var a := Area3D.new()
	var cs := CollisionShape3D.new()
	if height > 0.0:
		var cap := CapsuleShape3D.new()
		cap.radius = radius
		cap.height = maxf(height, radius * 2.0)
		cs.shape = cap
	else:
		var sp := SphereShape3D.new()
		sp.radius = radius
		cs.shape = sp
	a.add_child(cs)
	return a

static func trigger2d(radius: float) -> Area2D:
	var a := Area2D.new()
	var cs := CollisionShape2D.new()
	var c := CircleShape2D.new()
	c.radius = radius
	cs.shape = c
	a.add_child(cs)
	return a

static func trigger_box2d(size: Vector2) -> Area2D:
	var a := Area2D.new()
	var cs := CollisionShape2D.new()
	var r := RectangleShape2D.new()
	r.size = size
	cs.shape = r
	a.add_child(cs)
	return a

# ---------- kraft ----------
# Knuffar alla RigidBody3D inom radien ifran centrum. Returnerar antalet
# traffade sa anroparen kan ge aterkoppling (ljud/partiklar) nar det small.
static func blast3d(root: Node, center: Vector3, force := 8.0, radius := 4.0) -> int:
	var n := 0
	for child in root.get_children():
		if child is RigidBody3D:
			var d: Vector3 = child.global_position - center
			var dist := d.length()
			if dist < radius and dist > 0.001:
				var falloff := 1.0 - dist / radius
				child.apply_impulse(d.normalized() * force * falloff + Vector3.UP * force * 0.35 * falloff)
				n += 1
	return n

static func blast2d(root: Node, center: Vector2, force := 400.0, radius := 160.0) -> int:
	var n := 0
	for child in root.get_children():
		if child is RigidBody2D:
			var d: Vector2 = child.global_position - center
			var dist := d.length()
			if dist < radius and dist > 0.001:
				var falloff := 1.0 - dist / radius
				child.apply_impulse(d.normalized() * force * falloff)
				n += 1
	return n

# En kinematisk kropp (spelaren) knuffar rigidbodies den gar in i. Anropa
# efter move_and_slide - CharacterBody3D puttar INTE av sig sjalv.
static func push_bodies(body: CharacterBody3D, strength := 3.0) -> void:
	for i in range(body.get_slide_collision_count()):
		var c := body.get_slide_collision(i)
		var other = c.get_collider()
		if other is RigidBody3D:
			other.apply_impulse(-c.get_normal() * strength, c.get_position() - other.global_position)
""";
}
