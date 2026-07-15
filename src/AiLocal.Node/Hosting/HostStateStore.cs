using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Nodes;
using AiLocal.Node.Roles;

namespace AiLocal.Node.Hosting;

internal sealed class PersistedHostState
{
    public int SchemaVersion { get; set; } = 1;
    public List<NodeInfo> Nodes { get; set; } = [];
    public List<string> BlockedNodeIds { get; set; } = [];
    public List<AgentTask> Tasks { get; set; } = [];
    public List<ConversationEntry> Messages { get; set; } = [];
    public List<HostNotice> Notices { get; set; } = [];
    public List<BacklogItem> Backlog { get; set; } = [];
    public DateTimeOffset SavedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Durable Host state. Every mutation is flushed to a temporary file and then
/// atomically replaces the primary snapshot while retaining a backup.
/// </summary>
public sealed class HostStateStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly object _gate = new();
    private PersistedHostState _state;

    public HostStateStore()
    {
        _state = Load();
    }

    public string StateFile => Path.Combine(SettingsPaths.DataDirectory, "host-state.json");

    public IReadOnlyList<NodeInfo> ReadNodes()
    {
        lock (_gate)
            return [.. _state.Nodes];
    }

    public IReadOnlySet<string> ReadBlockedNodeIds()
    {
        lock (_gate)
            return new HashSet<string>(_state.BlockedNodeIds, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<AgentTask> ReadTasks()
    {
        lock (_gate)
            return [.. _state.Tasks];
    }

    public IReadOnlyList<ConversationEntry> ReadMessages()
    {
        lock (_gate)
            return [.. _state.Messages];
    }

    public void SaveNodes(IEnumerable<NodeInfo> nodes, IEnumerable<string> blockedNodeIds)
    {
        lock (_gate)
        {
            _state.Nodes = [.. nodes];
            _state.BlockedNodeIds = blockedNodeIds
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            SaveLocked();
        }
    }

    public void SaveTasks(IEnumerable<AgentTask> tasks)
    {
        lock (_gate)
        {
            _state.Tasks = [.. tasks];
            SaveLocked();
        }
    }

    public void SaveMessages(IEnumerable<ConversationEntry> messages)
    {
        lock (_gate)
        {
            _state.Messages = [.. messages];
            SaveLocked();
        }
    }

    public IReadOnlyList<HostNotice> ReadNotices()
    {
        lock (_gate)
            return [.. _state.Notices];
    }

    public void SaveNotices(IEnumerable<HostNotice> notices)
    {
        lock (_gate)
        {
            _state.Notices = [.. notices];
            SaveLocked();
        }
    }

    public IReadOnlyList<BacklogItem> ReadBacklog()
    {
        lock (_gate)
            return [.. _state.Backlog];
    }

    public void SaveBacklog(IEnumerable<BacklogItem> backlog)
    {
        lock (_gate)
        {
            _state.Backlog = [.. backlog];
            SaveLocked();
        }
    }

    private PersistedHostState Load()
    {
        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        var primary = Path.Combine(SettingsPaths.DataDirectory, "host-state.json");
        var backup = primary + ".bak";

        return TryRead(primary)
            ?? TryRead(backup)
            ?? new PersistedHostState();
    }

    private static PersistedHostState? TryRead(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            return JsonSerializer.Deserialize<PersistedHostState>(
                File.ReadAllText(path),
                JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private void SaveLocked()
    {
        Directory.CreateDirectory(SettingsPaths.DataDirectory);
        _state.SavedAt = DateTimeOffset.UtcNow;

        var primary = StateFile;
        var temporary = primary + ".tmp";
        var backup = primary + ".bak";
        var bytes = JsonSerializer.SerializeToUtf8Bytes(_state, JsonOptions);

        using (var stream = new FileStream(
            temporary,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.WriteThrough))
        {
            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }

        if (File.Exists(primary))
        {
            try
            {
                File.Replace(temporary, primary, backup, ignoreMetadataErrors: true);
                return;
            }
            catch (PlatformNotSupportedException)
            {
            }
            catch (IOException)
            {
            }
        }

        if (File.Exists(primary))
            File.Copy(primary, backup, overwrite: true);
        File.Move(temporary, primary, overwrite: true);
    }
}
