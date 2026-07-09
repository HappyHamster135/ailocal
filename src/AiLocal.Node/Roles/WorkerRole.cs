using System.Net.Http.Json;
using System.Text.Json;
using AiLocal.Core.Configuration;
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

        // Click-to-pair, no typing: a Host that discovered this Worker via LAN
        // beacon sends a connect request with a random nonce. This node's own
        // operator must explicitly accept it (see /pairing/pending/{id}/accept)
        // before anything is trusted - see PairingCoordinator for the full flow.
        app.MapPost("/pairing/request", (PairingHandshakePayload req, PairingCoordinator pairing) =>
        {
            pairing.AddInbound(req.PeerId, req.PeerName, req.PeerEndpoint, req.Nonce);
            return Results.Ok(new { received = true });
        });

        app.MapGet("/pairing/pending", (PairingCoordinator pairing) =>
            Results.Ok(pairing.PendingInbound()));

        app.MapPost("/pairing/pending/{hostId}/reject", (string hostId, PairingCoordinator pairing) =>
        {
            pairing.RejectInbound(hostId);
            return Results.Ok(new { rejected = true });
        });

        app.MapPost("/pairing/pending/{hostId}/accept", async (
            string hostId, PairingCoordinator pairing, PersistentSettingsStore store, HostLocator hostLocator,
            NodeSettings settings, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var request = pairing.TakeInbound(hostId);
            if (request is null)
                return Results.NotFound(new { error = "no pending request from that host - it may have expired" });

            try
            {
                var selfEndpoint = $"http://{NetworkUtil.LocalIPv4()}:{settings.Port}";
                var payload = new PairingHandshakePayload(store.NodeId, settings.NodeName, selfEndpoint, request.Nonce);
                var client = httpFactory.CreateClient("cluster");
                using var response = await client.PostAsJsonAsync($"{request.RequesterEndpoint}/pairing/approved", payload, ct);
                if (!response.IsSuccessStatusCode)
                    return Results.Problem(
                        detail: $"Host {request.RequesterEndpoint} svarade {(int)response.StatusCode}.",
                        statusCode: StatusCodes.Status502BadGateway);

                var approval = await response.Content.ReadFromJsonAsync<PairingApprovalResponse>(ct);
                if (approval?.ClusterToken is not { Length: > 0 } token)
                    return Results.Problem(
                        detail: "Host skickade ingen klusternyckel.", statusCode: StatusCodes.Status502BadGateway);

                store.Update(new SettingsUpdate(HostEndpoint: request.RequesterEndpoint, ClusterToken: token), hostLocator);
                return Results.Ok(new { connected = true, host = request.RequesterName });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }
}
