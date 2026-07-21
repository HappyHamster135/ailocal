using System.Text.Json.Nodes;
using AiLocal.Core.Nodes;
using AiLocal.Core.Roles;

namespace AiLocal.Node.Hosting;

/// <summary>
/// Pure helpers for cluster-wide delivery and fleet updates:
/// - Rewriting a Worker's final SSE frame so preview/artifact links route
///   through the Host's proxy (/api/nodes/{id}/...) instead of pointing at
///   the Worker's own port (dead from every other machine's dashboard).
/// - Resolving an artifact file safely inside a workspace (traversal guard +
///   extension allowlist - the artifact endpoint must never become a
///   generic file reader).
/// - Picking which registered nodes a fleet update should target.
/// Kept free of ASP.NET types so every rule is unit-testable.
/// </summary>
public static class ClusterDelivery
{
    private static readonly string[] ArtifactExtensions = [".exe", ".zip"];

    /// <summary>Rewrites previewPath/artifactPath in a final SSE frame to go
    /// via the Host proxy for the given worker. Returns Json=null when the
    /// frame is not a final frame (step frames pass through untouched) or
    /// nothing needed rewriting.</summary>
    public static (string? Json, string? PreviewPath, string? ArtifactPath) RewriteFinalFrame(
        string frameJson, string workerId)
    {
        try
        {
            var node = JsonNode.Parse(frameJson);
            if (node?["final"] is null)
                return (null, null, null);

            var preview = RewritePath(node, "previewPath", workerId);
            var artifact = RewritePath(node, "artifactPath", workerId);
            if (preview is null && artifact is null)
                return (null, null, null);
            return (node.ToJsonString(), preview, artifact);
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static string? RewritePath(JsonNode node, string name, string workerId)
    {
        string? value;
        try { value = node[name]?.GetValue<string>(); }
        catch { return null; }
        if (string.IsNullOrWhiteSpace(value)) return null;

        string? rewritten = null;
        if (value.StartsWith("/api/preview/", StringComparison.Ordinal))
            rewritten = "/api/nodes/" + Uri.EscapeDataString(workerId) + "/preview/" + value["/api/preview/".Length..];
        else if (value.StartsWith("/api/artifact", StringComparison.Ordinal))
            rewritten = "/api/nodes/" + Uri.EscapeDataString(workerId) + "/artifact" + value["/api/artifact".Length..];

        if (rewritten is not null)
            node[name] = rewritten;
        return rewritten;
    }

    /// <summary>Full path of an artifact under the workspace, or null when the
    /// rel path escapes the root, the extension is not a distributable
    /// (.exe/.zip), or the file does not exist.</summary>
    public static string? ResolveArtifactFile(string workspaceRoot, string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        var root = Path.GetFullPath(workspaceRoot);
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, rel)); }
        catch { return null; }
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!ArtifactExtensions.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            return null;
        return File.Exists(full) ? full : null;
    }

    /// <summary>Which registered nodes a fleet update targets: Workers that
    /// are reachable and not mid-build. Busy nodes are skipped (an update
    /// kills the running build) unless force; cloud pseudo-rows can never
    /// appear (they are not in the registry) but the id guard stays as a
    /// belt-and-suspenders.</summary>
    public static (List<NodeInfo> Targets, List<NodeInfo> SkippedBusy, List<NodeInfo> SkippedOffline)
        UpdateTargets(IEnumerable<NodeInfo> nodes, bool force)
    {
        var targets = new List<NodeInfo>();
        var busy = new List<NodeInfo>();
        var offline = new List<NodeInfo>();
        foreach (var node in nodes)
        {
            if (node.Role != NodeRole.Worker) continue;
            if (node.Id.StartsWith("cloud:", StringComparison.Ordinal)) continue;
            if (node.Status == NodeStatus.Offline) { offline.Add(node); continue; }
            if (!force && (node.ActiveTasks > 0 || node.SelfReportedActive > 0)) { busy.Add(node); continue; }
            targets.Add(node);
        }
        return (targets, busy, offline);
    }
}
