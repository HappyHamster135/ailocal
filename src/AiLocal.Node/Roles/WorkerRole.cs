using System.Net.Http.Json;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

/// <summary>An "assignment" for agent mode - a goal the Worker works and
/// debugs on its own (read/write files, run commands per its configured
/// access level) rather than a single chat completion. WorkerId is Host-only
/// (see HostRole's /api/assignment) - it pins dispatch to one specific Worker
/// instead of auto-picking the least-busy one, for a sequence of subtasks
/// from the same plan that need to land on the same machine to share a
/// workspace. The Worker itself ignores it; a Worker only ever executes on
/// itself.</summary>
public sealed record AssignmentRequest(string Assignment, string? ModelHint = null, string? WorkerId = null, string? WorkspaceOverride = null, bool UseIsolation = false);

/// <summary>Response from POST /execute/isolation/create: the freshly created
/// worktree+branch for one isolated task.</summary>
public sealed record IsolationCreated(string TaskId, string Branch, string Worktree);

/// <summary>Body for POST /api/isolation/review (Host): review a completed
/// isolated task's diff with the Host's AI reviewer.</summary>
public sealed record IsolationReviewRequest(string WorkerEndpoint, string TaskId, string? Goal = null);

/// <summary>Response shape of Worker's POST /execute/isolation/diff.</summary>
public sealed record IsolationDiffResponse(string TaskId, string Diff);

