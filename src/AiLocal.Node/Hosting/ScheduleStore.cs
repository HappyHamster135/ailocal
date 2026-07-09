using System.Text.Json;

namespace AiLocal.Node.Hosting;

/// <summary>
/// A recurring goal template. Deliberately simple (interval-minutes or a
/// single daily time-of-day) rather than full cron syntax, so schedules are
/// easy to validate and reason about.
/// </summary>
public sealed class ScheduledGoal
{
    public required string Id { get; init; }
    public required string Name { get; set; }
    public required string Prompt { get; set; }
    public string? System { get; set; }
    public int? Parallelism { get; set; }

    /// <summary>Run every N minutes since the last run. Ignored if AtTimeOfDay is set.</summary>
    public int IntervalMinutes { get; set; } = 60;

    /// <summary>"HH:mm" in UTC - run once per day at this time instead of an interval.</summary>
    public string? AtTimeOfDay { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset? LastRunAt { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>Durable, atomically-written list of scheduled goals.</summary>
public sealed class ScheduleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private List<ScheduledGoal> _schedules;

    public ScheduleStore() => _schedules = Load();

    private static string FilePath => Path.Combine(SettingsPaths.DataDirectory, "schedules.json");

    public IReadOnlyList<ScheduledGoal> All()
    {
        lock (_gate)
            return [.. _schedules];
    }

    public ScheduledGoal? Get(string id)
    {
        lock (_gate)
            return _schedules.FirstOrDefault(s => s.Id == id);
    }

    public ScheduledGoal Create(
        string name, string prompt, string? system, int? parallelism, int intervalMinutes, string? atTimeOfDay)
    {
        var schedule = new ScheduledGoal
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            Name = name,
            Prompt = prompt,
            System = system,
            Parallelism = parallelism,
            IntervalMinutes = Math.Max(1, intervalMinutes),
            AtTimeOfDay = string.IsNullOrWhiteSpace(atTimeOfDay) ? null : atTimeOfDay
        };
        lock (_gate)
        {
            _schedules.Add(schedule);
            Save();
        }
        return schedule;
    }

    public bool Update(string id, Action<ScheduledGoal> apply)
    {
        lock (_gate)
        {
            var schedule = _schedules.FirstOrDefault(s => s.Id == id);
            if (schedule is null) return false;
            apply(schedule);
            Save();
            return true;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            var removed = _schedules.RemoveAll(s => s.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        var path = FilePath;
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_schedules, JsonOptions));
        File.Move(temp, path, overwrite: true);
    }

    private static List<ScheduledGoal> Load()
    {
        try
        {
            var path = FilePath;
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<ScheduledGoal>>(File.ReadAllText(path), JsonOptions) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
