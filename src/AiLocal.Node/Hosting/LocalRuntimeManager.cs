using System.Diagnostics;
using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Hardware;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

public sealed record LocalRuntimeStatus(
    bool OllamaOnPath,
    bool OllamaEndpointReachable,
    string Endpoint,
    string RecommendedModel,
    string DisplayName,
    bool RecommendedModelInstalled,
    string PullCommand);

public sealed record LocalRuntimePullResult(bool Success, string Model, int ExitCode, string Output);

public sealed record LocalRuntimeSetupStep(string Step, bool Ok, string Detail);

public sealed record LocalRuntimeSetupResult(bool Success, string Model, IReadOnlyList<LocalRuntimeSetupStep> Steps);

public sealed class LocalRuntimeManager
{
    private readonly NodeSettings _settings;
    private readonly LocalModelRecommendation _recommendation;
    private readonly IHttpClientFactory _httpFactory;

    public LocalRuntimeManager(
        NodeSettings settings,
        LocalModelRecommendation recommendation,
        IHttpClientFactory httpFactory)
    {
        _settings = settings;
        _recommendation = recommendation;
        _httpFactory = httpFactory;
    }

    public string RecommendedModel => _settings.Providers.OllamaModel ?? _recommendation.OllamaTag;

    public async Task<LocalRuntimeStatus> InspectAsync(CancellationToken ct = default)
    {
        var onPath = await CommandExistsAsync("ollama", ct);
        var (endpointReachable, installed) = await InspectOllamaEndpointAsync(ct);
        var model = RecommendedModel;

        return new LocalRuntimeStatus(
            onPath,
            endpointReachable,
            _settings.Providers.OllamaEndpoint,
            model,
            _recommendation.DisplayName,
            installed,
            $"ollama pull {model}");
    }

    public async Task<LocalRuntimePullResult> PullRecommendedModelAsync(CancellationToken ct = default)
    {
        var model = RecommendedModel;
        var psi = new ProcessStartInfo
        {
            FileName = "ollama",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("pull");
        psi.ArgumentList.Add(model);

        try
        {
            using var process = Process.Start(psi);
            if (process is null)
                return new LocalRuntimePullResult(false, model, -1, "failed to start ollama");

            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct);

            var output = (await outputTask) + (await errorTask);
            return new LocalRuntimePullResult(process.ExitCode == 0, model, process.ExitCode, output);
        }
        catch (Exception ex)
        {
            return new LocalRuntimePullResult(false, model, -1, ex.Message);
        }
    }

    /// <summary>
    /// One click: install Ollama if missing, start the service, and pull the
    /// recommended model. Each step reports independently so the UI can show
    /// exactly how far setup got.
    /// </summary>
    public async Task<LocalRuntimeSetupResult> SetupLocalAiAsync(CancellationToken ct = default)
    {
        var steps = new List<LocalRuntimeSetupStep>();

        var install = await EnsureOllamaInstalledAsync(ct);
        steps.Add(install);
        if (!install.Ok)
            return new LocalRuntimeSetupResult(false, RecommendedModel, steps);

        var serve = await EnsureOllamaRunningAsync(ct);
        steps.Add(serve);
        if (!serve.Ok)
            return new LocalRuntimeSetupResult(false, RecommendedModel, steps);

        var pull = await PullRecommendedModelAsync(ct);
        steps.Add(new LocalRuntimeSetupStep(
            $"Pull {pull.Model}",
            pull.Success,
            pull.Success ? "Model installed" : Trunc(pull.Output)));

        return new LocalRuntimeSetupResult(pull.Success, pull.Model, steps);
    }

    private async Task<LocalRuntimeSetupStep> EnsureOllamaInstalledAsync(CancellationToken ct)
    {
        if (await CommandExistsAsync("ollama", ct))
            return new LocalRuntimeSetupStep("Install Ollama", true, "Already installed");

        if (OperatingSystem.IsWindows())
            return await EnsureOllamaInstalledWindowsAsync(ct);

        if (OperatingSystem.IsLinux())
            return await EnsureOllamaInstalledLinuxAsync(ct);

        if (OperatingSystem.IsMacOS())
            return await EnsureOllamaInstalledMacAsync(ct);

        return new LocalRuntimeSetupStep("Install Ollama", false,
            "Automatic install is not available on this platform. Install Ollama from https://ollama.com/download.");
    }

