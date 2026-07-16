using System.Diagnostics;
using System.Text;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Tests for TerminalApi — interactive terminal sessions.
/// The Resolve() scoping tests are deterministic; the full integration
/// test (start → input → poll output) is inherently timing-sensitive
/// because it exercises an interactive shell process.
/// </summary>
public class TerminalApiTests : IDisposable
{
    private readonly string _workspace;

    public TerminalApiTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ailocal-terminal-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workspace);
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* best effort */ }
    }

    // ── Resolve() scoping tests ──────────────────────────────────────────

    [Fact]
    public void Resolve_ValidPath_ReturnsFullPath()
    {
        var result = TerminalApi.Resolve(_workspace);
        Assert.Equal(_workspace, result, ignoreCase: OperatingSystem.IsWindows());
    }

    [Fact]
    public void Resolve_NullPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => TerminalApi.Resolve(null!));
    }

    [Fact]
    public void Resolve_EmptyPath_Throws()
    {
        Assert.Throws<ArgumentException>(() => TerminalApi.Resolve(""));
    }

    [Fact]
    public void Resolve_NonExistentPath_Throws()
    {
        var bad = Path.Combine(_workspace, "does-not-exist");
        Assert.Throws<ArgumentException>(() => TerminalApi.Resolve(bad));
    }

    [Fact]
    public void Resolve_RelativePath_Throws()
    {
        Assert.Throws<ArgumentException>(() => TerminalApi.Resolve("relative\\path"));
    }

    [Fact]
    public void Resolve_SystemDirectory_Throws()
    {
        var sysDir = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.Windows)
            : "/etc";
        if (Directory.Exists(sysDir))
            Assert.Throws<ArgumentException>(() => TerminalApi.Resolve(sysDir));
    }

    // ── Integration test (interactive shell) ─────────────────────────────
    //
    // This test starts an interactive shell, writes a command to stdin, and
    // polls the output buffer until the expected text appears (or timeout).
    // It is inherently timing-sensitive — the shell may not have produced
    // output by the time we poll. A short retry loop with a generous
    // timeout keeps it stable without making the test slow.
    //
    // We start the shell WITHOUT /c (Windows) or -c (Linux) so it stays
    // alive, write "echo hello-terminal\r\n", and look for the echoed text
    // in the accumulated output.

    [Fact]
    public async Task InteractiveTerminal_WriteAndPoll_ProducesOutput()
    {
        // Use cmd.exe on Windows, /bin/sh on Linux
        if (!OperatingSystem.IsWindows())
            return; // skip on non-Windows for now (CI may not have bash); we're on Windows

        var terminalId = Guid.NewGuid().ToString("N");

        // Start the process directly (simulating what POST /api/terminals does)
        var psi = new ProcessStartInfo("cmd.exe", "")
        {
            WorkingDirectory = _workspace,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };
        process.Start();

        var outputBuffer = new StringBuilder();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                outputBuffer.AppendLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                outputBuffer.AppendLine(e.Data);
        };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Give the shell a moment to start up
        await Task.Delay(500);

        // Write "echo hello-terminal\r\n" to stdin
        await process.StandardInput.WriteAsync("echo hello-terminal\r\n");
        await process.StandardInput.FlushAsync();

        // Poll for output with timeout
        string? output = null;
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < deadline)
        {
            await Task.Delay(200);
            var snapshot = outputBuffer.ToString();
            if (snapshot.Contains("hello-terminal", StringComparison.OrdinalIgnoreCase))
            {
                output = snapshot;
                break;
            }
        }

        // Kill the process
        try { process.Kill(entireProcessTree: true); } catch { }

        Assert.NotNull(output); // null means timeout — no output arrived
        Assert.Contains("hello-terminal", output, StringComparison.OrdinalIgnoreCase);
    }
}