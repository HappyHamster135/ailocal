using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

public class SessionRunRegistryTests
{
    [Fact]
    public void TryBegin_NotAlreadyRunning_SucceedsWithALiveToken()
    {
        var registry = new SessionRunRegistry();

        var began = registry.TryBegin("s1", out var cts);

        Assert.True(began);
        Assert.NotNull(cts);
        Assert.False(cts.IsCancellationRequested);
        Assert.True(registry.IsRunning("s1"));
    }

    [Fact]
    public void TryBegin_AlreadyRunning_FailsWithoutDisturbingTheExistingRun()
    {
        var registry = new SessionRunRegistry();
        registry.TryBegin("s1", out var firstToken);

        var began = registry.TryBegin("s1", out var secondToken);

        Assert.False(began);
        Assert.Null(secondToken);
        Assert.False(firstToken.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_RunningSession_CancelsItsToken()
    {
        var registry = new SessionRunRegistry();
        registry.TryBegin("s1", out var cts);

        var cancelled = registry.Cancel("s1");

        Assert.True(cancelled);
        Assert.True(cts.IsCancellationRequested);
    }

    [Fact]
    public void Cancel_UnknownSession_ReturnsFalse()
    {
        var registry = new SessionRunRegistry();

        Assert.False(registry.Cancel("nonexistent"));
    }

    [Fact]
    public void End_AllowsARunToStartAgainForTheSameSession()
    {
        var registry = new SessionRunRegistry();
        registry.TryBegin("s1", out _);

        registry.End("s1");

        Assert.False(registry.IsRunning("s1"));
        Assert.True(registry.TryBegin("s1", out _));
    }

    [Fact]
    public void ActiveCount_ReflectsConcurrentSessions()
    {
        var registry = new SessionRunRegistry();
        registry.TryBegin("s1", out _);
        registry.TryBegin("s2", out _);

        Assert.Equal(2, registry.ActiveCount);

        registry.End("s1");

        Assert.Equal(1, registry.ActiveCount);
    }
}
