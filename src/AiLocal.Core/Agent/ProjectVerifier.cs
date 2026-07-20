using System.Text;
using System.Text.RegularExpressions;

namespace AiLocal.Core.Agent;

/// <summary>
/// Detects a project type from a workspace's files and runs the appropriate
/// build/test command, then extracts the failure summary. This is the
/// "verify" half of the programming loop: after the agent edits a file, the
/// executor can run this automatically so the model gets the compiler/test
/// errors back as feedback instead of declaring success on a broken change.
///
/// Pure filesystem + process execution - it carries no ChatClient, so it is
/// trivially unit-testable and the executor owns the process plumbing.
/// </summary>
public sealed class ProjectVerifier
{
    public enum ProjectKind { Unknown, DotNet, Node, Rust, Go, Python, Html5 }

    /// <summary>Runs a build (and test, when the ecosystem supports a single
    /// verify command) for the detected project under <paramref name="root"/>
    /// and returns a human-readable result the agent can act on.</summary>
    public async Task<VerifyResult> VerifyAsync(
        string root,
        Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCommand,
        CancellationToken ct)
    {
        var kind = Detect(root);
        if (kind == ProjectKind.Unknown)
            return new VerifyResult(false, kind, "No recognizable project found (looked for .csproj/.sln, package.json, Cargo.toml, go.mod, pyproject.toml/requirements.txt, index.html).", 0);

        // A plain HTML5 game/app has no build command - "verify" means parsing
        // every inline script and .js file for syntax errors (the failure mode
        // that otherwise ships as a silent black screen). Before this, verify
        // returned "No recognizable project" on every scaffolded HTML5 game,
        // which both confused the agent and left broken JS undetected.
        if (kind == ProjectKind.Html5)
            return VerifyHtml5(root);

        var command = CommandFor(kind);
        var (exitCode, output) = await runCommand(command, root, ct);
        var failures = ExtractFailures(kind, output);

        if (exitCode == 0)
            return new VerifyResult(true, kind, $"BUILD/VERIFY PASSED ({kind}).\n{Truncate(output, 1200)}", failures.Count);

        var summary = failures.Count > 0
            ? string.Join("\n", failures.Take(25))
            : "(no parseable errors - raw output below)";
        return new VerifyResult(false, kind,
            $"BUILD/VERIFY FAILED ({kind}, exit {exitCode}). Fix these and run verify again:\n{summary}\n\n--- raw tail ---\n{Truncate(output, 1500)}",
            failures.Count);
    }

