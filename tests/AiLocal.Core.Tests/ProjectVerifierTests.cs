using AiLocal.Core.Agent;
using Xunit;

namespace AiLocal.Core.Tests;

public class ProjectVerifierTests : IDisposable
{
    private readonly string _scratch =
        Path.Combine(Path.GetTempPath(), "ailocal-verify-tests-" + Guid.NewGuid().ToString("n"));

    public ProjectVerifierTests() => Directory.CreateDirectory(_scratch);
    public void Dispose() { try { Directory.Delete(_scratch, recursive: true); } catch { /* best effort */ } }

    [Fact]
    public void Detect_DotNet_FindsCsproj()
    {
        File.WriteAllText(Path.Combine(_scratch, "app.csproj"), "<Project />");
        Assert.Equal(ProjectVerifier.ProjectKind.DotNet, new ProjectVerifier().Detect(_scratch));
    }

    [Fact]
    public void Detect_Node_FindsPackageJson()
    {
        File.WriteAllText(Path.Combine(_scratch, "package.json"), "{}");
        Assert.Equal(ProjectVerifier.ProjectKind.Node, new ProjectVerifier().Detect(_scratch));
    }

    [Fact]
    public void Detect_Unknown_WhenNoProjectFiles()
    {
        Assert.Equal(ProjectVerifier.ProjectKind.Unknown, new ProjectVerifier().Detect(_scratch));
    }

    [Fact]
    public async Task Verify_ReturnsPass_WhenCommandExitsZero()
    {
        // Needs a detectable project so the runner is actually invoked.
        File.WriteAllText(Path.Combine(_scratch, "app.csproj"), "<Project />");
        var verifier = new ProjectVerifier();
        var result = await verifier.VerifyAsync(_scratch,
            (cmd, dir, ct) => Task.FromResult((0, "Build succeeded.")), CancellationToken.None);
        Assert.True(result.Success);
        Assert.Equal(ProjectVerifier.ProjectKind.DotNet, result.Kind);
    }

    [Fact]
    public async Task Verify_ExtractsDotNetErrors_FromFailedBuild()
    {
        var verifier = new ProjectVerifier();
        File.WriteAllText(Path.Combine(_scratch, "app.csproj"), "<Project />");
        var output = "C:\\src\\Program.cs(12,3): error CS1002: ; expected [app]";
        var result = await verifier.VerifyAsync(_scratch,
            (cmd, dir, ct) => Task.FromResult((1, output)), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("CS1002", result.Report);
    }

    [Fact]
    public async Task Verify_UnknownProject_ReportsNoProject()
    {
        var verifier = new ProjectVerifier();
        var result = await verifier.VerifyAsync(_scratch,
            (cmd, dir, ct) => Task.FromResult((0, "ignored")), CancellationToken.None);
        Assert.False(result.Success);
        Assert.Contains("No recognizable project", result.Report);
    }
}
