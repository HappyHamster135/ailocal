using System.Net.Http.Json;
using System.Text.Json.Nodes;
using AiLocal.Core.Configuration;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

/// <summary>
/// Operator-facing role. It aggregates every discovered Host and keeps a
/// Worker-to-Host index so node operations reach the correct cluster branch.
/// </summary>
public static class OverseerRole
{
    private sealed record HostPayload(string Endpoint, JsonNode? Json);

    public static void ConfigureServices(IServiceCollection services) { }

    public static void MapEndpoints(WebApplication app)
    {
        // Capture this Overseer's own cluster token so proxies fall back to it
        // for any Host we have no announced token for (same-machine case,
        // where both share the settings file and therefore the same token).
        var store = app.Services.GetRequiredService<PersistentSettingsStore>();
        var hosts = app.Services.GetRequiredService<HostRegistry>();
        hosts.OverseerToken = store.GetClusterToken();
        // Live source too: a token pasted into settings AFTER startup must
        // take effect immediately - the snapshot above alone meant 401 on
        // every proxy until the Overseer was restarted.
        hosts.OverseerTokenSource = store.GetClusterToken;

        app.MapGet("/", () => Results.Content(Dashboard.Html, "text/html"));

        app.MapGet("/api/host", (HostLocator locator, HostRegistry hosts) => Results.Ok(new
        {
            host = locator.HostEndpoint ?? hosts.PrimaryEndpoint,
            hosts = hosts.All
        }));
        app.MapGet("/api/hosts", (HostRegistry hosts) => Results.Ok(hosts.All));
        app.MapDelete("/api/hosts/{id}", (string id, HostRegistry hosts) =>
            hosts.Remove(id) ? Results.NoContent() : Results.NotFound());

        // A Host that discovers this Overseer announces itself here (carrying
        // its own cluster token) so the Overseer can proxy node-to-node calls
        // back to it using the *Host's* token - each node mints its own, and
        // presenting the Overseer's token to a remote Host is rejected with
        // 401. This is the LAN-trust opt-in: a Host chooses to be controllable
        // by announcing; one that never announces is simply never proxied to.
        app.MapPost("/cluster/announce", (NodeInfo node, HostRegistry hosts) =>
        {
            if (node is not { Role: NodeRole.Host })
                return Results.BadRequest(new { error = "only hosts may announce" });
            hosts.UpsertExplicitOrUpdate(node.Endpoint, node.Id, node.Name, node.ClusterToken);
            return Results.Ok(new { announced = node.Id });
        });

        app.MapGet("/api/nodes", AggregateNodes);
        app.MapGet("/api/topology", AggregateTopology);

        app.MapGet("/api/nodes/{id}", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Get, $"/api/nodes/{Esc(id)}", null, ct));
        app.MapDelete("/api/nodes/{id}", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Delete, $"/api/nodes/{Esc(id)}", null, ct));
        app.MapGet("/api/nodes/{id}/settings", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Get, $"/api/nodes/{Esc(id)}/settings", null, ct));
        app.MapPut("/api/nodes/{id}/settings", (string id, SettingsUpdate req, HostLocator locator,
            HostRegistry hosts, IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Put, $"/api/nodes/{Esc(id)}/settings", req, ct));
        app.MapGet("/api/nodes/{id}/runtime", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Get, $"/api/nodes/{Esc(id)}/runtime", null, ct));
        app.MapPost("/api/nodes/{id}/runtime/pull", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Post, $"/api/nodes/{Esc(id)}/runtime/pull", null, ct));
        app.MapPost("/api/nodes/{id}/runtime/setup", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Post, $"/api/nodes/{Esc(id)}/runtime/setup", null, ct));
        app.MapGet("/api/nodes/{id}/tasks", (string id, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyWorker(id, locator, hosts, hf, HttpMethod.Get, $"/api/nodes/{Esc(id)}/tasks", null, ct));

        app.MapGet("/api/tasks", (HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            AggregateArrays(locator, hosts, hf, "/tasks", mapWorkers: false, ct));

        app.MapGet("/api/chat", (HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Get, "/chat", null, ct));
        app.MapPost("/api/chat", (SubmitTaskRequest req, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Post, "/chat", req, ct));
        // Uppdrag-flödet från Overseerns dashboard: planeringen är en vanlig
        // buffrad proxy, men själva assignmenten är en SSE-ström (agentens
        // steg i realtid) och måste STREAMAS igenom - en buffrad proxy hade
        // först svarat när hela bygget var klart. Innan dessa två fanns fick
        // Overseer-vyn HTTP 404 på första planeringsanropet.
        app.MapPost("/api/goal-plan", (GoalPlanRequest req, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Post, "/api/goal-plan", req, ct, TimeSpan.FromSeconds(180)));

        app.MapPost("/api/assignment", async (AssignmentRequest req, HttpContext ctx,
            HostLocator locator, HostRegistry hosts, IHttpClientFactory hf, CancellationToken ct) =>
        {
            var candidates = PrimaryCandidates(locator, hosts);
            if (candidates.Count == 0)
                return Results.Problem("host not yet discovered");

            foreach (var endpoint in candidates)
            {
                HttpResponseMessage upstream;
                try
                {
                    var client = hf.CreateClient("cluster");
                    client.Timeout = Timeout.InfiniteTimeSpan; // agentbyggen tar minuter
                    var request = new HttpRequestMessage(HttpMethod.Post,
                        $"{endpoint.TrimEnd('/')}/api/assignment")
                    { Content = JsonContent.Create(req) };
                    var token = hosts.ClusterTokenFor(endpoint) ?? hosts.LiveOverseerToken;
                    if (!string.IsNullOrWhiteSpace(token))
                        request.Headers.TryAddWithoutValidation(ClusterSecurity.HeaderName, token);
                    upstream = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
                }
                catch
                {
                    continue; // denna Host onåbar - prova nästa kandidat
                }

                using var _ = upstream;
                if (!upstream.IsSuccessStatusCode)
                {
                    var body = await upstream.Content.ReadAsStringAsync(ct);
                    return Results.Content(body, upstream.Content.Headers.ContentType?.ToString() ?? "application/json",
                        statusCode: (int)upstream.StatusCode);
                }

                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.ContentType = "text/event-stream";
                await using var upstreamStream = await upstream.Content.ReadAsStreamAsync(ct);
                await upstreamStream.CopyToAsync(ctx.Response.Body, ct);
                return Results.Empty;
            }

            return Results.Problem("ingen Host gick att nå för assignmenten", statusCode: StatusCodes.Status502BadGateway);
        });

        app.MapPost("/api/tasks", (SubmitTaskRequest req, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Post, "/tasks", req, ct));

        app.MapGet("/api/providers", (HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Get, "/providers", null, ct));
        app.MapPut("/api/providers", (ProviderOrderUpdate req, HostLocator locator, HostRegistry hosts,
            IHttpClientFactory hf, CancellationToken ct) =>
            ProxyPrimary(locator, hosts, hf, HttpMethod.Put, "/providers", req, ct));
    }

    private static async Task<IResult> AggregateNodes(
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var payloads = await FetchAll(locator, hosts, httpFactory, "/api/nodes", ct);
        var nodes = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var payload in payloads)
        {
            if (payload.Json is not JsonArray array)
                continue;

            foreach (var node in array.OfType<JsonObject>())
            {
                var id = StringProperty(node, "id");
                if (id is null || !seen.Add(id))
                    continue;
                hosts.MapWorker(id, payload.Endpoint);
                nodes.Add(node.DeepClone());
            }
        }

        return Results.Json(nodes);
    }

    private static async Task<IResult> AggregateTopology(
        NodeSettings settings,
        PersistentSettingsStore persistentSettings,
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var payloads = await FetchAll(locator, hosts, httpFactory, "/api/topology", ct);
        var nodes = new JsonArray();
        var edges = new JsonArray();
        var overseerId = $"overseer-{persistentSettings.NodeId}";
        var seenNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { overseerId };
        var seenHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        nodes.Add(new JsonObject
        {
            ["id"] = overseerId,
            ["name"] = settings.NodeName,
            ["role"] = "Overseer",
            ["status"] = "Online",
            ["endpoint"] = $"http://127.0.0.1:{settings.Port}",
            ["activeTasks"] = 0,
            ["skills"] = new JsonArray()
        });

        foreach (var payload in payloads)
        {
            if (payload.Json is not JsonObject topology ||
                topology["nodes"] is not JsonArray sourceNodes)
                continue;

            JsonObject? hostNode = null;
            foreach (var sourceNode in sourceNodes.OfType<JsonObject>())
            {
                var role = StringProperty(sourceNode, "role");
                if (role == "Overseer")
                    continue;

                var id = StringProperty(sourceNode, "id");
                if (id is null)
                    continue;

                var copy = (JsonObject)sourceNode.DeepClone();
                if (role == "Host")
                {
                    hosts.UpdateIdentity(
                        payload.Endpoint,
                        id.StartsWith("host-", StringComparison.OrdinalIgnoreCase) ? id[5..] : id,
                        StringProperty(copy, "name") ?? payload.Endpoint);
                }

                if (!seenNodes.Add(id))
                    continue;

                if (role == "Host")
                {
                    copy["endpoint"] = payload.Endpoint;
                    copy["status"] = "Online";
                    hostNode = copy;
                    seenHosts.Add(payload.Endpoint);
                }
                else if (role == "Worker")
                {
                    hosts.MapWorker(id, payload.Endpoint);
                }

                nodes.Add(copy);
            }

            if (hostNode is not null)
            {
                var hostId = StringProperty(hostNode, "id")!;
                edges.Add(new JsonObject { ["source"] = overseerId, ["target"] = hostId });

                if (topology["edges"] is JsonArray sourceEdges)
                {
                    foreach (var edge in sourceEdges.OfType<JsonObject>())
                    {
                        var source = StringProperty(edge, "source");
                        if (source == "overseer-local")
                            continue;
                        edges.Add(edge.DeepClone());
                    }
                }
            }
        }

        foreach (var host in hosts.All.Where(host => !seenHosts.Contains(host.Endpoint)))
        {
            var hostId = host.Id.StartsWith("host-", StringComparison.OrdinalIgnoreCase)
                ? host.Id
                : $"host-{host.Id}";
            if (!seenNodes.Add(hostId))
                continue;

            nodes.Add(new JsonObject
            {
                ["id"] = hostId,
                ["name"] = host.Name,
                ["role"] = "Host",
                ["status"] = "Offline",
                ["endpoint"] = host.Endpoint,
                ["activeTasks"] = 0,
                ["skills"] = new JsonArray()
            });
            edges.Add(new JsonObject { ["source"] = overseerId, ["target"] = hostId });
        }

        return Results.Json(new JsonObject
        {
            ["nodes"] = nodes,
            ["edges"] = edges
        });
    }

    private static async Task<IResult> AggregateArrays(
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        string path,
        bool mapWorkers,
        CancellationToken ct)
    {
        var payloads = await FetchAll(locator, hosts, httpFactory, path, ct);
        var result = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var payload in payloads)
        {
            if (payload.Json is not JsonArray array)
                continue;

            foreach (var item in array.OfType<JsonObject>())
            {
                var id = StringProperty(item, "id");
                if (id is not null && !seen.Add(id))
                    continue;
                if (mapWorkers && id is not null)
                    hosts.MapWorker(id, payload.Endpoint);
                result.Add(item.DeepClone());
            }
        }

        return Results.Json(result);
    }

    private static async Task<IResult> ProxyWorker(
        string workerId,
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct)
    {
        var endpoint = hosts.HostForWorker(workerId);
        if (endpoint is null)
        {
            await RefreshWorkerIndex(locator, hosts, httpFactory, ct);
            endpoint = hosts.HostForWorker(workerId);
        }

        if (endpoint is null)
            return Results.NotFound(new { error = "worker is not mapped to a known host" });
        return await ProxyEndpoint(endpoint, hosts, httpFactory, method, path, body, ct);
    }

    private static async Task RefreshWorkerIndex(
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        var payloads = await FetchAll(locator, hosts, httpFactory, "/api/nodes", ct);
        foreach (var payload in payloads)
        {
            if (payload.Json is not JsonArray nodes)
                continue;
            foreach (var node in nodes.OfType<JsonObject>())
            {
                if (StringProperty(node, "id") is { } id)
                    hosts.MapWorker(id, payload.Endpoint);
            }
        }
    }

    /// <summary>Candidate Host endpoints, best first. locator.HostEndpoint
    /// only ever gets set ONCE - the first Host beacon this Overseer ever saw
    /// (see ClusterHostedService.OnBeacon) - and never updates again, so
    /// betting solely on it can permanently stick to a Host that's since gone
    /// away or been reconfigured. hosts.All is ordered most-recently-seen
    /// first and self-updates on every beacon, so it's a much better signal
    /// for "which Host is actually alive right now" - try it first, and only
    /// fall back to the sticky locator endpoint (useful for an explicitly-
    /// configured, cross-subnet Host that LAN discovery would never see).</summary>
    private static List<string> PrimaryCandidates(HostLocator locator, HostRegistry hosts) =>
        hosts.All
            .Select(h => h.Endpoint)
            .Append(locator.HostEndpoint ?? "")
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private static async Task<IResult> ProxyPrimary(
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        var candidates = PrimaryCandidates(locator, hosts);

        if (candidates.Count == 0)
            return Results.Problem("host not yet discovered");

        IResult? lastResult = null;
        foreach (var endpoint in candidates)
        {
            var (reached, result) = await TryProxyEndpoint(endpoint, hosts, httpFactory, method, path, body, ct, timeout);
            if (reached)
                return result;
            lastResult = result;
        }

        return lastResult!;
    }

    private static async Task<IResult> ProxyEndpoint(
        string endpoint,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct) =>
        (await TryProxyEndpoint(endpoint, hosts, httpFactory, method, path, body, ct)).Result;

    /// <summary>Reached=true means the target answered (even a non-2xx status
    /// is a real answer worth relaying to the caller); Reached=false means the
    /// connection itself failed, so a multi-candidate caller should try the
    /// next endpoint instead of giving up.</summary>
    private static async Task<(bool Reached, IResult Result)> TryProxyEndpoint(
        string endpoint,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        HttpMethod method,
        string path,
        object? body,
        CancellationToken ct,
        TimeSpan? timeout = null)
    {
        try
        {
            var client = httpFactory.CreateClient("cluster");
            // 15 s racker for statuskoll och installningar, men INTE for
            // anrop som vantar pa en AI-modell (goal-plan tog >15 s och dog
            // med "HttpClient.Timeout of 15 seconds elapsing") - de anropen
            // skickar sin egen langre timeout.
            client.Timeout = timeout ?? TimeSpan.FromSeconds(15);
            using var request = new HttpRequestMessage(method, $"{endpoint.TrimEnd('/')}{path}");
            // Present the *target Host's* own cluster token, not the
            // Overseer's - each node mints its own and rejects others with
            // 401. Fall back to the Overseer's token only when we have no
            // recorded token for this Host (e.g. a cross-subnet Host added by
            // explicit endpoint that never announced).
            var token = hosts.ClusterTokenFor(endpoint) ?? hosts.LiveOverseerToken;
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation(ClusterSecurity.HeaderName, token);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            using var response = await client.SendAsync(request, ct);
            var content = await response.Content.ReadAsStringAsync(ct);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
            return (true, Results.Content(content, contentType, statusCode: (int)response.StatusCode));
        }
        catch (Exception ex)
        {
            return (false, Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway));
        }
    }

    private static async Task<IReadOnlyList<HostPayload>> FetchAll(
        HostLocator locator,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        string path,
        CancellationToken ct)
    {
        var endpoints = hosts.All
            .Select(host => host.Endpoint)
            .Append(locator.HostEndpoint ?? "")
            .Where(endpoint => !string.IsNullOrWhiteSpace(endpoint))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var tasks = endpoints.Select(endpoint =>
            Fetch(endpoint, hosts, httpFactory, path, ct));
        return await Task.WhenAll(tasks);
    }

    private static async Task<HostPayload> Fetch(
        string endpoint,
        HostRegistry hosts,
        IHttpClientFactory httpFactory,
        string path,
        CancellationToken ct)
    {
        try
        {
            var client = httpFactory.CreateClient("cluster");
            client.Timeout = TimeSpan.FromSeconds(4);
            var token = hosts.ClusterTokenFor(endpoint) ?? hosts.LiveOverseerToken;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint.TrimEnd('/')}{path}");
            if (!string.IsNullOrWhiteSpace(token))
                request.Headers.TryAddWithoutValidation(ClusterSecurity.HeaderName, token);
            using var response = await client.SendAsync(request, ct);
            var json = await response.Content.ReadAsStringAsync(ct);
            return new HostPayload(endpoint.TrimEnd('/'), JsonNode.Parse(json));
        }
        catch
        {
            return new HostPayload(endpoint.TrimEnd('/'), null);
        }
    }

    private static string? StringProperty(JsonObject value, string property) =>
        value[property]?.GetValue<string>();

    private static string Esc(string value) => Uri.EscapeDataString(value);
}
