using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Integration coverage for the studio flow: the agent can PAUSE and
/// ask the operator real questions mid-run (ask_user), and the answer is fed
/// back so the build continues - the exact mechanism behind "fråga om mer info
/// när den behöver". Drives the real AgentLoop + real AgentToolExecutor with a
/// scripted provider (only the model's words are faked, everything else is
/// real, including the ask_user delegate plumbing).</summary>
public class StudioAskUserTests : IDisposable
{
    private readonly string _workspace;

    public StudioAskUserTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ailocal-askuser-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AskUser_PausesForAnswer_ThenContinuesAndCompletes()
    {
        // The ask_user delegate returns a scripted operator answer, exactly
        // like the dashboard's answer box feeding a response back.
        var executor = new AgentToolExecutor(
            AgentAccessLevel.Full, _workspace,
            askUser: (req, ct) => Task.FromResult("Anvand en bla farg och 3 nivaer."));

        var callCount = 0;
        var provider = new FakeChatProvider("test", false, _ =>
        {
            callCount++;
            if (callCount == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "Jag behöver klargöra något.",
                    Model = "m", Provider = "test",
                    ToolCalls = [new ToolCall("c1", "ask_user",
                        "{\"questions\":[\"Vilken färg ska spelet ha?\",\"Hur många nivåer?\"],\"blocking\":true}")]
                });
            // Second call: the operator's answer must now be in the conversation.
            var toolMsg = _.Messages.Last(m => m.Role == "tool");
            Assert.Contains("bla farg", toolMsg.Content);
            Assert.Contains("3 nivaer", toolMsg.Content);
            return ProviderResponse.Ok(new ChatResponse { Content = "Tack, bygger nu.", Model = "m", Provider = "test" });
        });

        var loop = new AgentLoop(provider.CompleteAsync, executor);
        var result = await loop.RunAsync("bygg ett spel", AgentAccessLevel.Full);

        Assert.True(result.Success);
        // The operator's answer was fed back into the conversation as the
        // ask_user tool result, so the build could continue.
        Assert.Contains(result.Steps, s => s.Kind == "tool_result" && s.Detail.Contains("bla farg"));
        // The run continued past the question and finished.
        Assert.Contains(result.Steps, s => s.Kind == "tool_call" && s.Detail.Contains("ask_user"));
        Assert.Contains(result.Steps, s => s.Kind == "done");
    }

    [Fact]
    public void AskUser_NotAdvertised_WhenDelegateAbsent_WorkerMode()
    {
        // A Worker's autonomous assignment has no human to answer, so ask_user
        // must NOT be in the tool list (mirrors how the Worker wires it today).
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        Assert.DoesNotContain(executor.Tools, t => t.Name == "ask_user");
    }

    [Fact]
    public void AskUser_Advertised_WhenDelegatePresent()
    {
        var executor = new AgentToolExecutor(
            AgentAccessLevel.Full, _workspace,
            askUser: (_, _) => Task.FromResult("ok"));
        Assert.Contains(executor.Tools, t => t.Name == "ask_user");
    }

    [Fact]
    public async Task AskUser_RejectsEmptyQuestions()
    {
        var executor = new AgentToolExecutor(
            AgentAccessLevel.Full, _workspace,
            askUser: (_, _) => Task.FromResult("svar"));
        var result = await executor.ExecuteAsync(
            new ToolCall("c1", "ask_user", "{\"questions\":[]}"), default);
        Assert.True(result.IsError);
        Assert.Contains("non-empty", result.Output);
    }
}

/// <summary>Unit coverage for the mid-run question gate: a blocked agent call
/// unblocks when the operator answers, and auto-fails when the run cancels.</summary>
public class PendingInfoRegistryTests
{
    [Fact]
    public async Task RequestAsync_UnblocksWhenResolved()
    {
        var reg = new PendingInfoRegistry();
        var task = reg.RequestAsync("s1", new PendingInfoRequest("s1", [new InfoQuestion("Färg?")]), default);
        Assert.False(task.IsCompleted, "ask_user must block until the operator answers");

        Assert.NotNull(reg.Peek("s1"));
        Assert.True(reg.Resolve("s1", "röd"));
        Assert.Equal("röd", await task);
    }

    [Fact]
    public void RequestAsync_ResolveUnknownSession_ReturnsFalse()
    {
        var reg = new PendingInfoRegistry();
        Assert.False(reg.Resolve("nope", "x"));
    }

    [Fact]
    public async Task RejectAllForSession_UnblocksWithDefault()
    {
        var reg = new PendingInfoRegistry();
        var task = reg.RequestAsync("s1", new PendingInfoRequest("s1", [new InfoQuestion("Färg?")]), default);
        reg.RejectAllForSession("s1");
        // Cancel path returns the default sentinel; the agent turns it into an error.
        Assert.Equal("(avbruten)", await task);
    }
}
