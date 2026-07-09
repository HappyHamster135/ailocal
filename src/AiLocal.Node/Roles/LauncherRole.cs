using System.Net.Http.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public static class LauncherRole
{
    public static void ConfigureServices(IServiceCollection services) { }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(Dashboard.Html, "text/html"));

        // One click: start a Host, then a Worker paired to it. The Worker adopts
        // the Host's freshly-minted cluster token automatically so pairing does
        // not require a manual copy/paste step for the common co-located case.
        app.MapPost("/api/quickstart", async (LocalNodeLauncher launcher, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var host = await launcher.StartAsync(new LaunchNodeRequest(NodeRole.Host), ct);
            if (!host.Started)
                return Results.Ok(new { started = false, error = host.Error ?? "Could not start Host." });

            var token = await FetchClusterTokenAsync(host.Endpoint, httpFactory, ct);
            var worker = await launcher.StartAsync(
                new LaunchNodeRequest(NodeRole.Worker, HostEndpoint: host.Endpoint, ClusterToken: token), ct);
            return Results.Ok(new
            {
                started = true,
                hostEndpoint = host.Endpoint,
                worker = worker.Started,
                workerError = worker.Error
            });
        });

        app.MapGet("/api/host", () => Results.Ok(new { host = (string?)null }));
        app.MapGet("/api/nodes", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/tasks", () => Results.Ok(Array.Empty<object>()));
        app.MapGet("/api/chat", () => Results.Ok(Array.Empty<object>()));

        app.MapPost("/api/chat", () =>
            Results.BadRequest(new { error = "Start Host or Overseer first." }));

        app.MapGet("/api/providers", (NodeSettings settings) =>
            Results.Ok(ProviderOrderApi.Read(settings)));

        app.MapPut("/api/providers", (ProviderOrderUpdate req, NodeSettings settings) =>
            Results.Ok(ProviderOrderApi.Update(req, settings)));
    }

    private static async Task<string?> FetchClusterTokenAsync(
        string hostEndpoint, IHttpClientFactory httpFactory, CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);
            var settings = await client.GetFromJsonAsync<HostSettingsProbe>($"{hostEndpoint}/api/settings", ct);
            return settings?.ClusterToken;
        }
        catch
        {
            return null;
        }
    }

    private sealed record HostSettingsProbe(string? ClusterToken);
}
