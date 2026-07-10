using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Uses a per-test scratch AILOCAL_DATA_DIR so HostStateStore never touches a
/// real installation's host-state.json. [Collection("EnvIsolated")] keeps
/// this from running concurrently with other classes that also mutate that
/// same process-wide environment variable (e.g. HostRegistryTests) - xUnit
/// only guarantees sequential execution within one class by default, not
/// across classes, and two racing to set/restore it corrupts each other.
/// </summary>
[Collection("EnvIsolated")]
public class WorkerRegistryTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _previousDataDir;

    public WorkerRegistryTests()
    {
        _previousDataDir = Environment.GetEnvironmentVariable("AILOCAL_DATA_DIR");
        _dataDir = Path.Combine(Path.GetTempPath(), "ailocal-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _previousDataDir);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static NodeInfo Heartbeat(string id) => new()
    {
        Id = id,
        Name = "worker-1",
        Role = NodeRole.Worker,
        Endpoint = "http://127.0.0.1:5081",
        MaxConcurrentTasks = 1
    };

    [Fact]
    public void Upsert_ReRegistration_KeepsSameObjectIdentity()
    {
        var registry = new WorkerRegistry(new HostStateStore());

        registry.Upsert(Heartbeat("w1"));
        var first = registry.Get("w1");

        // Simulates the Worker's ~15s heartbeat re-registering while a
        // dispatch is in flight and holding a reference to `first`.
        registry.Upsert(Heartbeat("w1"));
        var second = registry.Get("w1");

        Assert.Same(first, second);
    }

    [Fact]
    public void Upsert_ReRegistrationMidDispatch_DoesNotLoseCapacityRelease()
    {
        // Reproduces the reported bug: a slot is claimed (ActiveTasks++), a
        // heartbeat lands mid-dispatch, then the dispatch finishes and
        // releases its slot (ActiveTasks--) via the reference it was handed
        // at claim time. That release must be visible through the registry,
        // not silently lost because Upsert swapped in a new NodeInfo object.
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        var claimed = registry.Get("w1")!;
        claimed.ActiveTasks++;
        claimed.Status = NodeStatus.Busy;

        // Heartbeat arrives while the task above is still "in flight".
        registry.Upsert(Heartbeat("w1"));

        // Dispatch finishes; releases the slot via its original reference.
        claimed.ActiveTasks--;
        claimed.Status = NodeStatus.Idle;

        var live = registry.Get("w1")!;
        Assert.Equal(0, live.ActiveTasks);
        Assert.Equal(NodeStatus.Idle, live.Status);
        Assert.True(registry.AvailableWorkers().Single().ActiveTasks == 0);
    }

    [Fact]
    public void Upsert_PreservesActiveTasksAcrossHeartbeat_WhenGenuinelyBusy()
    {
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        registry.Get("w1")!.ActiveTasks = 1;

        // A Worker's own heartbeat payload never reports ActiveTasks (it's
        // Host-tracked), so a naive Upsert would reset it to 0 here.
        registry.Upsert(Heartbeat("w1"));

        Assert.Equal(1, registry.Get("w1")!.ActiveTasks);
        Assert.Equal(NodeStatus.Busy, registry.Get("w1")!.Status);
    }

    [Fact]
    public void Upsert_IdleWorkerFlaggedOfflineByStaleness_RecoversOnNextHeartbeat()
    {
        // Reproduces a second reported bug: a Worker that ever misses one
        // heartbeat window gets flagged Offline by MarkStale. Its next
        // successful heartbeat must clear that flag - an idle worker (no
        // ActiveTasks to "upgrade" it back to Busy) must not stay stuck
        // Offline forever just because it was never busy.
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        var node = registry.Get("w1")!;
        node.Status = NodeStatus.Offline; // simulates MarkStale() after a missed window
        node.ActiveTasks = 0;

        registry.Upsert(Heartbeat("w1")); // Worker successfully re-registers

        Assert.Equal(NodeStatus.Idle, registry.Get("w1")!.Status);
        Assert.Contains(registry.Get("w1")!, registry.AvailableWorkers());
    }

    [Fact]
    public void Upsert_CarriesForwardHealthHistoryAcrossReRegistration()
    {
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        var node = registry.Get("w1")!;
        node.SuccessCount = 9;
        node.FailureCount = 1;
        node.AvgLatencyMs = 250;

        registry.Upsert(Heartbeat("w1"));

        var live = registry.Get("w1")!;
        Assert.Equal(9, live.SuccessCount);
        Assert.Equal(1, live.FailureCount);
        Assert.Equal(250, live.AvgLatencyMs);
    }

    [Fact]
    public void Upsert_UpdatesEndpointAndName_OnReRegistration()
    {
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        registry.Upsert(new NodeInfo
        {
            Id = "w1",
            Name = "renamed-worker",
            Role = NodeRole.Worker,
            Endpoint = "http://127.0.0.1:9999",
            MaxConcurrentTasks = 1
        });

        var live = registry.Get("w1")!;
        Assert.Equal("renamed-worker", live.Name);
        Assert.Equal("http://127.0.0.1:9999", live.Endpoint);
    }

    [Fact]
    public void Remove_BlocksTheIdFromReRegistering()
    {
        // "Ta bort fran gruppen" in the dashboard - removeNodeFromCluster's
        // own confirm dialog says the Worker can't register again until
        // restored. A heartbeat landing after Remove (HostRole's
        // POST /cluster/register handler) must be rejected, not silently
        // re-added.
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));

        var removed = registry.Remove("w1");
        var reRegistered = registry.Upsert(Heartbeat("w1"));

        Assert.True(removed);
        Assert.False(reRegistered);
        Assert.Null(registry.Get("w1"));
    }

    [Fact]
    public void Restore_UnblocksAPreviouslyRemovedId_AllowingReRegistration()
    {
        // Regression coverage for the v1.2.6 fix: a click-to-pair handshake
        // completing (HostRole's /pairing/approved, after the nonce has
        // verified both sides explicitly re-consented) calls Restore() so a
        // Worker removed long ago isn't stuck rejecting its heartbeat forever
        // with no way to fix it from the UI.
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));
        registry.Remove("w1");

        var restored = registry.Restore("w1");
        var reRegistered = registry.Upsert(Heartbeat("w1"));

        Assert.True(restored);
        Assert.True(reRegistered);
        Assert.NotNull(registry.Get("w1"));
    }

    [Fact]
    public void Restore_IdThatWasNeverBlocked_ReturnsFalse()
    {
        var registry = new WorkerRegistry(new HostStateStore());

        Assert.False(registry.Restore("never-existed"));
    }

    [Fact]
    public void Remove_OnlyBlocksTheRemovedId_OtherWorkersUnaffected()
    {
        var registry = new WorkerRegistry(new HostStateStore());
        registry.Upsert(Heartbeat("w1"));
        registry.Upsert(new NodeInfo
        {
            Id = "w2",
            Name = "worker-2",
            Role = NodeRole.Worker,
            Endpoint = "http://127.0.0.1:5082",
            MaxConcurrentTasks = 1
        });

        registry.Remove("w1");
        var stillWorks = registry.Upsert(new NodeInfo
        {
            Id = "w2",
            Name = "worker-2",
            Role = NodeRole.Worker,
            Endpoint = "http://127.0.0.1:5082",
            MaxConcurrentTasks = 1
        });

        Assert.True(stillWorks);
        Assert.NotNull(registry.Get("w2"));
    }
}
