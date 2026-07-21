using System.Globalization;
using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Turns a <see cref="PixelAnimator"/> spritesheet into a Godot 4 SpriteFrames
/// .tres resource - one AtlasTexture region per frame into the single sheet PNG,
/// plus the named walk/idle animations. An AnimatedSprite2D with this resource
/// plays real frame-by-frame animation. Pure text + a res:// path (no baked
/// binary, no scene-node references), so it imports cleanly headless.
/// </summary>
public static class GodotSpriteFrames
{
    /// <summary>Build the .tres text. <paramref name="texturePath"/> is the
    /// res://-relative name the sheet PNG is saved as (e.g. "player.png").</summary>
    public static string Build(string texturePath, AnimatedSpriteSheet sheet)
    {
        var res = "res://" + (texturePath ?? "sprite.png").Replace('\\', '/').TrimStart('/');
        var sb = new StringBuilder();
        sb.Append("[gd_resource type=\"SpriteFrames\" load_steps=").Append(sheet.FrameCount + 2).Append(" format=3]\n\n");
        sb.Append("[ext_resource type=\"Texture2D\" path=\"").Append(res).Append("\" id=\"1_sheet\"]\n\n");

        for (var f = 0; f < sheet.FrameCount; f++)
        {
            sb.Append("[sub_resource type=\"AtlasTexture\" id=\"AT").Append(f).Append("\"]\n");
            sb.Append("atlas = ExtResource(\"1_sheet\")\n");
            sb.Append("region = Rect2(").Append(f * sheet.FrameWidth).Append(", 0, ")
              .Append(sheet.FrameWidth).Append(", ").Append(sheet.FrameHeight).Append(")\n\n");
        }

        sb.Append("[resource]\nanimations = [");
        for (var a = 0; a < sheet.Anims.Count; a++)
        {
            var an = sheet.Anims[a];
            if (a > 0) sb.Append(", ");
            sb.Append("{\n\"frames\": [");
            for (var i = 0; i < an.FrameCount; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append("{\n\"duration\": 1.0,\n\"texture\": SubResource(\"AT")
                  .Append(an.StartFrame + i).Append("\")\n}");
            }
            sb.Append("],\n\"loop\": ").Append(an.Loop ? "true" : "false")
              .Append(",\n\"name\": &\"").Append(an.Name).Append("\",\n\"speed\": ")
              .Append(an.Fps.ToString(CultureInfo.InvariantCulture)).Append("\n}");
        }
        sb.Append("]\n");
        return sb.ToString();
    }

    /// <summary>Writes the sheet PNG + the .tres next to each other in a folder
    /// and returns the .tres file path. Names default to "player".</summary>
    public static (string PngPath, string TresPath) Write(string dir, string name, AnimatedSpriteSheet sheet)
    {
        Directory.CreateDirectory(dir);
        var png = Path.Combine(dir, name + ".png");
        var tres = Path.Combine(dir, name + "_frames.tres");
        File.WriteAllBytes(png, sheet.Png);
        File.WriteAllText(tres, Build(name + ".png", sheet));
        return (png, tres);
    }
}
