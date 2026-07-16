using AiLocal.Node.Roles;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Tests for FilesApi.Resolve — the workspace-scoped path resolution
/// that enforces the sandboxed access contract for the file explorer endpoints.
/// </summary>
public sealed class FilesApiScopingTests
{
    [Fact]
    public void Resolve_SubPath_ReturnsCombinedPath()
    {
        var result = FilesApi.Resolve(@"C:\ws", "sub/a.txt");
        Assert.Equal(@"C:\ws\sub\a.txt", result);
    }

    [Fact]
    public void Resolve_Dot_ReturnsRoot()
    {
        var result = FilesApi.Resolve(@"C:\ws", ".");
        Assert.Equal(@"C:\ws", result);
    }

    [Fact]
    public void Resolve_AbsolutePath_Throws()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            FilesApi.Resolve(@"C:\ws", @"C:\ws\x"));
        Assert.Contains("absolute paths not allowed", ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_ParentDirectoryTraversal_Throws()
    {
        var ex = Assert.Throws<UnauthorizedAccessException>(() =>
            FilesApi.Resolve(@"C:\ws", @"..\evil"));
        Assert.Contains("resolves outside", ex.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_SameDirectory_ReturnsRoot()
    {
        var result = FilesApi.Resolve(@"C:\ws", @".");
        Assert.Equal(@"C:\ws", result);
    }
}