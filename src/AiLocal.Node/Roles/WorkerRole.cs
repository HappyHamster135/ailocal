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
public sealed record AssignmentRequest(string Assignment, string? ModelHint = null, string? WorkerId = null, string? WorkspaceOverride = null, bool UseIsolation = false, int? TeamSize = null, string? ProjectRel = null, decimal? MaxCostUsd = null, bool ProducerMode = false);

/// <summary>One project in a node's portfolio (B6: shared by the local
/// /api/projects view and the cluster-reachable /execute/projects that the
/// Host aggregates into a cluster-wide gallery).</summary>
public sealed record ProjectSummary(
    string Rel, string Name, string Kind, string Engine, int Files,
    DateTimeOffset LastModified, bool Playable, int Snapshots, bool? LatestClean, string? LatestLabel);

/// <summary>Body for POST /api/benchmark/run: how many of the standard
/// prompts to run (default 3, clamped to the catalog size).</summary>
public sealed record BenchmarkRunRequest(int? Count = null);

/// <summary>Bodies for the project-portfolio endpoints: Rel is the project's
/// path relative to the workspace ("." = the workspace root itself).</summary>
public sealed record ProjectActionRequest(string Rel);
public sealed record ProjectRestoreRequest(string Rel, string File);

/// <summary>Body for POST /api/assignment/milestone: the operator's verdict
/// on a paused build's delivery contract.</summary>
public sealed record MilestoneDecisionRequest(string Id, bool Approve, string? Note = null);

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
        // includePreview:false betyder numera bara "pausa aldrig för
        // milstolpsgodkännande" (ingen sitter och tittar på klusterkörningar).
        // Förhandsvisnings-/artefaktlänkar beräknas ALLTID - Hosten skriver om
        // dem till sin egen proxy (/api/nodes/{id}/...) i final-framen så
        // Spela/Ladda ner fungerar från vilken dashboard som helst.
        app.MapPost("/execute/assignment", (
            AssignmentRequest req, HttpContext ctx, FallbackChatProvider provider,
            NodeSettings settings, IHttpClientFactory httpFactory, HostLocator hostLocator,
            PersistentSettingsStore settingsStore, AssignmentLog assignmentLog, AssignmentQueue queue, CancellationToken ct)
            => RunAssignmentAsync(req, ctx, provider, settings, httpFactory, hostLocator, settingsStore, assignmentLog, queue, ct, includePreview: false));

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

        // Uppdrag-flödet (plan + kör) ska fungera direkt på en fristående
        // Workers egen dashboard - inte bara via en Host.
        MapLocalAssignmentEndpoints(app);

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


    /// <summary>The agent-assignment engine: builds the full tool executor
    /// (scaffolds, builder, assets, playtest, vision, ...) and streams the
    /// run as SSE. Shared by /execute/assignment (cluster dispatch) and the
    /// LOCAL /api/assignment mapped for Launcher + standalone Worker
    /// dashboards, so a single node runs builds without any Host.</summary>
    internal static async Task<IResult> RunAssignmentAsync(
        AssignmentRequest req, HttpContext ctx, FallbackChatProvider provider,
        NodeSettings settings, IHttpClientFactory httpFactory, HostLocator hostLocator,
        PersistentSettingsStore settingsStore, AssignmentLog assignmentLog,
        AssignmentQueue queue, CancellationToken ct, bool includePreview = true)
    {
            var accessLevel = settings.Worker.AgentAccess;
            if (accessLevel == AgentAccessLevel.Off)
                return Results.Problem(
                    detail: "Agent mode is not enabled on this Worker (Installningar -> Agentlage).",
                    statusCode: StatusCodes.Status403Forbidden);

            if (string.IsNullOrWhiteSpace(req.Assignment))
                return Results.Problem(detail: "assignment text is required", statusCode: StatusCodes.Status400BadRequest);

            // The local /api/assignment wrapper writes a leading worker-frame
            // before delegating here, so the response may already be started.
            if (!ctx.Response.HasStarted)
            {
                ctx.Response.Headers.CacheControl = "no-cache";
                ctx.Response.ContentType = "text/event-stream";
            }
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

            // Ärlig kostnadsredovisning: bildanrop utan usage i svaret RÄKNAS
            // och redovisas öppet bredvid dollarsiffran ("N bild-/visionsanrop
            // utanför prislistan") - hellre en ärlig fotnot än en siffra som
            // ser komplett ut utan att vara det. (v1.91: visionsanrop MED usage
            // prissätts på riktigt - se AccountVision nedan.)
            var unpricedImageCalls = 0;
            var unpricedLock = new object();

            // B5: summera token-användning per modell över HELA uppdraget så
            // kostnaden kan redovisas öppet i slutresultatet. Lokala modeller
            // (Ollama) hoppas över - gratis compute är ingen utgift. (Blocket
            // bor FÖRE vision-/executor-delegaterna: de fångar variablerna.)
            var usageByModel = new Dictionary<string, (long In, long Out)>(StringComparer.OrdinalIgnoreCase);
            var usageLock = new object();

            // B5-gräns (opt-in): en per-uppdrags-kostnadstak. Bara när den är
            // satt förhandshämtas OpenRouter-katalogen EN gång (annars påverkas
            // inte bygglatensen), så varje svar kan prissättas live och loopen
            // stoppas när den ackumulerade kostnaden når taket.
            var maxCostUsd = req.MaxCostUsd.GetValueOrDefault();
            IReadOnlyList<CatalogModel> capCatalog = [];
            if (maxCostUsd > 0m)
                try { capCatalog = await new OpenRouterCatalog(httpFactory).GetAsync(ct); }
                catch { /* prissättning faller tillbaka på Anthropic-listan */ }
            var spentUsd = 0m;

            // Uppdragets ENDA väg till modellen: ensam agent, team-spår,
            // producent-roller, regissör och fixrundor ska ALLA gå genom denna
            // delegat - anrop utanför den hamnar utanför både kostnads-
            // redovisningen och Max$-taket (granskningen v1.83 hittade att
            // team-läget och regissörsanropen gick förbi: Max$ bet inte på
            // spåren och operatören visades en kostnad som saknade merparten).
            // v1.96: löpande förbrukningsnot i stegflödet - kostnaden syntes
            // förr först när bygget var KLART (500kr-lärdomen: synlighet under
            // resans gång, inte bara taket). costEmitter kopplas in när steg-
            // sänkan finns (deklareras senare i metoden).
            var accountedCalls = 0;
            Func<AgentStep, Task>? costEmitter = null;
            Func<ChatRequest, CancellationToken, Task<ProviderResponse>> completeAccounted = async (r, c) =>
            {
                var resp = await provider.CompleteAsync(r, c);
                string? note = null;
                if (resp.IsSuccess && resp.Response is { IsLocal: false } chat && !string.IsNullOrWhiteSpace(chat.Model))
                    lock (usageLock)
                    {
                        var cur = usageByModel.TryGetValue(chat.Model, out var v) ? v : (In: 0L, Out: 0L);
                        usageByModel[chat.Model] = (cur.In + chat.Usage.InputTokens, cur.Out + chat.Usage.OutputTokens);
                        if (maxCostUsd > 0m)
                            spentUsd += AssignmentCost.Price(
                                new Dictionary<string, (long In, long Out)> { [chat.Model] = (chat.Usage.InputTokens, chat.Usage.OutputTokens) },
                                capCatalog).Total;
                        accountedCalls++;
                        if (accountedCalls % 25 == 0)
                        {
                            var inTok = usageByModel.Values.Sum(u => u.In);
                            var outTok = usageByModel.Values.Sum(u => u.Out);
                            note = $"Förbrukning hittills: {inTok / 1000}k tokens in / {outTok / 1000}k ut"
                                + (maxCostUsd > 0m ? $" (~${spentUsd:0.00} av max ${maxCostUsd:0.00})" : "")
                                + $" - {accountedCalls} modellanrop.";
                        }
                    }
                if (note is not null && costEmitter is not null)
                    await costEmitter(new AgentStep("thinking", note));
                return resp;
            };
            decimal? capLimit = maxCostUsd > 0m ? maxCostUsd : null;
            Func<decimal>? capSpent = maxCostUsd > 0m ? () => { lock (usageLock) return spentUsd; } : null;

            // v1.91: visionsanrop PRISSÄTTS när svaret bär usage (alla fyra
            // leverantörsvägarna extraherar tokens + normaliserad modellslug) -
            // in i usageByModel som allt annat, och Max$ ser dem. Bara anrop
            // UTAN usage i svaret räknas kvar som oprissatta (fotnoten).
            void AccountVision(VisionResult r)
            {
                if (!r.Success) return;
                if (r.Model is { Length: > 0 } && (r.InputTokens > 0 || r.OutputTokens > 0))
                    lock (usageLock)
                    {
                        var cur = usageByModel.TryGetValue(r.Model, out var v) ? v : (In: 0L, Out: 0L);
                        usageByModel[r.Model] = (cur.In + r.InputTokens, cur.Out + r.OutputTokens);
                        if (maxCostUsd > 0m)
                            spentUsd += AssignmentCost.Price(
                                new Dictionary<string, (long In, long Out)> { [r.Model] = (In: r.InputTokens, Out: r.OutputTokens) },
                                capCatalog).Total;
                    }
                else
                    lock (unpricedLock) unpricedImageCalls++;
            }

            // Vision-ögat för playtestens skärmdumpar - samma analysator som
            // vision_review-verktyget använder. Utan konfigurerade nycklar
            // misslyckas analysen tyst och skärmdumpen rapporteras ändå.
            Func<string, string, CancellationToken, Task<(bool Success, string Text)>> BuildVisionReview() =>
                async (imagePath, question, vct) =>
                {
                    var analyzer = new VisionAnalyzer(httpFactory, settingsStore, settings.Providers);
                    var r = await analyzer.AnalyzeAsync(imagePath, question, vct);
                    AccountVision(r);   // v1.91: prissätts när usage finns
                    return (r.Success, FormatVisionResult(r));
                };

            var gameBuilder = new GameBuilder();
            Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCmd =
                (cmd, dir, rcCt) =>
                {
                    // /c "{cmd}" (wrappad), INTE /c {cmd}: cmd.exe:s citatregel
                    // strippar första+sista citatet när kommandot har FLER än
                    // två citattecken - `"godot" --export-release "Windows
                    // Desktop" "ut.exe"` (6 citat) blev "filename syntax
                    // incorrect" INNAN godot ens startade. Upptäckt live vid
                    // v1.90:s APK-verifiering; wrap-formen är cmd:s
                    // dokumenterade sätt att bevara inre citat exakt.
                    var psi = OperatingSystem.IsWindows()
                        ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
                        : new System.Diagnostics.ProcessStartInfo("/bin/sh", $"-c \"{cmd.Replace("\"", "\\\"")}\"");
                    psi.WorkingDirectory = Directory.Exists(dir) ? dir : Environment.CurrentDirectory;
                    psi.RedirectStandardOutput = true;
                    psi.RedirectStandardError = true;
                    psi.UseShellExecute = false;
                    psi.CreateNoWindow = true;
                    using var proc = new System.Diagnostics.Process { StartInfo = psi };
                    var so = new System.Text.StringBuilder();
                    var se = new System.Text.StringBuilder();
                    proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
                    proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
                    proc.Start();
                    proc.BeginOutputReadLine();
                    proc.BeginErrorReadLine();
                    proc.WaitForExit((int)TimeSpan.FromMinutes(30).TotalMilliseconds);
                    return Task.FromResult((proc.ExitCode, $"exit code: {proc.ExitCode}\n{so}\n{se}"));
                };
            // Fabrik i stället för en enda instans: team-läget kör en executor
            // PER WORKTREE (varsin rot, varsitt projektminne) - vanliga
            // körningar tar en enda för workspaceRoot precis som förut.
            AgentToolExecutor BuildExecutor(string rootDir) => new(accessLevel, rootDir, gate, settings.Worker.AllowInternet,
                new CommandGuard(settings.Worker.CommandGuard, settings.Worker.BlockedCommands),
                settings.Worker.ProjectMemoryEnabled ? new CodebaseIndex() : null,
                settings.Worker.ProjectMemoryEnabled ? new ProjectMemory(rootDir) : null,
                provisioner: async (tool, dest, provCt) =>
                {
                    var r = await new ToolProvisioner().ProvisionAsync(tool, dest, provCt);
                    return (r.Success, r.Output);
                },
                gameScaffolder: (engine, prompt, root, scafCt) =>
                {
                    var r = new GameScaffoldService().Scaffold(engine, prompt, root);
                    return Task.FromResult((r.Success, r.Output));
                },
                gameBuilder: (engine, root, buildCt) => { var res = gameBuilder.BuildAsync(engine, root, runCmd, buildCt); return res.ContinueWith(t => (t.Result.Success, t.Result.Output, t.Result.ExePath), TaskContinuationOptions.OnlyOnRanToCompletion); },
                appScaffolder: (tech, prompt, root, appCt) =>
                {
                    var r = new AppScaffoldService().Scaffold(tech, prompt, root);
                    return Task.FromResult((r.Success, r.Output));
                },
                taskDelegator: (subPrompt, subSystem, delCt) =>
                {
                    var delegator = new TaskDelegator(provider.CompleteAsync, accessLevel,
                        rootDir, settings.Worker.AllowInternet,
                        approvalGate: gate, commandGuard: new CommandGuard(settings.Worker.CommandGuard, settings.Worker.BlockedCommands),
                        provisioner: (tool, dest, provCt) => new ToolProvisioner().ProvisionAsync(tool, dest, provCt).ContinueWith(t => (t.Result.Success, t.Result.Output), TaskContinuationOptions.OnlyOnRanToCompletion),
                        gameScaffolder: (engine, p, root, scafCt) => { var r = new GameScaffoldService().Scaffold(engine, p, root); return Task.FromResult((r.Success, r.Output)); },
                        appScaffolder: (tech, p, root, appCt) => { var r = new AppScaffoldService().Scaffold(tech, p, root); return Task.FromResult((r.Success, r.Output)); },
                        gameBuilder: (engine, root, buildCt) => { var res = gameBuilder.BuildAsync(engine, root, runCmd, buildCt); return res.ContinueWith(t => (t.Result.Success, t.Result.Output, t.Result.ExePath), TaskContinuationOptions.OnlyOnRanToCompletion); },
                        askUser: null);
                    return delegator.DelegateAsync(subPrompt, subSystem, delCt);
                },
                assetGenerator: async (type, prompt, width, height, output, act) =>
                {
                    var gen = new AssetGenerator(httpFactory,
                        cloudImages: new CloudImageGenerator(httpFactory, settingsStore.GetApiKey));
                    var r = await gen.GenerateAsync(type, prompt, width, height, output, act);
                    // "molnmodell" i utdatan = riktigt betalt bildanrop (den
                    // procedurella fallbacken är gratis och räknas inte).
                    if (r.Success && r.Output.Contains("molnmodell"))
                        lock (unpricedLock) unpricedImageCalls++;
                    return (r.Success, r.Output, r.FilePath);
                },
                screenshotTool: async (windowTitle, output, sct) =>
                {
                    var tool = new ScreenshotTool();
                    var r = await tool.CaptureAsync(windowTitle, output, sct);
                    return (r.Success, r.Output, r.FilePath);
                },
                playtester: async (root, engine, ptCt) =>
                {
                    var tester = new GamePlaytester(httpFactory, BuildVisionReview());
                    var r = await tester.FullTestAsync(root, engine, TimeSpan.FromSeconds(15), ptCt);
                    return (r.Success, r.Summary, r.AverageFps, r.PeakMemoryMb, r.Duration);
                },
                packager: async (root, engine, name, outputDir, pkgCt) =>
                {
                    var pkg = new PackageService(httpFactory);
                    var r = await pkg.PackageAsync(root, engine, name, outputDir, pkgCt);
                    return (r.Success, r.Output, r.PackagePath, r.SizeBytes);
                },
                knowledgeBase: (engine, error) =>
                {
                    var fixes = GameKnowledgeBase.Lookup(engine, error);
                    var bestPractices = GameKnowledgeBase.GetBestPractices(engine);
                    var found = fixes.Count > 0;
                    var fixText = found
                        ? string.Join(Environment.NewLine, fixes.Select(f => $"**{f.ErrorPattern}**: {f.Fix}"))
                        : "No matching errors found.";
                    return Task.FromResult((found, fixText, bestPractices));
                },
                gameModules: GameModuleTool.Handle,
                visionReviewer: async (imagePath, question, vct) =>
                {
                    var analyzer = new VisionAnalyzer(httpFactory, settingsStore, settings.Providers);
                    var r = await analyzer.AnalyzeAsync(imagePath, question, vct);
                    AccountVision(r);   // v1.91: prissätts när usage finns
                    return (r.Success, FormatVisionResult(r));
                },
                // v1.94: uppdragets originaltext = genreunderlag när modellen
                // anropar scaffold utan prompt (fotbollsmanager -> Pixel Rush-
                // buggen). Assignment-vägen förskaffoldar oftast själv, men
                // agentens egna scaffold-anrop ska aldrig tappa genren.
                taskHint: req.Assignment);
            var executor = BuildExecutor(workspaceRoot);
            // Kostnadsblocket (usageByModel/completeAccounted/AccountVision)
            // bor FÖRE vision-/executor-delegaterna högre upp - de fångar
            // variablerna. Loopen skapas här där executorn finns.
            var loop = new AgentLoop(completeAccounted, executor, capLimit, capSpent);

            // Deterministic floor: a BUILD assignment on an EMPTY workspace
            // gets its scaffold created by the node itself, up-front - weak
            // local models sometimes answer a build request with prose and
            // zero tool calls, which used to end "successfully" with no files
            // at all. Now the production-bar project always lands on disk and
            // the model's job is to EXTEND it. Never touches a non-empty
            // workspace (existing projects must not be overwritten).
            // Varje steg skrivs BÅDE till SSE-strömmen och den persistenta
            // uppdragsloggen - så en omladdad/omstartad dashboard kan bygga
            // upp exakt samma stegvisning igen (se AssignmentLog).
            var logEntry = assignmentLog.Begin(req.Assignment, settings.NodeName);
            var logCompleted = false;
            // Ett skrivlås serialiserar ALLA skrivningar till svaret - steg,
            // keepalive-pingar och final-framen får aldrig flätas ihop.
            var writeLock = new SemaphoreSlim(1, 1);
            var lastWriteUtc = DateTime.UtcNow;
            async Task WriteFrameAsync(string payload, CancellationToken wct)
            {
                await writeLock.WaitAsync(wct);
                try
                {
                    await ctx.Response.WriteAsync(payload, wct);
                    await ctx.Response.Body.FlushAsync(wct);
                    lastWriteUtc = DateTime.UtcNow;
                }
                finally { writeLock.Release(); }
            }
            Func<AgentStep, Task> emitAgentStep = async step =>
            {
                assignmentLog.AddStep(logEntry, step.Kind, step.Detail);
                await WriteFrameAsync($"data: {JsonSerializer.Serialize(new { step })}\n\n", ct);
            };
            Task EmitStep(string kind, string detail) => emitAgentStep(new AgentStep(kind, detail));
            // v1.96: nu finns stegsänkan - koppla in den löpande förbruknings-
            // noten (var 25:e modellanrop) som completeAccounted komponerar.
            costEmitter = emitAgentStep;

            // SSE-keepalive: långa tysta faser (kövantan, godot-import på
            // 10+ minuter, kvalitetsgrind) fick mellanliggande proxies och
            // webbläsaren att stänga strömmen - "network error" i vyn trots
            // att bygget körde vidare. En kommentarsrad var 15:e sekund
            // håller hela kedjan nod -> Host -> dashboard vid liv; SSE-
            // parsers ignorerar rader utan "data:".
            using var pingCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var pingTask = Task.Run(async () =>
            {
                try
                {
                    while (!pingCts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(15), pingCts.Token);
                        if (DateTime.UtcNow - lastWriteUtc < TimeSpan.FromSeconds(12)) continue;
                        await WriteFrameAsync(": ping\n\n", pingCts.Token);
                    }
                }
                catch { /* strömmen stängd/avbruten - pingen är bara smörjmedel */ }
            });
            try
            {

            // ---- Uppdragskön: ETT bygge i taget per nod ---------------------
            // Två agentkörningar i samma arbetsyta kolliderar (delade filer,
            // kapplöpande skrivningar) och tidigare fanns inget skydd alls.
            // Nu köas överlappande uppdrag: positionen visas öppet, SSE-
            // strömmen och loggen hålls levande under väntan, och bygget
            // startar automatiskt när noden blir ledig.
            var wasQueued = false;
            using var queueSlot = await queue.EnterAsync(async position =>
            {
                wasQueued = true;
                await EmitStep("thinking",
                    $"Köad - ett annat uppdrag kör redan på den här noden (plats {position} i kön). " +
                    "Bygget startar automatiskt när noden blir ledig.");
            }, ct);
            if (wasQueued)
                await EmitStep("thinking", "Noden är ledig - bygget startar nu.");

            // Kvalitetsgrindens "skrevs något alls?"-kontroll jämför mot den
            // här tidpunkten - tagen FÖRE förskaffolden så även den räknas
            // som producerat arbete.
            var runStartUtc = DateTime.UtcNow;
            var buildIntent = HostRole.IsBuildRequest(req.Assignment);
            // Spel (Godot/HTML5/Unity) vs en ren app/verktyg. Bredare än "sager
            // det 'spel'": ett genrenamngivet uppdrag ("Football Manager Tycoon",
            // "en roguelike") ar ocksa ett spel - sa forskaffolden lagger ett
            // spel-kit OCH grinden kraver en spelbar spelleverans i stallet for
            // att godkanna en C#-konsolapp (rapporterat: fotbollsmanager blev en
            // textbaserad dotnet-app som grinden vinkade igenom).
            var wantsGame = GameScaffoldService.LooksLikeGame(req.Assignment);

            // ---- Uppgiftsmedveten modellroutning (kostnadsfokus) ------------
            // ModelTiers kostnadstrappar fanns hela tiden (coding: billig
            // coder upp till komplexitet 3, stark modell från 4) men uppdrags-
            // vägen frågade aldrig - allt gick till kedjans första modell
            // oavsett uppgift. Komplexiteten skattas deterministiskt (gratis),
            // valet visas öppet, och grindens eskalering lyfter till stark
            // tier vid hårda fel precis som förut. En uttrycklig hint från
            // anroparen respekteras alltid.
            var modelHint = req.ModelHint;
            if (modelHint is null && buildIntent)
            {
                // Motorn väger in i modellvalet: ett Godot/Unity-spel måste starta
                // på en kapabel modell (TaskComplexity bumpar motorspel), inte den
                // billiga tiern som promptens ord ensamma skulle landa på.
                var gameEngine = wantsGame ? GameScaffoldService.PickEngine(req.Assignment) : null;
                var (complexity, reason) = TaskComplexity.Estimate(req.Assignment, req.TeamSize, gameEngine);
                var (routeProvider, routedModel) = settings.Worker.ModelTiers.ForTask("coding", complexity);
                if (!string.IsNullOrWhiteSpace(routedModel))
                {
                    modelHint = routedModel;
                    await EmitStep("thinking",
                        $"Modellval: {routedModel} ({routeProvider}-rutt) - komplexitet {complexity}/5 ({reason}). " +
                        $"Hårda fel eskalerar automatiskt till {settings.Worker.ModelTiers.Complex}.");
                }
            }

            var assignmentText = req.Assignment;
            if (buildIntent && WorkspaceIsEmpty(workspaceRoot))
            {
                (bool Success, string Output) scaffold;
                if (wantsGame)
                {
                    var g = new GameScaffoldService().Scaffold("auto", req.Assignment, workspaceRoot);
                    scaffold = (g.Success, g.Output);
                }
                else
                {
                    var a = new AppScaffoldService().Scaffold("auto", req.Assignment, workspaceRoot);
                    scaffold = (a.Success, a.Output);
                }
                if (scaffold.Success)
                {
                    await EmitStep("tool_call", wantsGame ? "scaffold_game (automatisk projektgrund)" : "scaffold_app (automatisk projektgrund)");
                    await EmitStep("tool_result", scaffold.Output);
                    assignmentText = req.Assignment +
                        "\n\nOBS: En komplett, spelbar/körbar projektgrund är REDAN skapad i arbetsmappen (" + scaffold.Output +
                        "). Skapa INTE ett nytt projekt - läs DESIGN.md och index/koden, och UTÖKA grunden enligt uppdraget (innehåll, mekanik, polish). " +
                        "Kittet kan ha ett generiskt platshållartema (t.ex. plattformare/kiosk) - matchar det inte uppdraget: BYT TEMA som första steg " +
                        "(alla texter, entiteter, README/DESIGN ska följa uppdragets tema). Verifiera med verify/playtest när du är klar.";
                }
            }
            // Tema-vakt FÖRE kontinuiteten: nämner begäran ett tema som det
            // befintliga projektet saknar varje spår av ("fotboll" mot ett
            // bondgårdsprojekt) startas ett NYTT projekt i en undermapp i
            // stället - kontinuiteten fick annars agenten att bygga vidare
            // på fel spel (rapporterat: skördespel i stället för fotboll).
            else if (buildIntent
                && ProjectRootDetector.Detect(workspaceRoot) is { } existingRoot
                && ProjectContext.SeemsUnrelated(existingRoot, req.Assignment))
            {
                (bool Success, string Output) scaffold;
                if (wantsGame)
                {
                    var g = new GameScaffoldService().Scaffold("auto", req.Assignment, workspaceRoot);
                    scaffold = (g.Success, g.Output);
                }
                else
                {
                    var a = new AppScaffoldService().Scaffold("auto", req.Assignment, workspaceRoot);
                    scaffold = (a.Success, a.Output);
                }
                if (scaffold.Success)
                {
                    await EmitStep("tool_call", "nytt projekt (begäran gäller ett annat tema än det befintliga projektet)");
                    await EmitStep("tool_result", scaffold.Output);
                    assignmentText = req.Assignment +
                        "\n\nOBS: Begäran gäller ett ANNAT tema än det befintliga projektet i arbetsytan, så en NY projektgrund är skapad (" +
                        scaffold.Output + "). Arbeta i den nya projektmappen och rör inte det gamla projektet. " +
                        "Kittet är en generisk grund - BYT TEMA som första steg: alla texter, entiteter och benämningar " +
                        "ska följa användarens begärda tema, inte kittets platshållartema.";
                }
            }
            // Projektkontinuitet - spegelbilden av förskaffolden: en uppfölj-
            // ning ("gör spelet svårare") på en ICKE-tom arbetsyta får det
            // befintliga projektets kontext (mapp, motor, filer, DESIGN.md)
            // så standardbeteendet blir FORTSÄTT, aldrig börja om.
            else if (ProjectContext.Build(workspaceRoot, req.Assignment) is { } projectBrief)
            {
                assignmentText = req.Assignment + projectBrief;
            }

            // ---- Regissören: designkontrakt med mätbara kriterier -----------
            // En stark-modell-tur gör den svaga prompten till ett leverans-
            // kontrakt ("5 banor", "3 fiendetyper") som grinden följer upp.
            // Uppföljningar behåller projektets befintliga kontrakt.
            IReadOnlyList<string> contractCriteria = [];
            var isIteration = false;  // C6: uppfoljning pa ett redan kontrakterat projekt
            if (buildIntent)
            {
                var directorRoot = ProjectRootDetector.Detect(workspaceRoot) ?? workspaceRoot;
                // v1.87 (C5+): logga projektmappen SÅ FORT den är känd - ett
                // avbrutet bygge (nodomstart/krasch) kan då återupptas mot
                // samma projekt via Återuppta-knappen i historiken.
                assignmentLog.SetProject(logEntry, SafeProjectRel(workspaceRoot, directorRoot));
                if (!DirectorPass.AlreadyContracted(directorRoot))
                {
                    // Idéverkstaden: 2-3 slumpade genrefrön per körning gör
                    // att samma prompt ALDRIG ger samma spel två gånger -
                    // kitet är det deterministiska golvet, fröna är variationen
                    // (och svaga modeller bygger bra kring ett GIVET frö).
                    var directorGenre = GameScaffoldService.DetectGenre(req.Assignment);
                    var ideaSeeds = GenreIdeaBank.PickSeeds(directorGenre, count: 3);
                    await EmitStep("thinking",
                        "Inspirationsfrön till regissören: " + string.Join(" · ", ideaSeeds));
                    // Studiominne: regissören läser tidigare granskningsfynd för
                    // denna genre så kvaliteten stiger release för release i
                    // stället för att upprepa samma misstag.
                    var pastLessons = new StudioMemory().LessonsFor(directorGenre);
                    if (pastLessons.Count > 0)
                        await EmitStep("thinking",
                            "Studiominne (" + directorGenre + "): " + string.Join(" · ", pastLessons));
                    await EmitStep("tool_call", "regissören (designkontrakt med mätbara kriterier)");
                    var contract = await DirectorPass.RunAsync(
                        req.Assignment, directorRoot, settings.Worker.ModelTiers.Complex, completeAccounted, ct,
                        engine: GameBuilder.DetectEngine(directorRoot),
                        inspirationSeeds: ideaSeeds, pastLessons: pastLessons);
                    contractCriteria = contract.Criteria;
                    await EmitStep("tool_result", contract.ToMarkdown());
                    assignmentText += "\n\n" + contract.ToMarkdown() +
                        "\n\nBygg så att VARJE kontraktspunkt uppfylls - nodens kvalitetsgrind följer upp dem efteråt.";
                }
                else
                {
                    // C6 regressionsskydd: en uppföljning ("vidareutveckla") läser
                    // OM det befintliga leveranskontraktet, och kvalitetsgrindens
                    // oberoende granskning verifierar att ändringen inte tyst
                    // brutit någon befintlig punkt.
                    contractCriteria = DirectorPass.ReadCriteria(directorRoot);
                    isIteration = contractCriteria.Count > 0;
                    if (isIteration)
                        await EmitStep("thinking",
                            $"Regressionskoll: {contractCriteria.Count} befintliga kontraktspunkter verifieras mot ändringen så inget tyst går sönder.");
                }

                // Live-checklistan: kontraktspunkterna som ett eget steg så
                // dashboarden kan visa VAD som ska levereras medan det byggs -
                // regissörens uppföljning bockar sedan av dem (contract_status).
                if (contractCriteria.Count > 0)
                    await EmitStep("contract", JsonSerializer.Serialize(new { criteria = contractCriteria }));
            }

            // ---- Milstolpsgodkännande (valbart, bara lokala körningar) ------
            // Operatören får kontraktet och kan godkänna eller styra om med en
            // mening innan bygget drar igång. Klusterkörningar pausar aldrig
            // (ingen sitter och tittar där); timeout auto-godkänner.
            if (settings.Worker.MilestoneApproval && includePreview && contractCriteria.Count > 0)
            {
                var milestoneId = Guid.NewGuid().ToString("n")[..8];
                await EmitStep("awaiting_milestone", JsonSerializer.Serialize(new
                {
                    id = milestoneId,
                    contract = string.Join("\n", contractCriteria.Select(c => "- " + c))
                }));
                var (approved, note) = await MilestoneRegistry.WaitAsync(milestoneId, TimeSpan.FromMinutes(10), ct);
                if (!approved && !string.IsNullOrWhiteSpace(note))
                {
                    assignmentText += "\n\nOPERATÖRENS JUSTERING av inriktningen (väger tyngre än kontraktet där de krockar): " + note;
                    await EmitStep("thinking", "Operatören justerade inriktningen - bygget fortsätter med ändringen.");
                }
                else
                {
                    await EmitStep("thinking", approved
                        ? "Milstolpen godkänd - bygget fortsätter."
                        : "Milstolpen auto-godkändes (ingen respons inom 10 minuter).");
                }
            }

            // Same production-grade system prompt as interactive sessions -
            // an assignment dispatched through the cluster used to run with NO
            // system prompt at all, so the same goal came out far worse than
            // when typed into a session. AILOCAL.md in the workspace is
            // honored here too.
            var instructions = await ProjectInstructionsReader.TryReadAsync(workspaceRoot, ct);
            var system = AgentSystemPrompt.Build(workspaceRoot, accessLevel, instructions);

            // Iterationstaket är en KONTROLLPUNKT, inte en giljotin: gjorde
            // rundan verkliga filändringar fortsätter arbetet med hela
            // historiken kvar, upp till tre extra rundor (~200 iterationer
            // totalt). Utan filframsteg står taket kvar som runaway-skydd
            // precis som förut. (Rapporterat: "den slutar alltid vid 50
            // iterations även fast den fortfarande jobbar".)
            async Task<AgentRunResult> RunWithContinuationsAsync(string prompt) =>
                await RunWithContinuationsForModelAsync(prompt, modelHint);

            // Samma continuation-skydd men med valfri modell per körning, så
            // producent-lägets roller (t.ex. konstnären på den starka tiern) får
            // EXAKT samma iterationstak- och plan-vakts-skydd som en ensam agent
            // - inte en bar loop.RunAsync utan skyddsnät (granskning v1.83).
            async Task<AgentRunResult> RunWithContinuationsForModelAsync(string prompt, string? runModel)
            {
                var windowStart = DateTime.UtcNow;
                var runResult = await loop.RunAsync(prompt, accessLevel, runModel, onStep: emitAgentStep, ct, system: system);
                for (var round = 2; runResult.HitIterationCap && round <= 4; round++)
                {
                    if (ProjectRootDetector.NewestWriteUtc(workspaceRoot) < windowStart)
                    {
                        await EmitStep("tool_error",
                            "Iterationstaket nåddes utan några filändringar i senaste rundan - avbryter här (runaway-skydd).");
                        break;
                    }
                    await EmitStep("thinking",
                        $"Iterationstaket nått men arbetet gör framsteg - fortsätter där det slutade (runda {round} av 4).");
                    windowStart = DateTime.UtcNow;
                    runResult = await loop.RunAsync(
                        "Du nådde iterationstaket men uppdraget är inte klart än. Fortsätt EXAKT där du slutade - " +
                        "slutför återstoden av arbetet, kör verify, och avsluta när allt är på plats.",
                        accessLevel, runModel, onStep: emitAgentStep, ct, history: runResult.Messages, system: system);
                }

                // Plan-i-stället-för-utförande-vakten: svaga modeller avslutar
                // ibland med en presenterad plan eller en lov-fråga ("Let me
                // know if this plan meets your expectations!") - körningen ser
                // klar ut men inget byggdes. Ingen människa läser eller svarar
                // under ett bygge, så noden svarar åt operatören: utför.
                for (var push = 1; push <= 2 && buildIntent && runResult.Success && !runResult.HitCostCap
                     && PlanOnlyDetector.LooksUnexecuted(runResult.FinalAnswer); push++)
                {
                    await EmitStep("thinking",
                        "Agenten avslutade med en plan/fråga i stället för att utföra - noden svarar automatiskt: utför planen.");
                    runResult = await loop.RunAsync(
                        "Planen är godkänd. UTFÖR den nu i sin helhet - fråga aldrig om lov eller bekräftelse; " +
                        "ingen människa läser eller svarar under bygget. Bygg alla delar, kör verify, och avsluta " +
                        "först när allt är klart och verifierat.",
                        accessLevel, runModel, onStep: emitAgentStep, ct, history: runResult.Messages, system: system);
                }
                return runResult;
            }

            // ---- Team-läge: arkitekt -> parallella worktree-agenter -> merge.
            // Faller tillbaka till en ensam agent när git saknas/repo inte
            // gick att skapa (TeamBuild returnerar null) - team-läget får
            // aldrig göra ett bygge OMÖJLIGT som annars hade fungerat.
            AgentRunResult result;
            if (req.TeamSize is >= 2 && buildIntent)
            {
                // SSE-strömmen är EN ström - parallella spår måste serialisera
                // sina skrivningar, annars flätas frames sönder.
                var emitLock = new SemaphoreSlim(1, 1);
                Func<AgentStep, Task> teamEmit = async step =>
                {
                    await emitLock.WaitAsync(ct);
                    try { await emitAgentStep(step); }
                    finally { emitLock.Release(); }
                };
                await teamEmit(new AgentStep("thinking",
                    $"Team-läge: upp till {Math.Clamp(req.TeamSize.Value, 2, 4)} parallella utvecklare i varsin git-worktree."));
                // Både team- och producent-läge begärt (bara nåbart via råa
                // API:t - dashboarden gör dem ömsesidigt uteslutande): degradera
                // öppet så anroparen ser att producent-pipelinen hoppas över.
                if (req.ProducerMode)
                    await teamEmit(new AgentStep("thinking",
                        "Både team-läge och producent-läge begärdes - team-läget körs, producent-pipelinen hoppas över."));
                var gitService = new GitService();
                // Team-läget står och faller med git: på en dator utan git
                // (varken PATH eller provisionerad katalog) dog hela team-
                // vägen TYST i git init och föll tillbaka utan förklaring
                // (rapporterat). Nu provisioneras git automatiskt med öppna
                // steg - precis som python/godot redan provisioneras.
                if (!await gitService.IsGitAvailableAsync(ct))
                {
                    await teamEmit(new AgentStep("tool_call", "provision git (team-läget kräver git för worktrees)"));
                    try
                    {
                        var gitProv = await new ToolProvisioner().ProvisionAsync("git", "", ct);
                        await teamEmit(new AgentStep(gitProv.Success ? "tool_result" : "tool_error", gitProv.Output));
                    }
                    catch (Exception provEx)
                    {
                        await teamEmit(new AgentStep("tool_error",
                            $"git kunde inte provisioneras ({provEx.Message}) - team-läget kommer falla tillbaka till en ensam agent."));
                    }
                }
                // Modelltier-golv för team: fyra parallella utvecklare på den
                // billigaste tiern producerar prosa i fyra worktrees
                // (observerat live) - utan uttrycklig hint från anroparen kör
                // teamets utvecklare aldrig under Medium-tiern, och arkitekten
                // (ETT anrop som styr allt) kör alltid på den starka tiern.
                var teamHint = modelHint;
                if (string.IsNullOrWhiteSpace(req.ModelHint)
                    && string.Equals(modelHint, settings.Worker.ModelTiers.Simple, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(settings.Worker.ModelTiers.Medium))
                {
                    teamHint = settings.Worker.ModelTiers.Medium;
                    await teamEmit(new AgentStep("thinking",
                        $"Team-läge kräver mer modellmuskler - utvecklarna lyfts till {teamHint} (Medium-tiern)."));
                }
                var teamResult = await TeamBuild.RunAsync(
                    assignmentText, req.TeamSize.Value, workspaceRoot, accessLevel, teamHint,
                    system, completeAccounted, BuildExecutor, teamEmit,
                    gitService, new GitIsolationService(gitService), ct,
                    architectHint: settings.Worker.ModelTiers.Complex,
                    maxCostUsd: capLimit, spentSoFar: capSpent,
                    // Multi-modell: hårda spår får den starka tiern, enkla den
                    // billiga - olika modeller jobbar mot samma mål på EN maskin.
                    modelForTrack: difficulty => difficulty switch
                    {
                        "hard" => settings.Worker.ModelTiers.Complex,
                        "simple" => settings.Worker.ModelTiers.Simple,
                        _ => teamHint
                    });
                // null = git saknas/repo gick inte att skapa ELLER inget spår
                // producerade ändringar (TeamBuild har redan förklarat vilket
                // i strömmen) - den ensamma agenten med kvalitetsgrindens
                // tvingande rundor tar över.
                if (teamResult is null)
                    await teamEmit(new AgentStep("thinking",
                        "Team-läget slutförde inte bygget - kör som en ensam agent i stället."));
                result = teamResult ?? await RunWithContinuationsAsync(assignmentText);
            }
            else if (req.ProducerMode && buildIntent)
            {
                // C4: producent-läge - sekventiell rollpipeline (programmerare ->
                // konstnär -> ljuddesigner) på SAMMA arbetsyta, överlämning via
                // filerna. Konstnären kör den starka tiern (multi-modell).
                await emitAgentStep(new AgentStep("thinking",
                    "Producent-läge: sekventiell studiopipeline - programmerare, konstnär och ljuddesigner lämnar över till varandra."));
                // Varje roll körs genom SAMMA kostnadsbokförda, kostnadstakade loop
                // och samma continuation-skydd som en ensam agent (granskning v1.83:
                // producent-läget kringgick förr både kostnadstaket och continuations).
                result = await ProducerPipeline.RunAsync(
                    assignmentText, modelHint, settings.Worker.ModelTiers.Complex,
                    runRole: (prompt, model) => RunWithContinuationsForModelAsync(prompt, model),
                    emitAgentStep, ct);
            }
            else
            {
                result = await RunWithContinuationsAsync(assignmentText);
            }

            // ---- Nodens kvalitetsgrind --------------------------------------
            // Modellens eget "klart" räcker inte: noden kör verify + playtest
            // SJÄLV, matar tillbaka konkreta fel som en ny tur (max två
            // åtgärdsrundor med hela historiken kvar), eskalerar hårda
            // kvarvarande fel till den starka modelltiern en sista gång, och
            // underkänner till slut hellre än att rapportera falskt "Klar".
            QualityFindings? findings = null;
            async Task<QualityFindings> InspectAsync() => await AssignmentQualityGate.InspectAsync(
                workspaceRoot, buildIntent, runStartUtc, runCmd,
                playtest: async (root, engine, gct) =>
                {
                    var r = await new GamePlaytester(httpFactory, BuildVisionReview())
                        .FullTestAsync(root, engine, TimeSpan.FromSeconds(10), gct);
                    return (r.Success, r.Summary, (IReadOnlyList<string>)r.Issues);
                }, ct, gameExpected: buildIntent && wantsGame,
                // v2.2: genrekontraktet (grep-verifierbara formkrav, t.ex.
                // "15 begärda minispel => 15 räknade") får genre + prompt -
                // utan dessa var hela GenreContracts död kod i produktion.
                genre: GameScaffoldService.DetectGenre(req.Assignment),
                assignment: req.Assignment);

            // Auto-provisionera godot + exportmallar när projektet är Godot -
            // annars degraderar grinden tyst till statisk kontroll (inga
            // skriptfel fångas), fönsterdump-visionen kan inte köra spelet
            // och exe-exporten hoppar över sig själv. Samma självläkning som
            // git fick för team-läget: noden ordnar sina verktyg själv.
            if (result.Success && buildIntent
                && (findings?.ProjectRoot ?? ProjectRootDetector.Detect(workspaceRoot)) is { } godotCheckRoot
                && new ProjectVerifier().Detect(godotCheckRoot) == ProjectVerifier.ProjectKind.Godot
                && ToolLocator.Find("godot") is null)
            {
                await EmitStep("tool_call", "provision godot + godot-templates (full verifiering och exe-export av Godot-projekt)");
                try
                {
                    var provisioner = new ToolProvisioner();
                    var godotProv = await provisioner.ProvisionAsync("godot", "", ct);
                    await EmitStep(godotProv.Success ? "tool_result" : "tool_error", godotProv.Output);
                    if (godotProv.Success)
                    {
                        var templatesProv = await provisioner.ProvisionAsync("godot-templates", "", ct);
                        await EmitStep(templatesProv.Success ? "tool_result" : "tool_error", templatesProv.Output);
                    }
                }
                catch (Exception provEx)
                {
                    await EmitStep("tool_error",
                        $"godot kunde inte provisioneras ({provEx.Message}) - verifieringen körs statiskt och ingen exe exporteras.");
                }
            }

            const int maxFixRounds = 2;
            const int maxMilestoneRounds = 4;  // C5: längre bygge mot milstolpen medan framsteg görs
            var prevUnmet = int.MaxValue;
            for (var round = 0; result.Success; round++)
            {
                await EmitStep("tool_call", "kvalitetskontroll (nodens egen verify + playtest)");
                findings = await InspectAsync();
                await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);

                var contractUnmet = -1;  // -1 = ingen kontraktsgranskning (teknisk miss)
                // C5: kontraktet (MILSTOLPEN) granskas VARJE runda medan tekniken
                // är grön - inte bara runda 0 - så framstegen kan följas och bygget
                // fortsätter mot resten så länge ouppfyllda punkter minskar.
                // Grinden garanterade FUNGERANDE; det här kräver INTRESSANT.
                if (findings.Clean && contractCriteria.Count > 0)
                {
                    // Cross-modell-granskning: granskaren kör på den STARKA
                    // tiern - en ANNAN modell än byggaren (modelHint) i det
                    // vanliga fallet (billig modell bygger, stark granskar), så
                    // granskningen har andra felmoder än modellen som byggde.
                    var reviewModel = settings.Worker.ModelTiers.Complex;
                    var reviewHint = string.IsNullOrWhiteSpace(reviewModel) ? null : reviewModel;
                    await EmitStep("tool_call",
                        (isIteration ? "oberoende REGRESSIONSgranskning (håller befintliga kontraktspunkter)" : "oberoende granskning (kontrakt + uppenbara fel)")
                        + (reviewHint is null ? "" : $" - modell {reviewHint}"));
                    var unmet = await DirectorPass.ReviewAsync(
                        contractCriteria, findings.ProjectRoot ?? workspaceRoot, completeAccounted, ct,
                        reviewModelHint: reviewHint);
                    contractUnmet = unmet.Count;
                    await EmitStep("contract_status",
                        JsonSerializer.Serialize(new { criteria = contractCriteria, unmet }));
                    if (unmet.Count > 0)
                    {
                        findings = findings with
                        {
                            Clean = false,
                            Report = "Leveranskontraktet - ouppfyllda punkter:\n" + string.Join("\n", unmet.Select(u => "- " + u))
                        };
                        await EmitStep("tool_error", findings.Report);
                    }
                    else
                    {
                        await EmitStep("tool_result", "Regissören: alla kontraktspunkter bedöms uppfyllda.");
                    }
                }

                // v1.88: cross-modell KODgranskning - körs EN gång (runda 0) när
                // allt annat är grönt. Kontraktsgranskningen ser bara en evidens-
                // sammanfattning (4 filer × 3000 tecken); den här läser HELA
                // huvudkodfilerna på den starka tiern (annan modell än byggaren i
                // normalfallet) och jagar enbart riktiga buggar. Fynd → fixrunda.
                if (round == 0 && findings.Clean && buildIntent)
                {
                    var codeReviewModel = settings.Worker.ModelTiers.Complex;
                    var codeHint = string.IsNullOrWhiteSpace(codeReviewModel) ? null : codeReviewModel;
                    await EmitStep("tool_call",
                        "cross-modell kodgranskning (läser hela huvudkoden, jagar riktiga buggar)"
                        + (codeHint is null ? "" : $" - modell {codeHint}"));
                    var codeBugs = await CodeReviewPass.ReviewAsync(
                        findings.ProjectRoot ?? workspaceRoot, req.Assignment, completeAccounted, ct,
                        reviewModelHint: codeHint);
                    if (codeBugs.Count > 0)
                    {
                        findings = findings with
                        {
                            Clean = false,
                            Report = "Kodgranskaren hittade riktiga buggar:\n" + string.Join("\n", codeBugs.Select(b => "- " + b))
                        };
                        await EmitStep("tool_error", findings.Report);
                    }
                    else
                    {
                        await EmitStep("tool_result", "Kodgranskaren: inga riktiga buggar hittade.");
                    }
                }

                if (findings.Clean) break;
                // B5: kostnadstaket hoppar över fler betalda rundor (AgentLoop-taket
                // stoppar redan mitt i; det här sparar en extra rundas modellanrop).
                if (maxCostUsd > 0m && spentUsd >= maxCostUsd) break;
                // C5: milstolpe-driven förlängning - en ren TEKNISK miss behåller det
                // snäva taket (maxFixRounds); en KONTRAKTS-miss får fortsätta upp till
                // maxMilestoneRounds så länge ouppfyllda punkter MINSKAR (framsteg),
                // annars stannar den av - bygget konvergerar alltid, aldrig runaway.
                if (!AssignmentQualityGate.ShouldContinueFixing(round, contractUnmet, prevUnmet, maxFixRounds, maxMilestoneRounds))
                {
                    if (contractUnmet > 0)
                        await EmitStep("thinking",
                            contractUnmet >= prevUnmet
                                ? $"Milstolpen står stilla ({contractUnmet} punkter kvar, inget framsteg senaste rundan) - levererar det som är gjort."
                                : $"Nådde rundtaket ({maxMilestoneRounds}) med {contractUnmet} kontraktspunkter kvar - levererar det som är gjort.");
                    break;
                }
                if (contractUnmet > 0)
                {
                    prevUnmet = contractUnmet;
                    await EmitStep("thinking", $"Milstolpe: {contractCriteria.Count - contractUnmet}/{contractCriteria.Count} kontraktspunkter klara - fortsätter mot resten (runda {round + 1} av {maxMilestoneRounds}).");
                }
                result = await loop.RunAsync(AssignmentQualityGate.FixPrompt(findings), accessLevel, modelHint,
                    onStep: emitAgentStep, ct, history: result.Messages, system: system);
            }

            if (result.Success && findings is { Clean: false, HardFail: true })
            {
                // Kvalitetsbaserad eskalering: den billiga modellen gjorde
                // grovjobbet men kom inte i mål - låt den starka tiern göra
                // det sista lyftet med de konkreta bristerna som uppgift.
                // Saknas providern för tiern faller kedjan ändå tillbaka till
                // samma lokala modell, vilket bara kostar en extra runda.
                var strong = settings.Worker.ModelTiers.Complex;
                if (!string.IsNullOrWhiteSpace(strong) && !string.Equals(strong, modelHint, StringComparison.OrdinalIgnoreCase)
                    && !(maxCostUsd > 0m && spentUsd >= maxCostUsd))
                {
                    await EmitStep("thinking", $"Hårda fel kvarstår efter åtgärdsrundorna - eskalerar till starkare modell ({strong}) för en sista runda.");
                    result = await loop.RunAsync(AssignmentQualityGate.FixPrompt(findings), accessLevel, strong,
                        onStep: emitAgentStep, ct, history: result.Messages, system: system);
                    if (result.Success)
                    {
                        await EmitStep("tool_call", "kvalitetskontroll efter eskalering");
                        findings = await InspectAsync();
                        await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);
                    }
                }
            }

            // ---- v2.5: demorundor - spelbar demo + ägarens svar in i bygget --
            // Ägarens arbetsflöde: när prototypen är SPELBAR visas den som
            // live-vy i studiofliken (webbexport -> iframe; inget mappletande)
            // med 2-3 riktade frågor. Svaren blir en byggrunda med HÖGSTA
            // prioritet. Runda 1 = riktningskoll efter grind-grön prototyp;
            // runda 2 efter utvecklingsrundorna = SISTA ändringspunkten.
            // Klusterkörningar pausar aldrig; timeout 10 min auto-fortsätter.
            async Task<string?> DemoCheckpointAsync(int stage, string demoRoot)
            {
                if (!includePreview || !settings.Worker.DemoCheckpoints) return null;
                var demoEngine = GameBuilder.DetectEngine(demoRoot);
                string? demoPreview = null;
                if (demoEngine == "godot" && ToolLocator.Find("godot") is not null)
                {
                    await EmitStep("tool_call", $"webbexport för demorunda {stage} (live-vyn i studiofliken)");
                    var web = await gameBuilder.BuildWebAsync(demoRoot, runCmd, ct);
                    await EmitStep(web.Success ? "tool_result" : "tool_error",
                        web.Success
                            ? "Demon är spelbar direkt i studiofliken."
                            : "Webbexporten föll - demon visas via repris/skärmdumpar i stället. " + web.Output.Split('\n')[0]);
                    if (web.Success && web.WebPath is { } wp && File.Exists(wp))
                        demoPreview = "/api/preview/" + Path.GetRelativePath(workspaceRoot, wp).Replace('\\', '/');
                }
                else if (demoEngine == "html5" && File.Exists(Path.Combine(demoRoot, "index.html")))
                {
                    demoPreview = "/api/preview/" + Path.GetRelativePath(workspaceRoot, Path.Combine(demoRoot, "index.html")).Replace('\\', '/');
                }
                var replayFile = Path.Combine(demoRoot, "screenshots", "replay.png");
                var demoReplay = File.Exists(replayFile)
                    ? "/api/preview/" + Path.GetRelativePath(workspaceRoot, replayFile).Replace('\\', '/')
                    : null;
                string[] demoQs = stage == 1
                    ?
                    [
                        "Stämmer riktningen - är det här spelet du bad om?",
                        "Kändes svårigheten rimlig i det du hann prova?",
                        "Vad saknas mest just nu (innehåll, känsla, tydlighet)?",
                    ]
                    :
                    [
                        "Är spelet roligt nog att visa för någon annan?",
                        "Känns något buggigt eller ofärdigt?",
                        "SISTA ÄNDRINGSPUNKTEN: vad ska ändras före slutleverans?",
                    ];
                var demoId = Guid.NewGuid().ToString("n")[..8];
                await EmitStep("demo", JsonSerializer.Serialize(new
                {
                    id = demoId,
                    stage,
                    questions = demoQs,
                    previewPath = demoPreview,
                    replayPath = demoReplay,
                    note = stage == 2
                        ? "Sista ändringspunkten före slutleverans."
                        : "Prototypen är spelbar - grafik/ljud/innehåll poleras EFTER din feedback."
                }));
                await EmitStep("thinking", $"Demorunda {stage}: väntar på dina svar i studiofliken (auto-fortsätter efter 10 min)...");
                var (_, answers) = await MilestoneRegistry.WaitAsync(demoId, TimeSpan.FromMinutes(10), ct);
                if (string.IsNullOrWhiteSpace(answers))
                {
                    await EmitStep("tool_result", $"Demorunda {stage}: inga svar - bygget fortsätter enligt studiokritiken.");
                    return null;
                }
                await EmitStep("tool_result", $"Demorunda {stage} - dina svar tas in i bygget med högsta prioritet.");
                return answers;
            }

            async Task RunFeedbackRoundAsync(string feedback, int stage)
            {
                result = await loop.RunAsync(
                    $"DEMORUNDA {stage} - ÄGARENS FEEDBACK (högsta prioritet, bygg detta nu):\n{feedback}\n\n" +
                    "Regler: bygg vidare på det som finns, riv aldrig fungerande system, spelartext på engelska, " +
                    "inga råa formatsträngar/BBCode/datadumpar i UI. Kör verify när du är klar.",
                    accessLevel, modelHint, onStep: emitAgentStep, ct, history: result.Messages, system: system);
                await EmitStep("tool_call", $"kvalitetskontroll efter demorunda {stage}");
                findings = await InspectAsync();
                await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);
                if (!findings.Clean && !(maxCostUsd > 0m && spentUsd >= maxCostUsd))
                {
                    result = await loop.RunAsync(AssignmentQualityGate.FixPrompt(findings), accessLevel, modelHint,
                        onStep: emitAgentStep, ct, history: result.Messages, system: system);
                    findings = await InspectAsync();
                    await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);
                }
                if (findings.Clean)
                    result = result with { Success = true };
            }

            if (result.Success && findings is { Clean: true } && buildIntent && wantsGame
                && await DemoCheckpointAsync(1, findings.ProjectRoot ?? workspaceRoot) is { } demoAnswers1)
                await RunFeedbackRoundAsync(demoAnswers1, 1);

            // ---- v2.0.0: utvecklingsrundor - prototyp -> riktigt spel --------
            // Ägarens fundamentala krav: grindens gröna leverans är PROTOTYPEN,
            // inte slutprodukten. Studion granskar sitt eget verk (kritik på
            // billig Medium-tier över fyra axlar: större / snyggare / bättre
            // ljud / stabilare) och BYGGER förbättringarna som nya rundor
            // ovanpå historiken. Snapshot före varje runda = en försämrande
            // runda ÅTERSTÄLLS i stället för att skeppas. Max$-taket gäller.
            var polishTotal = Math.Clamp(settings.Worker.PolishRounds, 0, 3);
            if (result.Success && findings is { Clean: true } && buildIntent && wantsGame && polishTotal > 0)
            {
                for (var pr = 1; pr <= polishTotal; pr++)
                {
                    if (maxCostUsd > 0m && spentUsd >= maxCostUsd * 0.8m)
                    {
                        await EmitStep("thinking",
                            $"Utvecklingsrunda {pr}/{polishTotal} hoppas över - kostnadstaket är nästan nått (~${spentUsd:0.00} av max ${maxCostUsd:0.00}).");
                        break;
                    }
                    var polishRoot = findings.ProjectRoot ?? workspaceRoot;
                    var preSnap = ProjectSnapshots.Capture(workspaceRoot, polishRoot,
                        $"prototyp före utvecklingsrunda {pr}", clean: true, findings.Engine);
                    var preSnapFile = preSnap.Success
                        ? ProjectSnapshots.List(workspaceRoot, polishRoot).FirstOrDefault()?.File
                        : null;

                    var critiqueHint = string.IsNullOrWhiteSpace(settings.Worker.ModelTiers.Medium)
                        ? null : settings.Worker.ModelTiers.Medium;
                    await EmitStep("tool_call",
                        $"studiokritik (utvecklingsrunda {pr}/{polishTotal}): vad hade kunnat göras bättre?"
                        + (critiqueHint is null ? "" : $" - modell {critiqueHint}"));
                    // v2.4: kritiken får BILDBEVIS - sondens titel- och
                    // mittspelsdumpar går genom visionsmodellen (art director-
                    // pass) så "ser tomt/oproffsigt ut" upptäcks ur riktiga
                    // pixlar, inte gissas ur koden.
                    var polishShots = new[] { "playtest-title.png", "playtest.png", "playtest-late.png" }
                        .Select(n => Path.Combine(polishRoot, "screenshots", n))
                        .Where(File.Exists).ToList();
                    var improvements = await PolishPass.CritiqueAsync(
                        polishRoot, req.Assignment, findings.Report, completeAccounted, ct, critiqueHint,
                        BuildVisionReview(), polishShots);
                    if (improvements.Count == 0)
                    {
                        await EmitStep("tool_result",
                            "Studiokritiken: inget väsentligt att lyfta - spelet levereras som det är.");
                        break;
                    }
                    await EmitStep("tool_result",
                        $"Utvecklingsrunda {pr}/{polishTotal} - studion bygger:\n" +
                        string.Join("\n", improvements.Select(s => "- " + s)));

                    result = await loop.RunAsync(PolishPass.BuildPrompt(pr, polishTotal, improvements),
                        accessLevel, modelHint, onStep: emitAgentStep, ct, history: result.Messages, system: system);

                    await EmitStep("tool_call", $"kvalitetskontroll efter utvecklingsrunda {pr}");
                    var after = await InspectAsync();
                    await EmitStep(after.Clean ? "tool_result" : "tool_error", after.Report);
                    if (!after.Clean && !(maxCostUsd > 0m && spentUsd >= maxCostUsd))
                    {
                        // EN åtgärdsrunda - samma feedbackmönster som grinden.
                        result = await loop.RunAsync(AssignmentQualityGate.FixPrompt(after), accessLevel, modelHint,
                            onStep: emitAgentStep, ct, history: result.Messages, system: system);
                        await EmitStep("tool_call", $"kvalitetskontroll efter åtgärd (runda {pr})");
                        after = await InspectAsync();
                        await EmitStep(after.Clean ? "tool_result" : "tool_error", after.Report);
                    }
                    if (after.Clean)
                    {
                        findings = after;
                        // Grinden är sanningen: landade rundan grönt räknas
                        // uppdraget som lyckat även om modellturen slog i tak
                        // (v1.95-lärdomen - kassera aldrig landat arbete).
                        result = result with { Success = true };
                        continue;
                    }
                    // Rundan försämrade bygget - återställ prototypen ärligt.
                    if (preSnapFile is not null
                        && ProjectSnapshots.Restore(workspaceRoot, polishRoot, preSnapFile) is { Success: true })
                    {
                        await EmitStep("tool_result",
                            $"Utvecklingsrunda {pr} försämrade bygget - prototypen ÅTERSTÄLLD från snapshot. Levererar den godkända versionen.");
                        await EmitStep("tool_call", "kvalitetskontroll efter återställning");
                        findings = await InspectAsync();
                        await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);
                        result = result with { Success = true };
                    }
                    else
                    {
                        await EmitStep("tool_error",
                            $"Utvecklingsrunda {pr} försämrade bygget och kunde inte återställas - kvarvarande brister redovisas.");
                        findings = after;
                    }
                    break;
                }
            }

            // ---- v2.5: demorunda 2 - SISTA ändringspunkten före leverans ----
            if (result.Success && findings is { Clean: true } && buildIntent && wantsGame
                && await DemoCheckpointAsync(2, findings.ProjectRoot ?? workspaceRoot) is { } demoAnswers2)
                await RunFeedbackRoundAsync(demoAnswers2, 2);

            if (result.Success && findings is not null)
            {
                result = findings switch
                {
                    { Clean: true } => result with
                    {
                        FinalAnswer = result.FinalAnswer + "\n\n---\nKvalitetskontroll: godkänd (noden körde verify + playtest själv)."
                    },
                    { HardFail: true } => result with
                    {
                        Success = false,
                        FinalAnswer = result.FinalAnswer + "\n\n---\nKvalitetskontrollen underkände resultatet:\n" + findings.Report
                    },
                    _ => result with
                    {
                        FinalAnswer = result.FinalAnswer + "\n\n---\nKvarvarande anmärkningar från kvalitetskontrollen:\n" + findings.Report
                    }
                };
            }

            // Studiominne: spara denna genres granskningsfynd (design/ljud/kontrakt)
            // så regissören varnar för dem nästa gång samma genre byggs - studion
            // lär sig release för release i stället för att upprepa misstagen.
            if (buildIntent && findings is { Clean: false })
            {
                var mem = new StudioMemory();
                var lessonGenre = GameScaffoldService.DetectGenre(req.Assignment);
                foreach (var line in findings.Report.Split('\n'))
                {
                    var l = line.Trim();
                    if (l.Contains("SPELDESIGN") || l.Contains("STUDIOROLL") || l.StartsWith("- "))
                        mem.Record(lessonGenre, l.TrimStart('-', ' '));
                }
            }

            // Versionshistorik: varje godkänt uppdrag fryser projektet som en
            // snapshot - ångra-knappen i Projekt-vyn för uppföljningar som
            // gjorde spelet sämre.
            if (result.Success && findings?.ProjectRoot is { } snapshotRoot)
            {
                var snapshot = ProjectSnapshots.Capture(
                    workspaceRoot, snapshotRoot, req.Assignment, findings.Clean, findings.Engine);
                if (snapshot.Success)
                    await EmitStep("tool_result", $"Projektsnapshot: {snapshot.Output}");
            }

            // Godot-leverans: efter godkänt bygge exporteras en körbar exe
            // automatiskt (samma väg som build_game) när godot-binären finns -
            // ett "riktigt spel" ska sluta som en fil man kan dubbelklicka,
            // inte ett projekt man måste öppna i editorn (användarrapport:
            // "fick ingen spelbar fil i godot"). Saknas godot: ärlig notis.
            string? artifactPath = null;
            if (result.Success && buildIntent
                && (findings?.ProjectRoot ?? ProjectRootDetector.Detect(workspaceRoot)) is { } deliveryRoot
                && new ProjectVerifier().Detect(deliveryRoot) == ProjectVerifier.ProjectKind.Godot)
            {
                if (ToolLocator.Find("godot") is not null)
                {
                    await EmitStep("tool_call", "build_game (automatisk export till körbar exe)");
                    var build = await gameBuilder.BuildAsync("godot", deliveryRoot, runCmd, ct);
                    await EmitStep(build.Success ? "tool_result" : "tool_error", build.Output);
                    if (build.Success && build.ExePath is { } exe && File.Exists(exe))
                        artifactPath = "/api/artifact?path=" + Uri.EscapeDataString(
                            Path.GetRelativePath(workspaceRoot, exe).Replace('\\', '/'));
                    else if (!build.Success)
                        // v1.99: en misslyckad export får ALDRIG gömmas bakom
                        // modellens "Klart"-sammanfattning (live-sett: exit 1
                        // i stegflödet men slutsvaret sa godkänt utan exe).
                        // Ärligheten ska stå i själva slutsvaret.
                        result = result with
                        {
                            FinalAnswer = result.FinalAnswer +
                                "\n\nOBS: exe-exporten misslyckades - spelet levereras som Godot-projekt (öppna via Spela/preview), ingen dubbelklickbar exe. " +
                                "Detalj: " + build.Output.Split('\n')[0]
                        };
                }
                else
                {
                    await EmitStep("thinking",
                        "godot är inte provisionerad på den här noden - ingen exe exporterades. " +
                        "Kör provision \"godot\" + \"godot-templates\" så levereras spelbara exe-filer automatiskt.");
                }
            }

            // Förhandsvisningslänken beräknas ALLTID vid lyckat bygge - för
            // klusterkörningar skriver Hosten om vägen till sin proxy
            // (/api/nodes/{id}/preview/...) så Spela-knappen fungerar från
            // vilken dashboard som helst, inte bara på noden som byggde.
            string? previewPath = null;
            string? replayPath = null;
            if (result.Success)
            {
                var projectRoot = findings?.ProjectRoot ?? ProjectRootDetector.Detect(workspaceRoot);
                var entry = projectRoot is null ? null : Path.Combine(projectRoot, "index.html");
                if (entry is not null && File.Exists(entry))
                    previewPath = "/api/preview/" + Path.GetRelativePath(workspaceRoot, entry).Replace('\\', '/');

                // B3: kort speltest-repris (APNG) som fönstersonden spelade in i
                // screenshots/replay.png - visas i uppdragsresultatet ("så här
                // ser ditt spel ut när det spelas"). Bara när sonden faktiskt
                // producerade en; samma /api/preview-väg som Spela-länken.
                var replay = projectRoot is null ? null : Path.Combine(projectRoot, "screenshots", "replay.png");
                if (replay is not null && File.Exists(replay))
                    replayPath = "/api/preview/" + Path.GetRelativePath(workspaceRoot, replay).Replace('\\', '/');
            }

            assignmentLog.Complete(logEntry, result.Success, result.FinalAnswer, previewPath, artifactPath,
                projectRel: SafeProjectRel(workspaceRoot, findings?.ProjectRoot ?? ProjectRootDetector.Detect(workspaceRoot)));
            logCompleted = true;

            // B5: uppskatta uppdragets kostnad (Anthropic + OpenRouter-pris) och
            // redovisa den öppet - även ett misslyckat bygge kostade tokens.
            // Null (allt lokalt/okänt pris) döljer siffran i stället för att visa
            // en missvisande nolla.
            decimal? costUsd = await AssignmentCost.EstimateAsync(usageByModel, httpFactory, ct);
            // Ärlighet: bild-/visionsanrop kan inte prissättas (egna API:er) -
            // antalet redovisas bredvid siffran så den aldrig ser komplett ut
            // när den inte är det. 0 = fältet utelämnas (null) i UI:t.
            int? unpricedCalls = unpricedImageCalls > 0 ? unpricedImageCalls : null;

            await WriteFrameAsync(
                $"data: {JsonSerializer.Serialize(new { final = result, previewPath, artifactPath, replayPath, costUsd, unpricedCalls })}\n\n", ct);
            return Results.Empty;
            }
            finally
            {
                // Stäng keepalive-pingen innan svaret avslutas.
                pingCts.Cancel();
                try { await pingTask; } catch { /* redan avslutad */ }
                // Avbrott/undantag mitt i körningen får aldrig lämna ett evigt
                // "Running"-inlägg som dashboarden pollar på i all oändlighet.
                if (!logCompleted)
                    assignmentLog.Complete(logEntry, success: false, "Körningen avbröts innan den blev klar.", previewPath: null);
            }
    }

    /// <summary>Local (no-cluster) planning + assignment endpoints for the
    /// dashboard's Uppdrag flow, so it works on a Launcher or a standalone
    /// Worker WITHOUT any Host: /api/goal-plan plans on this node's own
    /// provider chain and /api/assignment runs the agent in-process. Before
    /// this, both routes existed only on the Host role - the desktop app
    /// (which starts its node as Launcher) got HTTP 404 on the very first
    /// planning call.</summary>
    public static void MapLocalAssignmentEndpoints(WebApplication app)
    {
        app.MapPost("/api/goal-plan", async (
            GoalPlanRequest req, FallbackChatProvider provider, NodeSettings settings, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Goal))
                return Results.Problem(detail: "goal text is required", statusCode: StatusCodes.Status400BadRequest);

            var planner = new GoalPlanner(provider.CompleteAsync);
            var maxParts = Math.Clamp(req.MaxParts ?? 6, 1, 8);
            IReadOnlyList<PlannedSubtask>? plan = null;
            try { plan = await planner.PlanAsync(req.Goal, maxParts, ct); }
            catch { /* fall through to the actionable 502 below */ }
            if (plan is null)
                return Results.Problem(
                    detail: "Kunde inte skapa en plan med nodens AI-modell. Kontrollera att en provider är konfigurerad (Ollama eller en API-nyckel i Inställningar), eller formulera målet som ett enda uppdrag.",
                    statusCode: StatusCodes.Status502BadGateway);

            return Results.Ok(new
            {
                worker = new { id = "local", name = settings.NodeName },
                subtasks = plan.Select(p => new { p.Title, p.Description, p.Independent })
            });
        });

        app.MapPost("/api/assignment", async (
            AssignmentRequest req, HttpContext ctx, FallbackChatProvider provider,
            NodeSettings settings, IHttpClientFactory httpFactory, HostLocator hostLocator,
            PersistentSettingsStore settingsStore, AssignmentLog assignmentLog, AssignmentQueue queue, CancellationToken ct) =>
        {
            // Mirror the engine's own early checks BEFORE the response starts,
            // so they still surface as proper HTTP errors instead of dying
            // inside an already-started SSE stream.
            if (settings.Worker.AgentAccess == AgentAccessLevel.Off)
                return Results.Problem(
                    detail: "Agentläge är inte aktiverat på den här noden (Inställningar -> Agentläge).",
                    statusCode: StatusCodes.Status403Forbidden);
            if (string.IsNullOrWhiteSpace(req.Assignment))
                return Results.Problem(detail: "assignment text is required", statusCode: StatusCodes.Status400BadRequest);

            // B2 (iterationsknappen): klienten kanner bara projektets RELATIVA
            // vag, sa den skickar ProjectRel i stallet for en absolut
            // WorkspaceOverride. Resolva den till projektmappen (samma
            // traversal-/projektvakt som ovriga portfoljendpoints) sa
            // kontinuitetsbriefen kor mot RATT projekt - inte bara det senast
            // aktiva som ProjectRootDetector annars skulle gissa pa.
            if (string.IsNullOrWhiteSpace(req.WorkspaceOverride) && !string.IsNullOrWhiteSpace(req.ProjectRel))
            {
                var projectDir = ResolveProjectDir(settings, req.ProjectRel);
                if (projectDir is null)
                    return Results.Problem(detail: "okänt projekt att vidareutveckla", statusCode: StatusCodes.Status404NotFound);
                req = req with { WorkspaceOverride = projectDir };
            }

            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";
            // Same leading worker-frame contract as the Host's /api/assignment,
            // so the dashboard's plan runner works identically here.
            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { worker = new { id = "local", name = settings.NodeName } })}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            return await RunAssignmentAsync(req, ctx, provider, settings, httpFactory, hostLocator, settingsStore, assignmentLog, queue, ct);
        });

        // Persistent uppdragshistorik för DEN HÄR nodens körningar - samma
        // PascalCase-form som SSE-framarna (se HostRole:s motsvarighet).
        app.MapGet("/api/assignment-log", (AssignmentLog assignmentLog) =>
            Results.Text(JsonSerializer.Serialize(assignmentLog.Snapshot()), "application/json"));

        // Milstolpsgodkännandet: dashboarden svarar hit när operatören
        // klickar Godkänn/Justera på ett pausat bygge.
        app.MapPost("/api/assignment/milestone", (MilestoneDecisionRequest req) =>
            MilestoneRegistry.Resolve(req.Id, req.Approve, req.Note)
                ? Results.Ok(new { resolved = true })
                : Results.Problem(detail: "Milstolpen är redan avgjord eller okänd.", statusCode: StatusCodes.Status404NotFound));

        // Benchmark-sviten: självmätning av NODENS byggförmåga per version.
        // Lokal-only precis som assignment-motorn den mäter.
        app.MapGet("/api/benchmark", (BenchmarkService bench) =>
            Results.Text(JsonSerializer.Serialize(new
            {
                bench.Running,
                Progress = bench.Progress.ToArray(),
                History = bench.History
            }), "application/json"));

        // ---- Projektvyn: portfölj, paketering, mapp, radering, rollback ----
        app.MapGet("/api/projects", (NodeSettings settings) =>
            Results.Text(JsonSerializer.Serialize(ListLocalProjects(settings)), "application/json"));

        app.MapPost("/api/projects/package", async (ProjectActionRequest req, NodeSettings settings,
            IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (ResolveProjectDir(settings, req.Rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            var pkg = new PackageService(httpFactory);
            var r = await pkg.PackageAsync(dir, "auto", Path.GetFileName(dir), Path.Combine(dir, "dist"), ct);
            return Results.Text(JsonSerializer.Serialize(new { r.Success, r.Output, r.PackagePath }), "application/json");
        });

        // v1.90: Android-APK fran projektvyn. Sjalvprovisionerande kedja
        // (provision("android-sdk") hamtar SDK + standard-godot + mallar);
        // utan kedjan svarar vagen med den arliga guiden i Output.
        app.MapPost("/api/projects/build-android", async (ProjectActionRequest req, NodeSettings settings, CancellationToken ct) =>
        {
            if (ResolveProjectDir(settings, req.Rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            Func<string, string, CancellationToken, Task<(int, string)>> run = (cmd, wd, rct) =>
            {
                var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
                {
                    WorkingDirectory = Directory.Exists(wd) ? wd : Environment.CurrentDirectory,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = new System.Diagnostics.Process { StartInfo = psi };
                var so = new System.Text.StringBuilder();
                var se = new System.Text.StringBuilder();
                proc.OutputDataReceived += (_, e) => { if (e.Data is not null) so.AppendLine(e.Data); };
                proc.ErrorDataReceived += (_, e) => { if (e.Data is not null) se.AppendLine(e.Data); };
                proc.Start();
                proc.BeginOutputReadLine();
                proc.BeginErrorReadLine();
                // Event-lasning + WaitForExit(processen, inte piparna): exporten
                // startar adb-servern som arver handtagen och lever kvar (v1.90).
                if (!proc.WaitForExit((int)TimeSpan.FromMinutes(10).TotalMilliseconds))
                {
                    try { proc.Kill(entireProcessTree: true); } catch { /* redan död */ }
                    return Task.FromResult((-1, $"tidsgräns\n{so}\n{se}"));
                }
                return Task.FromResult((proc.ExitCode, $"{so}\n{se}"));
            };
            var r = await new GameBuilder().BuildAndroidAsync(dir, run, ct);
            return Results.Text(JsonSerializer.Serialize(new { r.Success, r.Output, r.ApkPath }), "application/json");
        });

        app.MapPost("/api/projects/open-folder", (ProjectActionRequest req, NodeSettings settings) =>
        {
            if (ResolveProjectDir(settings, req.Rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            // Lokala roller = samma maskin som operatören; öppna Utforskaren.
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"\"{dir}\"")
            { UseShellExecute = true });
            return Results.Ok(new { opened = true });
        });

        app.MapPost("/api/projects/delete", (ProjectActionRequest req, NodeSettings settings) =>
        {
            if (req.Rel == ".")
                return Results.Problem(detail: "Projektet ligger i arbetsytans rot - radera manuellt, inte härifrån.",
                    statusCode: StatusCodes.Status400BadRequest);
            if (ResolveProjectDir(settings, req.Rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            Directory.Delete(dir, recursive: true);
            return Results.Ok(new { deleted = true });
        });

        app.MapGet("/api/projects/snapshots", (string rel, NodeSettings settings) =>
        {
            if (ResolveProjectDir(settings, rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
            return Results.Text(JsonSerializer.Serialize(ProjectSnapshots.List(root, dir)), "application/json");
        });

        app.MapPost("/api/projects/restore", (ProjectRestoreRequest req, NodeSettings settings) =>
        {
            if (ResolveProjectDir(settings, req.Rel) is not { } dir)
                return Results.Problem(detail: "okänt projekt", statusCode: StatusCodes.Status404NotFound);
            var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
            var (success, output) = ProjectSnapshots.Restore(root, dir, req.File);
            return success
                ? Results.Ok(new { restored = true, output })
                : Results.Problem(detail: output, statusCode: StatusCodes.Status400BadRequest);
        });

        app.MapPost("/api/benchmark/run", (BenchmarkRunRequest req, BenchmarkService bench,
            AssignmentLog assignmentLog, NodeSettings settings) =>
        {
            if (settings.Worker.AgentAccess == AgentAccessLevel.Off)
                return Results.Problem(detail: "Agentläge krävs för benchmark (Inställningar -> Agentläge).",
                    statusCode: StatusCodes.Status403Forbidden);
            if (assignmentLog.RunningCount > 0)
                return Results.Problem(detail: "Ett uppdrag kör redan - benchmarken skulle konkurrera om modellen. Vänta tills det är klart.",
                    statusCode: StatusCodes.Status409Conflict);
            var started = bench.TryStart(req.Count ?? 3, settings.Port, SelfUpdater.CurrentVersion);
            return started
                ? Results.Ok(new { started = true })
                : Results.Problem(detail: "En benchmark kör redan.", statusCode: StatusCodes.Status409Conflict);
        });

        // "Öppna resultatet": serverar det senast byggda projektet direkt
        // från nodens arbetsyta så prompt -> spelbart spel blir ETT klick.
        // Utan entré-redirecten fick användaren själv leta upp mappen på
        // disk och dubbelklicka index.html.
        app.MapGet("/api/preview", (NodeSettings settings) => PreviewEntry(settings));
        app.MapGet("/api/preview/{**path}", (string path, NodeSettings settings) => ServePreviewFile(settings, path));

        // Nedladdning av byggd artefakt (Godot-exe / paket-zip) från den här
        // nodens arbetsyta - ClusterDelivery vaktar traversal + filtyp.
        app.MapGet("/api/artifact", (string? path, NodeSettings settings) => ServeArtifactFile(settings, path));
    }

    /// <summary>The node's project portfolio: recognizable projects in the
    /// workspace root (and its immediate subfolders), newest first. Shared by
    /// the local /api/projects view and the cluster-reachable /execute/projects
    /// the Host aggregates (B6).</summary>
    internal static List<ProjectSummary> ListLocalProjects(NodeSettings settings)
    {
        var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
        var verifier = new ProjectVerifier();
        var candidates = new List<string>();
        if (Directory.Exists(root))
        {
            if (verifier.Detect(root) != ProjectVerifier.ProjectKind.Unknown)
                candidates.Add(root);
            try
            {
                candidates.AddRange(Directory.EnumerateDirectories(root)
                    .Where(d => !Path.GetFileName(d).StartsWith('.')
                        && verifier.Detect(d) != ProjectVerifier.ProjectKind.Unknown));
            }
            catch { /* oläsbar arbetsyta - visa det som gick */ }
        }

        return candidates.Select(dir =>
        {
            var rel = Path.GetRelativePath(root, dir);
            var latest = ProjectSnapshots.List(root, dir).FirstOrDefault();
            int files;
            try { files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Count(f => !f.Contains(".git")); }
            catch { files = 0; }
            return new ProjectSummary(
                rel == "." ? "." : rel.Replace('\\', '/'),
                rel == "." ? Path.GetFileName(root) + " (roten)" : Path.GetFileName(dir),
                verifier.Detect(dir).ToString(),
                GameBuilder.DetectEngine(dir),
                files,
                ProjectRootDetector.NewestWriteUtc(dir),
                File.Exists(Path.Combine(dir, "index.html")),
                ProjectSnapshots.List(root, dir).Count,
                latest?.Clean,
                latest?.Label);
        }).OrderByDescending(p => p.LastModified).ToList();
    }

    /// <summary>v1.87: projektmappens relativa väg för uppdragsloggen (så ett
    /// avbrutet bygge kan återupptas). Null om roten är okänd eller ligger
    /// UTANFÖR arbetsytan - en logg-post får aldrig peka utåt.</summary>
    internal static string? SafeProjectRel(string workspaceRoot, string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot)) return null;
        try
        {
            var rel = Path.GetRelativePath(workspaceRoot, projectRoot).Replace('\\', '/');
            return rel.StartsWith("..") || Path.IsPathRooted(rel) ? null : rel;
        }
        catch { return null; }
    }

    /// <summary>Resolves a project's rel path against the workspace with a
    /// traversal guard AND the requirement that the target actually IS a
    /// recognizable project - the portfolio endpoints must never operate on
    /// arbitrary folders.</summary>
    internal static string? ResolveProjectDir(NodeSettings settings, string? rel)
    {
        if (string.IsNullOrWhiteSpace(rel)) return null;
        var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, rel)); }
        catch { return null; }
        if (!full.Equals(root, StringComparison.OrdinalIgnoreCase)
            && !full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!Directory.Exists(full)) return null;
        return new ProjectVerifier().Detect(full) != ProjectVerifier.ProjectKind.Unknown ? full : null;
    }

    internal static string PreviewWorkspaceRoot(NodeSettings settings) =>
        string.IsNullOrWhiteSpace(settings.Worker.WorkspacePath)
            ? Path.Combine(SettingsPaths.DataDirectory, "agent-workspace")
            : settings.Worker.WorkspacePath;

    static IResult PreviewEntry(NodeSettings settings)
    {
        var root = PreviewWorkspaceRoot(settings);
        var project = ProjectRootDetector.Detect(root);
        var entry = project is null ? null : Path.Combine(project, "index.html");
        if (entry is null || !File.Exists(entry))
            return Results.Problem(
                detail: "Inget förhandsvisningsbart projekt (index.html) hittades i den här nodens arbetsyta - byggdes projektet på en annan nod?",
                statusCode: StatusCodes.Status404NotFound);
        return Results.Redirect("/api/preview/" + Path.GetRelativePath(root, entry).Replace('\\', '/'));
    }

    // Medveten allowlist: bara filtyper ett HTML5-spel/projekt behöver.
    // Källkod utanför listan, dolda filer och .ailocal-* (projektminne)
    // serveras aldrig.
    private static readonly Dictionary<string, string> PreviewContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".svg"] = "image/svg+xml",
        [".webp"] = "image/webp",
        [".ico"] = "image/x-icon",
        [".wav"] = "audio/wav",
        [".mp3"] = "audio/mpeg",
        [".ogg"] = "audio/ogg",
        [".ttf"] = "font/ttf",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".txt"] = "text/plain; charset=utf-8",
        [".md"] = "text/plain; charset=utf-8"
    };

    internal static IResult ServeArtifactFile(NodeSettings settings, string? rel)
    {
        var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
        var full = ClusterDelivery.ResolveArtifactFile(root, rel);
        return full is null
            ? Results.NotFound()
            : Results.File(full, "application/octet-stream", Path.GetFileName(full));
    }

    internal static IResult ServePreviewFile(NodeSettings settings, string? path)
    {
        var root = Path.GetFullPath(PreviewWorkspaceRoot(settings));
        string full;
        try { full = Path.GetFullPath(Path.Combine(root, path ?? "")); }
        catch { return Results.NotFound(); }
        // Traversal-vakt: den upplösta sökvägen MÅSTE ligga under arbetsytan.
        if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return Results.NotFound();
        var name = Path.GetFileName(full);
        if (name.StartsWith('.'))
            return Results.NotFound();
        if (!PreviewContentTypes.TryGetValue(Path.GetExtension(full), out var contentType) || !File.Exists(full))
            return Results.NotFound();
        return Results.File(full, contentType);
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

    /// <summary>Formats a VisionResult for the agent: the analysis text plus
    /// the issue list, so the model gets both the narrative and actionable
    /// bullet points. Shared with SessionApi's wiring.</summary>
    internal static string FormatVisionResult(VisionResult r) =>
        r.Analysis + (r.Issues.Count > 0
            ? "\n\nVisuella problem:\n" + string.Join("\n", r.Issues.Select(i => "- " + i))
            : "\n\n(inga visuella problem listade)");

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

    /// <summary>True when the agent workspace has no files at all - the only
    /// state where the deterministic pre-scaffold may run.</summary>
    internal static bool WorkspaceIsEmpty(string root)
    {
        try
        {
            return !Directory.Exists(root)
                || !Directory.EnumerateFileSystemEntries(root).Any();
        }
        catch
        {
            return false; // can't tell -> never risk scaffolding over something
        }
    }

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
