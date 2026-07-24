using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29 etapp 1: emitterar <c>Cast3D.gd</c> - projektets rollista översatt
/// till GDScript-literaler. Medvetet KOD och inte JSON som spelet måste
/// parsa: .gd packas alltid i exporten och behöver aldrig importeras
/// (lärdomen "godot --path importerar ALDRIG" gäller assets, inte skript),
/// och genereringen blir C#-testbar.
///
/// Detta är bron mellan 2D-identiteten och 3D-riggen: samma CharacterSpec
/// ger både pixelspriten och 3D-figurens färger och proportioner.
/// </summary>
public static class Cast3DScript
{
    public static string Build(IReadOnlyList<CharacterSpec> specs, ArtBible bible)
    {
        var sb = new StringBuilder();
        sb.Append("class_name Cast3D\n");
        sb.Append("# GENERERAD av AiLocal - projektets rollista som data.\n");
        sb.Append("# Samma CharacterSpec driver 2D-spriten (player_frames.tres) och\n");
        sb.Append("# 3D-riggen (Rig3D.actor), sa figuren kanns igen i bada.\n");
        sb.Append("# Lagg INTE till figurer for hand har - anvand generate_asset med\n");
        sb.Append("# parametern character, sa hamnar de i rollistan och blir stabila.\n\n");
        sb.Append($"const OUTLINE := {Col(bible.OutlineHex)}\n");
        sb.Append($"const STYLE := \"{Esc(bible.StyleName)}\"\n\n");
        sb.Append("const SPECS := {\n");
        for (var i = 0; i < specs.Count; i++)
        {
            var s = specs[i];
            var m = RigMetricsFactory.For(s.Traits);
            sb.Append($"\t\"{Esc(s.Slug)}\": {{\n");
            sb.Append($"\t\t\"slug\": \"{Esc(s.Slug)}\", \"name\": \"{Esc(s.DisplayName)}\", \"role\": \"{Esc(s.Role)}\",\n");
            sb.Append($"\t\t\"body\": \"{Esc(s.Traits.Body)}\", \"hair\": \"{Esc(s.Traits.Hair)}\", ");
            sb.Append($"\"face\": \"{Esc(s.Traits.Face)}\", \"mark\": \"{Esc(s.Traits.Mark)}\",\n");
            sb.Append($"\t\t\"skin\": {Ramp(s.Palette.SkinRamp)},\n");
            sb.Append($"\t\t\"shirt\": {Ramp(s.Palette.ShirtRamp)},\n");
            sb.Append($"\t\t\"pants\": {Ramp(s.Palette.PantsRamp)},\n");
            sb.Append($"\t\t\"hair_col\": {Ramp(s.Palette.HairRamp)},\n");
            sb.Append($"\t\t\"shoe\": {Col(s.Palette.Shoe)}, \"eye\": {Col(s.Palette.Eye)}, ");
            sb.Append($"\"eye_glint\": {(s.Palette.EyeGlint ? "true" : "false")},\n");
            sb.Append($"\t\t\"metrics\": {RigMetricsFactory.ToGd(m)}\n");
            sb.Append("\t}" + (i < specs.Count - 1 ? "," : "") + "\n");
        }
        sb.Append("}\n\n");
        sb.Append("static func spec(slug: String) -> Dictionary:\n");
        sb.Append("\tif SPECS.has(slug):\n\t\treturn SPECS[slug]\n");
        sb.Append("\tif SPECS.has(\"player\"):\n\t\treturn SPECS[\"player\"]\n");
        sb.Append("\treturn {}\n\n");
        sb.Append("static func slugs() -> Array:\n\treturn SPECS.keys()\n\n");
        sb.Append("static func has(slug: String) -> bool:\n\treturn SPECS.has(slug)\n");
        return sb.ToString();
    }

    /// <summary>Läser rollistan + bibeln ur projektet och skriver Cast3D.gd.
    /// Anropas av scaffolden och varje gång en ny karaktär skapas via
    /// generate_asset, så listan aldrig hamnar efter.</summary>
    public static bool WriteInto(string projectRoot)
    {
        try
        {
            var specs = CharacterCast.All(projectRoot);
            if (specs.Count == 0) return false;
            var bible = ArtBibleStore.Load(projectRoot);
            if (bible is null) return false;
            File.WriteAllText(Path.Combine(projectRoot, "Cast3D.gd"), Build(specs, bible));
            return true;
        }
        catch { return false; }
    }

    private static string Ramp(string[] hex) =>
        "[" + string.Join(", ", hex.Select(Col)) + "]";

    private static string Col(string hex)
    {
        var (r, g, b) = ArtBible.Hex(hex);
        return $"Color8({r}, {g}, {b})";
    }

    private static string Esc(string s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
}
