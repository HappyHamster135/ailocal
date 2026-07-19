using AiLocal.Node.Hosting;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.IO;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Coverage for the complete game scaffolds (B): a scaffolded
/// godot/unity project must contain a real, syntactically valid game - not a
/// stub - and every generated C# script must parse as valid C#.</summary>
public class GameScaffoldCompleteTests
{
    [Fact]
    public void Scaffold_Godot_ProducesCompletePlayableProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-godot-" + Path.GetRandomFileName());
        try
        {
            var (ok, _, _, files, output) = new GameScaffoldService().Scaffold("godot", "2d platformer", dir);
            Assert.True(ok, output);
            Assert.Contains("project.godot", files);
            Assert.Contains("Game.cs", files);
            Assert.Contains("Player.cs", files);
            Assert.Contains("Enemy.cs", files);
            Assert.Contains("Coin.cs", files);
            Assert.Contains("Game.tscn", files);
            Assert.Contains("Player.tscn", files);
            Assert.Contains("Enemy.tscn", files);
            Assert.Contains("Coin.tscn", files);
            // Procedural sound effects (zero downloads).
            Assert.True(File.Exists(Path.Combine(dir, "jump.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "coin.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "hurt.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "win.wav")));
            // The C# scripts must be syntactically valid.
            AssertCSharpValid(Path.Combine(dir, "Game.cs"));
            AssertCSharpValid(Path.Combine(dir, "Player.cs"));
            AssertCSharpValid(Path.Combine(dir, "Enemy.cs"));
            AssertCSharpValid(Path.Combine(dir, "Coin.cs"));
            // project.godot must carry the Windows export preset so --export works.
            Assert.Contains("Windows Desktop", File.ReadAllText(Path.Combine(dir, "project.godot")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    [Fact]
    public void Scaffold_Unity_ProducesCompletePlayableProject()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-unity-" + Path.GetRandomFileName());
        try
        {
            var (ok, _, _, files, output) = new GameScaffoldService().Scaffold("unity", "2d platformer", dir);
            Assert.True(ok, output);
            Assert.Contains("Assets/PlayerController.cs", files);
            Assert.Contains("Assets/Enemy.cs", files);
            Assert.Contains("Assets/Coin.cs", files);
            Assert.Contains("Assets/GameManager.cs", files);
            Assert.Contains("Assets/UIManager.cs", files);
            Assert.Contains("Assets/Scenes/SampleScene.unity", files);
            // C# scripts must be syntactically valid.
            AssertCSharpValid(Path.Combine(dir, "Assets/PlayerController.cs"));
            AssertCSharpValid(Path.Combine(dir, "Assets/Enemy.cs"));
            AssertCSharpValid(Path.Combine(dir, "Assets/Coin.cs"));
            AssertCSharpValid(Path.Combine(dir, "Assets/GameManager.cs"));
            AssertCSharpValid(Path.Combine(dir, "Assets/UIManager.cs"));
            // Scene pre-registered so a headless build has something to pack.
            Assert.Contains("Assets/Scenes/SampleScene.unity",
                File.ReadAllText(Path.Combine(dir, "ProjectSettings/EditorBuildSettings.asset")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void AssertCSharpValid(string path)
    {
        var code = File.ReadAllText(path);
        var tree = CSharpSyntaxTree.ParseText(code);
        var diags = tree.GetDiagnostics();
        Assert.Empty(diags.Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString()));
    }
}