    public ProjectKind Detect(string root)
    {
        if (!Directory.Exists(root)) return ProjectKind.Unknown;
        if (Directory.EnumerateFiles(root, "*.sln", SearchOption.TopDirectoryOnly).Any()
            || Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories).Any())
            return ProjectKind.DotNet;
        if (File.Exists(Path.Combine(root, "package.json")))
            return ProjectKind.Node;
        if (File.Exists(Path.Combine(root, "Cargo.toml")))
            return ProjectKind.Rust;
        if (File.Exists(Path.Combine(root, "go.mod")))
            return ProjectKind.Go;
        if (File.Exists(Path.Combine(root, "pyproject.toml")) || File.Exists(Path.Combine(root, "requirements.txt")))
            return ProjectKind.Python;
        // Last so a web project with a real build system (package.json etc.)
        // verifies through its toolchain instead of the plain-HTML path.
        if (File.Exists(Path.Combine(root, "index.html")))
            return ProjectKind.Html5;
        return ProjectKind.Unknown;
    }

    /// <summary>Syntax-checks index.html's inline scripts plus every loose .js
    /// file under the root (skipping dependency/build dirs) with a real JS
    /// parser. No toolchain needed - works offline on any machine.</summary>
    private static VerifyResult VerifyHtml5(string root)
    {
        var errors = new List<string>();
        var checkedFiles = 0;

        foreach (var htmlFile in Directory.EnumerateFiles(root, "*.html", SearchOption.TopDirectoryOnly))
        {
            checkedFiles++;
            foreach (var e in JsSyntaxChecker.CheckHtml(File.ReadAllText(htmlFile)))
                errors.Add($"{Path.GetFileName(htmlFile)}: {e}");
        }

        var skip = new[] { "node_modules", ".git", "dist", "build", "bin", "obj" };
        foreach (var jsFile in Directory.EnumerateFiles(root, "*.js", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(root, jsFile);
            if (skip.Any(s => rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Contains(s, StringComparer.OrdinalIgnoreCase)))
                continue;
            checkedFiles++;
            if (JsSyntaxChecker.CheckScript(File.ReadAllText(jsFile)) is { } err)
                errors.Add($"{rel}: {err.Message} (rad {err.Line})");
        }

        if (checkedFiles == 0)
            return new VerifyResult(false, ProjectKind.Html5, "index.html found but no scripts to check.", 0);

        return errors.Count == 0
            ? new VerifyResult(true, ProjectKind.Html5,
                $"BUILD/VERIFY PASSED (Html5): {checkedFiles} file(s) parsed, no JavaScript syntax errors.", 0)
            : new VerifyResult(false, ProjectKind.Html5,
                "BUILD/VERIFY FAILED (Html5). JavaScript syntax errors - the game will not run until these are fixed:\n"
                + string.Join("\n", errors.Take(25)), errors.Count);
    }

    private static string CommandFor(ProjectKind kind) => kind switch
    {
        // Alla kommandon loses via ToolLocator: absolut sokvag nar verktyget
        // finns installerat/provisionerat men inte pa PATH (den korande
        // processen ser aldrig PATH-andringar), annars det nakna kommandot.
        ProjectKind.DotNet => $"{ToolLocator.CommandOrDefault("dotnet")} build",
        ProjectKind.Node => $"{ToolLocator.CommandOrDefault("npm")} test --if-present && {ToolLocator.CommandOrDefault("npm")} run build --if-present",
        ProjectKind.Rust => "cargo build",
        ProjectKind.Go => "go build ./...",
        // Absolut sokvag nar python finns installerad men inte pa PATH -
        // t.ex. direkt efter att provision-verktyget installerat den (den
        // korande processen ser aldrig nya PATH). Bara "python" gav exit
        // 9009 och agenten skippade verifieringen helt.
        ProjectKind.Python => $"{PythonLocator.CommandOrDefault()} -m compileall .",
        _ => ""
    };

    /// <summary>Best-effort extraction of individual failure lines so the
    /// agent sees a concise, actionable list instead of megabytes of log.</summary>
    internal static List<string> ExtractFailures(ProjectKind kind, string output)
    {
        var failures = new List<string>();
        if (string.IsNullOrEmpty(output)) return failures;

        switch (kind)
        {
            case ProjectKind.DotNet:
                // CS1002, error CS0123, etc. Capture up to the message start.
                foreach (Match m in Regex.Matches(output, @"[^\n]*(error|warning) CS\d+[^\n]*", RegexOptions.IgnoreCase))
                    failures.Add(m.Value.Trim());
                break;
            case ProjectKind.Node:
            case ProjectKind.Rust:
            case ProjectKind.Go:
                foreach (Match m in Regex.Matches(output, @"[^\n]*\b(error|ERROR|Error)\b[^\n]*"))
                    failures.Add(m.Value.Trim());
                break;
            case ProjectKind.Python:
                foreach (Match m in Regex.Matches(output, @"[^\n]*\b(SyntaxError|IndentationError|Error)\b[^\n]*"))
                    failures.Add(m.Value.Trim());
                break;
        }
        return failures.Distinct().ToList();
    }

    private static string Truncate(string s, int max) =>
        s.Length > max ? s[..max] + $"\n...(truncated, {s.Length} chars total)" : s;
}

public sealed record VerifyResult(bool Success, ProjectVerifier.ProjectKind Kind, string Report, int FailureCount);
