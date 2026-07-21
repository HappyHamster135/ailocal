namespace AiLocal.Core.Agent;

/// <summary>
/// Finds provisioned/installed toolchains even when they are NOT on PATH -
/// the generalization of <see cref="PythonLocator"/>. Two situations create
/// that state: the provision tool just extracted/installed a tool (the
/// running node keeps its old environment), or the machine has the tool in a
/// well-known location without PATH configured. Every build/verify/run path
/// resolves through here so "exit 9009: command not found" means the tool is
/// genuinely absent - and then the agent provisions it and retries instead
/// of skipping the step.
/// </summary>
public static class ToolLocator
{
    /// <summary>Where the provision tool installs portable toolchains.</summary>
    public static string ToolsRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiLocal", "tools");

    /// <summary>Absolute path to the tool's executable, or null when not
    /// found in any known location. Supported: python, node, npm, git,
    /// java, dotnet, godot, blender.</summary>
    public static string? Find(string tool) => tool.ToLowerInvariant() switch
    {
        "python" => PythonLocator.Find(),
        "node" => FirstExisting(
            GlobExe(ToolsRoot, "node-v*", "node.exe"),
            [@"C:\Program Files\nodejs\node.exe"]),
        "npm" => FirstExisting(
            GlobExe(ToolsRoot, "node-v*", "npm.cmd"),
            [@"C:\Program Files\nodejs\npm.cmd"]),
        "git" => FirstExisting(
            [Path.Combine(ToolsRoot, "MinGit", "cmd", "git.exe")],
            [@"C:\Program Files\Git\cmd\git.exe", @"C:\Program Files (x86)\Git\cmd\git.exe"]),
        "java" => FirstExisting(
            GlobExe(ToolsRoot, "jdk-*", Path.Combine("bin", "java.exe")),
            GlobExe(@"C:\Program Files\Eclipse Adoptium", "jdk-*", Path.Combine("bin", "java.exe"))),
        "dotnet" => FirstExisting(
            [Path.Combine(ToolsRoot, "dotnet", "dotnet.exe")],
            [@"C:\Program Files\dotnet\dotnet.exe"]),
        // GlobFilesDeep, inte GlobFiles: provisioneraren extraherar zipen
        // till en UNDERMAPP (tools\Godot_v4.3-.../Godot_v...exe) - toppnivå-
        // sökningen hittade aldrig den provisionerade binären, så hela
        // godot-kedjan (headless-parse, exe-export, spelkörning i grinden)
        // trodde godot saknades TROTS lyckad provisionering. Mönstret
        // "Godot_v*" undviker GodotSharp\Tools\GodotTools.*-exen.
        // Huvudexen FÖRE _console-varianten: console-wrappern öppnar spel-
        // fönstret i en BARNPROCESS, så fönstersonden/dumpen (som tittar på
        // den startade processens MainWindowHandle) ser aldrig spelet.
        "godot" => FirstExisting(
            GlobFilesDeep(ToolsRoot, "Godot_v*.exe")
                .OrderBy(f => f.Contains("_console", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                .ThenByDescending(f => f, StringComparer.OrdinalIgnoreCase),
            [@"C:\Program Files\Godot\godot.exe", @"C:\Program Files (x86)\Godot\godot.exe"]),
        "blender" => FirstExisting(
            GlobFilesDeep(ToolsRoot, "blender*.exe"),
            [@"C:\Program Files\Blender Foundation\Blender 4.3\blender.exe"]),
        _ => null
    };

    /// <summary>The command string build/verify should use: an absolute
    /// quoted path when a known install exists, otherwise the bare command so
    /// PATH-configured machines behave exactly as before.</summary>
    public static string CommandOrDefault(string tool) =>
        Find(tool) is { } path ? $"\"{path}\"" : tool;

    private static string? FirstExisting(IEnumerable<string> primary, IEnumerable<string> fallback)
    {
        foreach (var candidate in primary.Concat(fallback))
            if (File.Exists(candidate))
                return candidate;
        return null;
    }

    /// <summary>All "<paramref name="parent"/>/<paramref name="dirPattern"/>/
    /// <paramref name="relativeExe"/>" paths, newest directory first.</summary>
    private static IEnumerable<string> GlobExe(string parent, string dirPattern, string relativeExe)
    {
        try
        {
            if (!Directory.Exists(parent)) return [];
            return Directory.GetDirectories(parent, dirPattern)
                .OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase)
                .Select(d => Path.Combine(d, relativeExe));
        }
        catch
        {
            return [];
        }
    }

    private static IEnumerable<string> GlobFiles(string parent, string filePattern)
    {
        try
        {
            if (!Directory.Exists(parent)) return [];
            return Directory.GetFiles(parent, filePattern)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Som GlobFiles men rekursivt - för verktyg vars zip extraherar
    /// till en undermapp under tools-katalogen (godot, blender).</summary>
    private static IEnumerable<string> GlobFilesDeep(string parent, string filePattern)
    {
        try
        {
            if (!Directory.Exists(parent)) return [];
            return Directory.GetFiles(parent, filePattern, SearchOption.AllDirectories)
                .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return [];
        }
    }
}
