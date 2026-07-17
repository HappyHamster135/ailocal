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
public sealed class GameScaffoldService
{
    public record ScaffoldResult(bool Success, string Path, string Engine, string[] Files, string Output);

    public ScaffoldResult Scaffold(string engine, string prompt, string root)
    {
        engine = (engine ?? "").Trim().ToLowerInvariant();
        if (engine != "unity" && engine != "godot")
            return new(false, "", "", [], "engine maste vara 'unity' eller 'godot'.");
        if (string.IsNullOrWhiteSpace(root))
            return new(false, "", engine, [], "root (mapp att skapa projektet i) kravs.");
        if (Directory.Exists(root) && Directory.GetFiles(root, "*", SearchOption.AllDirectories).Length > 0)
            return new(false, "", engine, [], "root-mappen ar inte tom - valj en tom mapp.");

        Directory.CreateDirectory(root);
        var files = engine == "unity"
            ? ScaffoldUnity(root, prompt)
            : ScaffoldGodot(root, prompt);
        return new(true, root, engine, files, $"{engine} projekt skapat i {root} ({files.Length} filer).");
    }

    static string[] ScaffoldUnity(string root, string prompt)
    {
        var name = new DirectoryInfo(root).Name;
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var isPlatformer = prompt.Contains("platform", StringComparison.OrdinalIgnoreCase);
        var files = new List<string>();

        // .sln + .csproj so WorkspaceService (and the engine) see a buildable C# project.
        Write(root, $"{name}.sln", Sln(name));
        Write(root, $"{name}/{name}.csproj", Csproj(name));
        Write(root, $"{name}/Assembly-CSharp.csproj", Csproj(name));
        files.Add($"{name}.sln"); files.Add($"{name}/{name}.csproj"); files.Add($"{name}/Assembly-CSharp.csproj");

        // A scene + a starter script so there is something to open and build.
        var scriptName = isPlatformer ? "PlayerController" : "GameManager";
        Write(root, $"Assets/{scriptName}.cs", UnityScript(scriptName, isPlatformer, is3D));
        Write(root, "Assets/Scenes/SampleScene.unity", UnityScene(is3D));
        files.Add($"Assets/{scriptName}.cs"); files.Add("Assets/Scenes/SampleScene.unity");

        // Minimal project settings so Unity accepts the folder as a project.
        Write(root, "ProjectSettings/ProjectVersion.txt", "m_EditorVersion: 6000.2.13f1\nm_EditorVersionWithRevision: 6000.2.13f1 (default)\n");
        Write(root, "Packages/manifest.json", "{\n  \"dependencies\": {\n    \"com.unity.modules.physics\": \"1.0.0\"\n  }\n}\n");
        // Register the sample scene in the build so a headless -buildWindowsPlayer
        // actually has something to compile/pack (Unity refuses with "no scenes"
        // otherwise). The guid must match the scene's .meta file below.
        var sceneGuid = Guid.NewGuid().ToString("N").Substring(0, 32);
        Write(root, "Assets/Scenes/SampleScene.unity.meta", "fileFormatVersion: 2\nguid: " + sceneGuid + "\n" +
            "DefaultImporter:\n  externalObjects: {}\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n");
        Write(root, "ProjectSettings/EditorBuildSettings.asset",
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1045 &1\nEditorBuildSettings:\n" +
            "  m_ObjectHideFlags: 0\n  serializedVersion: 2\n  m_Scenes:\n" +
            "  - enabled: 1\n    path: Assets/Scenes/SampleScene.unity\n    guid: " + sceneGuid + "\n");
        files.Add("ProjectSettings/ProjectVersion.txt"); files.Add("Packages/manifest.json");
        files.Add("Assets/Scenes/SampleScene.unity.meta"); files.Add("ProjectSettings/EditorBuildSettings.asset");
        return files.ToArray();
    }

    static string[] ScaffoldGodot(string root, string prompt)
    {
        var is3D = prompt.Contains("3d", StringComparison.OrdinalIgnoreCase);
        var files = new List<string>();
        Write(root, "project.godot", GodotProject(is3D));
        Write(root, "Main.tscn", GodotScene(is3D));
        Write(root, "Main.cs", GodotScript());
        files.Add("project.godot"); files.Add("Main.tscn"); files.Add("Main.cs");
        return files.ToArray();
    }

    static void Write(string root, string rel, string content)
    {
        var path = Path.Combine(root, rel);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
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

    static string UnityScript(string name, bool platformer, bool is3D) => @"
using UnityEngine;

public class " + name + @" : MonoBehaviour
{
    void Start()
    {
        Debug.Log(""" + name + @" started. TODO: game logic."");
    }

    void Update()
    {
" + (platformer ? @"        // Minimal platformer movement stub.
        float x = Input.GetAxis(""Horizontal"");
        transform.Translate(x * Time.deltaTime * 3f, 0, 0);
" : @"        // Stub - replace with your game loop.
") + @"    }
}
";

    static string UnityScene(bool is3D) =>
        @"%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!1 &" + (is3D ? "3" : "1") + @" mainCamera
GameObject:
  m_Name: MainCamera
--- !u!1 &2
GameObject:
  m_Name: Directional Light
";

    static string GodotProject(bool is3D) =>
        "[application]\n\n" +
            "config/name=\"GodotGame\"\n" +
            "run/main_scene=\"res://Main.tscn\"\n" +
            (is3D ? "[rendering]\nrenderer/rendering_method=\"forward_plus\"\n" : "[display]\nwindow/size/viewport_width=1280\nwindow/size/viewport_height=720\n") +
            // Export preset named "Windows Desktop" - matches the --export-release
            // target used by WorkspaceService, so the Godot "Bygg spel" button
            // actually produces a .exe instead of erroring "preset not found".
            "\n[export]\n\n" +
            "[export_profile]\n" +
            "name=\"Windows Desktop\"\n" +
            "platform=\"Windows Desktop\"\n" +
            "export_path=\"build/GodotGame.exe\"\n" +
            "include_filter=\"\"\n" +
            "exclude_filter=\"\"\n" +
            "patches=\"\"\n";

    static string GodotScene(bool is3D) =>
        @"[gd_scene load_steps=2 format=3 uid=""uid://b" + (is3D ? "3d" : "2d") + @"game""]

[ext_resource type=""Script"" path=""res://Main.cs"" id=""1""]

[node name=""Main"" type=""Node2D""]
script = ExtResource(""1"")
";

    static string GodotScript() =>
        @"using Godot;

public partial class Main : Node2D
{
    public override void _Ready()
    {
        GD.Print(""Godot game started. TODO: game logic."");
    }
}
";
}
