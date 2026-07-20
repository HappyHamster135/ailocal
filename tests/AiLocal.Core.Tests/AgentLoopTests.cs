using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;
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
        // The loop reads its tool list from the EXECUTOR (the single source
        // of truth) - so the executor, not just the RunAsync parameter, must
        // be Off, which is the only combination real call sites can produce
        // (both derive from the same settings value).
        var loop = new AgentLoop(provider.CompleteAsync, new AgentToolExecutor(AgentAccessLevel.Off, _workspace));

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
        // Cap raised 25 -> 50 in v1.27.0: real multi-file builds hit 25 (a
        // football manager died at the cap after 18 min of legitimate work).
        Assert.True(provider.CallCount <= 50, $"expected the loop to stop at the iteration cap, but the provider was called {provider.CallCount} times");
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
    public async Task RunAsync_NoHistory_ReturnsJustTheNewUserAndAssistantTurns()
    {
        var provider = FakeChatProvider.Success("test", "42.");
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        var result = await loop.RunAsync("what is the answer?", AgentAccessLevel.Sandboxed);

        Assert.Equal(2, result.Messages.Count);
        Assert.Equal("user", result.Messages[0].Role);
        Assert.Equal("what is the answer?", result.Messages[0].Content);
        Assert.Equal("assistant", result.Messages[1].Role);
        Assert.Equal("42.", result.Messages[1].Content);
    }

    [Fact]
    public async Task RunAsync_WithHistory_SeedsConversationAndAppendsRatherThanReplacing()
    {
        var history = new List<ChatMessage>
        {
            new("user", "write hello.txt"),
            new("assistant", "Done, I created hello.txt")
        };
        // Snapshot the count/content at request time, not the ChatRequest
        // object itself - AgentLoop passes its live, still-mutating `messages`
        // list by reference, so holding onto `request` and inspecting it
        // after RunAsync returns would see the FINAL state (including turns
        // added after this request was sent), not what was actually sent.
        int? sentCount = null;
        string? sentLastContent = null;
        var provider = new FakeChatProvider("test", false, request =>
        {
            sentCount = request.Messages.Count;
            sentLastContent = request.Messages[^1].Content;
            return ProviderResponse.Ok(new ChatResponse { Content = "Added the second line.", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("now add a second line", AgentAccessLevel.Sandboxed, history: history);

        // The provider must have seen the full prior conversation plus the
        // new turn - not just the new turn alone (that would mean resume
        // isn't actually threading context through to the model).
        Assert.Equal(3, sentCount);
        Assert.Equal("now add a second line", sentLastContent);

        // Resume is only coherent if the returned Messages includes BOTH the
        // seeded history AND this run's own new turns, ready to be persisted
        // and passed back in as `history` again for a third message.
        Assert.Equal(4, result.Messages.Count);
        Assert.Equal("write hello.txt", result.Messages[0].Content);
        Assert.Equal("now add a second line", result.Messages[2].Content);
        Assert.Equal("Added the second line.", result.Messages[3].Content);
    }

    [Fact]
    public async Task RunAsync_WithHistory_DoesNotMutateTheCallersOriginalList()
    {
        var history = new List<ChatMessage> { new("user", "first"), new("assistant", "ok") };
        var originalCount = history.Count;
        var provider = FakeChatProvider.Success("test", "second reply");
        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());

        await loop.RunAsync("second message", AgentAccessLevel.Sandboxed, history: history);

        Assert.Equal(originalCount, history.Count);
    }

    [Fact]
    public async Task RunAsync_SystemPrompt_ReachesTheProviderRequest()
    {
        ChatRequest? captured = null;
        var provider = new FakeChatProvider("test", false, request =>
        {
            captured = request;
            return ProviderResponse.Ok(new ChatResponse { Content = "ok", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        await loop.RunAsync("go", AgentAccessLevel.Sandboxed, system: "You work in /some/folder.");

        Assert.Equal("You work in /some/folder.", captured?.System);
    }

    [Fact]
    public async Task RunAsync_NoSystemGiven_LeavesItNull()
    {
        ChatRequest? captured = null;
        var provider = new FakeChatProvider("test", false, request =>
        {
            captured = request;
            return ProviderResponse.Ok(new ChatResponse { Content = "ok", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        await loop.RunAsync("go", AgentAccessLevel.Sandboxed);

        Assert.Null(captured?.System);
    }

    [Fact]
    public async Task RunAsync_TotalUsage_SumsAcrossEveryIteration()
    {
        var callCount = 0;
        var provider = new FakeChatProvider("test", false, _ =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "checking",
                    Model = "m", Provider = "test",
                    Usage = new TokenUsage(100, 20),
                    ToolCalls = [new ToolCall("call_1", "list_files", "{}")]
                });
            return ProviderResponse.Ok(new ChatResponse
            {
                Content = "done", Model = "m", Provider = "test",
                Usage = new TokenUsage(150, 10)
            });
        });

        var loop = new AgentLoop(provider.CompleteAsync, Sandboxed());
        var result = await loop.RunAsync("go", AgentAccessLevel.Sandboxed);

        Assert.Equal(250, result.TotalUsage.InputTokens);
        Assert.Equal(30, result.TotalUsage.OutputTokens);
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

    /// <summary>
    /// The whole point of AiLocal: a Worker agent, driven ONLY by a chat
    /// message, PRODUCES a real game project on disk via one scaffold_game
    /// tool call - no human curl, no code-as-text. This drives the full chain
    /// (scripted model -> real AgentLoop -> real AgentToolExecutor -> real
    /// GameScaffoldService) and asserts an actual index.html lands on disk.
    /// </summary>
    [Fact]
    public async Task RunAsync_ModelCallsScaffoldGame_ProducesRealGameOnDisk()
    {
        Directory.CreateDirectory(_workspace);
        // Real scaffolder delegate, exactly as WorkerRole/SessionApi wire it.
        var executor = new AgentToolExecutor(
            AgentAccessLevel.Sandboxed, _workspace,
            gameScaffolder: (engine, prompt, root, ct) =>
            {
                var r = new AiLocal.Node.Hosting.GameScaffoldService().Scaffold(engine, prompt, root);
                return Task.FromResult((r.Success, r.Output));
            });

        var callCount = 0;
        var provider = new FakeChatProvider("test", false, _ =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "Jag skapar spelet.",
                    Model = "m", Provider = "test",
                    ToolCalls = [new ToolCall("call_1", "scaffold_game",
                        """{"engine":"html5","prompt":"en 2d plattformare med hopp","root":"spel"}""")]
                });
            return ProviderResponse.Ok(new ChatResponse { Content = "Spelet är klart.", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, executor);
        var result = await loop.RunAsync("bygg ett 2d-spel", AgentAccessLevel.Sandboxed);

        Assert.True(result.Success);
        // The agent PRODUCED a runnable artifact, autonomously:
        var indexHtml = Path.Combine(_workspace, "spel", "index.html");
        Assert.True(File.Exists(indexHtml), "scaffold_game must have written a real index.html on disk");
        var html = await File.ReadAllTextAsync(indexHtml);
        Assert.Contains("<canvas", html);
        Assert.Contains("requestAnimationFrame", html);

        var kinds = result.Steps.Select(s => s.Kind).ToList();
        Assert.Contains("tool_call", kinds);
        Assert.Contains("tool_result", kinds);
        Assert.Contains("done", kinds);
    }
}
