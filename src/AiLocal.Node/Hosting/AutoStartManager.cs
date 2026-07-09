using System.Runtime.Versioning;
using AiLocal.Core.Configuration;
using AiLocal.Core.Roles;
using Microsoft.Win32;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Registers/unregisters this node to relaunch automatically when the current
/// Windows user logs in, so a reboot (e.g. after a power outage) does not
/// require the operator to manually restart every machine in the cluster.
/// Best-effort: failures are swallowed by the caller, never fatal to settings.
/// </summary>
public static class AutoStartManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueNamePrefix = "AiLocal-";

    [SupportedOSPlatformGuard("windows")]
    public static bool IsSupported => OperatingSystem.IsWindows();

    /// <summary>
    /// Registers a boot-time launch for this node's current role/port/name.
    /// The command line is self-contained (no --parent-pid), since at login
    /// there is no Launcher process to supervise it.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static void Enable(NodeSettings settings)
    {
        if (!IsSupported) return;

        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) return;

        // Launcher (interactive Desktop app or bare headless exe) self-selects
        // its default mode with zero arguments - pass none so it starts the
        // normal picker/window instead of a specific server role.
        var command = settings.Role == NodeRole.Launcher
            ? $"\"{exe}\""
            : BuildServerCommand(exe, settings);

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.SetValue(ValueName(settings.Role), command);
    }

    [SupportedOSPlatform("windows")]
    public static void Disable(NodeRole role)
    {
        if (!IsSupported) return;

        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true);
        key?.DeleteValue(ValueName(role), throwOnMissingValue: false);
    }

    private static string BuildServerCommand(string exe, NodeSettings settings)
    {
        var args = new List<string> { "--role", settings.Role.ToString(), "--port", settings.Port.ToString() };

        if (!string.IsNullOrWhiteSpace(settings.NodeName))
            args.AddRange(["--name", settings.NodeName]);

        if (settings.Role is NodeRole.Worker or NodeRole.Overseer &&
            !string.IsNullOrWhiteSpace(settings.HostEndpoint))
            args.AddRange(["--host", settings.HostEndpoint]);

        // Host/Worker run headless at boot; Overseer opens its dashboard so the
        // operator sees cluster status without hunting for the port.
        if (settings.Role != NodeRole.Overseer)
            args.Add("--no-browser");

        return $"\"{exe}\" {string.Join(' ', args.Select(Quote))}";
    }

    private static string Quote(string value) => value.Contains(' ') ? $"\"{value}\"" : value;

    private static string ValueName(NodeRole role) => $"{ValueNamePrefix}{role}";
}
