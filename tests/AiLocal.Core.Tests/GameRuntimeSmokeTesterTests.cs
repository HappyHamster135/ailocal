using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The headless runtime playtest: games are EXECUTED (Jint + stub
/// DOM), not just parsed. Locks the three detections that matter - crashes
/// inside the frame loop, id-drift, and alert() blocking the loop - plus
/// that a healthy game pumps its frames clean.</summary>
public class GameRuntimeSmokeTesterTests
{
    private static GameRuntimeSmokeTester.SmokeResult Run(string html) =>
        new GameRuntimeSmokeTester().Run(html, frames: 30);

    [Fact]
    public void HealthyGame_PumpsFramesWithoutErrors()
    {
        var result = Run("""
            <canvas id="c"></canvas><span id="score">0</span>
            <script>
            const cv = document.getElementById('c'), ctx = cv.getContext('2d');
            let x = 0;
            function loop(){ x++; ctx.fillRect(x, 0, 10, 10);
              document.getElementById('score').textContent = x;
              requestAnimationFrame(loop); }
            loop();
            </script>
            """);
        Assert.True(result.LoadedCleanly);
        Assert.Empty(result.Errors);
        Assert.Equal(30, result.FramesPumped);
    }

    [Fact]
    public void CrashInsideFrameLoop_IsReported()
    {
        var result = Run("""
            <canvas id="c"></canvas>
            <script>
            let n = 0;
            function loop(){ n++; if (n === 3) explodera(); requestAnimationFrame(loop); }
            loop();
            </script>
            """);
        Assert.Contains(result.Errors, e => e.Contains("frame", StringComparison.OrdinalIgnoreCase)
            && e.Contains("explodera"));
    }

    [Fact]
    public void CrashAtLoad_MarksNotLoadedCleanly()
    {
        var result = Run("<script>funktionSomInteFinns();</script>");
        Assert.False(result.LoadedCleanly);
        Assert.Contains(result.Errors, e => e.Contains("laddning"));
    }

    [Fact]
    public void IdDrift_IsReported()
    {
        var result = Run("""
            <span id="poang"></span>
            <script>document.getElementById('poeng').textContent = '5';</script>
            """);
        Assert.Contains(result.Errors, e => e.Contains("poeng") && e.Contains("finns inte"));
    }

    [Fact]
    public void DynamicallyCreatedIds_AreNotFlaggedAsDrift()
    {
        var result = Run("""
            <script>
            const d = document.createElement('div');
            d.innerHTML = '<span id=dyn-score></span>';
            document.body.appendChild(d);
            document.getElementById('dyn-score');
            </script>
            """);
        Assert.DoesNotContain(result.Errors, e => e.Contains("dyn-score"));
    }

    [Fact]
    public void AlertUsage_IsFlaggedAsWarning()
    {
        var result = Run("<script>alert('Game Over');</script>");
        Assert.Contains(result.Warnings, w => w.Contains("alert()"));
    }

    [Fact]
    public void CrashBehindStartGate_IsReachedByTheInputDriver()
    {
        // The crash only exists in gameplay: update() early-returns until the
        // start button is clicked AND a key is held. Without the input driver
        // this game passes; with it the crash must surface - proving the
        // smoke run exercises real gameplay code, not just the draw loop.
        var result = new GameRuntimeSmokeTester().Run("""
            <button id="startBtn">Start</button><canvas id="c"></canvas>
            <script>
            let started = false; const keys = {};
            document.getElementById('startBtn').onclick = () => { started = true; };
            addEventListener('keydown', e => keys[e.key.toLowerCase()] = true);
            function update(){ if (!started) return;
              if (keys['arrowright'] || keys['d']) kraschaIGameplay(); }
            function loop(){ update(); requestAnimationFrame(loop); }
            loop();
            </script>
            """, frames: 60);
        Assert.Contains(result.Errors, e => e.Contains("kraschaIGameplay"));
    }
}
