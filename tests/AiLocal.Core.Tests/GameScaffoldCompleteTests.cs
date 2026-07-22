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
            // v1.85: ren GDScript som de andra kiten - inga C#-filer (mono-
            // beroendet borta; kittet kan nu headless-verifieras + har juice).
            Assert.Contains("Main.gd", files);
            Assert.Contains("Main.tscn", files);
            Assert.Contains("DESIGN.md", files);
            Assert.DoesNotContain(files, f => f.EndsWith(".cs"));
            // Procedural sound effects (zero downloads).
            Assert.True(File.Exists(Path.Combine(dir, "click.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "coin.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "hurt.wav")));
            Assert.True(File.Exists(Path.Combine(dir, "win.wav")));
            // Animerade sprites (PixelAnimator -> SpriteFrames) refereras och finns.
            Assert.True(File.Exists(Path.Combine(dir, "player_frames.tres")));
            Assert.True(File.Exists(Path.Combine(dir, "enemy_frames.tres")));
            // App icon + it is wired into project.godot.
            Assert.True(File.Exists(Path.Combine(dir, "icon.ico")));
            AssertValidIco(Path.Combine(dir, "icon.ico"));
            Assert.Contains("config/icon=\"res://icon.ico\"", File.ReadAllText(Path.Combine(dir, "project.godot")));
            // Godot 4 export preset so --export-release "Windows Desktop" works.
            Assert.Contains("export_presets.cfg", files);
            var preset = File.ReadAllText(Path.Combine(dir, "export_presets.cfg"));
            Assert.Contains("[preset.0]", preset);
            Assert.Contains("name=\"Windows Desktop\"", preset);
            Assert.Contains("application/icon=\"res://icon.ico\"", preset);
            // GDScript-syntaxen verifieras av riktig godot i GodotKitTests
            // (gated headless) - har lases filstrukturen och produktionsbiten.
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
            // App icon + Steam appid placeholder (publishable build).
            Assert.True(File.Exists(Path.Combine(dir, "icon.ico")));
            AssertValidIco(Path.Combine(dir, "icon.ico"));
            Assert.True(File.Exists(Path.Combine(dir, "steam_appid.txt")));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    private static void AssertValidIco(string path)
    {
        var b = File.ReadAllBytes(path);
        Assert.True(b.Length > 22, "ico too small");
        // ICONDIR: reserved(0), type(1), count(1)
        Assert.True(b[0] == 0 && b[1] == 0, "reserved must be 0");
        Assert.True(b[2] == 1 && b[3] == 0, "must be type icon");
        // ICONDIRENTRY (first): bWidth,bHeight,bColorCount,bReserved (4),
        // wPlanes(2), wBitCount(2), dwBytesInRes(4) @ entry+8, dwImageOffset(4) @ entry+12
        int entry = 6;
        int dibSize = BitConverter.ToInt32(b, entry + 8);      // dwBytesInRes
        int dibOffset = BitConverter.ToInt32(b, entry + 12);    // dwImageOffset
        Assert.True(dibSize > 40 && b.Length >= dibOffset + dibSize, "DIB missing/payload too small");
        // BITMAPINFOHEADER begins at the image offset; biSize should be 40.
        Assert.Equal(40, BitConverter.ToInt32(b, dibOffset));
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
