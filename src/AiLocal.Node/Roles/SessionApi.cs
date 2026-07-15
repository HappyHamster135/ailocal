using System.Text;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public sealed record SessionCreateRequest(string FolderPath, string? Title = null);
public sealed record SessionUpdateRequest(string? Title = null, bool? Pinned = null);
public sealed record SessionMessageRequest(string Message, string? ModelHint = null);
public sealed record ChangeDecisionRequest(bool Approve, string? Reason = null);

/// <summary>
/// A session is local-only by design (see SessionStore) - every endpoint
/// here acts on THIS node's own SessionStore/AgentLoop, never proxies
/// cross-machine the way the Host-mediated Assignment/GoalPlanner flow does.
/// Lives under /api/sessions, not /execute/* - ClusterSecurity excludes
/// /execute/* from the loopback trust bypass (node-to-node only), and a
/// local browser tab must be able to run its own session without a cluster
/// token, the same as every other /api/* endpoint a dashboard calls directly.
/// </summary>
public static class SessionApi
{
    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/api/sessions", (SessionStore store) =>
            Results.Ok(store.All().Select(SessionSummary)));

        // A literal segment always wins over "{id}" at the same position in
        // ASP.NET Core's routing regardless of registration order, so this
        // never gets swallowed by GET /api/sessions/{id} below - but it's
        // listed first for readability anyway. Polled by the dashboard's
        // status bar (see renderStatusBar) for the real cross-tab/scheduled
        // "active agents" count - SessionRunRegistry is in-memory-only and
        // otherwise invisible to the client.
        app.MapGet("/api/sessions/active-count", (SessionRunRegistry runs) =>
            Results.Ok(new { count = runs.ActiveCount }));

        app.MapPost("/api/sessions", (SessionCreateRequest req, SessionStore store) =>
        {
            if (string.IsNullOrWhiteSpace(req.FolderPath))
                return Results.Problem(detail: "folderPath is required", statusCode: StatusCodes.Status400BadRequest);

            string fullPath;
            try { fullPath = Path.GetFullPath(req.FolderPath); }
            catch (Exception ex) { return Results.Problem(detail: $"invalid folder path: {ex.Message}", statusCode: StatusCodes.Status400BadRequest); }

            if (!Path.IsPathRooted(req.FolderPath))
                return Results.Problem(detail: "folderPath must be absolute", statusCode: StatusCodes.Status400BadRequest);

            // No auto-mkdir - a fat-fingered path silently creating a new
            // directory tree is worse than a clear error, especially since
            // Full access has no confinement at all once a session exists.
            // Matches "cd into an existing project" like Claude Code/Codex.
            if (!Directory.Exists(fullPath))
                return Results.Problem(detail: $"folder does not exist: {fullPath}", statusCode: StatusCodes.Status400BadRequest);

            var session = store.Create(fullPath, req.Title);
            return Results.Ok(session);
        });

        app.MapGet("/api/sessions/{id}", (string id, SessionStore store) =>
            store.Get(id) is { } session ? Results.Ok(session) : Results.NotFound());

        app.MapPut("/api/sessions/{id}", (string id, SessionUpdateRequest req, SessionStore store) =>
        {
            var updated = store.Update(id, s =>
            {
                if (req.Title is { Length: > 0 }) s.Title = req.Title.Trim();
                if (req.Pinned is not null) s.Pinned = req.Pinned.Value;
            });
            return updated ? Results.Ok(store.Get(id)) : Results.NotFound();
        });

        // Admin-tier only (see ClusterSecurity.RequiresAdminTier) - matches
        // /api/nodes and /api/schedules DELETE. Never touches the folder or
        // its files, only forgets the record (see SessionStore.Remove).
        app.MapDelete("/api/sessions/{id}", (string id, SessionStore store) =>
            store.Remove(id) ? Results.NoContent() : Results.NotFound());

        app.MapPost("/api/sessions/{id}/cancel", (string id, SessionRunRegistry runs, PendingChangeRegistry pending) =>
        {
            runs.Cancel(id);
            pending.RejectAllForSession(id); // unblock a paused write so it can't hang
            return Results.Ok(new { cancelled = true });
        });

        app.MapGet("/api/sessions/{id}/pending-change", (string id, PendingChangeRegistry pending) =>
            pending.Peek(id) is { } change
                ? Results.Ok(new { path = change.Path, diff = change.Diff, oldContent = change.OldContent, newContent = change.NewContent })
                : Results.NotFound());

        app.MapPost("/api/sessions/{id}/approve-change", async (string id, ChangeDecisionRequest req, PendingChangeRegistry pending) =>
            pending.Resolve(id, new ChangeDecision(req.Approve, req.Reason))
                ? Results.Ok(new { resolved = true })
                : Results.NotFound());

        app.MapPost("/api/sessions/{id}/run", async (
            string id, SessionMessageRequest req, HttpContext ctx,
            SessionStore store, SessionRunRegistry runs, PendingChangeRegistry pending,
            FallbackChatProvider provider, NodeSettings settings, CancellationToken ct) =>
        {
            var session = store.Get(id);
            if (session is null)
                return Results.NotFound();

            var accessLevel = settings.Worker.AgentAccess;
            if (accessLevel == AgentAccessLevel.Off)
                return Results.Problem(
                    detail: "Agent mode is not enabled on this node (Installningar -> Agentlage).",
                    statusCode: StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(req.Message))
                return Results.Problem(detail: "message text is required", statusCode: StatusCodes.Status400BadRequest);

            if (!runs.TryBegin(id, out var runCts))
                return Results.Problem(
                    detail: "This session already has a run in progress.",
                    statusCode: StatusCodes.Status409Conflict);

            try
            {
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, runCts.Token);

                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.ContentType = "text/event-stream";

                var instructions = await ProjectInstructionsReader.TryReadAsync(session.FolderPath, linked.Token);
                var system = BuildSystemPrompt(session.FolderPath, accessLevel, instructions);

                // Each file the agent wants to write is surfaced to the
                // operator for review before it lands on disk: emit an
                // "awaiting_approval" step (with the diff) and block on the
                // registry until the dashboard approves/rejects. No gate on a
                // Worker's autonomous assignment - those write immediately.
                async Task<FileChangeDecision> Gate(FileChangeProposal proposal, CancellationToken ct2)
                {
                    var diff = LineDiff.Compute(proposal.OldContent ?? "", proposal.NewContent);
                    await ctx.Response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(new { step = new AgentStep("awaiting_approval", JsonSerializer.Serialize(new { path = proposal.Path, diff })) })}\n\n",
                        linked.Token);
                    await ctx.Response.Body.FlushAsync(linked.Token);
                    var decision = await pending.RequestAsync(id, new PendingChange(id, proposal.Path, proposal.OldContent, proposal.NewContent), ct2);
                    return new FileChangeDecision(decision.Approve, decision.Reason);
                }

                var executor = new AgentToolExecutor(accessLevel, session.FolderPath, Gate);
                var loop = new AgentLoop(provider.CompleteAsync, executor);

                var result = await loop.RunAsync(req.Message, accessLevel, req.ModelHint, onStep: async step =>
                {
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { step })}\n\n", linked.Token);
                    await ctx.Response.Body.FlushAsync(linked.Token);
                }, ct: linked.Token, history: session.Messages, system: system);

                // Only persist on success - AgentLoop's Messages doc comment
                // explains why: a failed/cancelled run's dangling new user
                // turn (and any partial tool-call turns) is discarded rather
                // than persisted, so the session's stored history stays
                // exactly as it was and the operator can just retry cleanly
                // instead of the conversation accumulating broken turns.
                if (result.Success)
                {
                    store.Update(id, s =>
                    {
                        s.Messages = result.Messages.ToList();
                        s.LastActiveAt = DateTimeOffset.UtcNow;
                        s.TotalUsage = new TokenUsage(
                            s.TotalUsage.InputTokens + result.TotalUsage.InputTokens,
                            s.TotalUsage.OutputTokens + result.TotalUsage.OutputTokens);
                    });
                }

                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { final = result })}\n\n", linked.Token);
                await ctx.Response.Body.FlushAsync(linked.Token);
                return Results.Empty;
            }
            finally
            {
                runs.End(id);
                pending.RejectAllForSession(id); // clear any stale pending change
            }
        });
    }

    private static object SessionSummary(Session s) => new
    {
        s.Id,
        s.Title,
        s.FolderPath,
        s.Pinned,
        s.CreatedAt,
        s.LastActiveAt,
        MessageCount = s.Messages.Count,
        s.TotalUsage
    };

    private static string BuildSystemPrompt(string folderPath, AgentAccessLevel level, string? projectInstructions)
    {
        var sb = new StringBuilder();
        sb.Append($"You are an autonomous agent working in the folder {folderPath}.");
        sb.Append(level == AgentAccessLevel.Full
            ? " You have file and command access on this computer; commands default to running in this folder."
            : " You can read, write, and list files within this folder only - you cannot run shell commands at this access level.");

        if (!string.IsNullOrWhiteSpace(projectInstructions))
        {
            sb.Append("\n\nProject instructions (from AILOCAL.md in this folder):\n");
            sb.Append(projectInstructions);
        }

        return sb.ToString();
    }
}
