using AiLocal.Core.Configuration;
using AiLocal.Core.Hardware;
using AiLocal.Core.Providers;
using AiLocal.Core.Roles;
using AiLocal.Node.Roles;
using Microsoft.AspNetCore.DataProtection;

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
        app.MapPut("/providers", (ProviderOrderUpdate req, PersistentSettingsStore store, HostLocator locator) =>
            UpdateSettings(store, locator, new SettingsUpdate(ProviderPriority: req.Priority)));

        if (settings.Role != NodeRole.Overseer)
        {
            app.MapGet("/api/providers", (NodeSettings s) => Results.Ok(ProviderOrderApi.Read(s)));
            app.MapPut("/api/providers", (ProviderOrderUpdate req, PersistentSettingsStore store, HostLocator locator) =>
                UpdateSettings(store, locator, new SettingsUpdate(ProviderPriority: req.Priority)));
        }

        app.MapGet("/api/settings", (PersistentSettingsStore store) => Results.Ok(store.Read()));
        app.MapPut("/api/settings", (SettingsUpdate req, PersistentSettingsStore store, HostLocator locator) =>
            UpdateSettings(store, locator, req));

        app.MapGet("/api/local", (NodeSettings s, RegistrationStatus registrationStatus) =>
        {
            var (state, detail) = registrationStatus.Read();
            return Results.Ok(new
            {
                role = s.Role.ToString(),
                name = s.NodeName,
                port = s.Port,
                endpoint = $"http://127.0.0.1:{s.Port}",
                isLauncher = s.Role == NodeRole.Launcher,
                pairing = new { state = state.ToString(), detail }
            });
        });

        app.MapPost("/api/launch", async (LaunchNodeRequest req, LocalNodeLauncher launcher, CancellationToken ct) =>
            Results.Ok(await launcher.StartAsync(req, ct)));

        app.MapGet("/api/version", () => Results.Ok(new { version = CurrentVersion }));

        // No real update server exists yet - this is the mechanism only. Set
        // Node:Host:UpdateManifestUrl to a hosted JSON file shaped like
        // {"version":"1.2.0","url":"https://.../ailocal.exe","notes":"..."}
        // to enable it.
        app.MapGet("/api/update-check", async (NodeSettings s, IHttpClientFactory hf, CancellationToken ct) =>
        {
            var manifestUrl = s.Host.UpdateManifestUrl;
            if (string.IsNullOrWhiteSpace(manifestUrl))
                return Results.Ok(new { enabled = false, currentVersion = CurrentVersion });

            try
            {
                var client = hf.CreateClient();
                client.Timeout = TimeSpan.FromSeconds(8);
                var manifest = await client.GetFromJsonAsync<UpdateManifest>(manifestUrl, ct);
                var updateAvailable = manifest is not null &&
                    Version.TryParse(manifest.Version, out var latest) &&
                    Version.TryParse(CurrentVersion, out var current) &&
                    latest > current;

                return Results.Ok(new
                {
                    enabled = true,
                    currentVersion = CurrentVersion,
                    latestVersion = manifest?.Version,
                    updateAvailable,
                    downloadUrl = manifest?.Url,
                    notes = manifest?.Notes
                });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { enabled = true, currentVersion = CurrentVersion, error = ex.Message });
            }
        });
    }

    private static string CurrentVersion =>
        typeof(NodeWebHost).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private static IResult UpdateSettings(
        PersistentSettingsStore store,
        HostLocator locator,
        SettingsUpdate update)
    {
        try
        {
            return Results.Ok(store.Update(update, locator));
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }
    }
}

internal sealed record UpdateManifest(string Version, string? Url, string? Notes);
