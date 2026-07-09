using AiLocal.Core.Configuration;
using Xunit;

namespace AiLocal.Core.Tests;

public class TlsSettingsTests
{
    [Fact]
    public void HttpsPortFor_TypicalFixedPort_ReturnsPortPlusOffset()
    {
        var tls = new TlsSettings { Enabled = true, PortOffset = 10000 };

        Assert.Equal(15080, tls.HttpsPortFor(5080));
    }

    [Fact]
    public void HttpsPortFor_Disabled_ReturnsNull()
    {
        var tls = new TlsSettings { Enabled = false, PortOffset = 10000 };

        Assert.Null(tls.HttpsPortFor(5080));
    }

    [Theory]
    [InlineData(60000)] // reproduces the reported crash: the desktop app's
                         // Launcher role binds an OS-assigned ephemeral port
                         // (commonly 49152-65535); 60000 + 10000 overflows 65535.
    [InlineData(65536)]
    [InlineData(int.MaxValue)]
    public void HttpsPortFor_SumExceedsMaxTcpPort_ReturnsNullInsteadOfCrashing(int highPort)
    {
        var tls = new TlsSettings { Enabled = true, PortOffset = 10000 };

        Assert.Null(tls.HttpsPortFor(highPort));
    }

    [Fact]
    public void HttpsPortFor_MaxValidPort_StillWorks()
    {
        var tls = new TlsSettings { Enabled = true, PortOffset = 0 };

        Assert.Equal(65535, tls.HttpsPortFor(65535));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void HttpsPortFor_NonPositivePort_ReturnsNull(int badPort)
    {
        var tls = new TlsSettings { Enabled = true, PortOffset = 10000 };

        Assert.Null(tls.HttpsPortFor(badPort));
    }
}
