using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
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
                return Task.FromResult((true, "built ok", "C:/game/build/PixelRush.exe"));
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
}
