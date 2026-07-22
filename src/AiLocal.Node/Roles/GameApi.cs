using AiLocal.Node.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace AiLocal.Node.Roles;

/// <summary>P1: "Skapa nytt spel" - scaffold a complete Unity/Godot project
/// from a prompt so the agent (and you) don't start from an empty folder.
/// P2: "Bygg spel" - takes the scaffolded project and produces a standalone
/// .exe via the engine's headless build (godot --export-release /
/// unity -buildWindows64Player). The engine must be installed on the machine;
/// it is NOT downloaded here.</summary>
public static class GameApi
{
    public static void MapEndpoints(WebApplication app, GameBuilder? builder = null)
    {
        // Captured by the build endpoint's closure. GameBuilder is NOT a DI
        // service, so declaring it as a handler parameter (the previous shape)
        // made minimal-API binding treat it as the JSON request body - which
        // both ignored the instance passed in here and consumed the body
        // stream before the handler's own ReadFromJsonAsync could run.
        var gameBuilder = builder ?? new GameBuilder();

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

        // Build the scaffolded (or hand-written) project into a standalone .exe.
        app.MapPost("/api/game/build", async (
            HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<GameBuildRequest>(ct);
            if (body is null)
                return Results.Problem(detail: "engine + root krävs", statusCode: StatusCodes.Status400BadRequest);

            var runCommand = new Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>>(
                (cmd, dir, c) => RunCommandAsync(cmd, dir, c));

            var result = await gameBuilder.BuildAsync(body.Engine ?? "auto", body.Root ?? "", runCommand, ct);
            return result.Success
                ? Results.Ok(new { success = true, exe = result.ExePath, output = result.Output })
                : Results.Problem(detail: result.Output, statusCode: StatusCodes.Status500InternalServerError);
        });
    }

    /// <summary>Run a shell command and capture exit code + output, used to
    /// drive the engine's headless build. Mirrors AgentToolExecutor.RunCommandCoreAsync
    /// (kept local so GameApi stays in the Node layer).</summary>
    static async Task<(int ExitCode, string Output)> RunCommandAsync(
        string command, string workingDirectory, CancellationToken ct)
    {
        var psi = OperatingSystem.IsWindows()
            // Wrappad /c "{cmd}" - se WorkerRole.runCmd: >2 citat i kommandot
            // (godot-exporter) strippades annars sönder av cmd.exe (v1.90).
            ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{command}\"")
            : new System.Diagnostics.ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");
        psi.WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = new System.Diagnostics.Process { StartInfo = psi };
        var stdout = new System.Text.StringBuilder();
        var stderr = new System.Text.StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        await process.WaitForExitAsync(ct);
        return (process.ExitCode, $"exit code: {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}");
    }

    private sealed record GameScaffoldRequest(string? Engine, string? Prompt, string? Root);
    private sealed record GameBuildRequest(string? Engine, string? Root);
}
