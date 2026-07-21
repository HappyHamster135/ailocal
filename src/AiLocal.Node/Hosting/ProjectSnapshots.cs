using System.IO.Compression;
using System.Text.Json;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

public sealed record SnapshotInfo(string File, string Label, DateTimeOffset TakenAt, bool Clean, string? Engine);

/// <summary>
/// Version history with rollback per project: every successfully gated
/// assignment zips the project it touched, so "gör spelet svårare" that made
/// the game WORSE has an undo button. Snapshot-based rather than git-based on
/// purpose - git only exists in workspaces where team mode ran, and the
/// safety net must never depend on that. Zips live outside the workspace
/// (in the node's data dir) so agents can never touch them.
/// </summary>
public static class ProjectSnapshots
{
    private const int MaxPerProject = 10;
    private const long MaxProjectBytes = 100 * 1024 * 1024;
    private static readonly string[] SkipDirs = [".git", ".worktrees", "node_modules", "bin", "obj", "__pycache__", "dist"];

    private static string BaseDir => Path.Combine(SettingsPaths.DataDirectory, "snapshots");

    /// <summary>Stable folder key for a project: its path relative to the
    /// workspace, sanitized ("." = root project -> "_rot").</summary>
    public static string KeyFor(string workspaceRoot, string projectRoot)
    {
        var rel = Path.GetRelativePath(Path.GetFullPath(workspaceRoot), Path.GetFullPath(projectRoot));
        if (rel == ".") return "_rot";
        return new string(rel.Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '_').ToArray());
    }

    public static (bool Success, string Output) Capture(
        string workspaceRoot, string projectRoot, string label, bool clean, string? engine)
    {
        try
        {
            if (!Directory.Exists(projectRoot))
                return (false, "projektmappen finns inte");
            var size = Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            if (size > MaxProjectBytes)
                return (false, $"projektet är {size / (1024 * 1024)} MB - för stort för snapshot (tak {MaxProjectBytes / (1024 * 1024)} MB)");

            var dir = Path.Combine(BaseDir, KeyFor(workspaceRoot, projectRoot));
            Directory.CreateDirectory(dir);
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            var zipPath = Path.Combine(dir, stamp + ".zip");

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories))
                {
                    var rel = Path.GetRelativePath(projectRoot, file);
                    var segments = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    if (segments.Any(s => SkipDirs.Contains(s, StringComparer.OrdinalIgnoreCase)))
                        continue;
                    zip.CreateEntryFromFile(file, rel.Replace('\\', '/'));
                }
            }

            var meta = new SnapshotInfo(Path.GetFileName(zipPath),
                label.Length > 80 ? label[..80] + "…" : label, DateTimeOffset.UtcNow, clean, engine);
            File.WriteAllText(Path.ChangeExtension(zipPath, ".json"), JsonSerializer.Serialize(meta));

            // Rulla ut äldsta utöver taket.
            foreach (var old in Directory.EnumerateFiles(dir, "*.zip").OrderByDescending(f => f).Skip(MaxPerProject))
            {
                TryDelete(old);
                TryDelete(Path.ChangeExtension(old, ".json"));
            }
            return (true, $"snapshot sparad ({new FileInfo(zipPath).Length / 1024} kB)");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static IReadOnlyList<SnapshotInfo> List(string workspaceRoot, string projectRoot)
    {
        var dir = Path.Combine(BaseDir, KeyFor(workspaceRoot, projectRoot));
        if (!Directory.Exists(dir)) return [];
        var result = new List<SnapshotInfo>();
        foreach (var metaFile in Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => f))
        {
            try
            {
                if (JsonSerializer.Deserialize<SnapshotInfo>(File.ReadAllText(metaFile)) is { } info
                    && File.Exists(Path.ChangeExtension(metaFile, ".zip")))
                    result.Add(info);
            }
            catch { /* trasig metapost hoppas över */ }
        }
        return result;
    }

    /// <summary>Restores a snapshot: clears the project directory (keeping
    /// git/worktree metadata) and extracts the zip. The safety rails: the
    /// snapshot must belong to exactly this project, and the project must
    /// live inside the workspace.</summary>
    public static (bool Success, string Output) Restore(string workspaceRoot, string projectRoot, string snapshotFile)
    {
        try
        {
            var fullWorkspace = Path.GetFullPath(workspaceRoot);
            var fullProject = Path.GetFullPath(projectRoot);
            if (!fullProject.StartsWith(fullWorkspace, StringComparison.OrdinalIgnoreCase))
                return (false, "projektet ligger utanför arbetsytan");
            if (snapshotFile.Contains('/') || snapshotFile.Contains('\\'))
                return (false, "ogiltigt snapshotnamn");
            var zipPath = Path.Combine(BaseDir, KeyFor(workspaceRoot, projectRoot), snapshotFile);
            if (!File.Exists(zipPath))
                return (false, "snapshoten finns inte");

            foreach (var entry in Directory.EnumerateFileSystemEntries(fullProject))
            {
                var name = Path.GetFileName(entry);
                if (name is ".git" or ".worktrees") continue;
                if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                else File.Delete(entry);
            }
            ZipFile.ExtractToDirectory(zipPath, fullProject, overwriteFiles: true);
            return (true, $"projektet återställt från {snapshotFile}");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }
}
