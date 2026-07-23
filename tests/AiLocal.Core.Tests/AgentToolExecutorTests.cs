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
    public async Task ReadFile_OffsetUtanLimit_LaserTillSlutet()
    {
        // v2.14: offset UTAN limit overflowade (start + int.MaxValue) till
        // "Non-negative number required" - sett tre gånger i ett live-transkript.
        await File.WriteAllLinesAsync(Path.Combine(_workspace, "long.txt"),
            Enumerable.Range(1, 20).Select(i => $"line {i}"));
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        var result = await executor.ExecuteAsync(Call("read_file", new { path = "long.txt", offset = 15 }), CancellationToken.None);
        Assert.False(result.IsError, result.Output);
        Assert.Contains("line 15", result.Output);
        Assert.Contains("line 20", result.Output);
        Assert.DoesNotContain("line 14", result.Output);
    }

    [Fact]
    public async Task Search_PathArEnFil_SokerIDenFilen()
    {
        // v2.14: live gav search med path="project.godot" (en FIL) felet
        // "search path not found" trots att filen fanns.
        await File.WriteAllTextAsync(Path.Combine(_workspace, "project.godot"),
            "[autoload]\nAudioManager=\"*res://audio/AudioManager.gd\"\n");
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        var result = await executor.ExecuteAsync(Call("search", new { pattern = "autoload", path = "project.godot" }), CancellationToken.None);
        Assert.False(result.IsError, result.Output);
        Assert.Contains("project.godot:1", result.Output);
    }

    [Fact]
    public async Task Glob_UtanPath_SokerIArbetsytan_InteProcessensCwd()
    {
        // v2.14: default var "." = processens cwd (nodens exe-katalog) för
        // Full-agenter - glob/search utan path gav alltid "no files match"
        // trots att filerna fanns i arbetsytan.
        await File.WriteAllTextAsync(Path.Combine(_workspace, "Main.gd"), "extends Node2D\n");
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var result = await executor.ExecuteAsync(Call("glob", new { pattern = "**/*.gd" }), CancellationToken.None);
        Assert.False(result.IsError, result.Output);
        Assert.Contains("Main.gd", result.Output);
    }

    [Fact]
    public async Task RunCommand_MedMaskningsartefakt_BlockerasMedFacit()
    {
        // v2.14: live "lagade" ett spår [ADDRESS] direkt på disk via
        // powershell -replace och skrev in trasiga värden - samma vakt som
        // write/edit gäller nu kommandon.
        var executor = new AgentToolExecutor(AgentAccessLevel.Full, _workspace);
        var result = await executor.ExecuteAsync(Call("run_command",
            new { command = "powershell -Command \"(Get-Content Main.gd) -replace '\\[ADDRESS\\]','fix' | Set-Content Main.gd\"" }),
            CancellationToken.None);
        Assert.True(result.IsError);
        Assert.Contains("maskning", result.Output, StringComparison.OrdinalIgnoreCase);
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
        // v1.96: arbetsytans absoluta prefix RELATIVISERAS i utdatan (blir ".")
        // så leverantörsmaskningen inte triggas - cwd:t bevisas av punkten.
        Assert.Contains(".", result.Output);
        Assert.DoesNotContain(_workspace, result.Output, StringComparison.OrdinalIgnoreCase);
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
        // v1.96: roten strippas ur utdatan -> cwd syns som "subfolder" relativt.
        Assert.Contains("subfolder", result.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(_workspace + Path.DirectorySeparatorChar, result.Output, StringComparison.OrdinalIgnoreCase);
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

    [Fact]
    public async Task EditFile_ReplacesTargetedText_AndLeavesRest()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(
            Call("write_file", new { path = "code.cs", content = "line one\nline two\nline three" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("edit_file", new { path = "code.cs", oldText = "line two", newText = "line TWO edited" }), CancellationToken.None);

        Assert.False(result.IsError);
        var after = await executor.ExecuteAsync(Call("read_file", new { path = "code.cs" }), CancellationToken.None);
        Assert.Contains("line TWO edited", after.Output);
        Assert.Contains("line one", after.Output);
        Assert.Contains("line three", after.Output);
    }

    [Fact]
    public async Task EditFile_CrlfFilMedLfAnkare_MatcharOchBevararRadslut()
    {
        // v1.99, live-fiaskot: git autocrlf konverterade worktree-filer till
        // CRLF, modellen skickade \n-ankare -> VARJE flerradig redigering
        // föll med "not found" (~40 anrop brändes på feldiagnosen "edit_file
        // klarar inte tabbar"; enradiga ankare fungerade, flerradiga aldrig).
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        var path = Path.Combine(_workspace, "crlf.gd");
        await File.WriteAllTextAsync(path, "func a():\r\n\tpass\r\n\r\nfunc b():\r\n\tpass\r\n");

        var result = await executor.ExecuteAsync(Call("edit_file", new
        {
            path = "crlf.gd",
            oldText = "\tpass\n\nfunc b():",   // \n-ankare over TRE rader
            newText = "\tpass\n\treturn\n\nfunc b():"
        }), CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        var after = await File.ReadAllTextAsync(path);
        Assert.Contains("\treturn\r\n", after);            // ändringen inne, med CRLF
        Assert.DoesNotContain("\n", after.Replace("\r\n", "")); // inga blandade radslut kvar
    }

    [Fact]
    public async Task EditFile_LfFil_ForblirLf()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        var path = Path.Combine(_workspace, "lf.gd");
        await File.WriteAllTextAsync(path, "func a():\n\tpass\n");

        var result = await executor.ExecuteAsync(Call("edit_file", new
        {
            path = "lf.gd",
            oldText = "func a():\n\tpass",
            newText = "func a():\n\treturn 1"
        }), CancellationToken.None);

        Assert.False(result.IsError, result.Output);
        var after = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("\r", after);
        Assert.Contains("\treturn 1\n", after);
    }

    [Fact]
    public async Task EditFile_AmbiguousWithoutReplaceAll_Fails()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(
            Call("write_file", new { path = "dup.txt", content = "repeat\nrepeat" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("edit_file", new { path = "dup.txt", oldText = "repeat", newText = "x" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("matched", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EditFile_ReplaceAll_HandlesMultiple()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(
            Call("write_file", new { path = "dup.txt", content = "repeat\nrepeat" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("edit_file", new { path = "dup.txt", oldText = "repeat", newText = "done", replaceAll = true }), CancellationToken.None);

        Assert.False(result.IsError);
        var after = await executor.ExecuteAsync(Call("read_file", new { path = "dup.txt" }), CancellationToken.None);
        Assert.DoesNotContain("repeat", after.Output);
        Assert.Equal(2, after.Output.Split("done").Length - 1);
    }

    [Fact]
    public async Task EditFile_MissingOldText_Fails()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(
            Call("write_file", new { path = "x.txt", content = "actual content" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("edit_file", new { path = "x.txt", oldText = "not present", newText = "y" }), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ReadFile_Slice_ReturnsOnlyRequestedLines()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        await executor.ExecuteAsync(
            Call("write_file", new { path = "big.txt", content = "a\nb\nc\nd\ne" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("read_file", new { path = "big.txt", offset = 2, limit = 2 }), CancellationToken.None);

        Assert.Contains("lines 2-3 of 5", result.Output);
        Assert.Contains("b", result.Output);
        Assert.Contains("c", result.Output);
        Assert.DoesNotContain("a", result.Output);
        Assert.DoesNotContain("d", result.Output);
    }

    [Fact]
    public async Task Search_FindsPatternWithLineNumbers()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        await executor.ExecuteAsync(
            Call("write_file", new { path = "src/app.cs", content = "class App {\n  void Run() {}\n}" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(
            Call("search", new { pattern = "void Run" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("app.cs:2:", result.Output);
    }

    [Fact]
    public async Task Glob_FindsFilesByExtension()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        Directory.CreateDirectory(Path.Combine(_workspace, "src"));
        await executor.ExecuteAsync(Call("write_file", new { path = "src/a.cs", content = "x" }), CancellationToken.None);
        await executor.ExecuteAsync(Call("write_file", new { path = "src/b.txt", content = "y" }), CancellationToken.None);
        await executor.ExecuteAsync(Call("write_file", new { path = "top.md", content = "z" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(Call("glob", new { pattern = "*.cs" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Contains("a.cs", result.Output);
        Assert.DoesNotContain("b.txt", result.Output);
        Assert.DoesNotContain("top.md", result.Output);
    }

    [Fact]
    public async Task Search_SkipsBuildAndVcsDirs()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        Directory.CreateDirectory(Path.Combine(_workspace, "bin"));
        Directory.CreateDirectory(Path.Combine(_workspace, ".git"));
        await executor.ExecuteAsync(Call("write_file", new { path = "bin/compiled.cs", content = "secret" }), CancellationToken.None);
        await executor.ExecuteAsync(Call("write_file", new { path = ".git/history.cs", content = "secret" }), CancellationToken.None);
        await executor.ExecuteAsync(Call("write_file", new { path = "real.cs", content = "secret" }), CancellationToken.None);

        var result = await executor.ExecuteAsync(Call("search", new { pattern = "secret" }), CancellationToken.None);

        Assert.Contains("real.cs", result.Output);
        Assert.DoesNotContain("compiled.cs", result.Output);
        Assert.DoesNotContain("history.cs", result.Output);
    }

    // --- scaffold_game tool: the whole point is a Worker can PRODUCE a real
    // game project autonomously (one tool call), not paste code-as-text. ---

    [Fact]
    public void ToolsFor_WithoutScaffolder_DoesNotAdvertiseScaffoldGame()
    {
        var tools = AgentToolExecutor.ToolsFor(AgentAccessLevel.Sandboxed, gameScaffold: false);
        Assert.DoesNotContain(tools, t => t.Name == "scaffold_game");
    }

    [Fact]
    public void ToolsFor_WithScaffolder_AdvertisesScaffoldGame()
    {
        var tools = AgentToolExecutor.ToolsFor(AgentAccessLevel.Sandboxed, gameScaffold: true);
        Assert.Contains(tools, t => t.Name == "scaffold_game");
    }

    [Fact]
    public async Task ScaffoldGame_InvokesDelegate_AndReturnsSuccess()
    {
        string? seenEngine = null, seenPrompt = null, seenRoot = null;
        var executor = new AgentToolExecutor(
            AgentAccessLevel.Sandboxed, _workspace,
            gameScaffolder: (engine, prompt, root, ct) =>
            {
                seenEngine = engine; seenPrompt = prompt; seenRoot = root;
                return Task.FromResult((true, $"{engine} projekt skapat i {root} (2 filer)."));
            });

        // The executor must advertise it to the model...
        Assert.Contains(executor.Tools, t => t.Name == "scaffold_game");

        // ...and executing it must reach the delegate with the args.
        var result = await executor.ExecuteAsync(
            Call("scaffold_game", new { engine = "html5", prompt = "en 2d plattformare", root = "mitt-spel" }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("html5", seenEngine);
        Assert.Equal("en 2d plattformare", seenPrompt);
        Assert.NotNull(seenRoot);
        Assert.EndsWith("mitt-spel", seenRoot!.Replace('\\', '/'));
        Assert.Contains("projekt skapat", result.Output);
    }

    [Fact]
    public async Task ScaffoldGame_WithoutDelegate_ReturnsError()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace); // no scaffolder
        var result = await executor.ExecuteAsync(
            Call("scaffold_game", new { engine = "html5", prompt = "x" }), CancellationToken.None);
        Assert.True(result.IsError);
    }

    // ---- game_module (ready-made production systems) -----------------------

    [Fact]
    public async Task GameModule_WhenWired_IsAdvertisedAndReachesDelegate()
    {
        string? seenAction = null, seenName = null, seenEngine = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            gameModules: (action, name, engine) =>
            {
                seenAction = action; seenName = name; seenEngine = engine;
                return Task.FromResult((true, "// module code here"));
            });

        Assert.Contains(executor.Tools, t => t.Name == "game_module");

        var result = await executor.ExecuteAsync(
            Call("game_module", new { action = "get", name = "inventory", engine = "html5" }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("get", seenAction);
        Assert.Equal("inventory", seenName);
        Assert.Equal("html5", seenEngine);
        Assert.Contains("module code here", result.Output);
    }

    [Fact]
    public async Task GameModule_WithoutDelegate_IsNotAdvertisedAndRefuses()
    {
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace);
        Assert.DoesNotContain(executor.Tools, t => t.Name == "game_module");
        var result = await executor.ExecuteAsync(
            Call("game_module", new { action = "list" }), CancellationToken.None);
        Assert.True(result.IsError);
    }

    // ---- vision_review ------------------------------------------------------

    [Fact]
    public async Task VisionReview_WhenWired_ResolvesPathAndReachesDelegate()
    {
        var imagePath = Path.Combine(_workspace, "shot.png");
        await File.WriteAllBytesAsync(imagePath, [1, 2, 3]);
        string? seenPath = null, seenQuestion = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            visionReviewer: (path, question, ct) =>
            {
                seenPath = path; seenQuestion = question;
                return Task.FromResult((true, "Ser bra ut. Inga visuella buggar."));
            });

        Assert.Contains(executor.Tools, t => t.Name == "vision_review");

        var result = await executor.ExecuteAsync(
            Call("vision_review", new { path = "shot.png", question = "ser spelet ratt ut?" }),
            CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(imagePath, seenPath);
        Assert.Equal("ser spelet ratt ut?", seenQuestion);
        Assert.Contains("Ser bra ut", result.Output);
    }

    [Fact]
    public async Task VisionReview_MissingImage_ErrorsWithoutCallingDelegate()
    {
        var called = false;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            visionReviewer: (_, _, _) => { called = true; return Task.FromResult((true, "x")); });
        var result = await executor.ExecuteAsync(
            Call("vision_review", new { path = "finns-inte.png" }), CancellationToken.None);
        Assert.True(result.IsError);
        Assert.False(called);
    }

    // ---- generate_asset / screenshot default output paths ------------------

    [Fact]
    public async Task GenerateAsset_OmittedOutput_DefaultsIntoWorkspaceAssetsFolder()
    {
        string? seenOutput = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            assetGenerator: (type, prompt, w, h, output, ct) =>
            {
                seenOutput = output;
                return Task.FromResult((true, "ok", (string?)output));
            });

        var result = await executor.ExecuteAsync(
            Call("generate_asset", new { type = "sprite", prompt = "a hero" }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(seenOutput);
        // The old behavior passed "" straight through, which blew up in
        // Path.GetFullPath before the generator could run.
        Assert.StartsWith(_workspace, seenOutput!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("assets", seenOutput!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", seenOutput!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Screenshot_OmittedOutput_DefaultsIntoWorkspaceScreenshotsFolder()
    {
        string? seenOutput = null;
        var executor = new AgentToolExecutor(AgentAccessLevel.Sandboxed, _workspace,
            screenshotTool: (windowTitle, output, ct) =>
            {
                seenOutput = output;
                return Task.FromResult((true, "ok", (string?)output));
            });

        var result = await executor.ExecuteAsync(
            Call("screenshot", new { }), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(seenOutput);
        Assert.StartsWith(_workspace, seenOutput!, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(".png", seenOutput!, StringComparison.OrdinalIgnoreCase);
    }
}
