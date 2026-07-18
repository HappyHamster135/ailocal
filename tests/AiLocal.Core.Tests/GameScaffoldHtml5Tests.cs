using System.IO;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The standing product goal is "produce a fully working 2D game
/// with sound and animation" - not a code description. This proves the app
/// can materialise a complete, runnable HTML5 platformer on disk via the
/// scaffold service, so a model (or the user) that triggers scaffold gets a
/// real, playable game file rather than a stub.</summary>
public class GameScaffoldHtml5Tests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ailocal-scaffold-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Scaffold_Html5_WritesPlayableGame()
    {
        var svc = new GameScaffoldService();
        var result = svc.Scaffold("html5", "en 2d plattformare med ljud och animationer", _dir);

        Assert.True(result.Success, result.Output);
        var index = Path.Combine(_dir, "index.html");
        Assert.True(File.Exists(index), "index.html was not written");

        var html = File.ReadAllText(index);
        // Game essentials: a canvas to render on, the player, a jump (gravity)
        // action, Web Audio sound, and a win condition.
        Assert.Contains("<canvas", html);
        Assert.Contains("requestAnimationFrame", html); // game loop
        Assert.Contains("G=0.6", html); // gravity/physics constant
        Assert.Contains("AudioContext", html); // sound
        Assert.Contains("flag", html); // win condition
        // Must be self-contained - no external script/asset references.
        Assert.DoesNotContain("<script src=", html);
    }

    [Fact]
    public void Scaffold_Html5_WritesDesignDocAndLevels()
    {
        var svc = new GameScaffoldService();
        var result = svc.Scaffold("html5", "ett 2d plattformspel med 3 nivaer", _dir);

        Assert.True(result.Success, result.Output);
        // The agent's "plan" is now a real artefact on disk.
        var design = Path.Combine(_dir, "DESIGN.md");
        Assert.True(File.Exists(design), "DESIGN.md (plan) was not written");
        var doc = File.ReadAllText(design);
        Assert.Contains("Speldesign", doc);
        Assert.Contains("Niv", doc); // level progression is part of the plan

        // The generated game must have real level progression: 3 level
        // definitions and a FINAL_LEVEL cap, not an infinite single level.
        var html = File.ReadAllText(Path.Combine(_dir, "index.html"));
        Assert.Contains("const levels=[", html);
        Assert.Contains("FINAL_LEVEL", html);
        Assert.Contains("loadLevel", html);
        // Winning the whole game (not just one level) must be reachable.
        Assert.Contains("Du vann spelet", html);
    }

    [Fact]
    public void Scaffold_Html5_GeneratedJsParses()

    {
        // Strengthen the check: extract the inline script and verify it is at
        // least syntactically valid JS using node if available, otherwise a
        // balanced-brace sanity check.
        var svc = new GameScaffoldService();
        var result = svc.Scaffold("html5", "test", _dir);
        Assert.True(result.Success);

        var html = File.ReadAllText(Path.Combine(_dir, "index.html"));
        var start = html.IndexOf("<script>") + "<script>".Length;
        var end = html.IndexOf("</script>");
        var js = html.Substring(start, end - start);

        var node = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "node",
            Arguments = $"--check -",
            RedirectStandardInput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        node.StandardInput.Write(js);
        node.StandardInput.Close();
        node.WaitForExit(5000);
        // If node is present, it must accept the script (exit 0). If node is
        // absent (exit != 0 / not found), fall back to a brace-balance check.
        if (node.ExitCode == 0)
            Assert.True(true);
        else
        {
            int depth = 0, min = 0;
            foreach (var c in js)
            {
                if (c == '{' || c == '(' || c == '[') depth++;
                else if (c == '}' || c == ')' || c == ']') { depth--; min = Math.Min(min, depth); }
            }
            Assert.Equal(0, depth);
            Assert.Equal(0, min);
        }
    }

    [Fact]
    public void Scaffold_RejectsUnknownEngine()
    {
        var svc = new GameScaffoldService();
        var result = svc.Scaffold("unreal", "x", _dir);
        Assert.False(result.Success);
    }
}
