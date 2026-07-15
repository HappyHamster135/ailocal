using AiLocal.Core.Agent;
using Xunit;

namespace AiLocal.Core.Tests;

public class ProjectInstructionsReaderTests : IDisposable
{
    private readonly string _folder;

    public ProjectInstructionsReaderTests()
    {
        _folder = Path.Combine(Path.GetTempPath(), "ailocal-instructions-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task TryReadAsync_FilePresent_ReturnsItsContent()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "AILOCAL.md"), "# Project notes\nUse tabs, not spaces.");

        var result = await ProjectInstructionsReader.TryReadAsync(_folder);

        Assert.Equal("# Project notes\nUse tabs, not spaces.", result);
    }

    [Fact]
    public async Task TryReadAsync_FileMissing_ReturnsNull()
    {
        var result = await ProjectInstructionsReader.TryReadAsync(_folder);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_FileIsWhitespaceOnly_ReturnsNull()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "AILOCAL.md"), "   \n\n  ");

        var result = await ProjectInstructionsReader.TryReadAsync(_folder);

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_FolderDoesNotExist_ReturnsNullNotException()
    {
        var result = await ProjectInstructionsReader.TryReadAsync(Path.Combine(_folder, "does-not-exist"));

        Assert.Null(result);
    }

    [Fact]
    public async Task TryReadAsync_OversizedFile_TruncatesWithMarker()
    {
        var huge = new string('x', 25_000);
        await File.WriteAllTextAsync(Path.Combine(_folder, "AILOCAL.md"), huge);

        var result = await ProjectInstructionsReader.TryReadAsync(_folder);

        Assert.NotNull(result);
        Assert.Contains("truncated, 25000 characters total", result);
        Assert.True(result!.Length < 25_000);
    }

    [Fact]
    public async Task TryReadAsync_Cancelled_ThrowsRatherThanSwallowing()
    {
        await File.WriteAllTextAsync(Path.Combine(_folder, "AILOCAL.md"), "content");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => ProjectInstructionsReader.TryReadAsync(_folder, cts.Token));
    }
}
