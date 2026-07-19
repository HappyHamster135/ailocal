using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Coverage for the new delegate_task tool (B): the lead agent can
/// hand a self-contained sub-task to a fresh sub-agent run and get its result
/// back, without the cluster. Drives the real AgentLoop + AgentToolExecutor
/// with a scripted provider; the delegator is a simple in-test lambda that
/// runs a sub-loop, proving the wiring end-to-end.</summary>
public class DelegateTaskTests
{
    private static Func<ChatRequest, CancellationToken, Task<ProviderResponse>> Provider(
        Func<ChatRequest, ProviderResponse> respond) =>
        (req, _) => Task.FromResult(respond(req));

    [Fact]
    public async Task DelegateTask_RunsSubAgentAndReturnsResult()
    {
        // Lead agent: first call delegates a sub-task; second call finishes.
        var leadCall = 0;
        var leadProvider = Provider(_ =>
        {
            leadCall++;
            if (leadCall == 1)
                return ProviderResponse.Ok(new ChatResponse
                {
                    Content = "Delegerar deluppgiften.",
                    Model = "m", Provider = "t",
                    ToolCalls = [new ToolCall("c1", "delegate_task",
                        "{\"prompt\":\"Skriv en funktion som adderar två tal.\"}")]
                });
            return ProviderResponse.Ok(new ChatResponse { Content = "Klar.", Model = "m", Provider = "t" });
        });

        // The delegator runs a SUB agent; script its provider so it returns a
        // concrete final answer (no further tools) on the first sub-call.
        var subCall = 0;
        var subProvider = Provider(_ =>
        {
            subCall++;
            return ProviderResponse.Ok(new ChatResponse
            {
                Content = "Sub-svar: function add(a,b){return a+b;}",
                Model = "m", Provider = "t"
            });
        });

        var delegator = (string prompt, string? system, CancellationToken ct) =>
        {
            var subExec = new AgentToolExecutor(AgentAccessLevel.Full, Path.GetTempPath());
            var loop = new AgentLoop(subProvider, subExec);
            return loop.RunAsync(prompt, AgentAccessLevel.Full, ct: ct, system: system)
                .ContinueWith(t =>
                {
                    var r = t.Result;
                    var final = r.Steps.LastOrDefault(s => s.Kind == "done")?.Detail
                                ?? r.Steps.LastOrDefault()?.Detail ?? "";
                    return (r.Success, final);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
        };

        var executor = new AgentToolExecutor(AgentAccessLevel.Full, Path.GetTempPath(),
            taskDelegator: delegator);

        var loop = new AgentLoop(leadProvider, executor);
        var result = await loop.RunAsync("bygg appen", AgentAccessLevel.Full);

        Assert.True(result.Success);
        // The delegate_task tool ran the sub-agent and its answer was folded
        // back into the lead conversation as a tool_result.
        Assert.Contains(result.Steps,
            s => s.Kind == "tool_result" && s.Detail.Contains("function add"));
        Assert.Contains(result.Steps, s => s.Kind == "done");
        Assert.True(subCall >= 1, "sub-agent must have been invoked");
    }

    [Fact]
    public void DelegateTask_NotAdvertised_WhenDelegatorAbsent()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, Path.GetTempPath());
        Assert.DoesNotContain(executor.Tools, t => t.Name == "delegate_task");
    }

    [Fact]
    public void DelegateTask_Advertised_WhenDelegatorPresent()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, Path.GetTempPath(),
            taskDelegator: (_, _, _) => Task.FromResult((true, "ok")));
        Assert.Contains(executor.Tools, t => t.Name == "delegate_task");
    }

    [Fact]
    public async Task DelegateTask_ErrorsWhenPromptMissing()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, Path.GetTempPath(),
            taskDelegator: (_, _, _) => Task.FromResult((true, "ok")));
        var result = await executor.ExecuteAsync(
            new ToolCall("c1", "delegate_task", "{}"), default);
        Assert.True(result.IsError);
        Assert.Contains("prompt", result.Output);
    }
}
