using AiLocal.Node.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace AiLocal.Node.Roles;

/// <summary>P1: "Skapa nytt spel" - scaffold a complete Unity/Godot project
/// from a prompt so the agent (and you) don't start from an empty folder.
/// The agent then fills in game logic via the file API, and Studio's
/// "Bygg spel" runs the headless build.</summary>
public static class GameApi
{
    public static void MapEndpoints(WebApplication app)
    {
        // Scaffold a fresh, buildable game project in an (empty) folder.
        app.MapPost("/api/game/scaffold", async (
            GameScaffoldService scaffold, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<GameScaffoldRequest>(ct);
            if (body is null)
                return Results.Problem(detail: "engine + prompt + root krävs", statusCode: StatusCodes.Status400BadRequest);
            var (success, path, engine, files, output) = scaffold.Scaffold(
                body.Engine ?? "", body.Prompt ?? "", body.Root ?? "");
            return success
                ? Results.Ok(new { success, path, engine, files, output })
                : Results.Problem(detail: output, statusCode: StatusCodes.Status400BadRequest);
        });
    }

    private sealed record GameScaffoldRequest(string? Engine, string? Prompt, string? Root);
}
