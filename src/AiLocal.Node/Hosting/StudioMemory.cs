using System.Text.Json;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

public sealed record StudioLesson(string Genre, string Text, long Ticks);

/// <summary>
/// The studio's LONG-TERM, cross-project memory - its institutional knowledge.
/// ProjectMemory is per-workspace; this is node-wide and keyed by genre: "the
/// last football-manager got dinged for unclear instructions", "top-down games
/// tended to be too hard". The director reads the lessons for a genre BEFORE
/// building the next game of that genre, so quality climbs release over release
/// instead of repeating the same mistakes. Persisted to the data dir, deduped
/// per (genre, text), most-recent-wins, capped. Never throws on IO.
/// </summary>
public sealed class StudioMemory
{
    private const int Cap = 200;
    private readonly string _path;
    private readonly object _lock = new();
    private List<StudioLesson> _lessons;

    public StudioMemory() : this(Path.Combine(SettingsPaths.DataDirectory, "studio-memory.json")) { }

    public StudioMemory(string path)
    {
        _path = path;
        _lessons = Load(_path);
    }

    /// <summary>Remember a lesson for a genre (a recurring finding/critique). The
    /// same lesson just refreshes its recency rather than duplicating.</summary>
    public void Record(string genre, string lesson)
    {
        var g = Norm(genre);
        var text = (lesson ?? "").Trim();
        if (text.Length == 0) return;
        if (text.Length > 240) text = text[..240];
        lock (_lock)
        {
            _lessons.RemoveAll(l => l.Genre == g && l.Text.Equals(text, StringComparison.OrdinalIgnoreCase));
            _lessons.Add(new StudioLesson(g, text, DateTime.UtcNow.Ticks));
            if (_lessons.Count > Cap) _lessons.RemoveRange(0, _lessons.Count - Cap);
            Save();
        }
    }

    /// <summary>The most recent distinct lessons for a genre - what the director
    /// should keep in mind this time.</summary>
    public IReadOnlyList<string> LessonsFor(string genre, int max = 5)
    {
        var g = Norm(genre);
        lock (_lock)
            return _lessons
                .Where(l => l.Genre == g)
                .OrderByDescending(l => l.Ticks)
                .Take(Math.Max(0, max))
                .Select(l => l.Text)
                .ToList();
    }

    private static string Norm(string? genre) => (genre ?? "general").Trim().ToLowerInvariant();

    private static List<StudioLesson> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<StudioLesson>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_lessons));
        }
        catch
        {
            // Studiominnet är en bekvämlighet - fylld disk får inte krascha noden.
        }
    }
}
