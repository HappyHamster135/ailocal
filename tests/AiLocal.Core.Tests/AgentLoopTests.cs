using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Drives AgentLoop with a scripted FakeChatProvider and a REAL
/// AgentToolExecutor against a real temp workspace, so tool execution is
/// genuinely exercised end to end (file actually gets written, etc.) rather
/// than mocked away - only the model's responses are scripted, since that's
/// the one part no test double can avoid faking.
/// </summary>
public class AgentLoopTests : IDisposable
{
    private readonly string _workspace;

    public AgentLoopTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ailocal-agentloop-tests-" + Guid.NewGuid().ToString("n"));
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* best effort */ }
    }

    private AgentToolExecutor Sandboxed() => new(AgentAccessLevel.Sandboxed, _workspace);

    [Fact]
    public async Task RunAsync_ModelAnswersImmediately_CompletesInOneIteration()
    {
        var provider = FakeChatProvider.Success("test", "The answer is 42.");
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        var result = await loop.RunAsync("what is the answer?", AgentAccessLevel.Sandboxed);

        Assert.True(result.Success);
        Assert.Equal("The answer is 42.", result.FinalAnswer);
        Assert.Equal(1, result.Iterations);
        Assert.Equal(1, provider.CallCount);
        Assert.Contains(result.Steps, s => s.Kind == "done");
    }

    [Fact]
    public async Task RunAsync_OffAccessLevel_RefusesWithoutCallingProvider()
    {
        var provider = FakeChatProvider.Success("test", "should never be reached");
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        var result = await loop.RunAsync("do something", AgentAccessLevel.Off);

        Assert.False(result.Success);
        Assert.Equal(0, provider.CallCount);
        Assert.Contains("not enabled", result.FinalAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_OneToolCallThenFinalAnswer_ExecutesToolForRealAndCompletes()
    {
        var callCount = 0;
        var provider = new FakeChatProvider("test", false, request =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "I'll write the file first.",
                    Model = "test-model",
                    Provider = "test",
                    ToolCalls = [new ToolCall("call_1", "write_file", """{"path":"result.txt","content":"agent was here"}""")]
                });

            // Second call: the tool result should now be in the conversation.
            var toolResultMessage = request.Messages.Last();
            Assert.Equal("tool", toolResultMessage.Role);
            Assert.Contains("wrote", toolResultMessage.Content);

            return ProviderResponse.Ok(new ChatResponse
            {
                Content = "Done - file written.",
                Model = "test-model",
                Provider = "test"
            });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("write a file", AgentAccessLevel.Sandboxed);

        Assert.True(result.Success);
        Assert.Equal("Done - file written.", result.FinalAnswer);
        Assert.Equal(2, result.Iterations);
        Assert.True(File.Exists(Path.Combine(_workspace, "result.txt")));
        Assert.Equal("agent was here", await File.ReadAllTextAsync(Path.Combine(_workspace, "result.txt")));

        var kinds = result.Steps.Select(s => s.Kind).ToList();
        Assert.Contains("tool_call", kinds);
        Assert.Contains("tool_result", kinds);
        Assert.Contains("done", kinds);
    }

    [Fact]
    public async Task RunAsync_MultipleToolCallsInOneRound_AllExecute()
    {
        var callCount = 0;
        var provider = new FakeChatProvider("test", false, _ =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "",
                    Model = "m", Provider = "test",
                    ToolCalls =
                    [
                        new ToolCall("call_1", "write_file", """{"path":"a.txt","content":"A"}"""),
                        new ToolCall("call_2", "write_file", """{"path":"b.txt","content":"B"}""")
                    ]
                });
            return ProviderResponse.Ok(new ChatResponse { Content = "both written", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("write two files", AgentAccessLevel.Sandboxed);

        Assert.True(result.Success);
        Assert.True(File.Exists(Path.Combine(_workspace, "a.txt")));
        Assert.True(File.Exists(Path.Combine(_workspace, "b.txt")));
        Assert.Equal(2, result.Steps.Count(s => s.Kind == "tool_call"));
    }

    [Fact]
    public async Task RunAsync_ToolExecutionError_FeedsErrorBackInsteadOfCrashing()
    {
        var callCount = 0;
        var provider = new FakeChatProvider("test", false, request =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "", Model = "m", Provider = "test",
                    // Escapes the sandbox - AgentToolExecutor must reject this,
                    // not throw, and the loop must feed that back as a normal
                    // tool result rather than crashing the whole run.
                    ToolCalls = [new ToolCall("call_1", "write_file", """{"path":"../../escape.txt","content":"x"}""")]
                });

            var toolResultMessage = request.Messages.Last();
            Assert.Equal("tool", toolResultMessage.Role);
            Assert.Contains("outside", toolResultMessage.Content, StringComparison.OrdinalIgnoreCase);
            return ProviderResponse.Ok(new ChatResponse { Content = "I see, cannot escape the workspace.", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("try to escape", AgentAccessLevel.Sandboxed);

        Assert.True(result.Success);
        Assert.Contains(result.Steps, s => s.Kind == "tool_error");
    }

    [Fact]
    public async Task RunAsync_ProviderFails_StopsImmediatelyWithError()
    {
        var provider = FakeChatProvider.Failing("test", ProviderOutcome.AuthFailed);
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        var result = await loop.RunAsync("do something", AgentAccessLevel.Sandboxed);

        Assert.False(result.Success);
        Assert.Equal(1, provider.CallCount);
        Assert.Contains(result.Steps, s => s.Kind == "error");
    }

    [Fact]
    public async Task RunAsync_ModelNeverStopsCallingTools_StopsAtIterationCap()
    {
        var provider = new FakeChatProvider("test", false, _ => ProviderResponse.Ok(new ChatResponse
        {
            Content = "",
            Model = "m",
            Provider = "test",
            ToolCalls = [new ToolCall(Guid.NewGuid().ToString("n"), "list_files", "{}")]
        }));

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("loop forever", AgentAccessLevel.Sandboxed);

        Assert.False(result.Success);
        Assert.True(provider.CallCount <= 25, $"expected the loop to stop at the iteration cap, but the provider was called {provider.CallCount} times");
        Assert.Contains("iterations", result.FinalAnswer, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RunAsync_Cancellation_StopsPromptly()
    {
        // Cancel deterministically (from within the scripted response itself)
        // rather than racing a wall-clock delay against a fake provider that
        // resolves near-instantly - a real provider's network round-trip
        // gives cancellation a real window to land in, but a synchronous
        // fake makes a time-based cancel a flaky test, not a real check.
        var callCount = 0;
        using var cts = new CancellationTokenSource();
        var provider = new FakeChatProvider("test", false, _ =>
        {
            callCount++;
            if (callCount == 3) cts.Cancel();
            return ProviderResponse.Ok(new ChatResponse
            {
                Content = "",
                Model = "m",
                Provider = "test",
                ToolCalls = [new ToolCall(Guid.NewGuid().ToString("n"), "list_files", "{}")]
            });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("loop forever", AgentAccessLevel.Sandboxed, ct: cts.Token);

        Assert.False(result.Success);
        Assert.Contains(result.Steps, s => s.Kind == "cancelled");
        // Stopped shortly after the 3rd call, well short of the 25-iteration cap.
        Assert.True(callCount <= 4, $"expected the loop to stop within ~1 iteration of cancelling, but the provider was called {callCount} times");
    }

    [Fact]
    public async Task RunAsync_EmitsStepsViaCallback_AsTheyHappen_NotOnlyAtTheEnd()
    {
        var provider = new FakeChatProvider("test", false, request =>
            request.Messages.Count == 1
                ? ProviderResponse.Ok(new ChatResponse
                {
                    Content = "checking",
                    Model = "m", Provider = "test",
                    ToolCalls = [new ToolCall("call_1", "list_files", "{}")]
                })
                : ProviderResponse.Ok(new ChatResponse { Content = "done", Model = "m", Provider = "test" }));

        var observedLive = new List<string>();
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        var result = await loop.RunAsync("go", AgentAccessLevel.Sandboxed, onStep: step =>
        {
            observedLive.Add(step.Kind);
            return Task.CompletedTask;
        });

        // The callback must have seen every step the final result reports -
        // this is what a progress UI would subscribe to while the run is
        // still in flight, not just a summary handed back at the very end.
        Assert.Equal(result.Steps.Select(s => s.Kind), observedLive);
        Assert.True(observedLive.Count > 1);
    }
}
