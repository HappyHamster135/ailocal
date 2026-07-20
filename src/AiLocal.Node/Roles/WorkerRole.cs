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
        // includePreview:false - klustervagen visar ingen forhandsvisnings-
        // lank, eftersom filerna ligger pa DEN HAR maskinen medan dashboarden
        // som startade uppdraget kor pa en annan (lanken vore dod dar).
        app.MapPost("/execute/assignment", (
            AssignmentRequest req, HttpContext ctx, FallbackChatProvider provider,
            NodeSettings settings, IHttpClientFactory httpFactory, HostLocator hostLocator,
            PersistentSettingsStore settingsStore, AssignmentLog assignmentLog, CancellationToken ct)
            => RunAssignmentAsync(req, ctx, provider, settings, httpFactory, hostLocator, settingsStore, assignmentLog, ct, includePreview: false));

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
        CancellationToken ct, bool includePreview = true)
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

            var gameBuilder = new GameBuilder();
            Func<string, string, CancellationToken, Task<(int ExitCode, string Output)>> runCmd =
                (cmd, dir, rcCt) =>
                {
                    var psi = OperatingSystem.IsWindows()
                        ? new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/c {cmd}")
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
            var executor = new AgentToolExecutor(accessLevel, workspaceRoot, gate, settings.Worker.AllowInternet,
                new CommandGuard(settings.Worker.CommandGuard, settings.Worker.BlockedCommands),
                settings.Worker.ProjectMemoryEnabled ? new CodebaseIndex() : null,
                settings.Worker.ProjectMemoryEnabled ? new ProjectMemory(workspaceRoot) : null,
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
                        workspaceRoot, settings.Worker.AllowInternet,
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
                    var gen = new AssetGenerator(httpFactory);
                    var r = await gen.GenerateAsync(type, prompt, width, height, output, act);
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
                    var tester = new GamePlaytester(httpFactory);
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
                    return (r.Success, FormatVisionResult(r));
                });
            var loop = new AgentLoop(provider.CompleteAsync, executor);

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
            Func<AgentStep, Task> emitAgentStep = async step =>
            {
                assignmentLog.AddStep(logEntry, step.Kind, step.Detail);
                await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { step })}\n\n", ct);
                await ctx.Response.Body.FlushAsync(ct);
            };
            Task EmitStep(string kind, string detail) => emitAgentStep(new AgentStep(kind, detail));
            try
            {

            // Kvalitetsgrindens "skrevs något alls?"-kontroll jämför mot den
            // här tidpunkten - tagen FÖRE förskaffolden så även den räknas
            // som producerat arbete.
            var runStartUtc = DateTime.UtcNow;
            var buildIntent = HostRole.IsBuildRequest(req.Assignment);

            var assignmentText = req.Assignment;
            if (buildIntent && WorkspaceIsEmpty(workspaceRoot))
            {
                var wantsGame = req.Assignment.Contains("spel", StringComparison.OrdinalIgnoreCase)
                    || req.Assignment.Contains("game", StringComparison.OrdinalIgnoreCase);
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
                        "). Skapa INTE ett nytt projekt - läs DESIGN.md och index/koden, och UTÖKA grunden enligt uppdraget (innehåll, mekanik, polish). Verifiera med verify/playtest när du är klar.";
                }
            }

            // Same production-grade system prompt as interactive sessions -
            // an assignment dispatched through the cluster used to run with NO
            // system prompt at all, so the same goal came out far worse than
            // when typed into a session. AILOCAL.md in the workspace is
            // honored here too.
            var instructions = await ProjectInstructionsReader.TryReadAsync(workspaceRoot, ct);
            var system = AgentSystemPrompt.Build(workspaceRoot, accessLevel, instructions);

            var result = await loop.RunAsync(assignmentText, accessLevel, req.ModelHint, onStep: emitAgentStep, ct, system: system);

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
                    var r = await new GamePlaytester(httpFactory).FullTestAsync(root, engine, TimeSpan.FromSeconds(10), gct);
                    return (r.Success, r.Summary, (IReadOnlyList<string>)r.Issues);
                }, ct);

            const int maxFixRounds = 2;
            for (var round = 0; result.Success; round++)
            {
                await EmitStep("tool_call", "kvalitetskontroll (nodens egen verify + playtest)");
                findings = await InspectAsync();
                await EmitStep(findings.Clean ? "tool_result" : "tool_error", findings.Report);
                if (findings.Clean || round >= maxFixRounds) break;
                result = await loop.RunAsync(AssignmentQualityGate.FixPrompt(findings), accessLevel, req.ModelHint,
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
                if (!string.IsNullOrWhiteSpace(strong) && !string.Equals(strong, req.ModelHint, StringComparison.OrdinalIgnoreCase))
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

            // Förhandsvisningslänk bara för lokala körningar (Launcher eller
            // fristående Worker) - då kör dashboarden på samma nod som
            // filerna och /api/preview kan servera projektet direkt.
            string? previewPath = null;
            if (includePreview && result.Success)
            {
                var projectRoot = findings?.ProjectRoot ?? ProjectRootDetector.Detect(workspaceRoot);
                var entry = projectRoot is null ? null : Path.Combine(projectRoot, "index.html");
                if (entry is not null && File.Exists(entry))
                    previewPath = "/api/preview/" + Path.GetRelativePath(workspaceRoot, entry).Replace('\\', '/');
            }

            assignmentLog.Complete(logEntry, result.Success, result.FinalAnswer, previewPath);
            logCompleted = true;

            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { final = result, previewPath })}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            return Results.Empty;
            }
            finally
            {
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
            PersistentSettingsStore settingsStore, AssignmentLog assignmentLog, CancellationToken ct) =>
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

            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";
            // Same leading worker-frame contract as the Host's /api/assignment,
            // so the dashboard's plan runner works identically here.
            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { worker = new { id = "local", name = settings.NodeName } })}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            return await RunAssignmentAsync(req, ctx, provider, settings, httpFactory, hostLocator, settingsStore, assignmentLog, ct);
        });

        // Persistent uppdragshistorik för DEN HÄR nodens körningar - samma
        // PascalCase-form som SSE-framarna (se HostRole:s motsvarighet).
        app.MapGet("/api/assignment-log", (AssignmentLog assignmentLog) =>
            Results.Text(JsonSerializer.Serialize(assignmentLog.Snapshot()), "application/json"));

        // "Öppna resultatet": serverar det senast byggda projektet direkt
        // från nodens arbetsyta så prompt -> spelbart spel blir ETT klick.
        // Utan entré-redirecten fick användaren själv leta upp mappen på
        // disk och dubbelklicka index.html.
        app.MapGet("/api/preview", (NodeSettings settings) => PreviewEntry(settings));
        app.MapGet("/api/preview/{**path}", (string path, NodeSettings settings) => ServePreviewFile(settings, path));
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

    static IResult ServePreviewFile(NodeSettings settings, string? path)
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
