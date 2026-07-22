using System.IO.Compression;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>B4 (delbar leverans): paketeringen ska inkludera skärmdumpar och
/// bära ett riktigt versionsnamn (inte den gamla hårdkodade v1.0), samt lägga
/// README i zip-roten - så "Packa" ger ett delbart paket med exe + README +
/// skärmdump.</summary>
public class PackageServiceTests : IDisposable
{
    private readonly string _root;

    public PackageServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "ailocal-pkg-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* städning */ }
    }

    [Fact]
    public async Task PackageAsync_InkluderarSkarmdumpOchVersionsnamn()
    {
        // Ett minimalt "byggt" projekt: en exe-attrapp + en skärmdump.
        var buildDir = Path.Combine(_root, "build");
        Directory.CreateDirectory(buildDir);
        await File.WriteAllTextAsync(Path.Combine(buildDir, "Spelet.exe"), "MZ dummy");
        var shots = Path.Combine(_root, "screenshots");
        Directory.CreateDirectory(shots);
        await File.WriteAllBytesAsync(Path.Combine(shots, "playtest.png"), new byte[] { 137, 80, 78, 71, 1, 2, 3, 4 });

        var outDir = Path.Combine(_root, "dist");
        var result = await new PackageService().PackageAsync(_root, "godot", "Spelet", outDir, CancellationToken.None);

        Assert.True(result.Success, result.Output);
        Assert.NotNull(result.PackagePath);

        // Versionsnamn: den riktiga AiLocal-versionen, inte den gamla v1.0.
        var expectedVersion = typeof(PackageService).Assembly.GetName().Version!.ToString(3);
        Assert.Contains($"-v{expectedVersion}.zip", result.PackagePath);
        Assert.DoesNotContain("-v1.0.zip", result.PackagePath!);

        using var zip = ZipFile.OpenRead(result.PackagePath!);
        var entries = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        Assert.Contains(entries, e => e.Contains("screenshots/playtest.png")); // skärmdump med
        Assert.Contains(entries, e => e.EndsWith("Spelet.exe"));               // exe med
        Assert.Contains(entries, e => e == "README.md");                       // README i zip-roten

        // README:n refererar skärmdumpen.
        var readmeEntry = zip.Entries.First(e => e.FullName.EndsWith("README.md"));
        using var reader = new StreamReader(readmeEntry.Open());
        var readme = await reader.ReadToEndAsync();
        Assert.Contains("playtest.png", readme);
    }
}
