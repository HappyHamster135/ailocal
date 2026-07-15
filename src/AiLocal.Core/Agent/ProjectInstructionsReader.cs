namespace AiLocal.Core.Agent;

/// <summary>
/// Reads a session's optional AILOCAL.md - project-level context the operator
/// drops at the root of a folder they've bound a session to, automatically
/// folded into that session's system prompt (see AgentLoop's `system`
/// parameter) so the agent starts each run already knowing about the
/// project instead of needing to be told the same thing every time.
/// </summary>
public static class ProjectInstructionsReader
{
    private const string FileName = "AILOCAL.md";
    private const int MaxChars = 20_000;

    /// <summary>Null on anything short of success (missing file, unreadable,
    /// not actually a file) - a project file is optional context, never a
    /// reason to fail a run.</summary>
    public static async Task<string?> TryReadAsync(string folderPath, CancellationToken ct = default)
    {
        try
        {
            var path = Path.Combine(folderPath, FileName);
            if (!File.Exists(path))
                return null;

            var content = await File.ReadAllTextAsync(path, ct);
            if (string.IsNullOrWhiteSpace(content))
                return null;

            return content.Length > MaxChars
                ? content[..MaxChars] + $"\n...(truncated, {content.Length} characters total)"
                : content;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // IOException, UnauthorizedAccessException, a symlink loop,
            // whatever - a project file the OS won't let us read is exactly
            // as absent as one that doesn't exist, from the agent's view.
            return null;
        }
    }
}
