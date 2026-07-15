using System.Text;

namespace AiLocal.Core.Agent;

/// <summary>
/// Persistent, append-only project memory stored as a markdown file in the
/// workspace (.ailocal-memory.md). Agents use it to remember decisions,
/// gotchas and conventions across sessions - the "grows per project" memory
/// the company builds up over time. Recall returns the whole file (it stays
/// small by design); Remember appends a dated entry.
/// </summary>
public sealed class ProjectMemory
{
    private static readonly string FileName = ".ailocal-memory.md";

    private readonly string _path;
    private readonly object _gate = new();

    public ProjectMemory(string workspaceRoot)
    {
        _path = Path.Combine(workspaceRoot, FileName);
    }

    public string Read()
    {
        lock (_gate)
            return File.Exists(_path) ? File.ReadAllText(_path) : "";
    }

    public void Remember(string note)
    {
        if (string.IsNullOrWhiteSpace(note)) return;
        var entry = $"\n## {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC\n{note.Trim()}\n";
        lock (_gate)
        {
            File.AppendAllText(_path, entry, Encoding.UTF8);
        }
    }

    public void Clear()
    {
        lock (_gate)
            if (File.Exists(_path)) File.WriteAllText(_path, "", Encoding.UTF8);
    }
}
