using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>Live smoke of the full provisioning chain (network!): download
/// MinGit from the pinned GitHub release (exercises the redirect-to-
/// githubusercontent allowance), full-zip extraction, and ToolLocator
/// resolution. Skipped silently when offline. Trait-filtered so the normal
/// suite never hits the network.</summary>
[Trait("Category", "LiveNetwork")]
public class LiveProvisionSmokeTests
{
    [Fact]
    public async Task ProvisionGit_DownloadsExtractsAndLocates()
    {
        // Nataccess + ~50 MB nedladdning: kors bara nar operatoren uttryckligen
        // ber om det (AILOCAL_LIVE_TESTS=1) - aldrig i den vanliga sviten.
        if (Environment.GetEnvironmentVariable("AILOCAL_LIVE_TESTS") != "1")
            return;

        var dest = Path.Combine(Path.GetTempPath(), "ailocal-provision-live-" + Guid.NewGuid().ToString("n"));
        try
        {
            var result = await new ToolProvisioner().ProvisionAsync("git", dest, CancellationToken.None);
            Assert.True(result.Success, result.Output);
            var git = Path.Combine(dest, "MinGit", "cmd", "git.exe");
            Assert.True(File.Exists(git), $"git.exe saknas efter extraktion: {git}");
        }
        finally
        {
            try { Directory.Delete(dest, recursive: true); } catch { /* best effort */ }
        }
    }
}
