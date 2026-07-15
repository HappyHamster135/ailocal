using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AiLocal.Core.Contracts;

namespace AiLocal.Core.Agent;

/// <summary>How much of the local machine an agent-mode task may touch. Off by
/// default on every Worker - a Host cannot dispatch an assignment to a Worker
/// whose own operator hasn't explicitly chosen Sandboxed or Full.</summary>
/// <remarks>This app's JSON pipeline serializes enums as their raw int value
/// by default (no global JsonStringEnumConverter - see NodeRole elsewhere,
/// which does the same). This one converts as a readable string ("Off" /
/// "Sandboxed" / "Full") instead, scoped to just this type rather than
/// changing global JSON options and risking every other enum's wire format.</remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentAccessLevel
{
    Off,
    /// <summary>File read/write/list only, confined to a dedicated workspace
    /// folder under this Worker's own data directory. No command execution.</summary>
    Sandboxed,
    /// <summary>Unrestricted file access and shell command execution, exactly
    /// like Claude Code/Codex have on the machine they run on.</summary>
    Full
}

/// <summary>
/// Executes one agent tool call against the real filesystem/shell, enforcing
/// whichever <see cref="AgentAccessLevel"/> this Worker's operator chose.
/// </summary>
/// <summary>A file-write the agent wants to make, offered to an operator for
/// review before it lands on disk. OldContent is null when the file is new.
/// All strings are already path-resolved to absolute by the executor.</summary>
public sealed record FileChangeProposal(string Path, string? OldContent, string NewContent);

/// <summary>Operator's answer to a <see cref="FileChangeProposal"/>. Approve =
/// false makes the executor return a tool error so the agent can adapt.</summary>
public sealed record FileChangeDecision(bool Approve, string? Reason = null);

public sealed class AgentToolExecutor
{
    private const int MaxOutputChars = 20_000;
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(5);

    private readonly AgentAccessLevel _level;
    private readonly string _workspaceRoot;
    private readonly bool _allowInternet;
    private readonly Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? _approvalGate;

    public AgentToolExecutor(
        AgentAccessLevel level,
        string workspaceRoot,
        Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? approvalGate = null,
        bool allowInternet = false)
    {
        _level = level;
        _workspaceRoot = Path.GetFullPath(workspaceRoot);
        _approvalGate = approvalGate;
        _allowInternet = allowInternet;
        if (_level == AgentAccessLevel.Sandboxed)
            Directory.CreateDirectory(_workspaceRoot);
    }

    /// <summary>The tool list THIS executor actually accepts - the single
    /// source of truth AgentLoop advertises to the model. Before this
    /// property existed the loop called the static ToolsFor(level) itself,
    /// which meant the loop and the executor had to agree on the flags out
    /// of band; an instance property can't drift from its own switch.</summary>
    public IReadOnlyList<ToolDefinition> Tools => ToolsFor(_level, _allowInternet);

    public static IReadOnlyList<ToolDefinition> ToolsFor(AgentAccessLevel level, bool allowInternet = false)
    {
        if (level == AgentAccessLevel.Off)
            return [];

        var tools = new List<ToolDefinition>
        {
            new("read_file", "Read the full text contents of a file.",
                """{"type":"object","properties":{"path":{"type":"string","description":"File path to read."}},"required":["path"]}"""),
            new("write_file", "Create or overwrite a text file with the given content. Creates parent directories if needed.",
                """{"type":"object","properties":{"path":{"type":"string"},"content":{"type":"string"}},"required":["path","content"]}"""),
            new("list_files", "List files and directories at a given path (non-recursive).",
                """{"type":"object","properties":{"path":{"type":"string","description":"Directory to list. Defaults to the workspace root."}}}""")
        };

        if (level == AgentAccessLevel.Full)
            tools.Add(new("run_command",
                "Run a shell command on this Worker's machine and return its stdout/stderr. Times out after 5 minutes.",
                """{"type":"object","properties":{"command":{"type":"string"},"workingDirectory":{"type":"string","description":"Optional; defaults to the current directory."}},"required":["command"]}"""));

        // Internet is a separate operator opt-in, not an access tier: it's
        // network reach, orthogonal to how much of the FILESYSTEM the agent
        // may touch - a Sandboxed agent with internet on can research docs
        // without gaining a single byte of extra disk access.
        if (allowInternet)
            tools.Add(new("fetch_url",
                "Fetch a web page over http/https and return its readable text content (HTML tags stripped). Use for looking things up on the internet.",
                """{"type":"object","properties":{"url":{"type":"string","description":"Absolute http:// or https:// URL to fetch."}},"required":["url"]}"""));

        return tools;
    }

