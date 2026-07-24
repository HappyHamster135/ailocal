namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.30 punkt 4: 3D-DJUPET. Fienderna i 3D-kiten gick rakt mot spelaren -
/// tvars igenom pelare och vaggar. Nav3D.gd bakar en navmesh ur banans
/// geometri i kod och ger riktig vagsokning runt hinder.
///
/// Bygger MEDVETET pa NavigationServer3D.map_get_path i stallet for
/// NavigationAgent3D: agenten begar sin vag asynkront och gav tom vag i den
/// skarpa sonden, medan serverfragan returnerade en korrekt 5-punktsvag runt
/// hindret pa forsta forsoket. Synkront, deterministiskt och utan
/// nodlivscykel att halla reda pa.
/// </summary>
public partial class GameScaffoldService
{
    static string[] AppendNav3DLib(string root, string[] files)
    {
        try
        {
            Write(root, "Nav3D.gd", GodotNav3DLib);
            return [.. files, "Nav3D.gd"];
        }
        catch { return files; }
    }

    const string GodotNav3DLib = """
class_name Nav3D
# Vagsokning i 3D. Utan detta gar fiender rakt mot spelaren och rakt genom
# vaggar - det enskilt tydligaste tecknet pa att en 3D-varld ar en kuliss.
#
#   const Nav3D = preload("res://Nav3D.gd")
#
#   # EN gang, efter att banan byggts:
#   Nav3D.bake(self)
#
#   # per fiende, nar den behover en ny vag:
#   e["route"] = Nav3D.route(self, e["node"].global_position, spelare_pos)
#   e["leg"] = 0
#
#   # varje bildruta:
#   e["leg"] = Nav3D.advance(e["node"].global_position, e["route"], e["leg"])
#   var dir = Nav3D.dir_along(e["node"].global_position, e["route"], e["leg"])
#   if dir != Vector3.ZERO:
#       node.position += dir * fart * delta

# Bakar en navmesh ur ALL synlig geometri under root. Anropas efter att
# banan byggts - hinder som laggs till senare kraver en ny bake().
static func bake(root: Node3D, agent_radius := 0.5, agent_height := 1.8, max_slope := 45.0) -> NavigationRegion3D:
	var region := NavigationRegion3D.new()
	root.add_child(region)
	var nm := NavigationMesh.new()
	nm.agent_radius = agent_radius
	nm.agent_height = agent_height
	nm.agent_max_slope = max_slope
	var src := NavigationMeshSourceGeometryData3D.new()
	NavigationServer3D.parse_source_geometry_data(nm, src, root)
	NavigationServer3D.bake_from_source_geometry_data(nm, src)
	region.navigation_mesh = nm
	return region

static func polygon_count(region: NavigationRegion3D) -> int:
	if region == null or region.navigation_mesh == null:
		return 0
	return region.navigation_mesh.get_polygon_count()

# Vagen mellan tva punkter. Tom array = ingen vag (t.ex. innan bake).
static func route(node: Node3D, from: Vector3, to: Vector3) -> PackedVector3Array:
	var w := node.get_world_3d()
	if w == null:
		return PackedVector3Array()
	return NavigationServer3D.map_get_path(w.navigation_map, from, to, true)

# Flyttar fram delmalet nar man ar framme vid det. Returnerar nytt index.
static func advance(pos: Vector3, r: PackedVector3Array, leg: int, reach := 0.8) -> int:
	var i := clampi(leg, 0, maxi(0, r.size() - 1))
	while i < r.size():
		var p := r[i]
		if Vector2(p.x - pos.x, p.z - pos.z).length() > reach:
			break
		i += 1
	return i

# Riktning mot nasta delmal (platt, y = 0). Vector3.ZERO nar vagen ar slut.
static func dir_along(pos: Vector3, r: PackedVector3Array, leg: int) -> Vector3:
	if leg < 0 or leg >= r.size():
		return Vector3.ZERO
	var d := r[leg] - pos
	d.y = 0.0
	if d.length() < 0.001:
		return Vector3.ZERO
	return d.normalized()

# Bekvamlighet: hela steget i ETT anrop for kit som inte vill halla state
# sjalva. Returnerar det uppdaterade benindexet.
static func follow(node: Node3D, r: PackedVector3Array, leg: int, speed: float, delta: float, reach := 0.8) -> int:
	var i := advance(node.global_position, r, leg, reach)
	var dir := dir_along(node.global_position, r, i)
	if dir != Vector3.ZERO:
		node.position += dir * speed * delta
	return i
""";
}
