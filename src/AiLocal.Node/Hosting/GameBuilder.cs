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
    /// "unity", or "auto" (detect by project files). Returns (success, output, exePath).
    /// godotFinder/unityFinder override engine detection (used by tests).</summary>
    public async Task<(bool Success, string Output, string? ExePath)> BuildAsync(
        string engine, string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct,
        Func<string?>? godotFinder = null,
        Func<string?>? unityFinder = null)
    {
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            return (false, "root ([ADDRESS]) kravs och maste finnas.", null);

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
            ? await BuildGodot(root, runCommand, ct, godotFinder)
            : await BuildUnity(root, runCommand, ct, unityFinder);
    }

    static string DetectEngine(string root)
    {
        if (File.Exists(Path.Combine(root, "project.godot"))) return "godot";
        if (File.Exists(Path.Combine(root, "ProjectSettings", "ProjectVersion.txt"))
            || Directory.Exists(Path.Combine(root, "Assets"))) return "unity";
        // A scaffolded HTML5 game IS the artifact - 'auto' must recognize it
        // instead of erroring with "okant motor 'unknown'" on the most common
        // project type (BuildAsync then reports that no build is needed).
        if (File.Exists(Path.Combine(root, "index.html"))) return "html5";
        return "unknown";
    }

    /// <summary>Output exe name derived from the project folder ("mitt-spel"
    /// -> "mitt-spel.exe") so every built game isn't shipped as PixelRush.exe.
    /// Falls back to "Game" when the folder name has no usable characters.</summary>
    internal static string DeriveExeName(string root)
    {
        var raw = Path.GetFileName(Path.TrimEndingDirectorySeparator(root ?? "")) ?? "";
        var name = new string(raw.Where(c => char.IsLetterOrDigit(c) || c is '-' or '_' or ' ' or '.').ToArray()).Trim().TrimEnd('.');
        return name.Length == 0 ? "Game" : name;
    }

    // ---- Godot -----------------------------------------------------------
    async Task<(bool, string, string?)> BuildGodot(string root,
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct,
        Func<string?>? godotFinder = null)
    {
        var godot = (godotFinder ?? FindGodot)();
        if (godot is null)
            return (false,
                "Godot ar inte installerat pa denna maskin. Installera Godot 4.3 (https://godotengine.org) " +
                "och se till att 'godot' eller '[ADDRESS]' finns i PATH eller " +
                "C:/Program Files/Godot/. 'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        // project.godot already names the preset "Windows Desktop" (ScaffoldGodot writes export_presets.cfg).
        // NOTE: do NOT pass --quit before --export-release - Godot 4 would exit before exporting.
        // --export-release performs the import+export and then exits on its own.
        // The CLI output path overrides the preset's export_path, so the exe
        // is named after the project folder rather than a hardcoded PixelRush.
        var preset = "Windows Desktop";
        var outExe = Path.Combine(root, "build", DeriveExeName(root) + ".exe");
        var cmd = MakeGodotCommand(godot, preset, outExe);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"godot export misslyckades (exit {exit}):\n{output}", null);
        if (!File.Exists(outExe))
            return (false, $"godot avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    internal static string MakeGodotCommand(string godotPath, string preset, string outExe)
        => $"\"{godotPath}\" --headless --export-release \"{preset}\" \"{outExe}\"";

    static string? FindGodot()
    {
        // Provisionerad godot (ToolLocator: %LOCALAPPDATA%\AiLocal\tools)
        // vinner - den finns garanterat inte pa PATH i den korande processen.
        if (AiLocal.Core.Agent.ToolLocator.Find("godot") is { } provisioned)
            return provisioned;
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
        Func<string, string, CancellationToken, Task<(int, string)>> runCommand, CancellationToken ct,
        Func<string?>? unityFinder = null)
    {
        var unity = (unityFinder ?? FindUnity)();
        if (unity is null)
            return (false,
                "Unity ar inte installerat pa denna maskin. Installera Unity 6000.x " +
                "(https://unity.com) och se till att 'Unity.exe' finns under C:/Program Files/Unity/Hub/Editor/<version>/Editor/. " +
                "'Bygg spel' kan inte ladda ner motorn sjalv.", null);

        var outExe = Path.Combine(root, "build", DeriveExeName(root) + ".exe");
        // -buildWindows64Player respects the scenes registered in EditorBuildSettings.asset.
        var cmd = MakeUnityCommand(unity, root, outExe);
        var (exit, output) = await runCommand(cmd, root, ct);
        if (exit != 0)
            return (false, $"unity build misslyckades (exit {exit}):\n{output}", null);
        if (!File.Exists(outExe))
            return (false, $"unity avslutade utan fel men .exe saknas: {outExe}\n{output}", null);
        return (true, $"Byggde {outExe} ({new FileInfo(outExe).Length} bytes).", outExe);
    }

    internal static string MakeUnityCommand(string unityPath, string projectRoot, string outExe)
        => $"\"{unityPath}\" -batchmode -quit -projectPath \"{projectRoot}\" -buildWindows64Player \"{outExe}\"";

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
