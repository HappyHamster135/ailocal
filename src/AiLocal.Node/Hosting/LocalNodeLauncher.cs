using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

public sealed record LaunchNodeRequest(
    NodeRole Role,
    int? Port = null,
    string? HostEndpoint = null,
    string? NodeName = null,
    string? ClusterToken = null);

public sealed record LaunchNodeResponse(
    bool Started,
    NodeRole Role,
    int Port,
    string Endpoint,
    int? ProcessId,
    string? Error,
    bool Reused = false);

public sealed record LocalNodeRecord(NodeRole Role, int Port, string Endpoint);

public sealed class LocalNodeLauncher
{
    private readonly NodeSettings _settings;
    private readonly IHttpClientFactory _httpFactory;
    private readonly SemaphoreSlim _launchGate = new(1, 1);

    // Roles started (or found already running) from THIS node process's own
    // "start a role" buttons. Lets the page the operator is actually looking
    // at - typically the Launcher, since starting a Worker deliberately
    // doesn't navigate away from it - show what it spawned and surface things
    // like a pending click-to-pair request without the operator needing to
    // know a port number and navigate there themselves.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<NodeRole, LocalNodeRecord> _known = new();

    public LocalNodeLauncher(NodeSettings settings, IHttpClientFactory httpFactory)
    {
        _settings = settings;
        _httpFactory = httpFactory;
    }

    public IReadOnlyList<LocalNodeRecord> KnownLocalNodes => _known.Values.OrderBy(n => n.Role).ToList();

    public async Task<LaunchNodeResponse> StartAsync(
        LaunchNodeRequest request,
        CancellationToken ct = default)
    {
        await _launchGate.WaitAsync(ct);
        try
        {
            return await StartCoreAsync(request, ct);
        }
        finally
        {
            _launchGate.Release();
        }
    }

