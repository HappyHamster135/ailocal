using System.Text.RegularExpressions;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>PixelAnimator + GodotSpriteFrames: real frame-by-frame pixel
/// animation (the walk cycle's legs actually move) composed into a spritesheet
/// and wired into a Godot SpriteFrames resource.</summary>
public class AnimationTests
{
    [Fact]
    public void Frames_WalkCycle_LegsActuallyMove()
    {
        var f = PixelAnimator.Frames("hjalte", 16);
        Assert.Equal(6, f.Count);
        // Walk phase 0 (index 2) vs phase 2 (index 4): legs step to opposite
        // sides, so the frames MUST differ - the whole point over a static sprite.
        Assert.NotEqual(Convert.ToBase64String(f[2]), Convert.ToBase64String(f[4]));
        // Idle bob: frame 0 vs frame 1 differ too.
        Assert.NotEqual(Convert.ToBase64String(f[0]), Convert.ToBase64String(f[1]));
    }

    [Fact]
    public void Build_DeterministicPerPrompt_VariesByPrompt()
    {
        Assert.Equal(PixelAnimator.Build("riddare").Png, PixelAnimator.Build("riddare").Png);
        Assert.NotEqual(PixelAnimator.Build("riddare").Png, PixelAnimator.Build("slemmonster").Png);
    }

    [Fact]
    public void Build_ProducesValidPngAndAnimations()
    {
        var s = PixelAnimator.Build("spelare", 16);
        Assert.Equal(6, s.FrameCount);
        Assert.Equal(16, s.FrameWidth);
        Assert.Contains(s.Anims, a => a.Name == "walk" && a.FrameCount == 4 && a.Loop);
        Assert.Contains(s.Anims, a => a.Name == "idle" && a.FrameCount == 2);
        // PNG-signatur (89 'P' 'N' 'G')
        Assert.True(s.Png.Length > 8 && s.Png[0] == 0x89 && s.Png[1] == (byte)'P'
            && s.Png[2] == (byte)'N' && s.Png[3] == (byte)'G');
    }

    [Fact]
    public void GodotSpriteFrames_BuildsValidTres()
    {
        var tres = GodotSpriteFrames.Build("player.png", PixelAnimator.Build("hjalte", 16));
        Assert.Contains("[gd_resource type=\"SpriteFrames\"", tres);
        Assert.Contains("path=\"res://player.png\"", tres);
        Assert.Contains("&\"walk\"", tres);
        Assert.Contains("&\"idle\"", tres);
        Assert.Equal(6, Regex.Matches(tres, "sub_resource type=\"AtlasTexture\"").Count);
        Assert.Contains("region = Rect2(32, 0, 16, 16)", tres); // frame 2 = x 2*16
    }

    [Fact]
    public void GodotSpriteFrames_Write_ProducesPngAndTres()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-anim-" + Guid.NewGuid().ToString("n"));
        try
        {
            var (png, tres) = GodotSpriteFrames.Write(dir, "player", PixelAnimator.Build("gubbe", 16));
            Assert.True(File.Exists(png));
            Assert.True(File.Exists(tres));
            Assert.Contains("player.png", File.ReadAllText(tres));
        }
        finally { try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ } }
    }
}
