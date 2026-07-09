using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Uses a per-test scratch AILOCAL_DATA_DIR so HostRegistry never touches a
/// real installation's overseer-hosts.json.
/// </summary>
public class HostRegistryTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _previousDataDir;

    public HostRegistryTests()
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

    [Fact]
    public void Remove_KnownHost_RemovesItFromAll()
    {
        var registry = new HostRegistry();
        registry.UpsertExplicit("http://192.168.1.50:5080");
        var id = registry.All.Single().Id;

        var removed = registry.Remove(id);

        Assert.True(removed);
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Remove_AcceptsHostPrefixedId_LikeTheTopologyView()
    {
        // AggregateTopology renders host ids as "host-{rawId}" - the remove
        // endpoint must accept that same prefixed form from the dashboard.
        var registry = new HostRegistry();
        registry.UpsertExplicit("http://192.168.1.50:5080");
        var id = registry.All.Single().Id;

        var removed = registry.Remove($"host-{id}");

        Assert.True(removed);
        Assert.Empty(registry.All);
    }

    [Fact]
    public void Remove_UnknownId_ReturnsFalseAndLeavesOthersUntouched()
    {
        var registry = new HostRegistry();
        registry.UpsertExplicit("http://192.168.1.50:5080");

        var removed = registry.Remove("not-a-real-id");

        Assert.False(removed);
        Assert.Single(registry.All);
    }

    [Fact]
    public void Remove_OneOfSeveralHosts_OnlyRemovesTheMatch()
    {
        var registry = new HostRegistry();
        registry.UpsertExplicit("http://192.168.1.50:5080");
        registry.UpsertExplicit("http://192.168.1.51:5080");
        var idToRemove = registry.All.First(h => h.Endpoint.EndsWith(":5080") && h.Endpoint.Contains("1.50")).Id;

        registry.Remove(idToRemove);

        Assert.Single(registry.All);
        Assert.Contains(registry.All, h => h.Endpoint.Contains("1.51"));
    }
}
