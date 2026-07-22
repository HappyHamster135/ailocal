using AiLocal.Core.Configuration;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>B2 (vidareutveckla-knappen): iterationsuppdraget skickar projektets
/// RELATIVA väg och /api/assignment resolvar den till projektmappen via
/// WorkerRole.ResolveProjectDir. Den vakten är load-bearing (delas av alla
/// portföljendpoints) men var otestad - det här låser traversal-skyddet,
/// kravet på att målet faktiskt ÄR ett projekt, och att en giltig rel pekar
/// rätt så kontinuitetsbriefen kör mot rätt mapp.</summary>
public class ProjectResolveTests : IDisposable
{
    private readonly string _workspace;
    private readonly NodeSettings _settings;

    public ProjectResolveTests()
    {
        _workspace = Path.Combine(Path.GetTempPath(), "ailocal-resolve-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_workspace);
        _settings = new NodeSettings();
        _settings.Worker.WorkspacePath = _workspace;
    }

    public void Dispose()
    {
        try { Directory.Delete(_workspace, recursive: true); } catch { /* städning */ }
    }

    [Fact]
    public void ResolveProjectDir_ScaffoldatProjekt_ResolvarRatt()
    {
        var result = new GameScaffoldService().Scaffold("godot", "bygg ett plattformsspel i godot", _workspace);
        Assert.True(result.Success, result.Output);
        var rel = Path.GetRelativePath(_workspace, result.Path).Replace('\\', '/');

        var dir = WorkerRole.ResolveProjectDir(_settings, rel);
        Assert.NotNull(dir);
        Assert.Equal(Path.GetFullPath(result.Path), Path.GetFullPath(dir!));
    }

    [Theory]
    [InlineData("../rymning")]
    [InlineData("..\\..\\rymning")]
    [InlineData("")]
    [InlineData(null)]
    public void ResolveProjectDir_TraversalOchTomt_GerNull(string? rel)
    {
        Assert.Null(WorkerRole.ResolveProjectDir(_settings, rel));
    }

    [Fact]
    public void ResolveProjectDir_MappSomInteArProjekt_GerNull()
    {
        // En vanlig mapp utan projektstruktur far aldrig resolvas - vakten
        // kraver att malet ar ett igenkant projekt, inte vilken mapp som helst.
        var plain = Path.Combine(_workspace, "bara-en-mapp");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "anteckning.txt"), "hej");

        Assert.Null(WorkerRole.ResolveProjectDir(_settings, "bara-en-mapp"));
    }
}
