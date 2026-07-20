using System.Text.RegularExpressions;
using Acornima;

namespace AiLocal.Core.Agent;

/// <summary>
/// Real JavaScript syntax validation (Acornima, the parser behind Jint) for
/// the HTML5 game pipeline. A syntax error in an inline script is the single
/// deadliest failure a weak model produces - the game renders a black screen
/// and nothing ever runs - and no build step exists for plain index.html to
/// catch it. This gives verify/playtest a compiler-grade gate that works
/// offline on every machine, no Node.js required.
/// </summary>
public static class JsSyntaxChecker
{
    private static readonly Regex ScriptBlock = new(
        "<script(?<attrs>[^>]*)>(?<body>.*?)</script>",
        RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public sealed record JsError(string Message, int Line, int Column);

    /// <summary>Parses one standalone script. Null when it is valid.</summary>
    public static JsError? CheckScript(string source, bool asModule = false)
    {
        try
        {
            var parser = new Parser();
            if (asModule) parser.ParseModule(source);
            else parser.ParseScript(source);
            return null;
        }
        catch (ParseErrorException ex)
        {
            return new JsError(ex.Error?.Description ?? ex.Message,
                ex.Error?.LineNumber ?? 0, ex.Error?.Column ?? 0);
        }
    }

    /// <summary>Every inline JS &lt;script&gt; body in document order. src=
    /// scripts and non-JS types (importmap/json) are skipped. Shared by the
    /// syntax check below and the runtime smoke tester so both agree on what
    /// "the game's scripts" means.</summary>
    public static IReadOnlyList<string> ExtractInlineScripts(string html)
    {
        var scripts = new List<string>();
        if (string.IsNullOrWhiteSpace(html)) return scripts;

        foreach (Match match in ScriptBlock.Matches(html))
        {
            var attrs = match.Groups["attrs"].Value;
            if (attrs.Contains("src=", StringComparison.OrdinalIgnoreCase))
                continue;
            var isModule = attrs.Contains("type=\"module\"", StringComparison.OrdinalIgnoreCase)
                || attrs.Contains("type='module'", StringComparison.OrdinalIgnoreCase);
            if (attrs.Contains("type=", StringComparison.OrdinalIgnoreCase) && !isModule &&
                !attrs.Contains("text/javascript", StringComparison.OrdinalIgnoreCase))
                continue; // importmap / application/json / templates

            var body = match.Groups["body"].Value;
            if (string.IsNullOrWhiteSpace(body)) continue;
            scripts.Add(body);
        }

        return scripts;
    }

    /// <summary>Extracts every inline &lt;script&gt; block from an HTML
    /// document and parses each. Returns one error line per broken block
    /// ("script block N: message (line X)" with the line number RELATIVE to
    /// the block), empty list when all scripts parse.</summary>
    public static IReadOnlyList<string> CheckHtml(string html)
    {
        var errors = new List<string>();
        var index = 0;
        foreach (var body in ExtractInlineScripts(html))
        {
            index++;
            // Module detection is lost by the shared extractor; parse as
            // script first and fall back to module on failure so neither
            // form false-positives.
            var error = CheckScript(body);
            if (error is not null && CheckScript(body, asModule: true) is not null)
                errors.Add($"script-block {index}: {error.Message} (rad {error.Line}, kolumn {error.Column} i blocket)");
        }

        return errors;
    }
}
