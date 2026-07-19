using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Locks the crash fixed in the session/assignment call sites: they
/// construct AssetGenerator without DI (no factory, no logger), and with no
/// REPLICATE_API_TOKEN the old non-null logger field threw
/// NullReferenceException BEFORE the procedural fallback could run - so
/// generate_asset was dead in the most common configuration. Uses the
/// EnvIsolated collection because the token is read from the process
/// environment.</summary>
[Collection("EnvIsolated")]
public class AssetGeneratorTests : IDisposable
{
    private readonly string _dir;
    private readonly string? _savedToken;

    public AssetGeneratorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ailocal-asset-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dir);
        _savedToken = Environment.GetEnvironmentVariable("REPLICATE_API_TOKEN");
        Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", null);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("REPLICATE_API_TOKEN", _savedToken);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task NoDependenciesAndNoApiToken_FallsBackToProceduralInsteadOfCrashing()
    {
        var generator = new AssetGenerator(); // exactly how SessionApi builds it
        var output = Path.Combine(_dir, "hero.png");

        var result = await generator.GenerateAsync(
            "sprite", "a pixel-art hero", 64, 64, output, CancellationToken.None);

        Assert.True(result.Success, result.Output);
        Assert.NotNull(result.FilePath);
        Assert.True(File.Exists(result.FilePath), $"expected a generated file at {result.FilePath}");
    }
}
