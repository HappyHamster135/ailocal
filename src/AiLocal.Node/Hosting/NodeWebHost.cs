using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Hardware;
using AiLocal.Core.Providers;
using AiLocal.Core.Roles;
using AiLocal.Node.Roles;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;

namespace AiLocal.Node.Hosting;

public sealed class RunningNodeApp : IAsyncDisposable
{
    internal RunningNodeApp(
        WebApplication app,
        NodeSettings settings,
        HardwareProfile hardware,
        LocalModelRecommendation recommendation)
    {
        App = app;
        Settings = settings;
        Hardware = hardware;
        Recommendation = recommendation;
    }

    public WebApplication App { get; }
    public NodeSettings Settings { get; }
    public HardwareProfile Hardware { get; }
    public LocalModelRecommendation Recommendation { get; }
    public string LocalEndpoint => $"http://127.0.0.1:{Settings.Port}";

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }

    public void WriteBanner()
    {
        Console.WriteLine();
        Console.WriteLine($"  AiLocal  |  role={Settings.Role}  name={Settings.NodeName}  port={Settings.Port}");
        Console.WriteLine("  Hardware: " + Hardware.Cpu + $", {Hardware.LogicalCores} cores, {Hardware.SystemMemoryGb} GB RAM"
            + (Hardware.Gpu is not null ? $", GPU {Hardware.Gpu} ({Hardware.GpuMemoryGb} GB)" : ", no NVIDIA GPU"));
        Console.WriteLine($"  Recommended local model: {Recommendation.DisplayName}  ->  ollama pull {Recommendation.OllamaTag}");
        Console.WriteLine($"  Provider chain: {string.Join(" -> ", Settings.Providers.Priority)}");
        Console.WriteLine();
    }
}

public static class NodeWebHost
{
    public static async Task<int> RunAsync(string[] args, CancellationToken ct = default)
    {
        var node = await BuildAsync(args);
        if (node is null) return 0;

        node.WriteBanner();
        await node.App.RunAsync(ct);
        return 0;
    }

    public static async Task<RunningNodeApp?> StartAsync(string[] args, CancellationToken ct = default)
    {
        var node = await BuildAsync(args);
        if (node is null) return null;

        await node.App.StartAsync(ct);
        node.WriteBanner();
        return node;
    }

    private static async Task<RunningNodeApp?> BuildAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        var settings = new NodeSettings();
        builder.Configuration.GetSection("Node").Bind(settings);

        if (RoleResolver.WantsHelp(args))
        {
            RoleResolver.PrintHelp();
            return null;
        }

        settings.Role = RoleResolver.Resolve(args, settings.Role);
        PersistentSettingsStore.LoadInto(settings);
        settings.Providers.Priority = settings.Providers.Priority.Distinct().ToList();

        if (settings.Port == 0)
            settings.Port = LocalNodeLauncher.DefaultPort(settings.Role);

        RoleResolver.ApplyOverrides(args, settings);

        // Host/Worker/Overseer are routinely launched with no console window
        // (LocalNodeLauncher.StartCoreAsync's CreateNoWindow, AutoStartManager's
        // login launch) - without this, CrashLog's unhandled-exceptions-only
        // record was the ONLY diagnostic trail a headless process ever left
        // behind. Every debugging session this app has needed so far required
        // live-attaching to a running process's own console output instead.
        builder.Logging.AddProvider(new FileLoggerProvider(
            Path.Combine(SettingsPaths.DataDirectory, "logs"),
            settings.Role.ToString().ToLowerInvariant(),
            LogLevel.Information));

