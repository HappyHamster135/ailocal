using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace AiLocal.Core.Agent;

/// <summary>How strictly <see cref="CommandGuard"/> screens shell commands
/// before a Worker runs them. Weak/small local models are the reason this
/// exists: they can be talked into "rm -rf" by a prompt injection, so the
/// guard stays on by default even in Full access mode.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CommandGuardLevel
{
    /// <summary>No screening - runs whatever the model asks (Claude Code / Codex parity).</summary>
    Off,
    /// <summary>Runs the command but prepends a warning to the output so the
    /// operator/context can see it was flagged.</summary>
    Warn,
    /// <summary>Refuses to run any command matching a dangerous pattern. Default.</summary>
    Block
}

/// <summary>
/// Screens shell commands for patterns that can destroy data or the machine
/// (the "rm -rf class"). The default deny-list covers the classics; an
/// operator can extend it per Worker via <see cref="WorkerProfileSettings.BlockedCommands"/>.
/// Matching is case-insensitive substring+regex over the raw command string,
/// so "rm -rf /", "RD /S /Q", "del /f /s", "mkfs", "dd if=", "shutdown",
/// "curl ... | sh" etc. all trip it.
/// </summary>
public sealed class CommandGuard
{
    // Ordered so the most destructive read clearly first; each is a case-insensitive
    // regex matched anywhere in the command. Kept deliberately broad - a false
    // block is a minor annoyance, a false pass can wipe a disk.
    private static readonly string[] DefaultDangerous =
    [
        // recursive/force deletion
        @"\brm\s+(-[^ ]*\s+)*(-r|-rf|-fr|--recursive)\b",
        @"\brm\s+(-[^ ]*\s+)*(-f|-rf|-fr)\s+/",
        @"\bdel\s+/[fsq]",
        @"\brd\s+/[sq]",
        @"\brmdir\s+/[sq]",
        // disk / partition destruction
        @"\bmkfs\b",
        @"\bdd\b[^|]*\bif\s*=",
        @"\bdd\b[^|]*\bof\s*=\s*/dev/",
        @"\bformat\s+[a-z]:",
        @"\bshred\b",
        // privilege / persistence changes that brick a box
        @"\b(chmod|chown)\s+(-r|--recursive)\s+.*\s+/",
        @"\b(chmod|chown)\s+777\s+/",
        // fork bombs / power
        @":\(\)\s*\{\s*:\s*\|\s*:",
        @"\bshutdown\b",
        @"\bhalt\b",
        @"\breboot\b",
        // piping a download straight into a shell (classic supply-chain trap)
        @"\b(curl|wget)\b[^|]*\|\s*(sudo\s+)?(ba)?sh\b",
        @"\|\s*(sudo\s+)?(ba)?sh\b",
        // move-to-trash that nukes the drive
        @"\bmv\s+[^|]*\s+/\s*$",
    ];

    private static readonly Lazy<Regex[]> Compiled = new(() =>
        DefaultDangerous.Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray());

    private readonly CommandGuardLevel _level;
    private readonly Regex[] _extra;

    public CommandGuard(CommandGuardLevel level, IEnumerable<string>? extraPatterns = null)
    {
        _level = level;
        _extra = (extraPatterns ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            .ToArray();
    }

    /// <summary>True when <paramref name="command"/> is refused - i.e. it matches
    /// a dangerous pattern AND the guard is in <see cref="CommandGuardLevel.Block"/>
    /// mode. In <see cref="CommandGuardLevel.Warn"/> or <see cref="CommandGuardLevel.Off"/>
    /// it is never refused (use <see cref="Screen"/> to see the matching warning).</summary>
    public bool IsBlocked(string command)
    {
        if (_level != CommandGuardLevel.Block) return false;
        return Matches(command);
    }

    private bool Matches(string command)
    {
        foreach (var re in Compiled.Value)
            if (re.IsMatch(command)) return true;
        foreach (var re in _extra)
            if (re.IsMatch(command)) return true;
        return false;
    }

    /// <summary>Refuses the command when <see cref="CommandGuardLevel.Block"/> and it
    /// matches; otherwise returns null. When <see cref="CommandGuardLevel.Warn"/> and it
    /// matches, returns a warning string the caller can prepend (but still runs).</summary>
    public string? Screen(string command)
    {
        if (_level == CommandGuardLevel.Off) return null;
        if (!Matches(command)) return null;
        if (_level == CommandGuardLevel.Block)
            return "KOMMANDO BLOCKERAT av kommando-skyddet (rm -rf-klassen). Begär ett säkrare kommando, t.ex. radera en specifik fil eller kör i en isolerad branch.";
        // Warn: let it run but flag it.
        return "VARNING: kommandot matchar kommando-skyddets varningslista (rm -rf-klassen). Det kördes ändå eftersom skyddet är i 'varna'-läge.";
    }

    public static IReadOnlyList<string> DefaultPatterns => DefaultDangerous;
}