    public async Task<ToolResult> ExecuteAsync(ToolCall call, CancellationToken ct)
    {
        try
        {
            using var args = JsonDocument.Parse(string.IsNullOrWhiteSpace(call.ArgumentsJson) ? "{}" : call.ArgumentsJson);
            var root = args.RootElement;

            return call.Name switch
            {
                "read_file" => await ReadFileAsync(call, root, ct),
                "write_file" => await WriteFileAsync(call, root, ct),
                "list_files" => ListFiles(call, root),
                "run_command" when _level == AgentAccessLevel.Full => await RunCommandAsync(call, root, ct),
                "run_command" => Error(call, "run_command is not available at this Worker's current access level (Sandboxed allows file access only)."),
                "fetch_url" when _allowInternet => await FetchUrlAsync(call, root, ct),
                "fetch_url" => Error(call, "fetch_url is not available - internet access is disabled on this Worker (Inställningar -> Agent & arbetsyta)."),
                _ => Error(call, $"unknown tool: {call.Name}")
            };
        }
        catch (UnauthorizedAccessException ex)
        {
            return Error(call, ex.Message);
        }
        catch (Exception ex)
        {
            return Error(call, $"tool execution failed: {ex.Message}");
        }
    }

    private async Task<ToolResult> ReadFileAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var path = ResolvePath(RequireString(args, "path"));
        if (!File.Exists(path))
            return Error(call, $"file not found: {path}");
        var content = await File.ReadAllTextAsync(path, ct);
        return new ToolResult(call.Id, call.Name, Truncate(content));
    }

    private async Task<ToolResult> WriteFileAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var path = ResolvePath(RequireString(args, "path"));
        var content = RequireString(args, "content");
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        // When a session wires an approval gate, the operator must preview and
        // approve every file write before it lands - the agent never writes
        // to disk blindly. No gate (e.g. a Worker's autonomous assignment) ->
        // write immediately, unchanged behavior.
        if (_approvalGate is not null)
        {
            string? oldContent = File.Exists(path) ? await File.ReadAllTextAsync(path, ct) : null;
            var decision = await _approvalGate(new FileChangeProposal(path, oldContent, content), ct);
            if (!decision.Approve)
                return Error(call, decision.Reason ?? "File write was rejected by the operator.");
        }

        await File.WriteAllTextAsync(path, content, ct);
        return new ToolResult(call.Id, call.Name, $"wrote {content.Length} characters to {path}");
    }

    private ToolResult ListFiles(ToolCall call, JsonElement args)
    {
        var requested = args.TryGetProperty("path", out var p) && p.ValueKind == JsonValueKind.String
            ? p.GetString()!
            : ".";
        var path = ResolvePath(requested);
        if (!Directory.Exists(path))
            return Error(call, $"directory not found: {path}");

        var entries = Directory.EnumerateFileSystemEntries(path)
            .Select(e => Directory.Exists(e) ? $"{Path.GetFileName(e)}/" : Path.GetFileName(e))
            .OrderBy(e => e, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new ToolResult(call.Id, call.Name, entries.Count > 0 ? string.Join('\n', entries) : "(empty directory)");
    }

    private async Task<ToolResult> RunCommandAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var command = RequireString(args, "command");
        var requestedDirectory = args.TryGetProperty("workingDirectory", out var wd) && wd.ValueKind == JsonValueKind.String
            ? wd.GetString()!
            : ".";
        // Same resolution as ResolvePath's Full branch: relative resolves
        // against _workspaceRoot (an explicit absolute path still passes
        // through unconfined - Full stays unrestricted, this only fixes what
        // a RELATIVE path/the omitted-argument default means). Used to
        // default straight to Environment.CurrentDirectory - wherever this
        // node process's own exe happened to launch from, unrelated to the
        // folder the agent is actually meant to be working in.
        var workingDirectory = Path.GetFullPath(requestedDirectory, _workspaceRoot);

        var psi = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? new ProcessStartInfo("cmd.exe", $"/c {command}")
            : new ProcessStartInfo("/bin/sh", $"-c \"{command.Replace("\"", "\\\"")}\"");
        // Unlike Sandboxed, a Full-mode executor's _workspaceRoot is never
        // auto-created (see the constructor) - Process.Start throws on a
        // working directory that doesn't exist, so fall all the way back to
        // this process's own cwd (guaranteed to exist) rather than risk that,
        // if even _workspaceRoot itself turns out to be missing.
        psi.WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory
            : Directory.Exists(_workspaceRoot) ? _workspaceRoot
            : Environment.CurrentDirectory;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = new CancellationTokenSource(CommandTimeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            TryKill(process);
            return Error(call, $"command timed out after {CommandTimeout.TotalMinutes:0}m and was killed. Partial output:\n{Truncate(stdout.ToString())}");
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            throw;
        }

        var output = $"exit code: {process.ExitCode}\nstdout:\n{stdout}\nstderr:\n{stderr}";
        return new ToolResult(call.Id, call.Name, Truncate(output), IsError: process.ExitCode != 0);
    }

    // Shared across calls/executors: sockets are pooled per handler, and a
    // per-call HttpClient would exhaust ports under an agent that reads many
    // pages. Redirects are capped by the default handler (50); the 10s
    // timeout is per request.
    private static readonly HttpClient FetchClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const int MaxFetchBytes = 1_000_000;

    private async Task<ToolResult> FetchUrlAsync(ToolCall call, JsonElement args, CancellationToken ct)
    {
        var rawUrl = RequireString(args, "url");
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Error(call, $"fetch_url only accepts absolute http:// or https:// URLs, got: {rawUrl}");

        using var response = await FetchClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode)
            return Error(call, $"fetch failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase} from {uri}");

        // Read at most MaxFetchBytes no matter what Content-Length claims -
        // the model asked for "the page", not an unbounded download.
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        var buffer = new char[MaxFetchBytes];
        var read = await reader.ReadBlockAsync(buffer, ct);
        var raw = new string(buffer, 0, read);

        var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
        var text = contentType.Contains("html", StringComparison.OrdinalIgnoreCase) || raw.TrimStart().StartsWith('<')
            ? HtmlToText(raw)
            : raw;
        return new ToolResult(call.Id, call.Name, Truncate($"[{uri}]\n{text}"));
    }

    /// <summary>Crude, dependency-free readable-text extraction: good enough
    /// for an agent to read documentation/articles, deliberately not a
    /// browser. Scripts/styles removed entirely, remaining tags stripped,
    /// the handful of entities that dominate real pages decoded, whitespace
    /// collapsed to at most one blank line.</summary>
    public static string HtmlToText(string html)
    {
        var text = Regex.Replace(html, "<(script|style)[^>]*>.*?</\\1>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<br\\s*/?>|</p>|</div>|</li>|</h[1-6]>|</tr>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = text.Replace("&nbsp;", " ").Replace("&amp;", "&").Replace("&lt;", "<")
            .Replace("&gt;", ">").Replace("&quot;", "\"").Replace("&#39;", "'");
        text = Regex.Replace(text, "[ \\t]+", " ");
        text = Regex.Replace(text, "( ?\\n ?)+", "\n");
        return text.Trim();
    }

    private static void TryKill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch { /* best effort */ }
    }

    private string ResolvePath(string requestedPath)
    {
        // Full is deliberately unconfined - not a security boundary, that's
        // the whole point of Full ("exactly like Claude Code/Codex have on
        // the machine they run on") - an absolute path still passes straight
        // through here untouched. What changes: a RELATIVE path used to
        // resolve against Environment.CurrentDirectory (wherever this node's
        // own exe happened to launch from), which has nothing to do with the
        // folder the agent is actually working in. Path.GetFullPath's two-arg
        // overload resolves a relative path against _workspaceRoot instead,
        // while still returning an absolute path as-is when one is given.
        if (_level == AgentAccessLevel.Full)
            return Path.GetFullPath(requestedPath, _workspaceRoot);

        // Sandboxed: reject absolute paths outright rather than trying to
        // "combine" them - Path.Combine(root, absolutePath) on Windows
        // silently DISCARDS root and returns the absolute path as-is, which
        // would otherwise be a trivial sandbox escape (write_file path:
        // "C:\Windows\System32\whatever" resolving right past the workspace).
        if (Path.IsPathRooted(requestedPath))
            throw new UnauthorizedAccessException(
                $"absolute paths are not allowed in sandboxed mode: '{requestedPath}' - use a path relative to the workspace root");

        var combined = Path.GetFullPath(Path.Combine(_workspaceRoot, requestedPath));
        var rootWithSeparator = _workspaceRoot.EndsWith(Path.DirectorySeparatorChar)
            ? _workspaceRoot
            : _workspaceRoot + Path.DirectorySeparatorChar;

        // Path.GetFullPath already collapses ".." segments, so this single
        // prefix check also catches relative traversal attempts like
        // "../../../etc/passwd" - nothing under _workspaceRoot survives
        // GetFullPath and still fails this check.
        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(combined, _workspaceRoot, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException(
                $"path '{requestedPath}' resolves outside this Worker's sandboxed workspace");

        return combined;
    }

    private static string RequireString(JsonElement args, string property) =>
        args.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new ArgumentException($"missing required argument: {property}");

    private static string Truncate(string s) =>
        s.Length > MaxOutputChars ? s[..MaxOutputChars] + $"\n...(truncated, {s.Length} characters total)" : s;

    private static ToolResult Error(ToolCall call, string message) => new(call.Id, call.Name, message, IsError: true);
}
