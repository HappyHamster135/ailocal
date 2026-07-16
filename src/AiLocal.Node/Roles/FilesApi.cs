using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Node.Hosting;
using Microsoft.AspNetCore.Mvc;

namespace AiLocal.Node.Roles;

/// <summary>
/// File explorer + code editor API for the dashboard. All file access is
/// workspace-scoped (sandboxed contract) — absolute paths in the request
/// body are rejected, and every resolved path must stay under the root.
/// </summary>
public static class FilesApi
{
    // Directories to skip when building the file tree — matches the skip
    // list in AgentToolExecutor.SkipDirs plus .worktrees (git isolation).
    private static readonly HashSet<string> SkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", "bin", "obj", "node_modules", ".worktrees"
    };

    // Extension allowlist for text content — files with these extensions
    // are always treated as text. Everything else is checked for null bytes.
    private static readonly HashSet<string> TextExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".txt", ".cs", ".js", ".ts", ".json", ".md", ".py", ".rs", ".go",
        ".c", ".cpp", ".h", ".hpp", ".html", ".css", ".xml", ".yml", ".yaml",
        ".toml", ".sh", ".ps1", ".sql", ".ini", ".cfg", ".csproj", ".sln",
        ".gitignore", ".lock", ".log"
    };

    private const int MaxFileSize = 5 * 1024 * 1024; // 5 MB
    private const int BinaryScanSize = 8 * 1024;       // first 8 KB for null-byte scan
    private const int MaxTreeDepth = 6;

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/files/tree", (
            [FromQuery] string? root,
            NodeSettings settings) =>
        {
            var resolvedRoot = ResolveRoot(root, settings);
            var entries = BuildTree(resolvedRoot, relativePath: "", depth: 0);
            return Results.Ok(entries);
        });

        app.MapGet("/api/files/content", async (
            [FromQuery] string path,
            [FromQuery] string? root,
            [FromQuery] int? offset,
            [FromQuery] int? limit,
            NodeSettings settings) =>
        {
            var resolvedRoot = ResolveRoot(root, settings);
            string fullPath;
            try
            {
                fullPath = Resolve(resolvedRoot, path);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            if (!File.Exists(fullPath))
                return Results.NotFound(new { error = $"file not found: {path}" });

            var fileInfo = new FileInfo(fullPath);
            if (fileInfo.Length > MaxFileSize)
                return Results.Problem(
                    detail: $"File exceeds 5 MB size limit ({fileInfo.Length} bytes).",
                    statusCode: StatusCodes.Status413RequestEntityTooLarge);

            if (IsBinary(fullPath))
                return Results.Problem(
                    detail: $"File appears to be binary (not a supported text format).",
                    statusCode: StatusCodes.Status415UnsupportedMediaType);

            var off = offset ?? 1;
            if (off < 1) off = 1;
            var lim = limit ?? 200;
            if (lim < 1) lim = 200;

            try
            {
                var allLines = await File.ReadAllLinesAsync(fullPath);
                var start = off - 1;
                if (start >= allLines.Length)
                    return Results.Ok(new
                    {
                        path,
                        totalLines = allLines.Length,
                        offset = off,
                        limit = lim,
                        lines = Array.Empty<string>(),
                        truncated = false
                    });

                var slice = allLines.Skip(start).Take(lim).ToArray();
                return Results.Ok(new
                {
                    path,
                    totalLines = allLines.Length,
                    offset = off,
                    limit = lim,
                    lines = slice,
                    truncated = slice.Length == lim && (start + lim) < allLines.Length
                });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Problem(detail: $"Could not read file: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });

        app.MapPost("/api/files/write", async (
            FileWriteRequest req,
            NodeSettings settings) =>
        {
            if (string.IsNullOrWhiteSpace(req.Path))
                return Results.BadRequest(new { error = "path is required" });
            if (req.Content is null)
                return Results.BadRequest(new { error = "content is required" });

            var resolvedRoot = ResolveRoot(req.Root, settings);
            string fullPath;
            try
            {
                fullPath = Resolve(resolvedRoot, req.Path);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            var dir = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            try
            {
                await File.WriteAllTextAsync(fullPath, req.Content);
                return Results.Ok(new { written = true, path = req.Path });
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return Results.Problem(detail: $"Could not write file: {ex.Message}",
                    statusCode: StatusCodes.Status500InternalServerError);
            }
        });
    }

    /// <summary>
    /// Resolve a root parameter: use the provided value if set, fall back to
    /// NodeSettings.Worker.WorkspacePath, then to a safe fallback under
    /// the app data directory. Throws 400-style exceptions for missing/invalid roots.
    /// </summary>
    private static string ResolveRoot(string? root, NodeSettings settings)
    {
        var rootPath = root;
        if (string.IsNullOrWhiteSpace(rootPath))
            rootPath = settings.Worker.WorkspacePath;

        if (string.IsNullOrWhiteSpace(rootPath))
            rootPath = Path.Combine(SettingsPaths.DataDirectory, "agent-workspace");

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(rootPath);
        }
        catch (Exception ex)
        {
            throw new ArgumentException($"Invalid root path: {ex.Message}");
        }

        if (!Directory.Exists(fullPath))
            throw new ArgumentException($"Root directory does not exist: {fullPath}");

        return fullPath;
    }

    /// <summary>
    /// Resolve a relative path against a workspace root, enforcing the
    /// sandboxed access contract:
    ///   - Absolute paths are rejected outright.
    ///   - The resolved path must stay under root (no directory traversal escape).
    /// 
    /// Mirrors AgentToolExecutor.ResolvePath (sandboxed mode).
    /// </summary>
    public static string Resolve(string root, string requestedRelative)
    {
        if (Path.IsPathRooted(requestedRelative))
            throw new UnauthorizedAccessException(
                "absolute paths not allowed - use path relative to root");

        var combined = Path.GetFullPath(Path.Combine(root, requestedRelative));
        var rootWithSeparator = root.EndsWith(Path.DirectorySeparatorChar)
            ? root
            : root + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, root, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"path '{requestedRelative}' resolves outside the workspace root");

        return combined;
    }

    /// <summary>
    /// Recursively build a flat list of {name, path, isDir, size} entries
    /// under the given directory, respecting skip-dirs and depth limit.
    /// </summary>
    private static List<FileTreeEntry> BuildTree(string dir, string relativePath, int depth)
    {
        var entries = new List<FileTreeEntry>();

        if (depth >= MaxTreeDepth)
            return entries;

        DirectoryInfo dirInfo;
        try
        {
            dirInfo = new DirectoryInfo(dir);
        }
        catch
        {
            return entries;
        }

        if (!dirInfo.Exists)
            return entries;

        // Collect directories first (sorted)
        foreach (var subDir in dirInfo.EnumerateDirectories().OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (SkipDirs.Contains(subDir.Name))
                continue;

            var subRelative = string.IsNullOrEmpty(relativePath)
                ? subDir.Name
                : $"{relativePath}/{subDir.Name}";

            entries.Add(new FileTreeEntry(subDir.Name, subRelative, true, 0));

            // Recurse
            entries.AddRange(BuildTree(subDir.FullName, subRelative, depth + 1));
        }

        // Then files (sorted)
        foreach (var file in dirInfo.EnumerateFiles().OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase))
        {
            var fileRelative = string.IsNullOrEmpty(relativePath)
                ? file.Name
                : $"{relativePath}/{file.Name}";

            entries.Add(new FileTreeEntry(file.Name, fileRelative, false, file.Length));
        }

        return entries;
    }

    /// <summary>
    /// Detect whether a file is binary. Checks the extension allowlist first;
    /// if the extension is not recognised, scans the first 8 KB for null bytes.
    /// </summary>
    private static bool IsBinary(string path)
    {
        var ext = Path.GetExtension(path);
        if (!string.IsNullOrEmpty(ext) && TextExtensions.Contains(ext))
            return false;

        // No recognised text extension — scan for null bytes
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BinaryScanSize);
            var buffer = new byte[BinaryScanSize];
            var bytesRead = stream.Read(buffer, 0, BinaryScanSize);
            for (var i = 0; i < bytesRead; i++)
            {
                if (buffer[i] == 0)
                    return true;
            }
        }
        catch
        {
            // If we can't read it, treat as binary
            return true;
        }

        return false;
    }
}

// --- Request / Response DTOs ---

public sealed record FileWriteRequest(string? Root, string Path, string Content);

public sealed record FileTreeEntry(string Name, string Path, bool IsDir, long Size);