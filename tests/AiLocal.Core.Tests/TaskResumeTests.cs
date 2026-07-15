using AiLocal.Core.Contracts;
using AiLocal.Node.Roles;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AiLocal.Core.Tests;

public class TaskResumeTests
{
    [Fact]
    public void CoerceRestartState_PausesInFlightTasks_LeavesTerminalAlone()
    {
        var running = new AgentTask { Id = "a", Prompt = "p", State = TaskState.Running };
        var queued = new AgentTask { Id = "b", Prompt = "p", State = TaskState.Queued };
        var dispatched = new AgentTask { Id = "c", Prompt = "p", State = TaskState.Dispatched };
        var pending = new AgentTask { Id = "d", Prompt = "p", State = TaskState.Pending };

        var completed = new AgentTask { Id = "e", Prompt = "p", State = TaskState.Completed };
        var failed = new AgentTask { Id = "f", Prompt = "p", State = TaskState.Failed };
        var cancelled = new AgentTask { Id = "g", Prompt = "p", State = TaskState.Cancelled };
        var alreadyPaused = new AgentTask { Id = "h", Prompt = "p", State = TaskState.Paused };

        foreach (var t in new[] { running, queued, dispatched, pending, completed, failed, cancelled, alreadyPaused })
            TaskBoard.CoerceRestartState(t);

        Assert.Equal(TaskState.Paused, running.State);
        Assert.Equal(TaskState.Paused, queued.State);
        Assert.Equal(TaskState.Paused, dispatched.State);
        Assert.Equal(TaskState.Paused, pending.State);

        // Terminal states must survive a restart untouched (no data loss).
        Assert.Equal(TaskState.Completed, completed.State);
        Assert.Equal(TaskState.Failed, failed.State);
        Assert.Equal(TaskState.Cancelled, cancelled.State);
        Assert.Equal(TaskState.Paused, alreadyPaused.State);

        // A paused task records no completion timestamp.
        Assert.Null(running.CompletedAt);
    }

    [Fact]
    public void PausedState_RoundTripsThroughJson()
    {
        var task = new AgentTask { Id = "x", Prompt = "p", State = TaskState.Paused, EscalationCount = 1, Parallelism = 3 };
        var json = System.Text.Json.JsonSerializer.Serialize(task);
        var back = System.Text.Json.JsonSerializer.Deserialize<AgentTask>(json);

        Assert.Equal(TaskState.Paused, back!.State);
        Assert.Equal(1, back.EscalationCount);
        Assert.Equal(3, back.Parallelism);
    }
}

public class EscalationTests
{
    private sealed class NoopLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => false;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) { }
    }
    private static readonly ILogger Log = new NoopLogger();

    [Fact]
    public void TryEscalate_BumpsComplexityAndResetsRetries()
    {
        var settings = new AiLocal.Core.Configuration.NodeSettings().Host;
        var task = new AgentTask { Id = "t", Prompt = "p", Complexity = 3, OriginalComplexity = 3 };

        var ok = HostRole.TryEscalate(task, settings, Log);

        Assert.True(ok);
        Assert.Equal(1, task.EscalationCount);
        Assert.Equal(4, task.Complexity);
        Assert.Equal(0, task.RetryCount);
    }

    [Fact]
    public void TryEscalate_StopsAtComplexityFive()
    {
        var settings = new AiLocal.Core.Configuration.NodeSettings().Host;
        var task = new AgentTask { Id = "t", Prompt = "p", Complexity = 5, EscalationCount = 0 };

        var ok = HostRole.TryEscalate(task, settings, Log);

        Assert.False(ok);
        Assert.Equal(5, task.Complexity);
        Assert.Equal(0, task.EscalationCount);
    }

    [Fact]
    public void TryEscalate_StopsAfterMaxEscalations()
    {
        var settings = new AiLocal.Core.Configuration.NodeSettings().Host;
        var task = new AgentTask { Id = "t", Prompt = "p", Complexity = 3, EscalationCount = settings.MaxEscalations };

        var ok = HostRole.TryEscalate(task, settings, Log);

        Assert.False(ok);
        Assert.Equal(3, task.Complexity);
    }
}
