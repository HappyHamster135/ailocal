using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace AiLocal.Node.Hosting;

public sealed class ClusterTokenHandler : DelegatingHandler
{
    private readonly PersistentSettingsStore _settings;

    public ClusterTokenHandler(PersistentSettingsStore settings)
    {
        _settings = settings;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_settings.GetClusterToken() is { Length: > 0 } token)
            request.Headers.TryAddWithoutValidation(ClusterSecurity.HeaderName, token);
        return base.SendAsync(request, cancellationToken);
    }
}

public static class ClusterSecurity
{
    public const string HeaderName = "X-AiLocal-Token";
    private const string AdminTierItemKey = "AiLocal.AdminTier";

    public static async Task Authorize(
        HttpContext context,
        PersistentSettingsStore settings,
        Func<Task> next)
    {
        var adminToken = settings.GetClusterToken();
        if (string.IsNullOrWhiteSpace(adminToken) || IsPublic(context.Request.Path))
        {
            context.Items[AdminTierItemKey] = true;
            await next();
            return;
        }

        var nodeOnly = IsNodeOnly(context.Request.Path);
        var isLoopback = context.Connection.RemoteIpAddress is { } address &&
            IPAddress.IsLoopback(address);

        // Physical/local access to this machine's own dashboard is already
        // trusted (same trust boundary as sitting at the keyboard) - skip the
        // token check entirely, same as before RBAC existed. Node-to-node
        // endpoints never get this bypass, even from loopback, since a
        // co-located rogue process should not be able to impersonate the cluster.
        if (!nodeOnly && isLoopback)
        {
            context.Items[AdminTierItemKey] = true;
            await next();
            return;
        }

        // The header is used by node-to-node calls and same-origin dashboard
        // fetches; the query string is a fallback for EventSource (the browser
        // SSE API cannot set custom headers) and for shareable pairing links.
        var presented = context.Request.Headers[HeaderName].FirstOrDefault()
            ?? context.Request.Query["token"].FirstOrDefault();
        if (SecureEquals(adminToken, presented))
        {
            context.Items[AdminTierItemKey] = true;
            await next();
            return;
        }

        // Operator tier: a second, lower-privilege token that can submit/view
        // goals, chat, and cancel tasks remotely, but not touch node membership,
        // settings, worker runtime setup, or the admin token itself. Never valid
        // for node-only (node-to-node) endpoints - those need real node identity.
        if (!nodeOnly && !RequiresAdminTier(context.Request.Method, context.Request.Path) &&
            settings.GetOperatorToken() is { Length: > 0 } operatorToken &&
            SecureEquals(operatorToken, presented))
        {
            context.Items[AdminTierItemKey] = false;
            await next();
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            error = "invalid or missing cluster token"
        });
    }

    /// <summary>True only when this request was authorized at admin tier (or
    /// auth is fully open, or came from the trusted-loopback bypass) - false
    /// for the restricted operator tier, and false (fail closed) if Authorize
    /// never ran for some reason. RequiresAdminTier only gates whether a
    /// request is ALLOWED through at all; endpoints that hand back secrets
    /// (e.g. GET /api/settings) must check this separately and redact for
    /// anything less than admin tier, since operator tier is allowed to GET
    /// plenty of endpoints that admin-gating alone would never distinguish.</summary>
    public static bool IsAdminTier(HttpContext context) =>
        context.Items.TryGetValue(AdminTierItemKey, out var value) && value is true;

    private static bool IsPublic(PathString path) =>
        path == "/" ||
        path.StartsWithSegments("/health") ||
        path.StartsWithSegments("/api/local") ||
        // The click-to-pair handshake is deliberately reachable without the
        // cluster token - that's the credential being negotiated. Each call
        // is instead bound to a random per-request nonce (see
        // PairingCoordinator) that only the two consenting parties ever see,
        // and the actual cluster token is never sent until both sides have
        // explicitly approved. Every other /pairing/* route (viewing or
        // approving pending requests) stays behind the normal token/loopback
        // gate - only these two exact handshake endpoints are public.
        path == "/pairing/request" ||
        path == "/pairing/approved" ||
        // A Host announces itself to an Overseer carrying its OWN token, which
        // the Overseer stores and later uses to proxy back. The Overseer's own
        // token isn't known to the Host yet (that's the whole problem this
        // solves), so the announce endpoint must be reachable without it -
        // same LAN-trust opt-in model as the pairing handshake.
        path == "/cluster/announce";

    private static bool IsNodeOnly(PathString path) =>
        path.StartsWithSegments("/cluster") ||
        path.StartsWithSegments("/execute") ||
        path.StartsWithSegments("/runtime");

    /// <summary>
    /// Actions an operator-tier token must never be allowed to perform: node
    /// membership changes, settings reads or writes (local or proxied to a
    /// worker - a GET here proxies straight to that worker's own /api/settings,
    /// which hands back the same shared cluster token /api/settings redacts
    /// for non-admin callers, see the redaction below), worker runtime
    /// installs, deleting a schedule, and deleting a session. Everything else
    /// that passes the security gate (goal/chat submission, task cancel,
    /// viewing nodes/tasks/stats/schedules, creating/running a schedule,
    /// creating/messaging/running a session) is operator-safe - a session
    /// already carries the exact same Full-mode file/command power an
    /// operator token could already reach via /api/assignment, so this
    /// doesn't widen what an operator-tier caller could already do.
    /// </summary>
    private static bool RequiresAdminTier(string method, PathString path)
    {
        if (path.StartsWithSegments("/api/nodes", out var remainder))
        {
            if (string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
                return true; // remove node

            var segments = remainder.Value ?? "";
            if (segments.EndsWith("/restore", StringComparison.Ordinal) &&
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return true;

            if (segments.EndsWith("/settings", StringComparison.Ordinal) &&
                (string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase)))
                return true;

            if ((segments.EndsWith("/runtime/pull", StringComparison.Ordinal) ||
                    segments.EndsWith("/runtime/setup", StringComparison.Ordinal)) &&
                string.Equals(method, "POST", StringComparison.OrdinalIgnoreCase))
                return true;

            return false;
        }

        if (path == "/api/settings" && string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWithSegments("/api/schedules") &&
            string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWithSegments("/api/hosts") &&
            string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            return true;

        if (path.StartsWithSegments("/api/sessions") &&
            string.Equals(method, "DELETE", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool SecureEquals(string expected, string? actual)
    {
        if (actual is null)
            return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var actualHash = SHA256.HashData(Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(expectedHash, actualHash);
    }
}
