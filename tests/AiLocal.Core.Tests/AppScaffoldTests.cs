using System.IO;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>The agent should be able to produce a runnable APP (not just a
/// game) in one tool call, picking the tech itself: python or csharp, or
/// 'auto'/empty to let the tool infer from the prompt. These tests prove the
/// scaffold writes real, complete projects to disk.</summary>
public class AppScaffoldTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "ailocal-appscaffold-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void Scaffold_Python_WritesRunnableApp()
    {
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("python", "ett cli-verktyg som räknar ord", _dir);

        Assert.True(result.Success, result.Output);
        Assert.True(File.Exists(Path.Combine(_dir, "main.py")));
        Assert.True(File.Exists(Path.Combine(_dir, "requirements.txt")));
        Assert.True(File.Exists(Path.Combine(_dir, "README.md")));

        var py = File.ReadAllText(Path.Combine(_dir, "main.py"));
        Assert.Contains("def main()", py);
        Assert.Contains("argparse", py);
        Assert.Contains("__main__", py);
    }

    [Fact]
    public void Scaffold_CSharp_WritesBuildableProject()
    {
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("csharp", "a console tool", _dir);

        Assert.True(result.Success, result.Output);
        // Dir name contains dashes -> the scaffold must sanitize it into a
        // valid C# identifier for csproj/namespace.
        var name = new DirectoryInfo(_dir).Name.Replace("-", "_");
        Assert.True(File.Exists(Path.Combine(_dir, $"{name}.csproj")));
        var program = Path.Combine(_dir, "Program.cs");
        Assert.True(File.Exists(program));

        var cs = File.ReadAllText(program);
        Assert.Contains("static int Main", cs);
        Assert.Contains($"namespace {name};", cs);
        // Sanity: braces must balance (guards against string-template bugs).
        int depth = 0, min = 0;
        foreach (var c in cs)
        {
            if (c == '{') depth++;
            else if (c == '}') { depth--; min = Math.Min(min, depth); }
        }
        Assert.Equal(0, depth);
        Assert.Equal(0, min);
    }

    [Fact]
    public void Scaffold_Auto_PicksCSharpFromPrompt()
    {
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("auto", "bygg ett dotnet-verktyg i c#", _dir);
        Assert.True(result.Success, result.Output);
        Assert.Equal("csharp", result.Tech);
    }

    [Fact]
    public void Scaffold_Auto_DefaultsToPython()
    {
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("", "en väderapp", _dir);
        Assert.True(result.Success, result.Output);
        Assert.Equal("python", result.Tech);
    }

    [Fact]
    public void Scaffold_RejectsUnknownTech()
    {
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("cobol", "x", _dir);
        Assert.False(result.Success);
    }

    [Fact]
    public void Scaffold_NonEmptyDir_FallsBackToSubfolder_AndKeepsExistingFiles()
    {
        // v1.29.0: en icke-tom root avvisas inte längre - scaffolden landar i
        // en härledd undermapp och befintliga filer lämnas orörda.
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "existing.txt"), "x");
        var svc = new AppScaffoldService();
        var result = svc.Scaffold("python", "x", _dir);
        Assert.True(result.Success, result.Output);
        Assert.NotEqual(Path.GetFullPath(_dir), Path.GetFullPath(result.Path));
        Assert.Equal("x", File.ReadAllText(Path.Combine(_dir, "existing.txt")));
    }

    [Fact]
    public void GameScaffold_Auto_PicksHtml5ByDefault()
    {
        var svc = new GameScaffoldService();
        var result = svc.Scaffold("auto", "en enkel 2d-plattformare", _dir);
        Assert.True(result.Success, result.Output);
        Assert.Equal("html5", result.Engine);
        Assert.True(File.Exists(Path.Combine(_dir, "index.html")));
    }
}
