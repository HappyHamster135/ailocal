namespace AiLocal.Core.Agent;

/// <summary>
/// Finds a usable python.exe on Windows even when it is NOT on PATH - which
/// is exactly the state right after the provision tool silently installed it:
/// the installer prepends PATH for NEW processes, but the already-running
/// node (and every run_command it spawns) keeps the old environment. Without
/// this, verify kept failing with exit 9009 and the agent skipped the step.
/// </summary>
public static class PythonLocator
{
    /// <summary>Absolute path to a python interpreter, or null when none is
    /// found in the well-known install locations. PATH itself is not probed -
    /// callers use <see cref="CommandOrDefault"/> which falls back to the
    /// bare "python" (resolved via PATH by the shell) when nothing is found.</summary>
    public static string? Find()
    {
        var candidates = new List<string>();

        // Per-user install (what the provision tool performs): newest first.
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Python");
        AddGlob(candidates, localPrograms, "Python3*");

        // All-users installs.
        AddGlob(candidates, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Python3*");
        AddGlob(candidates, @"C:\", "Python3*");

        foreach (var dir in candidates.OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
        {
            var exe = Path.Combine(dir, "python.exe");
            if (File.Exists(exe)) return exe;
        }

        // The py-launcher ships with every python.org install and lives in
        // Windows-mappen - it resolves the newest interpreter itself.
        var py = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "py.exe");
        return File.Exists(py) ? py : null;
    }

    /// <summary>The command string build/verify should use for python: an
    /// absolute quoted path when a known install exists, otherwise the bare
    /// "python" so PATH-configured machines behave exactly as before.</summary>
    public static string CommandOrDefault() =>
        Find() is { } path ? $"\"{path}\"" : "python";

    private static void AddGlob(List<string> into, string parent, string pattern)
    {
        try
        {
            if (Directory.Exists(parent))
                into.AddRange(Directory.GetDirectories(parent, pattern));
        }
        catch { /* otillgänglig katalog - hoppa över */ }
    }
}
