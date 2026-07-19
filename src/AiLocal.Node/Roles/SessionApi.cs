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
public sealed record AnswerInfoRequest(string? Answer = null);

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
            PendingInfoRegistry info, FallbackChatProvider provider, NodeSettings settings, CancellationToken ct) =>
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
                    var isNew = string.IsNullOrEmpty(proposal.OldContent);
                    await ctx.Response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(new { step = new AgentStep("awaiting_approval", JsonSerializer.Serialize(new {
                            path = proposal.Path,
                            diff,
                            isNew,
                            oldContent = proposal.OldContent ?? "",
                            newContent = proposal.NewContent
                        })) })}\n\n",
                        linked.Token);
                    await ctx.Response.Body.FlushAsync(linked.Token);
                    var decision = await pending.RequestAsync(id, new PendingChange(id, proposal.Path, proposal.OldContent, proposal.NewContent), ct2);
                    return new FileChangeDecision(decision.Approve, decision.Reason);
                }

                // ask_user: the agent pauses and asks the operator real
                // questions mid-run. We emit an "awaiting_info" SSE step so the
                // dashboard can show the questions and an answer box, then block
                // on PendingInfoRegistry until the operator replies (or the run
                // is cancelled). The answer is fed back to the agent as the
                // tool result.
                async Task<string> AskUser(string requestJson, CancellationToken ct2)
                {
                    await ctx.Response.WriteAsync(
                        $"data: {JsonSerializer.Serialize(new { step = new AgentStep("awaiting_info", requestJson) })}\n\n",
                        linked.Token);
                    await ctx.Response.Body.FlushAsync(linked.Token);
                    var parsed = JsonDocument.Parse(requestJson);
                    var questions = new List<InfoQuestion>();
                    if (parsed.RootElement.TryGetProperty("questions", out var q) && q.ValueKind == JsonValueKind.Array)
                        foreach (var item in q.EnumerateArray())
                            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString()))
                                questions.Add(new InfoQuestion(item.GetString()!));
                    var blocking = parsed.RootElement.TryGetProperty("blocking", out var b) && b.ValueKind == JsonValueKind.True;
                    var answer = await info.RequestAsync(id, new PendingInfoRequest(id, questions) { Blocking = blocking }, ct2);
                    return answer;
                }

                var executor = new AgentToolExecutor(accessLevel, session.FolderPath, Gate, settings.Worker.AllowInternet,
                    gameScaffolder: (engine, prompt, root, scafCt) =>
                    {
                        var r = new GameScaffoldService().Scaffold(engine, prompt, root);
                        return Task.FromResult((r.Success, r.Output));
                    },
                    appScaffolder: (tech, prompt, root, appCt) =>
                        {
                            var r = new AppScaffoldService().Scaffold(tech, prompt, root);
                            return Task.FromResult((r.Success, r.Output));
                        },
                        taskDelegator: (subPrompt, subSystem, delCt) =>
                        {
                            var delegator = new TaskDelegator(provider.CompleteAsync, accessLevel,
                                session.FolderPath, settings.Worker.AllowInternet,
                                approvalGate: null, commandGuard: new CommandGuard(settings.Worker.CommandGuard, settings.Worker.BlockedCommands),
                                provisioner: (tool, dest, provCt) => new ToolProvisioner().ProvisionAsync(tool, dest, provCt).ContinueWith(t => (t.Result.Success, t.Result.Output), TaskContinuationOptions.OnlyOnRanToCompletion),
                                gameScaffolder: (engine, p, root, scafCt) => { var r = new GameScaffoldService().Scaffold(engine, p, root); return Task.FromResult((r.Success, r.Output)); },
                                appScaffolder: (tech, p, root, appCt) => { var r = new AppScaffoldService().Scaffold(tech, p, root); return Task.FromResult((r.Success, r.Output)); },
                                askUser: null);
                            return delegator.DelegateAsync(subPrompt, subSystem, delCt);
                        },
                        askUser: AskUser);

                // Build a short plan first (GoalPlanner) so the operator sees
                // what the agent intends before it starts writing files. The
                // plan is shown as a "plan" SSE step; the agent then executes
                // it. If planning fails (no model / unparseable), we silently
                // skip it and let the agent work directly.
                try
                {
                    var planner = new GoalPlanner(provider.CompleteAsync);
                    var plan = await planner.PlanAsync(req.Message, maxParts: 8, ct: linked.Token);
                    if (plan is { Count: > 0 })
                    {
                        var planLines = plan.Select((s, i) => $"{i + 1}. {s.Title} - {s.Description}").ToList();
                        await ctx.Response.WriteAsync(
                            $"data: {JsonSerializer.Serialize(new { step = new AgentStep("plan", string.Join("\n", planLines)) })}\n\n",
                            linked.Token);
                        await ctx.Response.Body.FlushAsync(linked.Token);
                    }
                }
                catch { /* planning is best-effort; never block the build on it */ }

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
                    // Stamp a per-message timestamp on any turn the agent
                    // didn't already tag, so the transcript can show how long
                    // ago each message landed (and how long the session has
                    // run). AgentLoop's messages are records, so project with
                    // `with` rather than mutating shared history.
                    var now = DateTimeOffset.UtcNow;
                    var stamped = result.Messages
                        .Select(m => m.CreatedAt is null ? m with { CreatedAt = now } : m)
                        .ToList();
                    store.Update(id, s =>
                    {
                        s.Messages = stamped;
                        s.LastActiveAt = now;
                        s.TotalUsage = new TokenUsage(
                            s.TotalUsage.InputTokens + result.TotalUsage.InputTokens,
                            s.TotalUsage.OutputTokens + result.TotalUsage.OutputTokens);
                    });
    // GET a pending info request (the agent asked the operator a question).
    app.MapGet("/api/sessions/{id}/pending-info", (string id, PendingInfoRegistry info) =>
        info.Peek(id) is { } req
            ? Results.Ok(new { questions = req.Questions.Select(q => q.Text), blocking = req.Blocking })
            : Results.NotFound());

    // POST the operator's answer to a pending info request, unblocking the run.
    app.MapPost("/api/sessions/{id}/answer-info", (string id, AnswerInfoRequest req, PendingInfoRegistry info) =>
        info.Resolve(id, req.Answer ?? "")
            ? Results.Ok(new { resolved = true })
            : Results.NotFound());

                }

                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { final = result })}\n\n", linked.Token);
                await ctx.Response.Body.FlushAsync(linked.Token);
                return Results.Empty;
            }
            finally
            {
                runs.End(id);
                pending.RejectAllForSession(id); // clear any stale pending change
                info.RejectAllForSession(id);    // clear any stale pending info request
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
        sb.Append($"You are an autonomous coding agent working inside the folder \"{folderPath}\".");
        sb.Append(level == AgentAccessLevel.Full
            ? " You have file and shell/command access on this computer; commands run in this folder by default."
            : " You can read, write, and list files within this folder only - you cannot run shell commands at this access level.");

        sb.Append("\n\nYOUR JOB: You run a small GAME/APP STUDIO. When the user asks you to build something (a game, an app, a tool, a fix), you PRODUCE it - scaffold the real project, write the files, and make it actually runnable, like a studio shipping a real product. Do NOT just describe how it could be done or paste a text outline instead of the artifact. You think like a lead developer: break the work into a plan, then execute it step by step, verifying as you go.\n\nWORKFLOW:\n1. PLAN: For a non-trivial build, first lay out a short plan (what you'll create, the tech you'll use, the milestones). The system shows this to the user before you start writing.\n2. BUILD: scaffold the project with scaffold_game / scaffold_app, then extend it with edit_file until it genuinely works (real gameplay/features, not a stub). Prefer a runnable result over a description. Always pick the technology you judge best fits the project.\n3. ASK ONLY WHEN STUCK: If something is genuinely impossible to guess (contradictory or missing requirements that change the build), use ask_user with 1-3 concrete questions and PAUSE for the answer. Do NOT ask for permission, and do NOT ask about things you can reasonably assume - make a sensible default and keep building. Most prompts need zero questions.\n4. VERIFY: At Full access, run verify (and build/run) to confirm it actually works before you declare done.");

        sb.Append("\n\nAVAILABLE TOOLS: scaffold_game (create a complete, buildable GAME project in ONE call - CHOOSE the engine: 'html5' for a zero-install 2D platformer in the browser, 'unity'/'godot' for a heavier engine, or omit engine to let the tool pick the best fit), scaffold_app (create a complete, runnable APP in ONE call - CHOOSE the tech: 'python' or 'csharp', or omit to let the tool pick), write_file/create_file, edit_file, read_file, list_files, glob, search, and (when wired) ask_user, verify, run_command, fetch_url, recall, remember. For a game, call scaffold_game FIRST to produce the real project, then extend it. For an app/script/tool, call scaffold_app FIRST. Make every build feel production-quality: handle the obvious edge cases, add a little polish (menus, feedback, game-over/win states for games).");

        if (!string.IsNullOrWhiteSpace(projectInstructions))
        {
            sb.Append("\n\nPROJECT INSTRUCTIONS (from AILOCAL.md in this folder - follow these priorities and context):\n");
            sb.Append(projectInstructions);
        }

        sb.Append("\n\nReply in the same language the user writes in (e.g. Swedish if they write Swedish). Keep the user informed of what you are building, step by step.");

        return sb.ToString();
    }
}
