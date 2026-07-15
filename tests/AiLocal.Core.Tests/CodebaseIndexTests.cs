using AiLocal.Core.Agent;
using Xunit;

namespace AiLocal.Core.Tests;

public class CodebaseIndexTests : IDisposable
{
    private readonly string _root;
    public CodebaseIndexTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ailocal-idx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Build_IndexesSourceFilesAndSkipsIgnoredDirs()
    {
        File.WriteAllText(Path.Combine(_root, "auth.cs"), "public class AuthService { void Login() {} }");
        Directory.CreateDirectory(Path.Combine(_root, "bin"));
        File.WriteAllText(Path.Combine(_root, "bin", "skip.dll"), "junk");
        var idx = new CodebaseIndex();
        idx.Build(_root);
        Assert.Equal(1, idx.FileCount);
    }

    [Fact]
    public void Recall_ReturnsMostRelevantFileFirst()
    {
        File.WriteAllText(Path.Combine(_root, "auth.cs"), "public class AuthService { void Login() {} void Logout() {} }");
        File.WriteAllText(Path.Combine(_root, "math.cs"), "public class Calculator { void Add() {} }");
        var idx = new CodebaseIndex();
        idx.Build(_root);
        var hits = idx.Recall("AuthService login logout");
        Assert.NotEmpty(hits);
        Assert.Contains("auth.cs", hits[0].Path);
    }

    [Fact]
    public void Build_IsIncremental_UnchangedFilesAreKept()
    {
        var file = Path.Combine(_root, "svc.cs");
        File.WriteAllText(file, "public class Svc { void Do() {} }");
        var idx = new CodebaseIndex();
        idx.Build(_root);
        var firstCount = idx.FileCount;
        // Rebuild without changes - should not grow.
        idx.Build(_root);
        Assert.Equal(firstCount, idx.FileCount);
    }
}

public class ProjectMemoryTests : IDisposable
{
    private readonly string _root;
    public ProjectMemoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ailocal-mem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }
    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    [Fact]
    public void Remember_PersistsAndReadReturnsNotes()
    {
        var mem = new ProjectMemory(_root);
        Assert.Equal("", mem.Read());
        mem.Remember("Auth uses JWT in a httpOnly cookie.");
        mem.Remember("Retry logic lives in DispatchWithRetryAsync.");
        var text = mem.Read();
        Assert.Contains("Auth uses JWT", text);
        Assert.Contains("Retry logic lives in DispatchWithRetryAsync", text);
    }

    [Fact]
    public void Clear_EmptiesMemory()
    {
        var mem = new ProjectMemory(_root);
        mem.Remember("note");
        mem.Clear();
        Assert.Equal("", mem.Read());
    }
}