        // Plain HTTP always listens (loopback dashboard access never sees a
        // certificate warning). An additive HTTPS listener with a self-signed,
        // auto-generated certificate is used for node-to-node cluster traffic
        // when enabled - see TlsSettings for what this does and does not buy.
        //
        // The desktop app's Launcher role binds an OS-assigned ephemeral port
        // (see AiLocal.App/Program.cs GetFreeTcpPort), which can land anywhere
        // up to 65535 - adding PortOffset can then overflow the valid TCP port
        // range (0-65535) and crash Kestrel at startup. HttpsPortFor returns
        // null in that case; skip the HTTPS listener rather than crash - the
        // plain-HTTP listener still always works.
        var httpsPort = settings.Tls.HttpsPortFor(settings.Port);
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.ListenAnyIP(settings.Port);
            if (httpsPort is { } port)
            {
                var cert = TlsCertificateManager.GetOrCreate(settings.NodeName);
                kestrel.ListenAnyIP(port, listen => listen.UseHttps(cert));
            }
        });

        var hardware = await HardwareInspector.InspectAsync();
        var recommendation = ModelRecommender.Recommend(hardware);
        builder.Services.AddSingleton(hardware);
        builder.Services.AddSingleton(recommendation);
        builder.Services.AddDataProtection()
            .SetApplicationName("AiLocal")
            .PersistKeysToFileSystem(new DirectoryInfo(SettingsPaths.DataDirectory));
        builder.Services.AddSingleton<PersistentSettingsStore>();

        NodeComposition.AddSharedServices(builder.Services, settings);
        builder.Services.AddSingleton<LocalNodeLauncher>();
        builder.Services.AddHostedService<BrowserOpenHostedService>();
        if (settings.ParentProcessId.HasValue)
            builder.Services.AddHostedService<ParentProcessMonitorHostedService>();
        if (settings.Role != NodeRole.Launcher)
            builder.Services.AddHostedService<ClusterHostedService>();

        switch (settings.Role)
        {
            case NodeRole.Host: HostRole.ConfigureServices(builder.Services); break;
            case NodeRole.Worker: WorkerRole.ConfigureServices(builder.Services); break;
            case NodeRole.Overseer: OverseerRole.ConfigureServices(builder.Services); break;
            case NodeRole.Launcher: LauncherRole.ConfigureServices(builder.Services); break;
        }

        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var store = context.RequestServices.GetRequiredService<PersistentSettingsStore>();
            await ClusterSecurity.Authorize(context, store, next);
        });
        MapSharedEndpoints(app, settings, hardware, recommendation);

        switch (settings.Role)
        {
            case NodeRole.Host: HostRole.MapEndpoints(app); break;
            case NodeRole.Worker: WorkerRole.MapEndpoints(app); break;
            case NodeRole.Overseer: OverseerRole.MapEndpoints(app); break;
            case NodeRole.Launcher: LauncherRole.MapEndpoints(app); break;
        }

        return new RunningNodeApp(app, settings, hardware, recommendation);
    }

    private static void MapSharedEndpoints(
        WebApplication app,
        NodeSettings settings,
        HardwareProfile hardware,
        LocalModelRecommendation recommendation)
    {
        app.MapGet("/health", () => Results.Ok(new
        {
            role = settings.Role.ToString(),
            name = settings.NodeName,
            status = "ok"
        }));

        app.MapGet("/node/status", (FallbackChatProvider providers) => Results.Ok(new
        {
            role = settings.Role.ToString(),
            name = settings.NodeName,
            port = settings.Port,
            hardware,
            recommendedModel = recommendation,
            providerChain = providers.ProviderNames
        }));

        app.MapGet("/providers", (NodeSettings s) => Results.Ok(ProviderOrderApi.Read(s)));
        app.MapPut("/providers", (ProviderOrderUpdate req, PersistentSettingsStore store, HostLocator locator,
                [FromServices] WorkerRegistry? registry, IHttpClientFactory hf, ILoggerFactory lf) =>
            UpdateSettings(store, locator, new SettingsUpdate(ProviderPriority: req.Priority), registry, hf, lf));

        if (settings.Role != NodeRole.Overseer)
        {
            app.MapGet("/api/providers", (NodeSettings s) => Results.Ok(ProviderOrderApi.Read(s)));
            app.MapPut("/api/providers", (ProviderOrderUpdate req, PersistentSettingsStore store, HostLocator locator,
                    [FromServices] WorkerRegistry? registry, IHttpClientFactory hf, ILoggerFactory lf) =>
                UpdateSettings(store, locator, new SettingsUpdate(ProviderPriority: req.Priority), registry, hf, lf));
        }

        app.MapGet("/api/settings", (PersistentSettingsStore store, HttpContext ctx) =>
            Results.Ok(store.Read(includeSecrets: ClusterSecurity.IsAdminTier(ctx))));
        app.MapPut("/api/settings", (SettingsUpdate req, PersistentSettingsStore store, HostLocator locator,
                [FromServices] WorkerRegistry? registry, IHttpClientFactory hf, ILoggerFactory lf) =>
            UpdateSettings(store, locator, req, registry, hf, lf));

        app.MapGet("/api/local", (NodeSettings s, RegistrationStatus registrationStatus, HostLocator hostLocator) =>
        {
            var (state, detail) = registrationStatus.Read();
            return Results.Ok(new
            {
                role = s.Role.ToString(),
                name = s.NodeName,
                port = s.Port,
                endpoint = $"http://127.0.0.1:{s.Port}",
                isLauncher = s.Role == NodeRole.Launcher,
                // Only a Worker actually runs the register-loop that keeps this
                // meaningful (see ClusterHostedService.RegisterLoopAsync) - null
                // for every other role, same as hostEndpoint below.
                pairing = new { state = state.ToString(), detail },
                hostEndpoint = s.Role == NodeRole.Worker ? hostLocator.HostEndpoint : null
            });
        });

        app.MapPost("/api/launch", async (LaunchNodeRequest req, LocalNodeLauncher launcher, CancellationToken ct) =>
            Results.Ok(await launcher.StartAsync(req, ct)));

        // Surfaces roles started from THIS page (typically the Launcher,
        // since starting a Worker deliberately doesn't navigate away from it)
        // with a pending-pairing-request count fetched over loopback, so a
        // click-to-pair request on a Worker the operator can't see (because
        // they're still looking at the Launcher screen) doesn't go unnoticed.
        app.MapGet("/api/local-nodes", async (
            LocalNodeLauncher launcher, NodeSettings settings, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var client = httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMilliseconds(500);

            var candidates = new Dictionary<NodeRole, LocalNodeRecord>();
            foreach (var record in launcher.KnownLocalNodes)
                candidates[record.Role] = record;

            // KnownLocalNodes only remembers roles THIS process itself started
            // or reused via /api/launch - a sibling role started independently
            // (a separate tray action, or relaunched after this process itself
            // restarted, e.g. for an update) is invisible to it, which meant
            // its pending click-to-pair requests silently never surfaced here.
            // Probe the well-known default ports directly, in parallel, so an
            // already-running sibling is found either way.
            var missingRoles = new[] { NodeRole.Host, NodeRole.Worker, NodeRole.Overseer }
                .Where(role => role != settings.Role && !candidates.ContainsKey(role));
            var probed = await Task.WhenAll(missingRoles.Select(async role =>
            {
                var port = LocalNodeLauncher.DefaultPort(role);
                var endpoint = $"http://127.0.0.1:{port}";
                try
                {
                    using var health = await client.GetAsync($"{endpoint}/health", ct);
                    if (!health.IsSuccessStatusCode) return (LocalNodeRecord?)null;
                    using var stream = await health.Content.ReadAsStreamAsync(ct);
                    using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                    if (doc.RootElement.TryGetProperty("role", out var roleProp) &&
                        string.Equals(roleProp.GetString(), role.ToString(), StringComparison.OrdinalIgnoreCase))
                        return (LocalNodeRecord?)new LocalNodeRecord(role, port, endpoint);
                }
                catch { /* nothing listening on that default port */ }
                return (LocalNodeRecord?)null;
            }));
            foreach (var found in probed)
                if (found is { } record) candidates[record.Role] = record;

            var results = await Task.WhenAll(candidates.Values.OrderBy(n => n.Role).Select(async node =>
            {
                var pendingCount = 0;
                try
                {
                    using var response = await client.GetAsync($"{node.Endpoint}/pairing/pending", ct);
                    if (response.IsSuccessStatusCode)
                    {
                        using var stream = await response.Content.ReadAsStreamAsync(ct);
                        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
                        pendingCount = document.RootElement.ValueKind == JsonValueKind.Array
                            ? document.RootElement.GetArrayLength()
                            : 0;
                    }
                }
                catch { /* that node may not be a Worker, or isn't up right now */ }

                return (object)new { role = node.Role.ToString(), port = node.Port, endpoint = node.Endpoint, pendingPairingRequests = pendingCount };
            }));

            return Results.Ok(results);
        });

        app.MapGet("/api/version", () => Results.Ok(new { version = CurrentVersion }));

        app.MapGet("/api/update-check", async (IHttpClientFactory hf, CancellationToken ct) =>
            Results.Ok(await SelfUpdater.CheckAsync(hf, ct)));

        // Explicit, operator-clicked only (see the button in Settings) - this
        // never runs on its own. Downloads the matching exe from this repo's
        // latest GitHub release, hands off to an external script to swap it
        // in (this process can't overwrite the file it's running from), and
        // restarts. SelfUpdater responds before this process's own port goes
        // away, so the caller still gets an HTTP response.
        app.MapPost("/api/update-apply", async (IHttpClientFactory hf, IHostApplicationLifetime lifetime, CancellationToken ct) =>
        {
            var result = await SelfUpdater.ApplyAsync(hf, lifetime, app.Logger, ct);
            return result.Started
                ? Results.Ok(new { restarting = true })
                : Results.Problem(detail: result.Error ?? "Uppdateringen misslyckades.", statusCode: StatusCodes.Status502BadGateway);
        });

        // Sessions are local-only (see SessionStore) and available on every
        // role, not just Host/Worker - same tier as everything else mapped
        // in this method, which runs unconditionally before the per-role switch.
        SessionApi.MapEndpoints(app);
        DialogsApi.MapEndpoints(app);
    }

    private static string CurrentVersion => SelfUpdater.CurrentVersion;

    private static async Task<IResult> UpdateSettings(
        PersistentSettingsStore store,
        HostLocator locator,
        SettingsUpdate update,
        WorkerRegistry? registry,
        IHttpClientFactory httpFactory,
        ILoggerFactory loggerFactory)
    {
        try
        {
            var oldToken = store.GetClusterToken();
            var result = store.Update(update, locator);
            var newToken = store.GetClusterToken();

            // Rotating the token (manually here, or the pairing self-heal in
            // HostRole) otherwise silently orphans every already-registered
            // Worker - they keep using the old token until an operator
            // notices something broke and manually re-enters it everywhere.
            if (registry is not null && !string.IsNullOrWhiteSpace(oldToken) &&
                !string.IsNullOrWhiteSpace(newToken) && oldToken != newToken)
            {
                await PropagateTokenToKnownWorkersAsync(
                    oldToken, newToken, registry, httpFactory, loggerFactory.CreateLogger("token-rotation"));
            }

            return Results.Ok(result);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }

    /// <summary>
    /// Best-effort push of a freshly-rotated cluster token to every currently
    /// -known Worker, authenticating with the OLD token (the only one they
    /// still recognize at this point - they haven't heard about the new one
    /// yet). Internal so HostRole's pairing self-heal can reuse it too.
    /// </summary>
    internal static async Task PropagateTokenToKnownWorkersAsync(
        string? oldToken,
        string newToken,
        WorkerRegistry registry,
        IHttpClientFactory httpFactory,
        ILogger log)
    {
        foreach (var worker in registry.All)
        {
            try
            {
                // Deliberately NOT the "cluster" named client: it has a
                // DelegatingHandler that auto-attaches this Host's CURRENT
                // (already-rotated) token to every request, which would
                // stomp the OLD token this call needs to authenticate with.
                // That client also prefers each worker's TLS endpoint, whose
                // self-signed cert only the "cluster" client is configured to
                // trust - so this uses the plain endpoint on the default client.
                var client = httpFactory.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(5);
                using var request = new HttpRequestMessage(HttpMethod.Put, $"{worker.Endpoint}/api/settings")
                {
                    Content = JsonContent.Create(new SettingsUpdate(ClusterToken: newToken))
                };
                // No old token to present (the Host had none, e.g. the
                // pairing self-heal case) means the Worker itself is very
                // likely also token-less right now, in which case it accepts
                // any call unauthenticated - same "fail open" rule this Host
                // was just running under. If the Worker DOES have a token
                // already, this push simply fails and it stays stale, same as
                // before this method existed.
                if (!string.IsNullOrWhiteSpace(oldToken))
                    request.Headers.TryAddWithoutValidation(ClusterSecurity.HeaderName, oldToken);
                using var response = await client.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                    log.LogWarning("failed to push rotated cluster token to {Worker}: HTTP {Status}",
                        worker.Name, (int)response.StatusCode);
            }
            catch (Exception ex)
            {
                log.LogWarning("failed to push rotated cluster token to {Worker}: {Message}", worker.Name, ex.Message);
            }
        }
    }
}
