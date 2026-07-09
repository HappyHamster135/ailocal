using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public static class WorkerRole
{
    // The provider fallback chain is registered in NodeComposition (shared).
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<LocalRuntimeBootstrapper>();
    }

    public static void MapEndpoints(WebApplication app)
    {
        // Host calls this to run a delegated unit of work.
        app.MapPost("/execute", async (ChatRequest req, FallbackChatProvider provider, CancellationToken ct) =>
        {
            var result = await provider.CompleteAsync(req, ct);
            return result.IsSuccess
                ? Results.Ok(result.Response)
                : Results.Problem(
                    detail: result.Error ?? "provider failure",
                    statusCode: result.Outcome == ProviderOutcome.FatalError ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable);
        });

        // SSE variant used by the Host for single-worker chat dispatches so the
        // operator sees text arrive incrementally instead of "submit and wait".
        app.MapPost("/execute/stream", async (ChatRequest req, HttpContext ctx, FallbackChatProvider provider, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";

            await foreach (var chunk in provider.StreamAsync(req, ct))
            {
                object frame = chunk.Delta is not null
                    ? new { delta = chunk.Delta }
                    : new
                    {
                        final = new
                        {
                            success = chunk.Final?.IsSuccess ?? false,
                            error = chunk.Final?.Error,
                            response = chunk.Final?.Response
                        }
                    };
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(frame)}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        app.MapGet("/runtime", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.InspectAsync(ct)));

        app.MapPost("/runtime/pull", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.PullRecommendedModelAsync(ct)));

        // One click: install Ollama (if missing), start it, and pull the model.
        app.MapPost("/runtime/setup", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.SetupLocalAiAsync(ct)));
    }
}