    private async Task<LocalRuntimeSetupStep> EnsureOllamaInstalledWindowsAsync(CancellationToken ct)
    {
        // Prefer winget (Ollama installs per-user, so no elevation prompt).
        if (await CommandExistsAsync("winget", ct))
        {
            var (code, _) = await RunAsync("winget",
                ["install", "--id", "Ollama.Ollama", "-e", "--silent",
                 "--accept-package-agreements", "--accept-source-agreements"],
                TimeSpan.FromMinutes(15), ct);
            if (code == 0 && await CommandExistsAsync("ollama", ct))
                return new LocalRuntimeSetupStep("Install Ollama", true, "Installed via winget");
            // otherwise fall through to the official installer
        }

        try
        {
            var installer = Path.Combine(Path.GetTempPath(), "OllamaSetup.exe");
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(15);
            using (var download = await client.GetAsync(
                "https://ollama.com/download/OllamaSetup.exe",
                HttpCompletionOption.ResponseHeadersRead, ct))
            {
                download.EnsureSuccessStatusCode();
                await using var fs = File.Create(installer);
                await download.Content.CopyToAsync(fs, ct);
            }

            var (code, output) = await RunAsync(installer, ["/VERYSILENT", "/NORESTART"], TimeSpan.FromMinutes(15), ct);
            if (await CommandExistsAsync("ollama", ct) || await IsEndpointReachableAsync(ct))
                return new LocalRuntimeSetupStep("Install Ollama", true, "Installed via official installer");

            return new LocalRuntimeSetupStep("Install Ollama", false,
                $"Installer exit {code}. {Trunc(output)}".Trim());
        }
        catch (Exception ex)
        {
            return new LocalRuntimeSetupStep("Install Ollama", false, ex.Message);
        }
    }

    /// <summary>
    /// Runs Ollama's official install script (https://ollama.com/install.sh), the
    /// same one-liner documented at https://ollama.com/download/linux. Requires
    /// sudo on most distros, so this only succeeds unattended when the Worker
    /// process already has root or passwordless sudo - otherwise it fails with
    /// the script's own permission error, which is surfaced back to the caller.
    /// UNVERIFIED: never exercised on a real Linux machine (dev sandbox is
    /// Windows-only) - the command mirrors Ollama's published instructions.
    /// </summary>
    private async Task<LocalRuntimeSetupStep> EnsureOllamaInstalledLinuxAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            client.Timeout = TimeSpan.FromMinutes(5);
            var script = await client.GetStringAsync("https://ollama.com/install.sh", ct);
            var scriptPath = Path.Combine(Path.GetTempPath(), "ailocal-ollama-install.sh");
            await File.WriteAllTextAsync(scriptPath, script, ct);

            var (code, output) = await RunAsync("sh", [scriptPath], TimeSpan.FromMinutes(15), ct);
            if (code == 0 && await CommandExistsAsync("ollama", ct))
                return new LocalRuntimeSetupStep("Install Ollama", true, "Installed via ollama.com/install.sh");

