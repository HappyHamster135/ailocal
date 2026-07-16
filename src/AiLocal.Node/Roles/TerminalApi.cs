using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AiLocal.Node.Roles;

public sealed record TerminalCreateRequest(string Root, string? Shell = null);
public sealed record TerminalInputRequest(string Data);

/// <summary>
/// A live interactive terminal session backed by a shell process.
/// Used by the dashboard to provide an in-browser terminal against the
/// workspace, similar to what VS Code's integrated terminal provides.
/// </summary>
internal sealed class TerminalSession : IDisposable
{
    public string Id { get; }
    public Process Process { get; }
    public StringBuilder OutputBuffer { get; } = new();
    public object Lock { get; } = new();
    public bool IsDead { get; set; }
    public DateTime CreatedAt { get; } = DateTime.UtcNow;
    private Timer? _cleanupTimer;

    public TerminalSession(string id, Process process)
    {
        Id = id;
        Process = process;
        process.Exited += (_, _) =>
        {
            lock (Lock)
            {
                IsDead = true;
            }
            // Schedule cleanup 5 minutes after exit
            _cleanupTimer = new Timer(_ =>
            {
                TerminalApi.RemoveDeadSession(Id);
            }, null, TimeSpan.FromMinutes(5), Timeout.InfiniteTimeSpan);
        };
        process.EnableRaisingEvents = true;

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (Lock)
                {
                    OutputBuffer.AppendLine(e.Data);
                }
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
            {
                lock (Lock)
                {
                    OutputBuffer.AppendLine(e.Data);
                }
            }
        };

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();
        try { if (!Process.HasExited) Process.Kill(entireProcessTree: true); } catch { /* best-effort */ }
        Process.Dispose();
    }
}

/// <summary>
/// Interactive terminal endpoints for the dashboard.
/// Provides a workspace-scoped shell that stays alive until killed.
/// Registered alongside other API endpoints in NodeWebHost.cs.
/// </summary>
public static class TerminalApi
{
    private static readonly ConcurrentDictionary<string, TerminalSession> _sessions = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Resolve and validate a workspace root path.
    /// Must exist, be absolute, and not be a system directory.
    /// </summary>
    /// <exception cref="ArgumentException">If the path is invalid, does not exist, or is a system directory.</exception>
    public static string Resolve(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Root path must not be empty.", nameof(root));

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(root);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid root path: {ex.Message}", nameof(root));
        }

        if (!Path.IsPathRooted(fullPath))
            throw new ArgumentException("Root path must be absolute.", nameof(root));

        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"Directory does not exist: {fullPath}", nameof(root));

        // Block system directories for safety
        var systemDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // Windows
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64"),
            // Unix
            "/etc", "/bin", "/sbin", "/usr", "/usr/bin", "/usr/sbin", "/usr/lib",
            "/lib", "/lib64", "/dev", "/proc", "/sys", "/boot", "/root",
            "/var", "/var/log", "/var/cache", "/var/lib",
            // macOS
            "/System", "/private",
        };

        // Check if the path is exactly a system directory
        if (systemDirs.Contains(fullPath))
            throw new ArgumentException($"Root path is a system directory and is not allowed: {fullPath}", nameof(root));

        // Check if the path is inside a system directory on Unix
        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            foreach (var sysDir in systemDirs)
            {
                if (sysDir.StartsWith('/') && fullPath.StartsWith(sysDir + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    throw new ArgumentException($"Root path is inside a system directory and is not allowed: {fullPath}", nameof(root));
            }
        }

        return fullPath;
    }

    /// <summary>
    /// Remove a dead session from the registry (called by cleanup timer).
    /// </summary>
    internal static void RemoveDeadSession(string id)
    {
        if (_sessions.TryRemove(id, out var session))
        {
            session.Dispose();
        }
    }

    public static void MapEndpoints(WebApplication app)
    {
        // POST /api/terminals - start a new interactive terminal session
        app.MapPost("/api/terminals", (TerminalCreateRequest req) =>
        {
            string resolvedRoot;
            try
            {
                resolvedRoot = Resolve(req.Root);
            }
            catch (ArgumentException ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status400BadRequest);
            }

            // Determine shell: default to cmd.exe on Windows, /bin/sh on Unix
            string shellExe;
            string shellArgs;
            var shell = req.Shell?.Trim().ToLowerInvariant() ?? "";

            if (OperatingSystem.IsWindows())
            {
                if (shell is "sh" or "bash")
                {
                    // Try to find git-bash or WSL bash
                    var bashPaths = new[]
                    {
                        @"C:\Program Files\Git\bin\bash.exe",
                        @"C:\Program Files (x86)\Git\bin\bash.exe",
                        @"C:\Windows\System32\bash.exe",
                    };
                    var found = bashPaths.FirstOrDefault(File.Exists);
                    if (found is not null)
                    {
                        shellExe = found;
                        shellArgs = "";
                    }
                    else
                    {
                        shellExe = "cmd.exe";
                        shellArgs = "";
                    }
                }
                else
                {
                    shellExe = "cmd.exe";
                    shellArgs = "";
                }
            }
            else
            {
                // Linux / macOS
                shellExe = shell is "bash" ? "/bin/bash" : "/bin/sh";
                shellArgs = "";
            }

            var psi = new ProcessStartInfo(shellExe, shellArgs)
            {
                WorkingDirectory = resolvedRoot,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            Process process;
            try
            {
                process = new Process { StartInfo = psi };
                process.Start();
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"Failed to start shell process: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }

            var terminalId = Guid.NewGuid().ToString("N");
            var session = new TerminalSession(terminalId, process);
            _sessions[terminalId] = session;

            return Results.Ok(new { terminalId });
        });

        // POST /api/terminals/{id}/input - write data to terminal stdin
        app.MapPost("/api/terminals/{id}/input", async (string id, TerminalInputRequest req) =>
        {
            if (!_sessions.TryGetValue(id, out var session))
                return Results.NotFound();

            lock (session.Lock)
            {
                if (session.IsDead)
                    return Results.Problem(detail: "Process has exited.", statusCode: StatusCodes.Status410Gone);
            }

            try
            {
                var data = req.Data;
                // Append newline if the data doesn't already end with one
                if (!data.EndsWith('\n'))
                    data += '\n';

                await session.Process.StandardInput.WriteAsync(data);
                await session.Process.StandardInput.FlushAsync();
                return Results.Ok(new { written = true });
            }
            catch (Exception ex)
            {
                return Results.Problem(
                    detail: $"Failed to write to terminal: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        // GET /api/terminals/{id}/output - poll accumulated output
        app.MapGet("/api/terminals/{id}/output", (string id) =>
        {
            if (!_sessions.TryGetValue(id, out var session))
                return Results.NotFound();

            string output;
            bool isDead;

            lock (session.Lock)
            {
                output = session.OutputBuffer.ToString();
                session.OutputBuffer.Clear();
                isDead = session.IsDead;
            }

            if (isDead)
                output += "(process exited)";

            return Results.Ok(new { output });
        });

        // POST /api/terminals/{id}/kill - kill terminal process
        app.MapPost("/api/terminals/{id}/kill", (string id) =>
        {
            if (!_sessions.TryRemove(id, out var session))
                return Results.NotFound();

            session.Dispose();
            return Results.Ok(new { killed = true });
        });
    }
}