using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The compiler-grade JS gate for HTML5 games: a syntax error means
/// a silent black screen, so verify/playtest must catch it offline without
/// Node.js. Locks parser behaviour, the verify integration and the exe-name
/// derivation added in the same push.</summary>
public class JsSyntaxCheckerTests : IDisposable
{
    private readonly string _dir;

    public JsSyntaxCheckerTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-jscheck-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void CheckScript_ValidModernJs_ReturnsNull()
    {
        Assert.Null(JsSyntaxChecker.CheckScript(
            "const a = {x: 1}; const b = a?.x ?? 0; const f = async () => { await Promise.resolve(b); };"));
    }

    [Fact]
    public void CheckScript_BrokenJs_ReturnsErrorWithLine()
    {
        var error = JsSyntaxChecker.CheckScript("const a = 1;\nfunction f( { return 2; }");
        Assert.NotNull(error);
        Assert.Equal(2, error!.Line);
    }

    [Fact]
    public void CheckHtml_FindsTheBrokenBlock_AndSkipsSrcScripts()
    {
        var html = """
            <script src='lib.js'></script>
            <script>const ok = 1;</script>
            <script>const broken = ;</script>
            """;
        var errors = JsSyntaxChecker.CheckHtml(html);
        Assert.Single(errors);
        Assert.Contains("script-block 3", errors[0]);
    }

    [Fact]
    public async Task ProjectVerifier_Html5_PassesValidAndFailsBroken()
    {
        var verifier = new ProjectVerifier();
        Func<string, string, CancellationToken, Task<(int, string)>> noCommand =
            (_, _, _) => throw new InvalidOperationException("Html5 verify must not run a shell command");

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"),
            "<canvas id='c'></canvas><script>const x = 1;</script>");
        Assert.Equal(ProjectVerifier.ProjectKind.Html5, verifier.Detect(_dir));
        var ok = await verifier.VerifyAsync(_dir, noCommand, CancellationToken.None);
        Assert.True(ok.Success, ok.Report);

        await File.WriteAllTextAsync(Path.Combine(_dir, "index.html"),
            "<script>function f( { }</script>");
        var bad = await verifier.VerifyAsync(_dir, noCommand, CancellationToken.None);
        Assert.False(bad.Success);
        Assert.Contains("syntax", bad.Report, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectVerifier_RealBuildSystemStillWinsOverIndexHtml()
    {
        File.WriteAllText(Path.Combine(_dir, "index.html"), "<script>1;</script>");
        File.WriteAllText(Path.Combine(_dir, "package.json"), "{}");
        Assert.Equal(ProjectVerifier.ProjectKind.Node, new ProjectVerifier().Detect(_dir));
    }

    [Theory]
    [InlineData(@"C:\games\mitt-spel", "mitt-spel")]
    [InlineData(@"C:\games\Space Blaster!", "Space Blaster")]
    [InlineData(@"C:\games\...", "Game")]
    public void DeriveExeName_UsesFolderNameSafely(string root, string expected)
    {
        Assert.Equal(expected, GameBuilder.DeriveExeName(root));
    }
}
