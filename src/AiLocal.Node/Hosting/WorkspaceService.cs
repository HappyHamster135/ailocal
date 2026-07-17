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

        // p3: Unity project - Assets/ + ProjectSettings/ is the tell. Check
        // BEFORE .sln/.csproj: Unity generates a .sln next to the project, but
        // the real build must go through Unity's batchmode, not dotnet.
        if (Directory.Exists(Path.Combine(root, "Assets")) &&
            Directory.Exists(Path.Combine(root, "ProjectSettings")))
        {
            return kind switch
            {
                "test" => new("Unity", ["-batchmode", "-quit", "-projectPath", root,
                    "-runTests", "-testPlatform", "EditMode"], "test"),
                "run" => new("Unity", ["-batchmode", "-quit", "-projectPath", root], "run"),
                "game" => new("Unity", ["-batchmode", "-quit", "-projectPath", root,
                    "-buildWindowsPlayer", Path.Combine(root, "build", "GameTest.exe")], "game"),
                _ => new("Unity", ["-batchmode", "-quit", "-projectPath", root,
                    "-buildTarget", "Win64"], "build"),
            };
        }
        // p3: Godot project - project.godot at the root.
        if (File.Exists(Path.Combine(root, "project.godot")))
        {
            return kind switch
            {
                "test" => new("godot", ["--headless", "--quit", "--path", root], "test"),
                "run" => new("godot", ["--path", root], "run"),
                "game" => new("godot", ["--headless", "--quit", "--path", root,
                    "--export-release", "Windows Desktop"], "game"),
                _ => new("godot", ["--headless", "--quit", "--path", root, "--build"], "build"),
            };
        }

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
        // p3: Python project - requirements.txt / pyproject.toml / a .py entry.
        if (File.Exists(Path.Combine(root, "requirements.txt")) ||
            File.Exists(Path.Combine(root, "pyproject.toml")) ||
            Directory.GetFiles(root, "*.py").Length > 0)
        {
            var entry = FirstPythonEntry(root);
            return kind switch
            {
                "test" => new("python", ["-m", "pytest"], "test"),
                "run" => new("python", [entry ?? "main.py"], "run"),
                _ => new("pip", ["install", "-r", "requirements.txt"], "build"),
            };
        }
        return null; // unknown project kind - pass through, nothing to do
    }

    static string? FirstPythonEntry(string root)
    {
        return Directory.GetFiles(root, "main.py").FirstOrDefault()
            ?? Directory.GetFiles(root, "app.py").FirstOrDefault()
            ?? Directory.GetFiles(root, "*.py").FirstOrDefault();
    }

    static string? FirstProject(string root)
    {
        var p = Directory.GetFiles(root, "*.csproj").FirstOrDefault()
            ?? Directory.GetFiles(root, "*.fsproj").FirstOrDefault();
        return p is null ? null : Path.GetFileName(p);
    }

    /// <summary>Resolves a game-engine executable name to a full path. "Unity"
    /// lives under Unity Hub's Editor folder (not on PATH); "godot" is usually
    /// on PATH but may also need a common install location. Falls back to the
    /// bare name so non-Windows / already-on-PATH setups still work.</summary>
    static string ResolveEnginePath(string fileName)
    {
        if (fileName.Equals("Unity", StringComparison.OrdinalIgnoreCase))
        {
            // Unity Hub installs editors under %PROGRAMFILES%\Unity\Hub\Editor\<ver>\Editor\Unity.exe
            var baseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Unity", "Hub", "Editor");
            if (Directory.Exists(baseDir))
            {
                var latest = Directory.GetDirectories(baseDir)
                    .OrderByDescending(d => d)
                    .Select(d => Path.Combine(d, "Editor", "Unity.exe"))
                    .FirstOrDefault(File.Exists);
                if (latest is not null) return latest;
            }
        }
        if (fileName.Equals("godot", StringComparison.OrdinalIgnoreCase))
        {
            // Try PATH first (handled by UseShellExecute=false + Process), then
            // a few well-known install spots.
            foreach (var cand in new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Godot", "Godot.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Godot", "Godot.exe"),
            })
            {
                if (File.Exists(cand)) return cand;
            }
        }
        return fileName;
    }

    /// <summary>Runs the requested command, returning (Success, Output).
    /// kind "verify" maps to the run command but with a short (30s) timeout so
    /// an operator can confirm the app boots and emits startup output without
    /// the process hanging forever.</summary>
    public async Task<(bool Success, string Output)> RunAsync(
        string root, string kind, CancellationToken ct = default)
    {
        // P6: "verify" = run the app, but capture startup output and stop.
        var effectiveKind = kind == "verify" ? "run" : kind;
        var cmd = DetectCommand(root, effectiveKind);
        if (cmd is null)
            return (true, "inget känt byggsystem i arbetsmappen - inget att göra.");
        var timeoutSec = kind == "verify" ? 30 : 300;
        try
        {
            var fileName = ResolveEnginePath(cmd.FileName);
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
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
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSec));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
            try { await proc.WaitForExitAsync(linked.Token); }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                try { proc.Kill(true); } catch { /* best effort */ }
                var msg = kind == "verify"
                    ? "Appen startade (verifiering avbröts efter " + timeoutSec + "s för att fånga uppstart):\n" + output.ToString().TrimEnd()
                    : "Kommandot timade ut efter " + timeoutSec + "s: " + cmd.FileName + " " + string.Join(" ", cmd.Arguments);
                return (true, msg);
            }
            return (proc.ExitCode == 0, output.ToString().TrimEnd());
        }
        catch (Exception ex)
        {
            return (false, "Kunde inte köra " + cmd.FileName + ": " + ex.Message);
        }
    }
}
