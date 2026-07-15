using System.Text.Json;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;

namespace AiLocal.Node.Hosting;

/// <summary>A folder-bound, resumable agent session - the local (this-machine-
/// only, see SessionRunRegistry) counterpart to the Host-mediated Assignment/
/// GoalPlanner flow. Messages is the exact conversation AgentLoop.RunAsync
/// returns/accepts, so resuming is just passing it back in as `history`.
/// Whether a run is currently in progress is deliberately NOT a field here -
/// that's transient, in-memory-only state (see SessionRunRegistry); a
/// persisted "running" flag would go stale the moment the process crashed
/// mid-run, falsely claiming a run is still active on the next launch.</summary>
public sealed class Session
{
    public required string Id { get; init; }
    public required string Title { get; set; }
    public required string FolderPath { get; init; }
    public bool Pinned { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastActiveAt { get; set; } = DateTimeOffset.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];
    public TokenUsage TotalUsage { get; set; } = TokenUsage.Zero;
}

/// <summary>
/// Durable per-role session store - CRUD shaped like ScheduleStore, write
/// durability shaped like HostStateStore (a session's conversation history is
/// real user data worth protecting, unlike a recreatable schedule
/// definition), corruption-logging on read shaped like
/// PersistentSettingsStore. One file per role (not one shared file), because
/// two role-processes can run on the same machine at once (e.g. Quickstart's
/// Host+Worker) with independent NodeIds already - a single shared file would
/// let them race the same session record with no lock between separate
/// processes.
/// </summary>
public sealed class SessionStore
{
    private const int MaxSessions = 200;
    private const int MaxMessagesPerSession = 500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _filePath;
    private List<Session> _sessions;

    public SessionStore(NodeSettings settings)
    {
        _filePath = Path.Combine(SettingsPaths.DataDirectory, $"{settings.Role.ToString().ToLowerInvariant()}.sessions.json");
        _sessions = Load();
    }

    /// <summary>Light enough for a sidebar list - full Messages are fetched
    /// via Get(id) only when a session is actually opened.</summary>
    public IReadOnlyList<Session> All()
    {
        lock (_gate)
            return _sessions.OrderByDescending(s => s.LastActiveAt).ToList();
    }

    public Session? Get(string id)
    {
        lock (_gate)
            return _sessions.FirstOrDefault(s => s.Id == id);
    }

    public Session Create(string folderPath, string? title)
    {
        lock (_gate)
        {
            var session = new Session
            {
                Id = Guid.NewGuid().ToString("n")[..8],
                Title = string.IsNullOrWhiteSpace(title) ? DefaultTitle(folderPath) : title.Trim(),
                FolderPath = folderPath
            };
            _sessions.Add(session);
            Prune();
            Save();
            return session;
        }
    }

    public bool Update(string id, Action<Session> apply)
    {
        lock (_gate)
        {
            var session = _sessions.FirstOrDefault(s => s.Id == id);
            if (session is null) return false;
            apply(session);
            TrimMessages(session);
            Save();
            return true;
        }
    }

    public bool Remove(string id)
    {
        lock (_gate)
        {
            // Never touches the folder or its files - only forgets the
            // record, same as removing a Worker never touches its data.
            var removed = _sessions.RemoveAll(s => s.Id == id) > 0;
            if (removed) Save();
            return removed;
        }
    }

    private static string DefaultTitle(string folderPath)
    {
        var name = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? folderPath : name;
    }

    /// <summary>Session-count cap: evicts the oldest-inactive non-pinned
    /// sessions first. Pinned sessions are fully exempt.</summary>
    private void Prune()
    {
        if (_sessions.Count > MaxSessions)
        {
            var overflow = _sessions.Count - MaxSessions;
            var evictable = _sessions.Where(s => !s.Pinned).OrderBy(s => s.LastActiveAt).Take(overflow).ToList();
            foreach (var session in evictable)
                _sessions.Remove(session);
        }

        foreach (var session in _sessions)
            TrimMessages(session);
    }

    /// <summary>Trims in whole conversation turns (a user message through
    /// everything up to the next user message), never mid tool-call - a
    /// dangling tool_call with no matching tool-role reply breaks the next
    /// request to most providers. If even the newest turn alone exceeds the
    /// cap, nothing is trimmed (an over-budget but coherent conversation
    /// beats a corrupted one).</summary>
    private static void TrimMessages(Session session)
    {
        if (session.Messages.Count <= MaxMessagesPerSession)
            return;

        var turnStarts = new List<int>();
        for (var i = 0; i < session.Messages.Count; i++)
        {
            if (session.Messages[i].Role == "user")
                turnStarts.Add(i);
        }

        foreach (var start in turnStarts)
        {
            if (session.Messages.Count - start > MaxMessagesPerSession)
                continue;
            session.Messages = session.Messages.Skip(start).ToList();
            return;
        }
    }

    private List<Session> Load()
    {
        return TryRead(_filePath) ?? TryRead(_filePath + ".bak") ?? [];
    }

    private List<Session>? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<List<Session>>(File.ReadAllText(path), JsonOptions);
        }
        catch (Exception ex)
        {
            CrashLog.Write($"SessionsCorrupted({Path.GetFileName(path)})", ex);
            return null;
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        var temporary = _filePath + ".tmp";
        var backup = _filePath + ".bak";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_sessions, JsonOptions);

        using (var stream = new FileStream(
            temporary, FileMode.Create, FileAccess.Write, FileShare.None,
            4096, FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(_filePath))
        {
            try
            {
                File.Replace(temporary, _filePath, backup, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException) { }
            catch (IOException) { }
        }

        if (File.Exists(_filePath))
            File.Copy(_filePath, backup, overwrite: true);
        File.Move(temporary, _filePath, overwrite: true);
    }
}