    private async Task<LaunchNodeResponse> StartCoreAsync(
        LaunchNodeRequest request,
        CancellationToken ct)
    {
        if (request.Role == NodeRole.Launcher)
            return new LaunchNodeResponse(false, request.Role, _settings.Port, "", null, "Launcher cannot launch Launcher.");

        // Idempotent: reuse an already-running instance of this role instead
        // of spawning a duplicate. Clicking the same role button twice used
        // to silently start a second process on the next free port, sharing
        // the same persisted node identity but broadcasting from a different
        // port - confusing on its own, and it breaks click-to-pair (whoever
        // is trying to reach "the Worker" might hit either process).
        if (request.Port is not > 0 &&
            await FindRunningInstanceAsync(request.Role, ct) is { } running)
        {
            _known[request.Role] = new LocalNodeRecord(request.Role, running.Port, running.Endpoint);
            return new LaunchNodeResponse(true, request.Role, running.Port, running.Endpoint, null, null, Reused: true);
        }

        var port = request.Port is > 0 ? request.Port.Value : DefaultPort(request.Role);
        if (!IsPortAvailable(port))
        {
            if (request.Port is > 0)
                return new LaunchNodeResponse(false, request.Role, port, "", null, $"Port {port} is already in use.");

            port = FindAvailablePort(port);
            if (port == 0)
                return new LaunchNodeResponse(false, request.Role, 0, "", null, "No available local port was found.");
        }

        var endpoint = $"http://127.0.0.1:{port}";
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe))
            return new LaunchNodeResponse(false, request.Role, port, endpoint, null, "Could not resolve current executable path.");

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--role");
        psi.ArgumentList.Add(request.Role.ToString());
        psi.ArgumentList.Add("--port");
        psi.ArgumentList.Add(port.ToString());
        psi.ArgumentList.Add("--no-browser");
        psi.ArgumentList.Add("--parent-pid");
        psi.ArgumentList.Add(Environment.ProcessId.ToString());

        var name = string.IsNullOrWhiteSpace(request.NodeName) ? Environment.MachineName : request.NodeName;
        psi.ArgumentList.Add("--name");
        psi.ArgumentList.Add(name);

        if (!string.IsNullOrWhiteSpace(request.HostEndpoint) &&
            request.Role is NodeRole.Worker or NodeRole.Overseer)
        {
            psi.ArgumentList.Add("--host");
            psi.ArgumentList.Add(request.HostEndpoint);
        }

        if (!string.IsNullOrWhiteSpace(request.ClusterToken))
        {
            psi.ArgumentList.Add("--cluster-token");
            psi.ArgumentList.Add(request.ClusterToken);
        }

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new LaunchNodeResponse(false, request.Role, port, endpoint, null, "Could not start node process.");

            var ready = await WaitUntilReadyAsync(process, endpoint, request.Role, ct);
            if (!ready)
            {
                var error = process.HasExited
                    ? $"Node process exited with code {process.ExitCode}."
                    : "Node did not become ready within 8 seconds.";
                return new LaunchNodeResponse(false, request.Role, port, endpoint, process.Id, error);
            }

            _known[request.Role] = new LocalNodeRecord(request.Role, port, endpoint);
            return new LaunchNodeResponse(true, request.Role, port, endpoint, process.Id, null);
        }
        catch (Exception ex)
        {
            return new LaunchNodeResponse(false, request.Role, port, endpoint, null, ex.Message);
        }
    }

    /// <summary>
    /// Checks whether this role is already running on its default port and
    /// actually answering as that role - not just that something occupies the
    /// port. Only checks the default port: a role that already got bumped to
    /// a non-default port (an existing duplicate from before this guard
    /// existed) won't be found here, but no new duplicate will be created on
    /// top of it either since the default port is what a fresh launch would try.
    /// </summary>
    private async Task<(int Port, string Endpoint)?> FindRunningInstanceAsync(NodeRole role, CancellationToken ct)
    {
        var port = DefaultPort(role);
        var endpoint = $"http://127.0.0.1:{port}";
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMilliseconds(500);
            using var response = await client.GetAsync($"{endpoint}/health", ct);
            if (!response.IsSuccessStatusCode)
                return null;

            var body = await response.Content.ReadAsStringAsync(ct);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("role", out var roleProperty) &&
                string.Equals(roleProperty.GetString(), role.ToString(), StringComparison.OrdinalIgnoreCase))
                return (port, endpoint);
        }
        catch { /* nothing there, or it's not a health endpoint - fall through to a normal launch */ }

        return null;
    }

    private async Task<bool> WaitUntilReadyAsync(
        Process process,
        string endpoint,
        NodeRole expectedRole,
        CancellationToken ct)
    {
        var client = _httpFactory.CreateClient();
        client.Timeout = TimeSpan.FromMilliseconds(500);
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(8);

        while (DateTimeOffset.UtcNow < deadline && !ct.IsCancellationRequested)
        {
            if (process.HasExited)
                return false;

            try
            {
                using var response = await client.GetAsync($"{endpoint}/health", ct);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    using var document = JsonDocument.Parse(body);
                    if (document.RootElement.TryGetProperty("role", out var role) &&
                        string.Equals(role.GetString(), expectedRole.ToString(), StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (HttpRequestException)
            {
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
            }

            await Task.Delay(150, ct);
        }

        return false;
    }

    private static int FindAvailablePort(int start)
    {
        for (var port = start; port <= Math.Min(65535, start + 100); port++)
        {
            if (IsPortAvailable(port))
                return port;
        }

        return 0;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            if (IPGlobalProperties.GetIPGlobalProperties()
                .GetActiveTcpListeners()
                .Any(endpoint => endpoint.Port == port))
                return false;

            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    public static int DefaultPort(NodeRole role) => role switch
    {
        NodeRole.Launcher => 5088,
        NodeRole.Host => 5080,
        NodeRole.Worker => 5081,
        NodeRole.Overseer => 5082,
        _ => 5088
    };
}

public sealed class BrowserOpenHostedService : BackgroundService
{
    private readonly NodeSettings _settings;
    private readonly ILogger<BrowserOpenHostedService> _log;

    public BrowserOpenHostedService(NodeSettings settings, ILogger<BrowserOpenHostedService> log)
    {
        _settings = settings;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Ui.OpenBrowser || _settings.Role == NodeRole.Worker)
            return;

        try
        {
            await Task.Delay(900, stoppingToken);
            var url = $"http://127.0.0.1:{_settings.Port}";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _log.LogDebug("could not open browser: {Message}", ex.Message);
        }
    }
}
