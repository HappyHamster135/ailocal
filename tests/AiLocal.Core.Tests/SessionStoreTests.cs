using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Uses a per-test scratch AILOCAL_DATA_DIR so SessionStore never
/// touches a real installation's *.sessions.json.</summary>
[Collection("EnvIsolated")]
public class SessionStoreTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _previousDataDir;

    public SessionStoreTests()
    {
        _previousDataDir = Environment.GetEnvironmentVariable("AILOCAL_DATA_DIR");
        _dataDir = Path.Combine(Path.GetTempPath(), "ailocal-session-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _previousDataDir);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static SessionStore NewStore(NodeRole role = NodeRole.Worker) =>
        new(new NodeSettings { Role = role });

    [Fact]
    public void Create_NoTitleGiven_DerivesOneFromTheFolderName()
    {
        var store = NewStore();

        var session = store.Create(Path.Combine("C:", "projects", "my-app"), title: null);

        Assert.Equal("my-app", session.Title);
        Assert.NotEmpty(session.Id);
        Assert.Empty(session.Messages);
    }

    [Fact]
    public void Create_TitleGiven_UsesItTrimmed()
    {
        var store = NewStore();

        var session = store.Create(@"C:\projects\my-app", "  My Project  ");

        Assert.Equal("My Project", session.Title);
    }

    [Fact]
    public void Get_ReturnsFullSessionIncludingMessages()
    {
        var store = NewStore();
        var created = store.Create(@"C:\projects\a", null);
        store.Update(created.Id, s => s.Messages.Add(new ChatMessage("user", "hi")));

        var fetched = store.Get(created.Id);

        Assert.NotNull(fetched);
        Assert.Single(fetched!.Messages);
        Assert.Equal("hi", fetched.Messages[0].Content);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        var store = NewStore();

        Assert.Null(store.Get("nonexistent"));
    }

    [Fact]
    public void All_OrdersByLastActiveAtDescending()
    {
        var store = NewStore();
        var older = store.Create(@"C:\a", null);
        var newer = store.Create(@"C:\b", null);
        store.Update(older.Id, s => s.LastActiveAt = DateTimeOffset.UtcNow.AddDays(-1));
        store.Update(newer.Id, s => s.LastActiveAt = DateTimeOffset.UtcNow);

        var all = store.All();

        Assert.Equal(newer.Id, all[0].Id);
        Assert.Equal(older.Id, all[1].Id);
    }

    [Fact]
    public void Update_UnknownId_ReturnsFalseWithoutThrowing()
    {
        var store = NewStore();

        Assert.False(store.Update("nonexistent", s => s.Title = "x"));
    }

    [Fact]
    public void Remove_KnownSession_RemovesItButNeverTouchesTheFolder()
    {
        var store = NewStore();
        var folder = Path.Combine(_dataDir, "a-real-folder");
        Directory.CreateDirectory(folder);
        var session = store.Create(folder, null);

        var removed = store.Remove(session.Id);

        Assert.True(removed);
        Assert.Null(store.Get(session.Id));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalse()
    {
        var store = NewStore();

        Assert.False(store.Remove("nonexistent"));
    }

    [Fact]
    public void Persistence_SurvivesAcrossStoreInstances()
    {
        var first = NewStore();
        var created = first.Create(@"C:\projects\durable", "Durable session");
        first.Update(created.Id, s =>
        {
            s.Messages.Add(new ChatMessage("user", "remember this"));
            s.Pinned = true;
        });

        var second = NewStore();
        var reloaded = second.Get(created.Id);

        Assert.NotNull(reloaded);
        Assert.Equal("Durable session", reloaded!.Title);
        Assert.True(reloaded.Pinned);
        Assert.Single(reloaded.Messages);
        Assert.Equal("remember this", reloaded.Messages[0].Content);
    }

    [Fact]
    public void DifferentRoles_UseSeparateFiles_NoCrossContamination()
    {
        var workerStore = NewStore(NodeRole.Worker);
        var hostStore = NewStore(NodeRole.Host);
        workerStore.Create(@"C:\worker-only", null);

        Assert.Single(workerStore.All());
        Assert.Empty(hostStore.All());
    }

    [Fact]
    public void CorruptedPrimaryFile_FallsBackToBackup()
    {
        var first = NewStore();
        var created = first.Create(@"C:\projects\recoverable", "Recoverable");

        var primaryPath = Directory.GetFiles(_dataDir, "*.sessions.json").Single(p => !p.EndsWith(".bak"));
        // Force a second save so a real .bak snapshot exists, then corrupt
        // only the primary - Load() must fall back to .bak rather than
        // silently starting from an empty session list.
        first.Update(created.Id, s => s.Title = "Recoverable v2");
        File.WriteAllText(primaryPath, "{ not valid json at all");

        var second = NewStore();

        Assert.NotEmpty(second.All());
    }

    [Fact]
    public void SessionCountCap_EvictsOldestInactiveNonPinnedFirst()
    {
        var store = NewStore();
        var pinned = store.Create(@"C:\pinned", "pinned");
        store.Update(pinned.Id, s => { s.Pinned = true; s.LastActiveAt = DateTimeOffset.UtcNow.AddYears(-1); });

        // One more than the 200-session cap, all older than "pinned" and all
        // unpinned, so eviction has to remove some of THESE, never "pinned".
        for (var i = 0; i < 200; i++)
        {
            var s = store.Create($@"C:\bulk-{i}", $"bulk-{i}");
            store.Update(s.Id, x => x.LastActiveAt = DateTimeOffset.UtcNow.AddDays(-2));
        }

        var all = store.All();
        Assert.True(all.Count <= 200, $"expected the store to stay at or under the cap, had {all.Count}");
        Assert.Contains(all, s => s.Id == pinned.Id);
    }

    [Fact]
    public void MessageTrim_StaysUnderTheCap_WithoutBreakingAToolCallMidTurn()
    {
        var store = NewStore();
        var session = store.Create(@"C:\chatty", null);

        store.Update(session.Id, s =>
        {
            // 600 messages across many turns, including tool_call/tool pairs
            // that must never end up separated by the trim.
            for (var turn = 0; turn < 200; turn++)
            {
                s.Messages.Add(new ChatMessage("user", $"turn {turn}"));
                s.Messages.Add(new ChatMessage("assistant", "working on it",
                    ToolCalls: [new ToolCall($"call-{turn}", "list_files", "{}")]));
                s.Messages.Add(new ChatMessage("tool", "result", ToolCallId: $"call-{turn}", ToolName: "list_files"));
            }
        });

        var trimmed = store.Get(session.Id)!;

        Assert.True(trimmed.Messages.Count <= 500, $"expected trimming to a cap of 500, had {trimmed.Messages.Count}");
        Assert.Equal("user", trimmed.Messages[0].Role);
        // No assistant turn with tool calls should appear without its
        // matching tool-role result immediately after it.
        for (var i = 0; i < trimmed.Messages.Count; i++)
        {
            if (trimmed.Messages[i].ToolCalls is { Count: > 0 })
                Assert.Equal("tool", trimmed.Messages[i + 1].Role);
        }
    }
}
