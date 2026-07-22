using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

/// <summary>One recorded assignment run: the prompt, every streamed step
/// (thinking/tool rows), and the outcome. PascalCase on the wire so the
/// dashboard can reuse the exact same step-rendering it uses for live SSE
/// frames (which serialize AgentStep with default options).</summary>
public sealed class AssignmentLogEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("n");
    public string Prompt { get; set; } = "";
    public string? WorkerName { get; set; }
    public string State { get; set; } = "Running"; // Running | Completed | Failed
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public List<AgentStep> Steps { get; set; } = [];
    public string? FinalAnswer { get; set; }
    public string? PreviewPath { get; set; }
    public string? ArtifactPath { get; set; }
    /// <summary>v1.87 (C5+): projektmappen (relativ arbetsytan) körningen
    /// jobbade i - sätts så fort projektroten är känd, så ett AVBRUTET bygge
    /// (nodomstart/krasch) kan återupptas mot samma projekt och kontrakt.</summary>
    public string? ProjectRel { get; set; }
}

/// <summary>
/// Persisted history of assignment runs on THIS node. Before this, the whole
/// step log (the agent's visible thinking and tool rows) lived only in the
/// dashboard tab's JS state: a page reload, a stray mouse-4 back-navigation
/// or an app relaunch showed at most a bare final answer, and an ongoing
/// run looked like nothing was happening at all (user report). The dashboard
/// rehydrates from GET /api/assignment-log at startup and polls it while an
/// entry is still Running, so the display survives anything short of the
/// node itself dying - and after a node restart, stale Running entries are
/// marked Failed instead of spinning forever.
/// </summary>
public sealed class AssignmentLog
{
    private const int MaxEntries = 40;
    private const int MaxStepsPerEntry = 400;
    private const int MaxStepDetailChars = 4000;
    private const int FlushEveryNSteps = 10;

    private readonly object _lock = new();
    private readonly List<AssignmentLogEntry> _entries;
    private readonly string _path;

    public AssignmentLog() : this(Path.Combine(SettingsPaths.DataDirectory, "assignment-log.json")) { }

    public AssignmentLog(string path)
    {
        _path = path;
        _entries = Load(path);
        // Ett Running-inlagg pa disk betyder att noden dog/startades om mitt
        // i korningen - markera det arligt i stallet for evig spinner.
        var changed = false;
        foreach (var entry in _entries.Where(e => e.State == "Running"))
        {
            entry.State = "Failed";
            entry.FinishedAt ??= DateTimeOffset.UtcNow;
            entry.FinalAnswer ??= "Noden startades om innan körningen hann bli klar.";
            changed = true;
        }
        if (changed) Save();
    }

    public int RunningCount
    {
        get { lock (_lock) return _entries.Count(e => e.State == "Running"); }
    }

    public AssignmentLogEntry Begin(string prompt, string? workerName)
    {
        var entry = new AssignmentLogEntry
        {
            Prompt = prompt.Length > 2000 ? prompt[..2000] + "…" : prompt,
            WorkerName = workerName
        };
        lock (_lock)
        {
            _entries.Add(entry);
            while (_entries.Count > MaxEntries) _entries.RemoveAt(0);
            Save();
        }
        return entry;
    }

    public void AddStep(AssignmentLogEntry entry, string kind, string detail)
    {
        if (detail.Length > MaxStepDetailChars) detail = detail[..MaxStepDetailChars] + "…";
        lock (_lock)
        {
            if (entry.Steps.Count >= MaxStepsPerEntry) return;
            entry.Steps.Add(new AgentStep(kind, detail));
            if (entry.Steps.Count % FlushEveryNSteps == 0) Save();
        }
    }

    /// <summary>Sätts så fort projektroten är känd (INTE först vid Complete) -
    /// annars saknar just de avbrutna körningarna, de som behöver återupptas,
    /// sin projektmapp.</summary>
    public void SetProject(AssignmentLogEntry entry, string? projectRel)
    {
        if (string.IsNullOrWhiteSpace(projectRel)) return;
        lock (_lock)
        {
            entry.ProjectRel = projectRel;
            Save();
        }
    }

    public void Complete(AssignmentLogEntry entry, bool success, string? finalAnswer, string? previewPath, string? artifactPath = null, string? projectRel = null)
    {
        lock (_lock)
        {
            entry.State = success ? "Completed" : "Failed";
            entry.FinishedAt = DateTimeOffset.UtcNow;
            entry.FinalAnswer = finalAnswer;
            entry.PreviewPath = previewPath;
            entry.ArtifactPath = artifactPath;
            if (!string.IsNullOrWhiteSpace(projectRel)) entry.ProjectRel = projectRel;
            Save();
        }
    }

    /// <summary>Newest first - the dashboard renders oldest-at-top itself.</summary>
    public IReadOnlyList<AssignmentLogEntry> Snapshot()
    {
        lock (_lock)
        {
            // Djup nog kopia for trad-sakerhet: Steps-listan muteras av
            // AddStep medan en GET serialiserar - dela aldrig referensen.
            return _entries
                .OrderByDescending(e => e.StartedAt)
                .Select(e => new AssignmentLogEntry
                {
                    Id = e.Id,
                    Prompt = e.Prompt,
                    WorkerName = e.WorkerName,
                    State = e.State,
                    StartedAt = e.StartedAt,
                    FinishedAt = e.FinishedAt,
                    Steps = [.. e.Steps],
                    FinalAnswer = e.FinalAnswer,
                    PreviewPath = e.PreviewPath,
                    ArtifactPath = e.ArtifactPath,
                    ProjectRel = e.ProjectRel
                })
                .ToList();
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_entries));
            File.Move(tmp, _path, overwrite: true);
        }
        catch
        {
            // Historik ar en bekvamlighet - fylld disk/laslas far aldrig
            // krascha sjalva korningen som loggar.
        }
    }

    private static List<AssignmentLogEntry> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<AssignmentLogEntry>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
