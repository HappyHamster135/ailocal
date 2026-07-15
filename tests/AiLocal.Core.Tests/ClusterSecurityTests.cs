using System.Net;
using AiLocal.Core.Configuration;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// Exercises the actual auth gate (Authorize/IsAdminTier/RequiresAdminTier)
/// through a real DefaultHttpContext rather than a mock - this is the exact
/// logic behind two real bugs found and fixed in one session: an operator
/// token could read the admin cluster token back out of GET /api/settings
/// and the equivalent Worker-settings proxy, both via paths RequiresAdminTier
/// only gated for PUT. No Kestrel/WebApplicationFactory needed - Authorize
/// takes a plain HttpContext, and DefaultHttpContext is a real (if minimal)
/// implementation of it.
/// </summary>
[Collection("EnvIsolated")]
public class ClusterSecurityTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _previousDataDir;
    private readonly PersistentSettingsStore _store;
    private readonly HostLocator _hostLocator = new();

    public ClusterSecurityTests()
    {
        _previousDataDir = Environment.GetEnvironmentVariable("AILOCAL_DATA_DIR");
        _dataDir = Path.Combine(Path.GetTempPath(), "ailocal-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _dataDir);

        _store = new PersistentSettingsStore(
            new NodeSettings { Role = NodeRole.Host },
            new EphemeralDataProtectionProvider());
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _previousDataDir);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    private static DefaultHttpContext RemoteContext(
        string? token, string path = "/api/nodes", string method = "GET", string ip = "192.168.1.50")
    {
        var ctx = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        ctx.Request.Path = path;
        ctx.Request.Method = method;
        if (token is not null)
            ctx.Request.Headers[ClusterSecurity.HeaderName] = token;
        return ctx;
    }

    private async Task<bool> InvokeAuthorize(DefaultHttpContext ctx)
    {
        var called = false;
        await ClusterSecurity.Authorize(ctx, _store, () => { called = true; return Task.CompletedTask; });
        return called;
    }

    [Fact]
    public async Task Authorize_AdminToken_FromRemote_PassesAtAdminTier()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        var admin = _store.GetClusterToken()!;

        var ctx = RemoteContext(admin);
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
        Assert.True(ClusterSecurity.IsAdminTier(ctx));
    }

    [Fact]
    public async Task Authorize_OperatorToken_FromRemote_PassesButNotAtAdminTier()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        _store.Update(new SettingsUpdate(RegenerateOperatorToken: true), _hostLocator);
        var operatorToken = _store.GetOperatorToken()!;

        var ctx = RemoteContext(operatorToken);
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
        Assert.False(ClusterSecurity.IsAdminTier(ctx));
    }

    [Fact]
    public async Task Authorize_NoToken_FromRemote_Rejects()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);

        var ctx = RemoteContext(token: null);
        var passed = await InvokeAuthorize(ctx);

        Assert.False(passed);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Authorize_WrongToken_FromRemote_Rejects()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);

        var ctx = RemoteContext("not-the-right-token-at-all");
        var passed = await InvokeAuthorize(ctx);

        Assert.False(passed);
    }

    [Theory]
    [InlineData("/api/settings", "PUT")]
    [InlineData("/api/nodes/abc123/settings", "GET")]
    [InlineData("/api/nodes/abc123/settings", "PUT")]
    [InlineData("/api/nodes/abc123", "DELETE")]
    [InlineData("/api/hosts/abc123", "DELETE")]
    [InlineData("/api/sessions/abc123", "DELETE")]
    public async Task Authorize_OperatorToken_OnAdminOnlyRoute_Rejects(string path, string method)
    {
        // Regression coverage for the two settings-leak fixes: GET on the
        // top-level settings is handled by redaction (not rejection - see
        // the /api/settings-specific test below), but the Worker-settings
        // proxy (GET /api/nodes/{id}/settings) proxies a raw, un-redactable
        // response, so it's gated outright like every other admin-only route.
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        _store.Update(new SettingsUpdate(RegenerateOperatorToken: true), _hostLocator);
        var operatorToken = _store.GetOperatorToken()!;

        var ctx = RemoteContext(operatorToken, path, method);
        var passed = await InvokeAuthorize(ctx);

        Assert.False(passed);
        Assert.Equal(StatusCodes.Status401Unauthorized, ctx.Response.StatusCode);
    }

    [Fact]
    public async Task Authorize_OperatorToken_OnOperatorSafeRoute_Passes()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        _store.Update(new SettingsUpdate(RegenerateOperatorToken: true), _hostLocator);
        var operatorToken = _store.GetOperatorToken()!;

        var ctx = RemoteContext(operatorToken, "/api/chat", "POST");
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
    }

    /// <summary>Creating, messaging, and running a session are operator-safe -
    /// only DELETE is admin-only (see the theory above). A session already
    /// carries the exact same Full-mode power an operator token could already
    /// reach via /api/assignment, so this doesn't widen what it could do.</summary>
    [Theory]
    [InlineData("/api/sessions", "POST")]
    [InlineData("/api/sessions", "GET")]
    [InlineData("/api/sessions/abc123", "GET")]
    [InlineData("/api/sessions/abc123", "PUT")]
    [InlineData("/api/sessions/abc123/run", "POST")]
    [InlineData("/api/sessions/abc123/cancel", "POST")]
    public async Task Authorize_OperatorToken_OnSessionRoute_Passes(string path, string method)
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        _store.Update(new SettingsUpdate(RegenerateOperatorToken: true), _hostLocator);
        var operatorToken = _store.GetOperatorToken()!;

        var ctx = RemoteContext(operatorToken, path, method);
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
        Assert.False(ClusterSecurity.IsAdminTier(ctx));
    }

    /// <summary>A session must be startable from a local browser tab with no
    /// token at all - /api/sessions is NOT node-only (unlike /execute/*), so
    /// it gets the same trusted-loopback bypass every other /api/* route does.</summary>
    [Fact]
    public async Task Authorize_Loopback_OnSessionRunRoute_PassesWithNoTokenAtAdminTier()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);

        var ctx = RemoteContext(token: null, path: "/api/sessions/abc123/run", method: "POST", ip: "127.0.0.1");
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
        Assert.True(ClusterSecurity.IsAdminTier(ctx));
    }

    [Fact]
    public async Task Authorize_PairingRequestPath_IsPublic_EvenWithNoToken()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);

        var ctx = RemoteContext(token: null, path: "/pairing/request", method: "POST");
        var passed = await InvokeAuthorize(ctx);

        Assert.True(passed);
        Assert.True(ClusterSecurity.IsAdminTier(ctx));
    }

    [Fact]
    public async Task Authorize_NodeOnlyRoute_IgnoresOperatorToken_EvenOnOperatorSafeMethod()
    {
        _store.Update(new SettingsUpdate(RegenerateClusterToken: true), _hostLocator);
        _store.Update(new SettingsUpdate(RegenerateOperatorToken: true), _hostLocator);
        var operatorToken = _store.GetOperatorToken()!;

        var ctx = RemoteContext(operatorToken, "/cluster/register", "POST");
        var passed = await InvokeAuthorize(ctx);

        Assert.False(passed);
    }

    [Fact]
    public void IsAdminTier_WhenAuthorizeNeverRan_FailsClosed()
    {
        var ctx = new DefaultHttpContext();
        Assert.False(ClusterSecurity.IsAdminTier(ctx));
    }
}
