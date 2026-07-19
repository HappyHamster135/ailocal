using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Runs a self-contained sub-task as a fresh AgentLoop pass and returns its
/// final answer. This is what backs the <c>delegate_task</c> tool: the lead
/// agent hands off a piece of work (e.g. "implement the save-system module")
/// so it can stay focused on orchestration instead of drowning in every
/// sub-file. The sub-run has no memory of the parent run - the prompt must be
/// fully self-contained - and it reuses THIS node's provider and tool set, so
/// it can read/write the same workspace and call the same tools (including
/// scaffold/verify/run_command).
///
/// Pure delegation: no new process, no new node. Just another loop iteration
/// tree. If the sub-run fails or loops out, the error text is returned so the
/// lead agent can adapt rather than the whole build dying.
/// </summary>
public sealed class TaskDelegator
{
    private readonly Func<ChatRequest, CancellationToken, Task<ProviderResponse>> _complete;
    private readonly AgentAccessLevel _level;
    private readonly string _workspaceRoot;
    private readonly bool _allowInternet;
    private readonly Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? _approvalGate;
    private readonly CommandGuard _commandGuard;
    private readonly CodebaseIndex? _codeIndex;
    private readonly ProjectMemory? _memory;
    private readonly Func<string, string, CancellationToken, Task<(bool, string)>>? _provisioner;
    private readonly Func<string, string, string, CancellationToken, Task<(bool, string)>>? _gameScaffolder;
    private readonly Func<string, string, string, CancellationToken, Task<(bool, string)>>? _appScaffolder;
    private readonly Func<string, CancellationToken, Task<string>>? _askUser;

    public TaskDelegator(
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        AgentAccessLevel level,
        string workspaceRoot,
        bool allowInternet = false,
        Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? approvalGate = null,
        CommandGuard? commandGuard = null,
        CodebaseIndex? codeIndex = null,
        ProjectMemory? memory = null,
        Func<string, string, CancellationToken, Task<(bool, string)>>? provisioner = null,
        Func<string, string, string, CancellationToken, Task<(bool, string)>>? gameScaffolder = null,
        Func<string, string, string, CancellationToken, Task<(bool, string)>>? appScaffolder = null,
        Func<string, CancellationToken, Task<string>>? askUser = null)
    {
        _complete = complete;
        _level = level;
        _workspaceRoot = workspaceRoot;
        _allowInternet = allowInternet;
        _approvalGate = approvalGate;
        _commandGuard = commandGuard ?? new CommandGuard(CommandGuardLevel.Off);
        _codeIndex = codeIndex;
        _memory = memory;
        _provisioner = provisioner;
        _gameScaffolder = gameScaffolder;
        _appScaffolder = appScaffolder;
        _askUser = askUser;
    }

    public async Task<(bool Success, string Output)> DelegateAsync(
        string subPrompt, string? system, CancellationToken ct)
    {
        // The sub-agent gets the same tools as the lead agent, except it does
        // NOT itself get delegate_task (no unbounded recursion of sub-sub-
        // agents) and no ask_user (the sub-run is unattended; if it needs the
        // human it should surface that in its result text for the lead agent).
        var subExecutor = new AgentToolExecutor(
            _level, _workspaceRoot, _approvalGate, _allowInternet, _commandGuard,
            _codeIndex, _memory, _provisioner, _gameScaffolder, _appScaffolder,
            askUser: null);

        var loop = new AgentLoop(_complete, subExecutor);
        var result = await loop.RunAsync(subPrompt, _level, onStep: _ => Task.CompletedTask,
            ct: ct, system: system);

        // The final answer is the last nonempty step (the "done" step for a
        // successful run, the "error" step otherwise).
        var final = result.Success
            ? (result.Steps.LastOrDefault(s => s.Kind == "done")?.Detail
               ?? result.Steps.LastOrDefault()?.Detail
               ?? "(sub-task completed with no final message)")
            : (result.Steps.LastOrDefault(s => s.Kind is "error" or "cancelled")?.Detail
               ?? "Sub-task failed.");
        return (result.Success, final);
    }
}
