using AiLocal.Core.Agent;
using AiLocal.Core.Contracts;
using Xunit;

namespace AiLocal.Core.Tests;

public class AgentToolExecutorTests : IDisposable
{
    private readonly string _workspace;

    public AgentToolExecutorTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ailocal-agent-tests-" + Guid.NewGuid().ToString("n"));
        // Sandboxed's own constructor already creates this; Full's doesn't
        // (see AgentToolExecutor) - create it upfront so every test here
        // reflects a real session's folder, which always exists by the time
        // an executor is built (SessionStore validates that before allowing
        // one to be created).
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* best effort */ }
    }

    private static ToolCall Call(string name, object args) =>
        new(Guid.NewGuid().ToString("n"), name, System.Text.Json.JsonSerializer.Serialize(args));

    [Fact]
    public void ToolsFor_Off_ReturnsNoTools()
    {
        Assert.Empty(AgentToolExecutor.ToolsFor(AgentAccessLevel.Off));
    }

    [Fact]
    public void ToolsFor_Sandboxed_HasFileToolsButNotRunCommand()
    {
        var tools = AgentToolExecutor.ToolsFor(AgentAccessLevel.Sandboxed);
        Assert.Contains(tools, t => t.Name == "read_file");
        Assert.Contains(tools, t => t.Name == "write_file");
        Assert.Contains(tools, t => t.Name == "list_files");
        Assert.DoesNotContain(tools, t => t.Name == "run_command");
    }

    [Fact]
    public void ToolsFor_Full_IncludesRunCommand()
    {
        var tools = AgentToolExecutor.ToolsFor(AgentAccessLevel.Full);
        Assert.Contains(tools, t => t.Name == "run_command");
    }

    [Fact]
    public async Task Sandboxed_WriteThenReadFile_RoundTrips()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var writeResult = await executor.ExecuteAsync(
            Call("write_file", new { path = "notes.txt", content = "hello agent" }), CancellationToken.None);
        Assert.False(writeResult.IsError);

        var readResult = await executor.ExecuteAsync(
            Call("read_file", new { path = "notes.txt" }), CancellationToken.None);
        Assert.False(readResult.IsError);
        Assert.Equal("hello agent", readResult.Output);
    }

    [Fact]
    public async Task Sandboxed_WriteFile_CreatesParentDirectories()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = "sub/dir/file.txt", content = "nested" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_workspace, "sub", "dir", "file.txt")));
    }

    [Theory]
    [InlineData("../escape.txt")]
    [InlineData("../../escape.txt")]
    [InlineData("sub/../../escape.txt")]
    [InlineData("..\\escape.txt")]
    public async Task Sandboxed_RelativeTraversal_IsRejected(string maliciousPath)
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = maliciousPath, content = "escaped" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("outside", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), "escape.txt")));
    }

    [Fact]
    public async Task Sandboxed_AbsolutePath_IsRejected()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        var outsideTarget = Path.Combine(Path.GetTempPath(), "ailocal-agent-tests-escape-" + Guid.NewGuid().ToString("n") + ".txt");

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = outsideTarget, content = "escaped via absolute path" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("absolute paths are not allowed", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(outsideTarget));
    }

    [Fact]
    public async Task Sandboxed_RunCommand_IsRefused_NotSilentlyIgnored()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(
            Call("run_command", new { command = "echo hi" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not available", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sandboxed_ListFiles_DefaultsToWorkspaceRoot()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(Call("write_file", new { path = "a.txt", content = "a" }), CancellationToken.None);
        await executor.ExecuteAsync(Call("write_file", new { path = "b.txt", content = "b" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(Call("list_files", new { }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("a.txt", result.Output);
        Assert.Contains("b.txt", result.Output);
    }

    [Fact]
    public async Task Sandboxed_ReadFile_MissingFile_ReturnsErrorNotException()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(Call("read_file", new { path = "nope.txt" }), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task Full_WriteThenReadFile_WithAbsolutePath_Works()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var absolutePath = Path.Combine(_workspace, "full-mode-test.txt");

        var writeResult = await executor.ExecuteAsync(
            Call("write_file", new { path = absolutePath, content = "full access" }), CancellationToken.None);
        Assert.False(writeResult.IsError);

        var readResult = await executor.ExecuteAsync(
            Call("read_file", new { path = absolutePath }), CancellationToken.None);
        Assert.Equal("full access", readResult.Output);
    }

    [Fact]
    public async Task Full_RunCommand_ExecutesAndCapturesOutput()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var command = OperatingSystem.IsWindows() ? "echo hello-from-agent" : "echo hello-from-agent";

        var result = await executor.ExecuteAsync(Call("run_command", new { command }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("hello-from-agent", result.Output);
        Assert.Contains("exit code: 0", result.Output);
    }

    /// <summary>Regression: run_command's default working directory used to
    /// be Environment.CurrentDirectory (wherever this node's own exe happened
    /// to launch from) - a session-bound agent must default to ITS folder.</summary>
    [Fact]
    public async Task Full_RunCommand_NoExplicitWorkingDirectory_DefaultsToWorkspaceRoot()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var command = OperatingSystem.IsWindows() ? "cd" : "pwd";

        var result = await executor.ExecuteAsync(Call("run_command", new { command }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(_workspace, result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Full_RunCommand_RelativeWorkingDirectory_ResolvesAgainstWorkspaceRoot()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        Directory.CreateDirectory(Path.Combine(_workspace, "subfolder"));
        var command = OperatingSystem.IsWindows() ? "cd" : "pwd";

        var result = await executor.ExecuteAsync(
            Call("run_command", new { command, workingDirectory = "subfolder" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains(Path.Combine(_workspace, "subfolder"), result.Output, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Regression: a relative read/write/list path in Full mode used
    /// to resolve against Environment.CurrentDirectory, not the session's own
    /// folder - the same class of bug as run_command's working directory.</summary>
    [Fact]
    public async Task Full_WriteFile_RelativePath_ResolvesAgainstWorkspaceRoot()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = "relative.txt", content = "landed in the workspace" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_workspace, "relative.txt")));
    }

    /// <summary>Regression guard: Full access must stay completely unconfined
    /// for absolute paths - the workspace-binding fix above must only change
    /// what a RELATIVE path means, never add confinement Full never had.</summary>
    [Fact]
    public async Task Full_WriteFile_AbsolutePathOutsideWorkspace_StillWorksUnconfined()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var outsidePath = Path.Combine(Path.GetTempPath(), "ailocal-agent-tests-outside-" + Guid.NewGuid().ToString("n") + ".txt");

        try
        {
            var result = await executor.ExecuteAsync(
                Call("write_file", new { path = outsidePath, content = "full access reaches outside the workspace" }), CancellationToken.None);

            Assert.False(result.IsError);
            Assert.True(File.Exists(outsidePath));
        }
        finally
        {
            try { File.Delete(outsidePath); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task UnknownTool_ReturnsErrorNotException()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(Call("delete_everything", new { }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("unknown tool", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task MissingRequiredArgument_ReturnsErrorNotException()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(Call("write_file", new { path = "x.txt" }), CancellationToken.None);

        Assert.True(result.IsError);
    }

    // --- Approval gate (first coverage - the gate had none before) ---

    [Fact]
    public async Task Gate_Approve_WritesTheFile()
    {
        FileChangeProposal? seen = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            (proposal, _) => { seen = proposal; return Task.FromResult(new FileChangeDecision(true)); });

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = "ok.txt", content = "granskad" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("granskad", File.ReadAllText(Path.Combine(_workspace, "ok.txt")));
        Assert.NotNull(seen);
        Assert.Null(seen!.OldContent);
        Assert.Equal("granskad", seen.NewContent);
    }

    [Fact]
    public async Task Gate_Reject_DoesNotWrite_AndReturnsReasonAsToolError()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            (_, _) => Task.FromResult(new FileChangeDecision(false, "AI-granskaren avvisade ändringen: fel filtyp.")));

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = "nej.txt", content = "x" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("AI-granskaren avvisade ändringen: fel filtyp.", result.Output);
        Assert.False(File.Exists(Path.Combine(_workspace, "nej.txt")));
    }

    [Fact]
    public async Task Gate_SeesOldContent_WhenOverwritingExistingFile()
    {
        File.WriteAllText(Path.Combine(_workspace, "fanns.txt"), "gammalt");
        FileChangeProposal? seen = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            (proposal, _) => { seen = proposal; return Task.FromResult(new FileChangeDecision(true)); });

        await executor.ExecuteAsync(
            Call("write_file", new { path = "fanns.txt", content = "nytt" }), CancellationToken.None);

        Assert.Equal("gammalt", seen!.OldContent);
    }

    [Fact]
    public async Task NoGate_WritesImmediately_UnchangedBehavior()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);

        var result = await executor.ExecuteAsync(
            Call("write_file", new { path = "fritt.txt", content = "utan gate" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(File.Exists(Path.Combine(_workspace, "fritt.txt")));
    }

    // --- fetch_url / internet access ---

    [Fact]
    public void ToolsFor_AllowInternet_AddsFetchUrl_AndDefaultOmitsIt()
    {
        Assert.Contains(AgentToolExecutor.ToolsFor(AgentAccessLevel.Sandboxed, allowInternet: true),
            t => t.Name == "fetch_url");
        Assert.DoesNotContain(AgentToolExecutor.ToolsFor(AgentAccessLevel.Sandboxed),
            t => t.Name == "fetch_url");
        // Off means no tools at all, internet or not.
        Assert.Empty(AgentToolExecutor.ToolsFor(AgentAccessLevel.Off, allowInternet: true));
    }

    [Fact]
    public void InstanceTools_MatchConstructorFlags()
    {
        var withInternet = new AgentToolExecutor(AgentAccessLevel.Full, _workspace, allowInternet: true);
        var without = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);

        Assert.Contains(withInternet.Tools, t => t.Name == "fetch_url");
        Assert.DoesNotContain(without.Tools, t => t.Name == "fetch_url");
    }

    [Fact]
    public async Task FetchUrl_WhenInternetDisabled_ReturnsToolError()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);

        var result = await executor.ExecuteAsync(
            Call("fetch_url", new { url = "https://example.com" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("internet", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FetchUrl_RejectsNonHttpSchemes_WithoutTouchingNetwork()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace, allowInternet: true);

        var result = await executor.ExecuteAsync(
            Call("fetch_url", new { url = "file:///C:/Windows/win.ini" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("http", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void HtmlToText_StripsMarkupScriptsAndDecodesEntities()
    {
        var text = AgentToolExecutor.HtmlToText(
            "<html><head><style>body{color:red}</style><script>alert(1)</script></head>" +
            "<body><h1>Rubrik</h1><p>F&ouml;rsta &amp; andra.</p><div>Rad tv&aring;</div></body></html>");

        Assert.Contains("Rubrik", text);
        Assert.Contains("& andra", text);
        Assert.DoesNotContain("alert(1)", text);
        Assert.DoesNotContain("color:red", text);
        Assert.DoesNotContain("<p>", text);
    }
}