public static class WorkerRole
{
    // The provider fallback chain is registered in NodeComposition (shared).
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddHostedService<LocalRuntimeBootstrapper>();
        services.AddHostedService<AutoMergeHostedService>();
        services.AddSingleton<WorkspaceService>();
    }

    public static void MapEndpoints(WebApplication app)
    {
        // A Worker was originally headless-only (no reason for a human to
        // look at it), so unlike Host/Launcher/Overseer it never served the
        // dashboard at all - http://127.0.0.1:{port}/ was always a 404. Now
        // that click-to-pair needs a human to actually see and accept/reject
        // a pending request, that gap is a hard blocker. Dashboard.Html
        // already adapts to whatever role /api/local reports.
        app.MapGet("/", () => Results.Content(Dashboard.Html, "text/html"));

        // Host calls this to run a delegated unit of work.
        app.MapPost("/execute", async (ChatRequest req, FallbackChatProvider provider, CancellationToken ct) =>
        {
            var result = await provider.CompleteAsync(req, ct);
            return result.IsSuccess
                ? Results.Ok(result.Response)
                : Results.Problem(
                    detail: result.Error ?? "provider failure",
                    statusCode: result.Outcome == ProviderOutcome.FatalError ? StatusCodes.Status400BadRequest : StatusCodes.Status503ServiceUnavailable);
        });

        // SSE variant used by the Host for single-worker chat dispatches so the
        // operator sees text arrive incrementally instead of "submit and wait".
        app.MapPost("/execute/stream", async (ChatRequest req, HttpContext ctx, FallbackChatProvider provider, CancellationToken ct) =>
        {
            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";

            await foreach (var chunk in provider.StreamAsync(req, ct))
            {
                object frame = chunk.Delta is not null
                    ? new { delta = chunk.Delta }
                    : new
                    {
                        final = new
                        {
                            success = chunk.Final?.IsSuccess ?? false,
                            error = chunk.Final?.Error,
                            response = chunk.Final?.Response
                        }
                    };
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(frame)}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }
        });

        // "Assignment" execution: agent mode, not a single chat completion -
        // the Worker reads/writes files and (at Full access) runs commands,
        // iterating until it decides the assignment is done. Gated on this
        // Worker's OWN AgentAccess setting, which only this Worker's operator
        // can raise (see PersistentSettingsStore.Update) - a Host has no path
        // to turn this on remotely, it can only ever be told no.
        app.MapPost("/execute/assignment", async (
            AssignmentRequest req, HttpContext ctx, FallbackChatProvider provider,
            NodeSettings settings, IHttpClientFactory httpFactory, HostLocator hostLocator,
            CancellationToken ct) =>
        {
            var accessLevel = settings.Worker.AgentAccess;
            if (accessLevel == AgentAccessLevel.Off)
                return Results.Problem(
                    detail: "Agent mode is not enabled on this Worker (Installningar -> Agentlage).",
                    statusCode: StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(req.Assignment))
                return Results.Problem(detail: "assignment text is required", statusCode: StatusCodes.Status400BadRequest);

            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";
            // The operator chooses the folder (Settings -> Agentlage) - a Worker
            // picks where ITS agent runs, never the Host. Null => the
            // default agent-workspace under this Worker's own data dir. A
            // non-null WorkspaceOverride (e.g. a git worktree created for task
            // isolation) takes precedence so the agent works in that isolated
            // directory instead of the shared workspace.
            var workspaceRoot = !string.IsNullOrWhiteSpace(req.WorkspaceOverride)
                ? req.WorkspaceOverride
                : string.IsNullOrWhiteSpace(settings.Worker.WorkspacePath)
                    ? Path.Combine(SettingsPaths.DataDirectory, "agent-workspace")
                    : settings.Worker.WorkspacePath;

            // AI review (opt-in): every file write pauses and asks the Host's
            // strong model first; a rejection comes back as a tool error with
            // the reviewer's reason so the (often small, local) model here
            // can correct itself and retry. Quality gate, not security -
            // review failures fail OPEN (see ChangeReviewer's doc).
            Func<FileChangeProposal, CancellationToken, Task<FileChangeDecision>>? gate = null;
            var reviewHost = hostLocator.HostEndpoint;
            if (settings.Worker.AiReviewWrites && !string.IsNullOrWhiteSpace(reviewHost))
            {
                var hostEndpoint = reviewHost;
                gate = async (proposal, gateCt) =>
                {
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(gateCt);
                        timeout.CancelAfter(TimeSpan.FromSeconds(60));
                        var client = httpFactory.CreateClient("cluster");
                        using var response = await client.PostAsJsonAsync(
                            $"{hostEndpoint}/cluster/review-change",
                            new ReviewChangeRequest(proposal.Path, proposal.OldContent, proposal.NewContent, req.Assignment),
                            timeout.Token);
                        if (!response.IsSuccessStatusCode)
                            return new FileChangeDecision(true);
                        var verdict = await response.Content.ReadFromJsonAsync<ReviewChangeResponse>(timeout.Token);
                        return verdict is { Approve: false }
                            ? new FileChangeDecision(false, $"AI-granskaren avvisade ändringen: {verdict.Reason}")
                            : new FileChangeDecision(true);
                    }
                    catch (OperationCanceledException) when (gateCt.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        return new FileChangeDecision(true);
                    }
                };
            }

            var executor = new AgentToolExecutor(accessLevel, workspaceRoot, gate, settings.Worker.AllowInternet,
                new CommandGuard(settings.Worker.CommandGuard, settings.Worker.BlockedCommands),
                settings.Worker.ProjectMemoryEnabled ? new CodebaseIndex() : null,
                settings.Worker.ProjectMemoryEnabled ? new ProjectMemory(workspaceRoot) : null,
                async (tool, dest, provCt) =>
                {
                    var r = await new ToolProvisioner().ProvisionAsync(tool, dest, provCt);
                    return (r.Success, r.Output);
                },
                (engine, prompt, root, scafCt) =>
                {
                    var r = new GameScaffoldService().Scaffold(engine, prompt, root);
                    return Task.FromResult((r.Success, r.Output));
                },
                (tech, prompt, root, appCt) =>
                {
                    var r = new AppScaffoldService().Scaffold(tech, prompt, root);
                    return Task.FromResult((r.Success, r.Output));
                });
            var loop = new AgentLoop(provider.CompleteAsync, executor);

            var result = await loop.RunAsync(req.Assignment, accessLevel, req.ModelHint, onStep: async step =>
            {
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { step })}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            }, ct);

            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { final = result })}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            return Results.Empty;
        });

        // --- Task isolation (git worktree per task) ---------------------------------
        // Each "employee" (agent run) gets its own worktree+branch so two
        // tasks on the same repo never overwrite each other. The Host creates
        // one before dispatching (with useIsolation), then merges or discards
        // it after the agent reports done - discard is the free undo button.

        app.MapPost("/execute/isolation/create", async (
            GitIsolationService isolation, NodeSettings settings, CancellationToken ct) =>
        {
            var repoPath = ResolveRepoPath(settings);
            if (repoPath is null)
                return Results.Problem(detail: "Git-isolation kräver att Agentlage -> Arbetsmapp är ett git-repo.", statusCode: StatusCodes.Status400BadRequest);
            if (!await isolation.CanIsolateAsync(repoPath, ct))
                return Results.Problem(detail: "Arbetsmappen är inte ett git-repo - kan inte isolera.", statusCode: StatusCodes.Status400BadRequest);

            var task = await isolation.CreateAsync(repoPath, "Uppgift", baseBranch: null, ct);
            return task is null
                ? Results.Problem(detail: "Kunde inte skapa isolerad arbetsyta (git worktree misslyckades).", statusCode: StatusCodes.Status500InternalServerError)
                : Results.Ok(new { taskId = task.TaskId, branch = task.BranchName, worktree = task.WorktreePath });
        });

        app.MapGet("/execute/isolation/list", (GitIsolationService isolation) =>
            Results.Ok(isolation.ListActive().Select(t => new
            {
                taskId = t.TaskId,
                branch = t.BranchName,
                baseBranch = t.BaseBranch,
                worktree = t.WorktreePath,
                title = t.Title,
                createdAt = t.CreatedAt
            })));

        app.MapPost("/execute/isolation/commit", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationCommitRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var result = await isolation.CommitAsync(body.TaskId, body.Message ?? "Agent-ändringar", ct);
            return Results.Ok(new { success = result.Success, output = result.Output });
        });

        app.MapPost("/execute/isolation/diff", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var diff = await isolation.DiffAsync(body.TaskId, ct);
            return Results.Ok(new { taskId = body.TaskId, diff });
        });

        app.MapPost("/execute/isolation/merge", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var (success, output) = await isolation.MergeAsync(body.TaskId, ct);
            return Results.Ok(new { success, output });
        });

        app.MapPost("/execute/isolation/discard", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            await isolation.DiscardAsync(body.TaskId, ct);
            return Results.Ok(new { discarded = true });
        });

        // Local dashboard mirror of the /execute/isolation/* endpoints so the
        // in-app Studio "Branches" tab works on a single node without a cluster
        // token. Sits under /api/ (not /execute/) so it is not node-only and
        // therefore reachable with ordinary local dashboard auth.
        MapIsolationEndpoints(app);

        app.MapGet("/runtime", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.InspectAsync(ct)));

        app.MapPost("/runtime/pull", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.PullRecommendedModelAsync(ct)));

        // One click: install Ollama (if missing), start it, and pull the model.
        app.MapPost("/runtime/setup", async (LocalRuntimeManager runtime, CancellationToken ct) =>
            Results.Ok(await runtime.SetupLocalAiAsync(ct)));

        // P2: Studio one-click Build / Run / Test against the workspace.
        MapWorkspaceEndpoints(app);

        // Click-to-pair, no typing: a Host that discovered this Worker via LAN
        // beacon sends a connect request with a random nonce. This node's own
        // operator must explicitly accept it (see /pairing/pending/{id}/accept)
        // before anything is trusted - see PairingCoordinator for the full flow.
        app.MapPost("/pairing/request", (PairingHandshakePayload req, PairingCoordinator pairing) =>
        {
            pairing.AddInbound(req.PeerId, req.PeerName, req.PeerEndpoint, req.Nonce);
            return Results.Ok(new { received = true });
        });

        app.MapGet("/pairing/pending", (PairingCoordinator pairing) =>
            Results.Ok(pairing.PendingInbound()));

        app.MapPost("/pairing/pending/{hostId}/reject", (string hostId, PairingCoordinator pairing) =>
        {
            pairing.RejectInbound(hostId);
            return Results.Ok(new { rejected = true });
        });

        app.MapPost("/pairing/pending/{hostId}/accept", async (
            string hostId, PairingCoordinator pairing, PersistentSettingsStore store, HostLocator hostLocator,
            NodeSettings settings, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            // Peek, don't consume: only remove the pending request once the
            // callback below actually succeeds, so a transient network/
            // firewall failure leaves it available to retry instead of
            // silently discarding an accept the operator already clicked.
            var request = pairing.GetInbound(hostId);
            if (request is null)
                return Results.NotFound(new { error = "no pending request from that host - it may have expired" });

            try
            {
                var selfEndpoint = $"http://{NetworkUtil.LocalIPv4()}:{settings.Port}";
                var payload = new PairingHandshakePayload(store.NodeId, settings.NodeName, selfEndpoint, request.Nonce);
                var client = httpFactory.CreateClient("cluster");
                using var response = await client.PostAsJsonAsync($"{request.RequesterEndpoint}/pairing/approved", payload, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    var reason = ExtractErrorReason(body) ?? $"HTTP {(int)response.StatusCode}";
                    return Results.Problem(
                        detail: $"Host {request.RequesterEndpoint} svarade: {reason}",
                        statusCode: StatusCodes.Status502BadGateway);
                }

                var approval = await response.Content.ReadFromJsonAsync<PairingApprovalResponse>(ct);
                if (approval?.ClusterToken is not { Length: > 0 } token)
                    return Results.Problem(
                        detail: "Host skickade ingen klusternyckel.", statusCode: StatusCodes.Status502BadGateway);

                pairing.RemoveInboundIfMatches(hostId, request.Nonce);
                store.Update(new SettingsUpdate(HostEndpoint: request.RequesterEndpoint, ClusterToken: token), hostLocator);
                return Results.Ok(new { connected = true, host = request.RequesterName });
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: StatusCodes.Status502BadGateway);
            }
        });
    }

    /// <summary>Isolation + auto-merge endpoints, factored out so the Launcher
    /// role (which runs a co-located Worker) can expose them too. Sits
    /// under /api/ so it is not node-only.</summary>
    public static void MapIsolationEndpoints(WebApplication app)
    {
        app.MapGet("/api/isolation/list", (GitIsolationService isolation) =>
            Results.Ok(isolation.ListActive().Select(t => new
            {
                taskId = t.TaskId,
                branch = t.BranchName,
                baseBranch = t.BaseBranch,
                worktree = t.WorktreePath,
                title = t.Title,
                createdAt = t.CreatedAt
            })));

        app.MapPost("/api/isolation/merge", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var (success, output) = await isolation.MergeAsync(body.TaskId, ct);
            return Results.Ok(new { success, output });
        });

        app.MapPost("/api/isolation/diff", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var diff = await isolation.DiffAsync(body.TaskId, ct);
            return Results.Ok(new { taskId = body.TaskId, diff });
        });

        app.MapPost("/api/isolation/discard", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            await isolation.DiscardAsync(body.TaskId, ct);
            return Results.Ok(new { discarded = true });
        });

        // CI gate: build (and test) the worktree to verify it's mergeable.
        app.MapPost("/api/isolation/ci", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var (success, output) = await isolation.RunCiGateAsync(body.TaskId, ct);
            return Results.Ok(new { success, output });
        });

        // CI gate + conditional merge: only merges when the build passes.
        app.MapPost("/api/isolation/merge-if-passing", async (
            GitIsolationService isolation, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var (ciPassed, ciOutput) = await isolation.RunCiGateAsync(body.TaskId, ct);
            if (!ciPassed)
                return Results.Ok(new { success = false, output = $"CI gate failed: {ciOutput}" });
            var (mergeSuccess, mergeOutput) = await isolation.MergeAsync(body.TaskId, ct);
            return Results.Ok(new { success = mergeSuccess, output = mergeOutput });
        });

        // Manual trigger for the A3 auto-merge loop.
        app.MapPost("/api/isolation/auto-merge-all", (IServiceProvider services) =>
        {
            AutoMergeHostedService.RunAutoMerge(services);
            return Results.Ok(new { ran = true });
        });

        // P6: actually run the task's app and report startup output, so an
        // operator can confirm it boots before merging. Runs in the worktree.
        app.MapPost("/api/isolation/verify", async (
            GitIsolationService isolation, WorkspaceService ws, HttpContext ctx, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<IsolationTaskRequest>(ct);
            if (body is null || string.IsNullOrWhiteSpace(body.TaskId))
                return Results.Problem(detail: "taskId krävs", statusCode: StatusCodes.Status400BadRequest);
            var task = isolation.Get(body.TaskId);
            if (task is null)
                return Results.Problem(detail: "unknown isolated task", statusCode: StatusCodes.Status400BadRequest);
            var (success, output) = await ws.RunAsync(task.WorktreePath, "verify", ct);
            return Results.Ok(new { success, output });
        });
    }

    /// <summary>Studio one-click Build / Run / Test against the workspace,
    /// factored out so the Launcher role can expose it too.</summary>
    public static void MapWorkspaceEndpoints(WebApplication app)
    {
        app.MapPost("/api/workspace/{kind}", async (
            WorkspaceService ws, HttpContext ctx, string kind, CancellationToken ct) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<WorkspaceRootRequest>(ct);
            var root = body?.Root?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(root))
                return Results.Problem(detail: "root (arbetsmapp) krävs", statusCode: StatusCodes.Status400BadRequest);
            var allowed = new[] { "build", "run", "test", "game" };
            if (!allowed.Contains(kind))
                return Results.Problem(detail: "okänt kommando: " + kind, statusCode: StatusCodes.Status400BadRequest);
            var (success, output) = await ws.RunAsync(root, kind, ct);
            return Results.Ok(new { success, output });
        });
    }

    /// <summary>Pulls a human-readable reason out of a failed response body -
    /// either a ProblemDetails "detail" field or a plain {"error": "..."}
    /// shape (both are used across this app's endpoints) - so a pairing
    /// failure shows the Host's actual reason instead of just a status code.</summary>
    private static string? ExtractErrorReason(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
            return null;

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == JsonValueKind.String)
                return detail.GetString();
            if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
        }
        catch { /* not JSON, or an unexpected shape - fall back to the status code */ }

        return null;
    }

    /// <summary>Request bodies for the isolation endpoints.</summary>
    private sealed record IsolationTaskRequest(string TaskId);
    private sealed record IsolationCommitRequest(string TaskId, string? Message = null);
    /// <summary>Request body for the Studio workspace build/run/test endpoints.</summary>
    private sealed record WorkspaceRootRequest(string? Root = null);

    /// <summary>Resolves the repo path the agent works in: the explicit
    /// WorkspacePath if set, else the default agent-workspace. Isolation only
    /// makes sense when that path is itself a git repo.</summary>
    private static string? ResolveRepoPath(NodeSettings settings)
    {
        var path = string.IsNullOrWhiteSpace(settings.Worker.WorkspacePath)
            ? Path.Combine(SettingsPaths.DataDirectory, "agent-workspace")
            : settings.Worker.WorkspacePath;
        return path;
    }
}
