using System.Text.RegularExpressions;

namespace AiLocal.Core.Agent;

/// <summary>
/// A small, dependency-free codebase index that "grows per project": it scans
/// a workspace once, then incrementally picks up new/changed files on later
/// calls (keyed by path + mtime + size), so repeated builds stay cheap and the
/// index accumulates structure over time. Used to give agents project context
/// via the <c>recall</c> tool without shipping a vector database.
///
/// Index contents per file: symbol names (types/functions/methods via a light
/// regex) + a tokenised inverted index for keyword recall. Recall scores by
/// token overlap between the query and each file's tokens/symbols.
/// </summary>
public sealed class CodebaseIndex
{
    private static readonly string[] DefaultSkipDirs =
        [".git", "bin", "obj", "node_modules", ".vs", "dist", "build", ".ailocal", ".idea"];

    private static readonly HashSet<string> IndexedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".cpp", ".c",
        ".h", ".hpp", ".rb", ".php", ".swift", ".kt", ".scala", ".sql", ".sh", ".json",
        ".yaml", ".yml", ".md", ".txt", ".html", ".css", ".vue", ".toml", ".xml"
    };

    // type Name = ...   class Name   struct Name   interface Name   enum Name   func Name(   def Name(   function Name(
    private static readonly Regex SymbolRegex = new(
        @"(?:^\s*(?:public|private|protected|internal|static|async|export|def|func|class|struct|interface|enum|type|fn)\b[^\n]{0,120}?\b([A-Za-z_][A-Za-z0-9_]*)\s*[\(\:])",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex TokenRegex = new(@"[A-Za-z_][A-Za-z0-9_]*", RegexOptions.Compiled);

    private readonly object _gate = new();
    private readonly Dictionary<string, FileEntry> _files = new(StringComparer.OrdinalIgnoreCase);

    public sealed record FileEntry(string Path, long Size, DateTime Modified, HashSet<string> Tokens, List<string> Symbols);

    public IReadOnlyCollection<FileEntry> Files
    {
        get { lock (_gate) return [.. _files.Values]; }
    }

    /// <summary>Scan (or re-scan, incrementally) <paramref name="root"/>. New or
    /// changed files are indexed; unchanged ones are kept as-is so the index
    /// grows rather than rebuilds from scratch each call.</summary>
    public void Build(string root)
    {
        if (!Directory.Exists(root)) return;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in EnumerateFiles(root))
        {
            seen.Add(file);
            var info = new FileInfo(file);
            lock (_gate)
            {
                if (_files.TryGetValue(file, out var existing)
                    && existing.Size == info.Length
                    && existing.Modified == info.LastWriteTimeUtc)
                    continue; // unchanged
            }
            IndexFile(file, info);
        }
        // Drop files that disappeared.
        lock (_gate)
        {
            foreach (var key in _files.Keys.Where(k => !seen.Contains(k)).ToList())
                _files.Remove(key);
        }
    }

    private void IndexFile(string file, FileInfo info)
    {
        string text;
        try { text = File.ReadAllText(file); }
        catch { return; }
        var symbols = SymbolRegex.Matches(text)
            .Cast<Match>()
            .Select(m => m.Groups[1].Value)
            .Where(s => s.Length > 1)
            .Distinct()
            .ToList();
        var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match m in TokenRegex.Matches(text))
        {
            var t = m.Value;
            if (t.Length >= 3) tokens.Add(t);
        }
        lock (_gate)
            _files[file] = new FileEntry(file, info.Length, info.LastWriteTimeUtc, tokens, symbols);
    }

    private IEnumerable<string> EnumerateFiles(string root)
    {
        foreach (var dir in Directory.EnumerateDirectories(root))
        {
            var name = Path.GetFileName(dir);
            if (DefaultSkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var f in EnumerateFiles(dir)) yield return f;
        }
        foreach (var f in Directory.EnumerateFiles(root))
        {
            if (!IndexedExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)) continue;
            yield return f;
        }
    }

    /// <summary>Returns the files most relevant to <paramref name="query"/>, best
    /// first. Score = overlapping tokens (+bonus for symbol-name hits).</summary>
    public IReadOnlyList<(string Path, int Score)> Recall(string query, int limit = 8)
    {
        var qTokens = TokenRegex.Matches(query)
            .Select(m => m.Value)
            .Where(t => t.Length >= 3)
            .Select(t => t.ToLowerInvariant())
            .ToHashSet();
        if (qTokens.Count == 0) return [];

        var scored = new List<(string Path, int Score)>();
        lock (_gate)
        {
            foreach (var f in _files.Values)
            {
                var score = f.Tokens.Intersect(qTokens, StringComparer.OrdinalIgnoreCase).Count();
                var symbolHits = f.Symbols.Count(s => qTokens.Contains(s.ToLowerInvariant()));
                score += symbolHits * 2;
                if (score > 0) scored.Add((f.Path, score));
            }
        }
        scored.Sort((a, b) => b.Score.CompareTo(a.Score));
        return scored.Take(limit).ToList();
    }

    public int FileCount { get { lock (_gate) return _files.Count; } }
}
