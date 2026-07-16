using System.Diagnostics;
using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// P2: one-click Build / Run / Test for the Studio "programming" loop.
/// Detects the workspace's project kind (mirrors GitIsolationService's
/// build detection) and runs the right command, surfacing output back to
/// the dashboard so an operator can build/run an app without leaving the UI.
/// </summary>
public sealed class WorkspaceService
{
    public record WsCommand(string FileName, string[] Arguments, string Kind);

    /// <summary>What command to run for build / run / test in this workspace.</summary>
    public WsCommand? DetectCommand(string root, string kind)
    {
        if (!Directory.Exists(root)) return null;
        var isDotnet = Directory.GetFiles(root, "*.sln").Length > 0
            || Directory.GetFiles(root, "*.csproj").Length > 0;
        if (isDotnet)
        {
            return kind switch
            {
                "test" => new("dotnet", ["test"], "test"),
                "run" => new("dotnet", ["run", "--project", FirstProject(root) ?? "."], "run"),
                _ => new("dotnet", ["build"], "build"),
            };
        }
        if (File.Exists(Path.Combine(root, "package.json")))
        {
            return kind switch
            {
                "test" => new("npm", ["test"], "test"),
                "run" => new("npm", ["start"], "run"),
                _ => new("npm", ["run", "build"], "build"),
            };
        }
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
        {
            return kind switch
            {
                "test" => new("cargo", ["test"], "test"),
                "run" => new("cargo", ["run"], "run"),
                _ => new("cargo", ["build"], "build"),
            };
        }
        if (File.Exists(Path.Combine(root, "go.mod")))
        {
            return kind switch
            {
                "test" => new("go", ["test", "./..."], "test"),
                "run" => new("go", ["run", "."], "run"),
                _ => new("go", ["build", "./..."], "build"),
            };
        }
        return null; // unknown project kind - pass through, nothing to do
    }

    static string? FirstProject(string root)
    {
        var p = Directory.GetFiles(root, "*.csproj").FirstOrDefault()
            ?? Directory.GetFiles(root, "*.fsproj").FirstOrDefault();
        return p is null ? null : Path.GetFileName(p);
    }

    /// <summary>Runs the requested command, returning (Success, Output).</summary>
    public async Task<(bool Success, string Output)> RunAsync(
        string root, string kind, CancellationToken ct = default)
    {
        var cmd = DetectCommand(root, kind);
        if (cmd is null)
            return (true, "inget känt byggsystem i arbetsmappen - inget att göra.");
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = cmd.FileName,
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            foreach (var a in cmd.Arguments) psi.ArgumentList.Add(a);

            using var proc = new Process { StartInfo = psi };
            var output = new StringBuilder();
            proc.OutputDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) output.AppendLine(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try { await proc.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try { proc.Kill(true); } catch { /* best effort */ }
                return (false, "Kommandot timade ut efter 5 min: " + cmd.FileName + " " + string.Join(" ", cmd.Arguments));
            }
            return (proc.ExitCode == 0, output.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return (false, "Kunde inte köra " + cmd.FileName + ": " + ex.Message);
        }
    }
}
