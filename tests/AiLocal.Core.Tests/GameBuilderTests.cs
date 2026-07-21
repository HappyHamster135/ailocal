using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Coverage for the build_game tool (P2 of the game pipeline):
/// the agent scaffolds a game, then calls build_game to produce a standalone
/// .exe. The tool is just a thin dispatcher over an injected GameBuilder
/// delegate (Node wires the real GameBuilder; here we mock it so the wiring,
/// arg parsing and success/error surfacing are verified without a real engine).</summary>
public class GameBuilderTests
{
    private static ToolCall Call(string name, object args) =>
        new(Guid.NewGuid().ToString("N"), name, System.Text.Json.JsonSerializer.Serialize(args));

    [Fact]
    public async Task BuildGame_Success_ReturnsExePath()
    {
        var captured = (string?)null;
        Func<string, string, CancellationToken, Task<(bool Success, string Output, string? ExePath)>>
            builder = (engine, root, ct) =>
            {
                captured = engine + "|" + root;
                return Task.FromResult((true, "built ok", (string?)"C:/game/build/PixelRush.exe"));
            };

        var executor = new AgentToolExecutor(
            AgentAccessLevel.Full, Path.GetTempPath(),
            gameBuilder: builder);

        var result = await executor.ExecuteAsync(
            Call("build_game", new { engine = "godot", root = "C:/game" }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("C:/game/build/PixelRush.exe", result.Output);
        Assert.StartsWith("godot|", captured);
        Assert.Contains("game", captured);
    }

    [Fact]
    public async Task BuildGame_Failure_SurfacesError()
    {
        Func<string, string, CancellationToken, Task<(bool Success, string Output, string? ExePath)>>
            builder = (engine, root, ct) =>
                Task.FromResult((false, "Godot is not installed on this machine.", (string?)null));

        var executor = new AgentToolExecutor(
            AgentAccessLevel.Full, Path.GetTempPath(),
            gameBuilder: builder);

        var result = await executor.ExecuteAsync(
            Call("build_game", new { engine = "auto", root = "C:/game" }),
            CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Godot is not installed", result.Output);
    }

    [Fact]
    public void ToolsFor_WithGameBuild_AdvertisesBuildGame()
    {
        var tools = AgentToolExecutor.ToolsFor(
            AgentAccessLevel.Full, gameBuild: true);
        Assert.Contains(tools, t => t.Name == "build_game");
    }

    [Fact]
    public void ToolsFor_WithoutGameBuild_DoesNotAdvertiseBuildGame()
    {
        var tools = AgentToolExecutor.ToolsFor(
            AgentAccessLevel.Full, gameBuild: false);
        Assert.DoesNotContain(tools, t => t.Name == "build_game");
    }

    // ---- GameBuilder itself: lock the engine command form (no real engine) ----

    [Fact]
    public async Task BuildGodot_CallsCorrectExportCommand()
    {
        var root = Path.Combine(Path.GetTempPath(), "gbtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "build"));
        File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n");

        // The exe is named after the project folder (DeriveExeName).
        var exeName = GameBuilder.DeriveExeName(root) + ".exe";
        var capturedCmd = (string?)null;
        var godotFinder = () => @"C:\Program Files\Godot\godot.exe";
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand =
            (cmd, workDir, ct) =>
            {
                capturedCmd = cmd;
                // godot --export-release would write the exe; simulate it.
                File.WriteAllText(Path.Combine(root, "build", exeName), "MZ");
                return Task.FromResult((0, ""));
            };

        var result = await new GameBuilder().BuildAsync(
            "godot", root, runCommand, CancellationToken.None, godotFinder);

        Assert.True(result.Success);
        Assert.Contains(@"C:\Program Files\Godot\godot.exe", capturedCmd);
        Assert.Contains("--headless", capturedCmd);
        Assert.Contains("--export-release", capturedCmd);
        Assert.Contains("Windows Desktop", capturedCmd);
        Assert.Contains(@"build\" + exeName, capturedCmd);
        // CRITICAL: Godot 4 exits before exporting if --quit precedes --export-release.
        Assert.DoesNotContain("--quit", capturedCmd);
        Assert.Equal(Path.Combine(root, "build", exeName), result.ExePath);
    }

    [Fact]
    public async Task BuildGodot_ExportFailure_SurfacesExitError()
    {
        var root = Path.Combine(Path.GetTempPath(), "gbtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n");

        var capturedCmd = (string?)null;
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand =
            (cmd, workDir, ct) =>
            {
                capturedCmd = cmd;
                return Task.FromResult((1, "ERROR: export preset not found"));
            };

        var result = await new GameBuilder().BuildAsync(
            "godot", root, runCommand, CancellationToken.None,
            godotFinder: () => @"C:\godot.exe");

        Assert.False(result.Success);
        Assert.Contains("export misslyckades", result.Output);
        Assert.Contains("export preset not found", result.Output);
        // the exact command that failed must reference the Windows preset
        Assert.Contains("Windows Desktop", capturedCmd);
    }

    [Fact]
    public async Task BuildGodot_NoEngine_ReportsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "gbtest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "project.godot"), "config_version=5\n");

        var result = await new GameBuilder().BuildAsync(
            "godot", root,
            (cmd, dir, ct) => Task.FromResult((0, "")), CancellationToken.None,
            godotFinder: () => null);

        Assert.False(result.Success);
        Assert.Contains("inte installerat", result.Output);
    }
}
