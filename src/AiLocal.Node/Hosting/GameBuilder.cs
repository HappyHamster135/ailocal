using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace AiLocal.Node.Hosting;

/// <summary>P2: "Bygg spel" - takes a scaffolded (or hand-written) Godot/Unity
/// project and produces a standalone .exe via the engine's headless build.
///
/// The builder does NOT download engines: that is gigabyte-scale, offline-fragile
/// and unsafe. It locates an already-installed engine on the machine, and if none
/// is found it returns a clear, actionable error so the agent/user can install it.
/// The shell plumbing is injected as a Func (reuses the host's process runner).
/// </summary>
public sealed class GameBuilder
{
    /// <summary>Build the game in <paramref name="root"/>. engine may be "godot",
    /// "unity", or "auto" (detect by project files). Returns (success, output, exePath).</summary>
    public async Task<(bool Success, string Output, string? ExePath)> BuildAsync(
        string engine, string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return (false, "root (mapp) kravs och maste finnas.", null);

        engine = (engine ?? "auto").Trim().ToLowerInvariant();
        if (engine == "auto")
            engine = DetectEngine(root);

        if (engine is not ("godot" or "unity"))
        {
            // html5 needs no engine binary - it already runs in a browser.
            if (engine == "html5")
                return (true, "html5-projekt kors i en webblasare (ingen build behovs).", null);
            return (false, $"okant motor '{engine}' - forvantad 'godot', 'unity' eller 'auto'.", null);
        }

        return engine == "godot"
            ? await BuildGodot(root, runCommand, ct)
            : await BuildUnity(root, runCommand, ct);
    }

    static string DetectEngine(string root)
    {
        if (File.Exists(Path.Combine(root, "project.godot"))) return "godot";
        if (File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"))
            || Directory.Exists(Path.Combine(root, "Assets"))) return "unity";
        return "unknown";
    }

    // ---- Godot -----------------------------------------------------------
    async Task<(bool, string, string?)> BuildGodot(string root,
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct)
    {
        var godot = FindGodot();
        if (godot is null)
            return (false,
                "Godox ar inte installerat pa denna maskin. Installera Godot 4.3 (https://godotengine.org) " +
                "och se till att 'godot' eller 'Godot_v4.3-stable_mono_win64.exe' finns i PATH eller " +
                "C:/Program Files/Godot/. 'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        // project.godot already names the preset "Windows Desktop" (ScaffoldGodot writes export_presets.cfg).
        var preset = "Windows Desktop";
        var outExe = Path.Combine(root, "build", "PixelRush.exe");
        var cmd = $"\"{godot}\" --headless --quit --export-release \"{preset}\" \"{outExe}\"";
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"godot export misslyckades (exit {exit}):\n{output}", null);
        if (!File.Exists(outExe))
            return (false, $"godot avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    static string? FindGodot()
    {
        var fromPath = FindOnPath("godot.exe") ?? FindOnPath("godot");
        if (fromPath is not null) return fromPath;
        var candidates = new[]
        {
            @"C:\Program Files\Godot\Godot_v4.3-stable_mono_win64.exe",
            @"C:\Program Files\Godot\godot.exe",
            @"C:\Program Files (x86)\Godot\godot.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Godot", "godot.exe"),
        };
        foreach (var c in candidates)
            if (File.Exists(c)) return c;
        return null;
    }

    // ---- Unity ------------------------------------------------------------
    async Task<(bool, string, string?)> BuildUnity(string root,
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct)
    {
        var unity = FindUnity();
        if (unity is null)
            return (false,
                "Unity ar inte installerat pa denna maskin. Installera Unity 6000.x " +
                "(https://unity.com) och se till att 'Unity.exe' finns under C:/Program Files/Unity/Hub/Editor/<version>/Editor/. " +
                "'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        var outExe = Path.Combine(root, "build", "PixelRush.exe");
        // -buildWindows64Player respects the scenes registered in EditorBuildSettings.asset.
        var cmd = $"\"{unity}\" -batchmode -quit -projectPath \"{root}\" -buildWindows64Player \"{outExe}\"";
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"unity build misslyckades (exit {exit}):\n{output}", null);
        if (!File.Exists(outExe))
            return (false, $"unity avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    static string? FindUnity()
    {
        var fromPath = FindOnPath("Unity.exe");
        if (fromPath is not null) return fromPath;
        var hub = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Unity", "Hub", "Editor");
        if (Directory.Exists(hub))
        {
            var editor = Directory.GetDirectories(hub)
                .Select(d => Path.Combine(d, "Editor", "Unity.exe"))
                .FirstOrDefault(File.Exists);
            if (editor is not null) return editor;
        }
        return null;
    }

    static string? FindOnPath(string fileName)
    {
        var paths = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paths)
        {
            try
            {
                var full = Path.Combine(p, fileName);
                if (File.Exists(full)) return full;
            }
            catch { /* ignore bad PATH entries */ }
        }
        return null;
    }
}