            return new LocalRuntimeSetupStep("Install Ollama", false,
                $"Install script exited {code}. {Trunc(output)} " +
                "Install manually: curl -fsSL https://ollama.com/install.sh | sh".Trim());
        }
        catch (Exception ex)
        {
            return new LocalRuntimeSetupStep("Install Ollama", false, ex.Message);
        }
    }

    /// <summary>
    /// Ollama on macOS ships as a drag-install .app bundle with no official
    /// unattended CLI installer; Homebrew is the one scriptable path. Falls
    /// back to a manual-install message when Homebrew isn't present rather
    /// than downloading and driving the .app installer, which Ollama does not
    /// support non-interactively.
    /// UNVERIFIED: never exercised on a real Mac (dev sandbox is Windows-only).
    /// </summary>
    private async Task<LocalRuntimeSetupStep> EnsureOllamaInstalledMacAsync(CancellationToken ct)
    {
        if (!await CommandExistsAsync("brew", ct))
            return new LocalRuntimeSetupStep("Install Ollama", false,
                "Automatic install needs Homebrew (https://brew.sh). Or install Ollama from https://ollama.com/download.");

        var (code, output) = await RunAsync("brew", ["install", "ollama"], TimeSpan.FromMinutes(15), ct);
        if (code == 0 && await CommandExistsAsync("ollama", ct))
            return new LocalRuntimeSetupStep("Install Ollama", true, "Installed via Homebrew");

        return new LocalRuntimeSetupStep("Install Ollama", false,
            $"brew install ollama exited {code}. {Trunc(output)}".Trim());
    }

    private async Task<LocalRuntimeSetupStep> EnsureOllamaRunningAsync(CancellationToken ct)
    {
        if (await IsEndpointReachableAsync(ct))
            return new LocalRuntimeSetupStep("Start Ollama", true, "Service reachable");

        try
        {
            // On Windows the Ollama server normally runs as its own background
            // service; launching `ollama serve` as a child of this node
            // (UseShellExecute=false, CreateNoWindow) dies with the parent or
            // gets sandboxed, so the endpoint never comes up. Spawn it
            // detached (UseShellExecute=true) so it survives and actually
            // listens on localhost:11434.
            var psi = OperatingSystem.IsWindows()
                ? new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "serve",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                }
                : new ProcessStartInfo
                {
                    FileName = "ollama",
                    Arguments = "serve",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
            using var process = Process.Start(psi);
            if (process is null)
                return new LocalRuntimeSetupStep("Start Ollama", false, "could not start ollama serve");
        }
        catch (Exception ex)
        {
            return new LocalRuntimeSetupStep("Start Ollama", false, ex.Message);
        }

        for (var i = 0; i < 40 && !ct.IsCancellationRequested; i++)
        {
            if (await IsEndpointReachableAsync(ct))
                return new LocalRuntimeSetupStep("Start Ollama", true, "Service started");
            await Task.Delay(500, ct);
        }

        return new LocalRuntimeSetupStep("Start Ollama", false,
            "Service did not become reachable. If Ollama is installed as a desktop app, start it manually from the Start menu, then retry.");
    }

    private async Task<bool> IsEndpointReachableAsync(CancellationToken ct)
    {
        var (reachable, _) = await InspectOllamaEndpointAsync(ct);
        return reachable;
    }

    private static async Task<(int ExitCode, string Output)> RunAsync(
        string file, IReadOnlyList<string> args, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = file,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args) psi.ArgumentList.Add(arg);

        Process? process;
        try { process = Process.Start(psi); }
        catch (Exception ex) { return (-1, ex.Message); }
        if (process is null) return (-1, "failed to start process");

        using (process)
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(ct);
            var errorTask = process.StandardError.ReadToEndAsync(ct);
            try
            {
                await process.WaitForExitAsync(ct).WaitAsync(timeout, ct);
            }
            catch (TimeoutException)
            {
                try { process.Kill(true); } catch { /* ignore */ }
                return (-1, "timed out");
            }

            string output;
            try { output = (await outputTask) + (await errorTask); }
            catch { output = ""; }
            return (process.ExitCode, output);
        }
    }

    private static string Trunc(string value) => value.Length > 400 ? value[..400] : value;

    private async Task<(bool Reachable, bool ModelInstalled)> InspectOllamaEndpointAsync(CancellationToken ct)
    {
        try
        {
            var client = _httpFactory.CreateClient();
            var endpoint = _settings.Providers.OllamaEndpoint.TrimEnd('/');
            using var response = await client.GetAsync($"{endpoint}/api/tags", ct);
            if (!response.IsSuccessStatusCode)
                return (false, false);

            var body = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(body);
            // Check the model the operator has actually chosen (falling back
            // to the recommendation) - not the bare recommendation, which is
            // what the provider itself will request. Otherwise a worker with
            // llama3.1:8b installed but a different recommended tag reports
            // "not installed" and can never be used.
            var model = EffectiveModel;
            var installed = false;

            if (doc.RootElement.TryGetProperty("models", out var models) &&
                models.ValueKind == JsonValueKind.Array)
            {
                installed = models.EnumerateArray().Any(m =>
                    m.TryGetProperty("name", out var name) &&
                    string.Equals(name.GetString(), model, StringComparison.OrdinalIgnoreCase));
            }

            return (true, installed);
        }
        catch
        {
            return (false, false);
        }
    }

    /// <summary>The model the provider will actually request: the operator's
    /// explicit choice, or the hardware recommendation when that's empty.</summary>
    private string EffectiveModel => string.IsNullOrWhiteSpace(_settings.Providers.OllamaModel)
        ? _recommendation.OllamaTag
        : _settings.Providers.OllamaModel;

    private static async Task<bool> CommandExistsAsync(string command, CancellationToken ct)
    {
        var tool = OperatingSystem.IsWindows() ? "where.exe" : "which";
        var psi = new ProcessStartInfo
        {
            FileName = tool,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add(command);

        try
        {
            using var process = Process.Start(psi);
            if (process is null) return false;
            await process.WaitForExitAsync(ct);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}

public sealed class LocalRuntimeBootstrapper : BackgroundService
{
    private readonly NodeSettings _settings;
    private readonly LocalRuntimeManager _runtime;
    private readonly ILogger<LocalRuntimeBootstrapper> _log;

    public LocalRuntimeBootstrapper(
        NodeSettings settings,
        LocalRuntimeManager runtime,
        ILogger<LocalRuntimeBootstrapper> log)
    {
        _settings = settings;
        _runtime = runtime;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_settings.Role != NodeRole.Worker || !_settings.Installer.AutoPullOllamaModel)
            return;

        var status = await _runtime.InspectAsync(stoppingToken);
        if (!status.OllamaOnPath)
        {
            _log.LogWarning("Ollama is not installed or not on PATH. Install Ollama, then run: {Command}", status.PullCommand);
            return;
        }

        if (status.RecommendedModelInstalled)
        {
            _log.LogInformation("recommended Ollama model already installed: {Model}", status.RecommendedModel);
            return;
        }

        _log.LogInformation("pulling recommended Ollama model: {Model}", status.RecommendedModel);
        var result = await _runtime.PullRecommendedModelAsync(stoppingToken);
        if (result.Success)
            _log.LogInformation("pulled Ollama model: {Model}", result.Model);
        else
            _log.LogWarning("failed to pull Ollama model {Model}: {Output}", result.Model, result.Output);
    }
}
