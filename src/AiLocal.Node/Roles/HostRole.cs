using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using AiLocal.Core.Agent;
using AiLocal.Core.Cluster;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Nodes;
using AiLocal.Core.Providers;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;

namespace AiLocal.Node.Roles;

public sealed record SubmitTaskRequest(
    string Prompt,
    string? System = null,
    int? Parallelism = null,
    string? ModelHint = null,
    List<string>? ProviderOrder = null);

public sealed record PlannedWorkItem(
    string Title,
    string Prompt,
    int Complexity = 3,
    string Skill = "general");

public sealed record GoalPlanRequest(string Goal, int? MaxParts = null);

public sealed record ConversationView(
    string Id,
    string Role,
    string Content,
    DateTimeOffset CreatedAt,
    string? TaskId,
    string? State,
    string? Provider,
    string? Model);

public sealed class ConversationEntry
{
    public required string Id { get; init; }
    public required string Role { get; init; }
    public required string Content { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public string? TaskId { get; init; }
}

/// <summary>Thread-safe registry of nodes known to the Host.</summary>
public sealed class WorkerRegistry
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(45);
    private readonly ConcurrentDictionary<string, NodeInfo> _nodes = new();
    private readonly HashSet<string> _blockedNodeIds;
    private readonly HostStateStore _store;

    public WorkerRegistry(HostStateStore store)
    {
        _store = store;
        _blockedNodeIds = new HashSet<string>(
            store.ReadBlockedNodeIds(),
            StringComparer.OrdinalIgnoreCase);

        foreach (var node in store.ReadNodes())
        {
            node.ActiveTasks = 0;
            node.Status = NodeStatus.Offline;
            _nodes[node.Id] = node;
        }
    }

    public bool Upsert(NodeInfo node)
    {
        lock (_blockedNodeIds)
        {
            if (_blockedNodeIds.Contains(node.Id))
                return false;
        }

        node.LastSeen = DateTimeOffset.UtcNow;

        if (_nodes.TryGetValue(node.Id, out var existing))
        {
            // Mutate the existing NodeInfo in place instead of replacing the
            // dictionary entry. WorkerSlotBroker.ClaimAsync hands out a
            // reference to this exact object, and a dispatch holds onto it for
            // the life of the call; its eventual `worker.ActiveTasks--` must
            // land on the same instance AvailableWorkers() will read later.
            // Swapping in a new object on every ~15s heartbeat would orphan
            // that reference mid-dispatch, silently leaking a claimed slot
            // forever (the live object's ActiveTasks would freeze at whatever
            // it was carried forward as, never decremented again).
            existing.Name = node.Name;
            existing.Role = node.Role;
            existing.Endpoint = node.Endpoint;
            existing.TlsEndpoint = node.TlsEndpoint;
            existing.Hardware = node.Hardware;
            existing.Skills = node.Skills;
            existing.MaxConcurrentTasks = node.MaxConcurrentTasks;
            existing.AgentAccess = node.AgentAccess;
            existing.ProviderPriority = node.ProviderPriority;
            existing.LocalModel = node.LocalModel;
            existing.RecommendedModel = node.RecommendedModel;
            existing.Version = node.Version;
            existing.ModelTiers = node.ModelTiers;
            existing.WorkspacePath = node.WorkspacePath;
            existing.LastSeen = node.LastSeen;
            // ActiveTasks is Host-tracked (via WorkerSlotBroker) and never part
            // of a Worker's own heartbeat payload - left untouched here. Status
            // IS always re-derived from it (not just conditionally upgraded) so
            // a successful heartbeat always clears a stale Offline flag left by
            // MarkStale - otherwise a Worker that ever missed one heartbeat
            // window would show Offline forever, even after reconnecting.
            existing.Status = existing.ActiveTasks > 0 ? NodeStatus.Busy : NodeStatus.Idle;
            Persist();
            return true;
        }

        _nodes[node.Id] = node;
        Persist();
        return true;
    }

    public IReadOnlyCollection<NodeInfo> All
    {
        get
        {
            MarkStale();
            return _nodes.Values.OrderBy(n => n.Name).ToList();
        }
    }

    public NodeInfo? Get(string id)
    {
        MarkStale();
        return _nodes.TryGetValue(id, out var node) ? node : null;
    }

    public bool Remove(string id)
    {
        var removed = _nodes.TryRemove(id, out _);
        lock (_blockedNodeIds)
            _blockedNodeIds.Add(id);
        Persist();
        return removed;
    }

    public bool Restore(string id)
    {
        bool restored;
        lock (_blockedNodeIds)
            restored = _blockedNodeIds.Remove(id);
        if (restored) Persist();
        return restored;
    }

    /// <summary>Least-busy online Workers first.</summary>
    public IReadOnlyList<NodeInfo> AvailableWorkers()
    {
        MarkStale();
        return _nodes.Values
            .Where(n => n.Role == NodeRole.Worker && n.Status != NodeStatus.Offline)
            .OrderBy(n => n.ActiveTasks)
            .ThenBy(n => n.LastSeen)
            .ToList();
    }

    private void MarkStale()
    {
        var now = DateTimeOffset.UtcNow;
        var changed = false;
        foreach (var node in _nodes.Values)
        {
            if (now - node.LastSeen > StaleAfter && node.Status != NodeStatus.Offline)
            {
                node.Status = NodeStatus.Offline;
                node.ActiveTasks = 0;
                changed = true;
            }
        }

        if (changed) Persist();
    }

    private void Persist()
    {
        List<string> blocked;
        lock (_blockedNodeIds)
            blocked = [.. _blockedNodeIds];
        _store.SaveNodes(_nodes.Values.OrderBy(node => node.Name), blocked);
    }
}

/// <summary>In-memory task board with bounded retention.</summary>
public sealed class TaskBoard
{
    private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();
    private readonly HostStateStore _store;
    private readonly NodeSettings _settings;

    public TaskBoard(HostStateStore store, NodeSettings settings)
    {
        _store = store;
        _settings = settings;
        foreach (var task in store.ReadTasks())
        {
            // An in-flight task at shutdown is NOT a failure - it was simply
            // interrupted. Mark it Paused so it can be resumed (see
            // ResumeInterruptedAsync), so the "company" keeps its week's work
            // across a reboot instead of losing everything to Failed.
            CoerceRestartState(task);
            _tasks[task.Id] = task;
        }
        Prune();
        Save();
    }

    /// <summary>On Host restart, in-flight tasks (Pending/Queued/Dispatched/
    /// Running) were only interrupted, not failed - coerce them to Paused so
    /// they can be resumed. Terminal states (Completed/Failed/Cancelled) and
    /// already-Paused tasks are left alone. Extracted so the rule is unit-
    /// testable without standing up the whole dispatch pipeline.</summary>
    internal static void CoerceRestartState(AgentTask task)
    {
        if (task.State is TaskState.Pending or TaskState.Queued or TaskState.Dispatched or TaskState.Running)
        {
            task.State = TaskState.Paused;
            task.Error = "Host startades om innan målet hann klaras av.";
            task.CompletedAt = null;
        }
    }

    /// <summary>Root tasks that were in flight when the Host last stopped and
    /// should be picked back up on startup. Children are resumed implicitly
    /// when their parent re-plans.</summary>
    public IReadOnlyList<AgentTask> InterruptedTasks() =>
        _tasks.Values
            .Where(t => t.ParentId is null && t.State == TaskState.Paused)
            .OrderBy(t => t.CreatedAt)
            .ToList();

    public AgentTask Create(string prompt, string? system, string? title = null, string? parentId = null)
    {
        var task = new AgentTask
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            Prompt = prompt,
            System = system,
            Title = title,
            ParentId = parentId
        };
        _tasks[task.Id] = task;
        Save();
        return task;
    }

    public AgentTask? Get(string id) => _tasks.TryGetValue(id, out var t) ? t : null;

    public IReadOnlyCollection<AgentTask> ChildrenOf(string id) =>
        _tasks.Values
            .Where(t => t.ParentId == id)
            .OrderBy(t => t.CreatedAt)
            .ToList();

    /// <summary>Every task ever delegated to a worker, newest first (its history chain).</summary>
    public IReadOnlyCollection<AgentTask> TasksForWorker(string workerId) =>
        _tasks.Values
            .Where(t => t.AssignedWorkerId == workerId)
            .OrderByDescending(t => t.CreatedAt)
            .ToList();

    public IReadOnlyCollection<AgentTask> All =>
        _tasks.Values.OrderByDescending(t => t.CreatedAt).ToList();

    public void Save()
    {
        Prune();
        _store.SaveTasks(_tasks.Values.OrderByDescending(task => task.CreatedAt));
    }

    /// <summary>
    /// Keeps the most recent N *finished* top-level goals (plus their children),
    /// so a long-running Host doesn't grow host-state.json forever. In-flight
    /// work is never pruned regardless of age.
    /// </summary>
    private void Prune()
    {
        var cap = Math.Max(10, _settings.Host.MaxCompletedTasks);
        var finishedRoots = _tasks.Values
            .Where(t => t.ParentId is null && t.State is TaskState.Completed or TaskState.Failed or TaskState.Cancelled)
            .OrderByDescending(t => t.CompletedAt ?? t.CreatedAt)
            .Skip(cap)
            .ToList();

        if (finishedRoots.Count == 0) return;

        foreach (var root in finishedRoots)
        {
            _tasks.TryRemove(root.Id, out _);
            foreach (var child in _tasks.Values.Where(t => t.ParentId == root.Id).ToList())
                _tasks.TryRemove(child.Id, out _);
        }
    }
}

public sealed class ChatBoard
{
    private readonly ConcurrentQueue<ConversationEntry> _messages = new();
    private readonly HostStateStore _store;
    private readonly NodeSettings _settings;

    public ChatBoard(HostStateStore store, NodeSettings settings)
    {
        _store = store;
        _settings = settings;
        foreach (var message in store.ReadMessages().OrderBy(message => message.CreatedAt))
            _messages.Enqueue(message);
        Prune();
    }

    public ConversationEntry AddUser(string content)
    {
        var entry = new ConversationEntry
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            Role = "user",
            Content = content
        };
        _messages.Enqueue(entry);
        Save();
        return entry;
    }

    public ConversationEntry AddAssistant(string content, string? taskId)
    {
        var entry = new ConversationEntry
        {
            Id = Guid.NewGuid().ToString("n")[..8],
            Role = "assistant",
            Content = content,
            TaskId = taskId
        };
        _messages.Enqueue(entry);
        Save();
        return entry;
    }

    public IReadOnlyList<ConversationView> All(TaskBoard tasks)
    {
        return _messages
            .Select(m =>
            {
                var task = m.TaskId is null ? null : tasks.Get(m.TaskId);
                var content = m.Content;
                if (m.Role == "assistant" && task is not null)
                {
                    content = task.State switch
                    {
                        TaskState.Completed => task.Result ?? "",
                        TaskState.Failed => task.Error ?? "Task failed.",
                        TaskState.Cancelled => "Cancelled.",
                        TaskState.Running => "Working...",
                        TaskState.Dispatched => "Dispatched to worker...",
                        TaskState.Queued => "Queued - waiting for a free worker...",
                        _ => "Queued..."
                    };
                }

                return new ConversationView(
                    m.Id,
                    m.Role,
                    content,
                    m.CreatedAt,
                    m.TaskId,
                    task?.State.ToString(),
                    task?.Provider,
                    task?.Model);
            })
            .ToList();
    }

    /// <summary>
    /// The last <paramref name="window"/> resolved turns, for threading as
    /// conversation context into a new chat-originated goal. Only completed
    /// assistant turns are included - "Working..."/error placeholders are
    /// never fed back to the model as if they were real replies.
    /// </summary>
    public IReadOnlyList<ChatMessage> RecentHistoryAsMessages(int window, TaskBoard tasks)
    {
        var result = new List<ChatMessage>();
        foreach (var m in _messages)
        {
            if (m.Role == "user")
            {
                result.Add(new ChatMessage("user", m.Content));
            }
            else if (m.Role == "assistant")
            {
                var task = m.TaskId is null ? null : tasks.Get(m.TaskId);
                if (task?.State == TaskState.Completed && !string.IsNullOrWhiteSpace(task.Result))
                    result.Add(new ChatMessage("assistant", task.Result));
            }
        }

        return result.Count > window ? result.Skip(result.Count - window).ToList() : result;
    }

    private void Save()
    {
        Prune();
        _store.SaveMessages(_messages);
    }

    private void Prune()
    {
        var cap = Math.Max(20, _settings.Host.MaxChatMessages);
        while (_messages.Count > cap)
            _messages.TryDequeue(out _);
    }
}

/// <summary>
/// First-pass planner. It only splits clearly listed work; arbitrary goals stay
/// as one task until a proper AI planning stage is added.
/// </summary>
public static class SimpleTaskPlanner
{
    public static IReadOnlyList<PlannedWorkItem> Plan(SubmitTaskRequest request, int availableWorkers)
    {
        var maxParts = Math.Max(1, Math.Min(request.Parallelism ?? availableWorkers, availableWorkers));
        if (maxParts < 2)
            return [new PlannedWorkItem("Task", request.Prompt)];

        var explicitItems = ExtractBullets(request.Prompt).Take(maxParts).ToList();
        if (explicitItems.Count > 1)
        {
            return explicitItems
                .Select((item, index) => new PlannedWorkItem(
                    $"Part {index + 1}",
                    BuildSubtaskPrompt(request.Prompt, item),
                    EstimateComplexity(item),
                    InferSkill(item)))
                .ToList();
        }

        var sections = request.Prompt
            .Split(["\n---\n", "\r\n---\r\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(maxParts)
            .ToList();

        if (sections.Count > 1)
        {
            return sections
                .Select((section, index) => new PlannedWorkItem(
                    $"Section {index + 1}",
                    BuildSubtaskPrompt(request.Prompt, section),
                    EstimateComplexity(section),
                    InferSkill(section)))
                .ToList();
        }

        return [new PlannedWorkItem("Task", request.Prompt)];
    }

    private static IEnumerable<string> ExtractBullets(string prompt)
    {
        foreach (var rawLine in prompt.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length < 3) continue;

            if (line.StartsWith("- ") || line.StartsWith("* "))
            {
                yield return line[2..].Trim();
                continue;
            }

            var dot = line.IndexOf('.');
            if (dot > 0 && int.TryParse(line[..dot], out _))
            {
                var item = line[(dot + 1)..].Trim();
                if (item.Length > 0) yield return item;
            }
        }
    }

    private static string BuildSubtaskPrompt(string originalPrompt, string item) =>
        $"""
        Original goal:
        {originalPrompt}

        Assigned subtask:
        {item}

        Complete only the assigned subtask. Be concrete and return a result that can be merged with other worker outputs.
        """;

    private static int EstimateComplexity(string text)
    {
        var words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
        var lower = text.ToLowerInvariant();
        var score = words switch
        {
            > 80 => 4,
            > 35 => 3,
            > 12 => 2,
            _ => 1
        };
        if (lower.Contains("architecture") || lower.Contains("analy") || lower.Contains("research") ||
            lower.Contains("säkerhet") || lower.Contains("optimize"))
            score++;
        return Math.Clamp(score, 1, 5);
    }

    private static string InferSkill(string text)
    {
        var lower = text.ToLowerInvariant();
        if (ContainsAny(lower, "code", "coding", "program", "bug", "api", "database", "kod", "utveckl"))
            return "coding";
        if (ContainsAny(lower, "research", "source", "market", "competitor", "undersök", "källa"))
            return "research";
        if (ContainsAny(lower, "write", "copy", "article", "report", "skriv", "text"))
            return "writing";
        if (ContainsAny(lower, "image", "vision", "photo", "bild", "video"))
            return "vision";
        if (ContainsAny(lower, "data", "spreadsheet", "statistics", "csv", "excel", "analys"))
            return "data";
        return "general";
    }

    private static bool ContainsAny(string text, params string[] values) =>
        values.Any(text.Contains);
}

internal sealed record GoalSubmission(AgentTask Root, int Subtasks, IReadOnlyList<string> WorkerNames);

internal sealed class WorkerCallFailedException(string message) : Exception(message);

public static class HostRole
{
    public static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<HostStateStore>();
        services.AddSingleton<WorkerRegistry>();
        services.AddSingleton<TaskBoard>();
        services.AddSingleton<ChatBoard>();
        services.AddSingleton<WorkerSlotBroker>();
        services.AddSingleton<TaskCancellationRegistry>();
        services.AddSingleton<TaskStreamHub>();
        services.AddSingleton<ScheduleStore>();
        services.AddHostedService<ScheduleRunner>();
        services.AddHostedService<HostAutoConnectService>();
    }

    public static void MapEndpoints(WebApplication app)
    {
        app.MapGet("/", () => Results.Content(Dashboard.Html, "text/html"));

        app.MapGet("/cluster/nodes", (WorkerRegistry reg) => Results.Ok(reg.All));

        app.MapPost("/cluster/register", (NodeInfo node, WorkerRegistry reg) =>
        {
            return reg.Upsert(node)
                ? Results.Ok(new { registered = node.Id })
                : Results.Json(
                    new { error = "worker removed from this cluster", nodeId = node.Id },
                    statusCode: StatusCodes.Status403Forbidden);
        });

        // AI review of a Worker's pending file write (see ChangeReviewer):
        // the Worker pauses before writing, sends the raw old/new content
        // here, and this Host's strongest configured model approves or
        // rejects with a reason the small model can act on. Node-only route
        // (/cluster/*), so only token-holding cluster members can ask.
        app.MapPost("/cluster/review-change", async (
            ReviewChangeRequest change, FallbackChatProvider provider, NodeSettings settings, CancellationToken ct) =>
            Results.Ok(await ChangeReviewer.ReviewAsync(provider, settings, change, ct)));

        // PR-style review of a completed isolated task: pulls the task's full
        // diff from the Worker and runs it through the Host's AI code reviewer
        // (ChangeReviewer) as a single diff. The operator then merges or
        // discards based on the verdict - this is the "AI granskar slutdiffen"
        // half of git-isolation, complementing the per-write pre-review.
        app.MapPost("/api/isolation/review", async (
            IsolationReviewRequest req, FallbackChatProvider provider, NodeSettings settings,
            IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.WorkerEndpoint) || string.IsNullOrWhiteSpace(req.TaskId))
                return Results.Problem(detail: "workerEndpoint och taskId krävs", statusCode: StatusCodes.Status400BadRequest);

            var client = httpFactory.CreateClient("cluster");
            using var diffReq = new HttpRequestMessage(HttpMethod.Post, $"{req.WorkerEndpoint.TrimEnd('/')}/execute/isolation/diff")
            {
                Content = JsonContent.Create(new { taskId = req.TaskId })
            };
            using var diffResp = await client.SendAsync(diffReq, ct);
            if (!diffResp.IsSuccessStatusCode)
                return Results.Problem(detail: "Kunde inte hämta diff från Worker.", statusCode: StatusCodes.Status502BadGateway);
            var diffPayload = await diffResp.Content.ReadFromJsonAsync<IsolationDiffResponse>(ct);
            var diff = diffPayload?.Diff ?? "";
            if (string.IsNullOrWhiteSpace(diff))
                return Results.Ok(new { approved = true, reason = "(ingen diff - tasken producerade inga ändringar)", diff });

            var verdict = await ChangeReviewer.ReviewAsync(provider, settings,
                new ReviewChangeRequest("(isolated task diff)", null, diff, req.Goal), ct);
            return Results.Ok(new { approved = verdict.Approve, reason = verdict.Reason, diff });
        });

        app.MapGet("/tasks", (TaskBoard board) => Results.Ok(board.All));
        app.MapGet("/api/tasks", (TaskBoard board) => Results.Ok(board.All));

        app.MapGet("/tasks/{id}", (string id, TaskBoard board) =>
            board.Get(id) is { } task ? Results.Ok(task) : Results.NotFound());

        app.MapGet("/tasks/{id}/children", (string id, TaskBoard board) =>
            Results.Ok(board.ChildrenOf(id)));

        app.MapPost("/tasks/{id}/cancel", CancelTask);
        app.MapPost("/api/tasks/{id}/cancel", CancelTask);

        app.MapGet("/tasks/{id}/stream", StreamTask);
        app.MapGet("/api/tasks/{id}/stream", StreamTask);

        app.MapGet("/api/stats", (TaskBoard board) => Results.Ok(BuildStats(board)));
        app.MapGet("/api/queue", (TaskBoard board) => Results.Ok(new
        {
            queued = board.All.Count(t => t.State == TaskState.Queued),
            inFlight = board.All.Count(t => t.State is TaskState.Dispatched or TaskState.Running)
        }));

        app.MapGet("/chat", (ChatBoard chat, TaskBoard board) => Results.Ok(chat.All(board)));
        app.MapGet("/api/chat", (ChatBoard chat, TaskBoard board) => Results.Ok(chat.All(board)));

        app.MapGet("/api/host", (NodeSettings settings) =>
            Results.Ok(new { host = $"http://127.0.0.1:{settings.Port}" }));
        app.MapGet("/api/nodes", (WorkerRegistry reg) => Results.Ok(reg.All));
        app.MapGet("/api/topology", BuildTopology);
        app.MapGet("/api/nodes/{id}", (string id, WorkerRegistry reg) =>
            reg.Get(id) is { } node ? Results.Ok(node) : Results.NotFound());
        app.MapDelete("/api/nodes/{id}", (string id, WorkerRegistry reg) =>
            reg.Remove(id) ? Results.NoContent() : Results.NotFound());
        app.MapPost("/api/nodes/{id}/restore", (string id, WorkerRegistry reg) =>
            reg.Restore(id) ? Results.Ok(new { restored = id }) : Results.NotFound());
        app.MapGet("/api/nodes/{id}/settings", ProxyWorkerSettings);
        app.MapPut("/api/nodes/{id}/settings", UpdateWorkerSettings);
        app.MapGet("/api/nodes/{id}/runtime", ProxyWorkerRuntime);
        app.MapPost("/api/nodes/{id}/runtime/pull", PullWorkerRuntime);
        app.MapPost("/api/nodes/{id}/runtime/setup", SetupWorkerRuntime);
        app.MapGet("/api/nodes/{id}/tasks", (string id, TaskBoard board) => Results.Ok(board.TasksForWorker(id)));

        // Click-to-pair, no typing: this Host sees Workers on the LAN via
        // their discovery beacon (ClusterHostedService) even before they're
        // registered. Clicking "connect" sends them a request with a random
        // nonce; the Worker's own operator must accept it (WorkerRole) before
        // /pairing/approved below ever gets called back - see PairingCoordinator.
        app.MapGet("/api/discovered-workers", (PairingCoordinator pairing, WorkerRegistry reg) =>
        {
            // Only hide a peer that's actually connected right now. Filtering
            // by "ever registered" instead of "currently online" meant a
            // Worker that went offline (stopped, lost its token, whatever)
            // stayed a permanent registry entry and could never be re-paired
            // through this list again - the Anslut button would simply never
            // come back for it.
            var connected = reg.All
                .Where(n => n.Status != NodeStatus.Offline)
                .Select(n => n.Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var discovered = pairing.Discovered(NodeRole.Worker).Where(p => !connected.Contains(p.Id));
            return Results.Ok(discovered);
        });

        app.MapGet("/api/pairing-status", (PairingCoordinator pairing) => Results.Ok(pairing.PendingOutbound()));

        app.MapPost("/api/discovered-workers/{workerId}/connect", async (
            string workerId, PairingCoordinator pairing, PersistentSettingsStore store, NodeSettings settings,
            IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            var peer = pairing.Get(workerId);
            if (peer is null)
                return Results.NotFound(new { error = "worker not currently visible on the network" });

            var (success, error) = await PairingConnect.SendRequestAsync(peer, pairing, store, settings, httpFactory, ct);
            return success
                ? Results.Ok(new { requested = true, worker = peer.Name })
                : Results.Problem(detail: error, statusCode: StatusCodes.Status502BadGateway);
        });

        // Public (see ClusterSecurity.IsPublic): called back by a Worker only
        // after ITS operator accepted the request above, echoing the same
        // nonce this Host generated. The cluster token is only ever handed
        // over here - after both sides have explicitly consented.
        app.MapPost("/pairing/approved", async (PairingHandshakePayload req, PairingCoordinator pairing,
            PersistentSettingsStore store, HostLocator hostLocator, WorkerRegistry registry,
            IHttpClientFactory httpFactory, ILoggerFactory loggerFactory) =>
        {
            if (!pairing.TryCompleteOutbound(req.PeerId, req.Nonce, out _))
                return Results.Json(new { error = "no matching pairing request" }, statusCode: StatusCodes.Status404NotFound);

            // A Worker removed via "Ta bort fran gruppen" stays on a permanent
            // block list (WorkerRegistry._blockedNodeIds) so it can't silently
            // re-register - that's by design (removeNodeFromCluster's own
            // confirm dialog says as much). But reaching this point means both
            // operators just explicitly re-consented through click-to-pair -
            // the Host clicked Anslut, the Worker's own operator clicked
            // Accept, and the nonce matched - which is a strictly stronger
            // signal than a heartbeat. That supersedes an old removal instead
            // of silently 403ing every register call afterward with no way to
            // fix it from the UI.
            registry.Restore(req.PeerId);

            var token = store.GetClusterToken();
            if (string.IsNullOrWhiteSpace(token))
            {
                // Click-to-pair promises no manual setup - a Host that never
                // got a token (or had it cleared) shouldn't make pairing fail
                // outright; mint one now so the handshake can still complete.
                var oldToken = token; // null/empty - the Host was running open
                store.Update(new SettingsUpdate(RegenerateClusterToken: true), hostLocator);
                token = store.GetClusterToken();

                // Any Worker already registered was relying on that open
                // access - without this, minting a token here would silently
                // lock every one of them out the moment this response lands.
                if (!string.IsNullOrWhiteSpace(token))
                    await NodeWebHost.PropagateTokenToKnownWorkersAsync(
                        oldToken, token, registry, httpFactory, loggerFactory.CreateLogger("token-rotation"));
            }

            if (string.IsNullOrWhiteSpace(token))
                return Results.Problem(detail: "Could not generate a cluster token.", statusCode: StatusCodes.Status500InternalServerError);

            return Results.Ok(new PairingApprovalResponse(token));
        });

        app.MapPost("/chat", SubmitChat);
        app.MapPost("/api/chat", SubmitChat);
        app.MapPost("/tasks", SubmitTask);
        app.MapPost("/api/tasks", SubmitTask);

        // An assignment is fundamentally different from a goal: one Worker
        // works it autonomously (reading/writing files, running commands per
        // its own configured access level) rather than the Host splitting it
        // into subtasks fanned out across several Workers - so this bypasses
        // the task planner/dispatch queue entirely and proxies straight
        // through to whichever connected Worker has agent mode enabled.
        app.MapPost("/api/assignment", async (
            AssignmentRequest req, HttpContext ctx, WorkerRegistry registry,
            IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            // A plan's sequential subtasks pin to one specific Worker (see
            // GoalPlanner) so later steps can see earlier ones' file changes -
            // auto-picking least-busy fresh each call could land different
            // subtasks on different machines with no shared filesystem between
            // them. Falls back to auto-pick when no WorkerId is given (a plain,
            // one-off assignment) or when the pinned Worker is no longer valid.
            var candidate = !string.IsNullOrWhiteSpace(req.WorkerId)
                ? registry.AvailableWorkers().FirstOrDefault(w => w.Id == req.WorkerId && w.AgentAccess != AgentAccessLevel.Off)
                : null;
            candidate ??= registry.AvailableWorkers().FirstOrDefault(w => w.AgentAccess != AgentAccessLevel.Off);
            if (candidate is null)
                return Results.Problem(
                    detail: "No connected Worker has agent mode enabled. Turn it on in that Worker's own Installningar -> Agentlage first.",
                    statusCode: StatusCodes.Status503ServiceUnavailable);

            var client = httpFactory.CreateClient("cluster");

            // Optional git task-isolation: give this assignment its own
            // worktree+branch on the Worker so it can't collide with another
            // task running against the same repo. Created before dispatch,
            // merged/discarded by the operator afterwards (the Worker's
            // /execute/isolation/* endpoints). A failure here falls back to a
            // normal (non-isolated) run rather than blocking the assignment.
            string? workspaceOverride = null;
            if (req.UseIsolation)
            {
                try
                {
                    using var createReq = new HttpRequestMessage(HttpMethod.Post, $"{candidate.PreferredEndpoint}/execute/isolation/create");
                    using var createResp = await client.SendAsync(createReq, ct);
                    if (createResp.IsSuccessStatusCode)
                    {
                        var created = await createResp.Content.ReadFromJsonAsync<IsolationCreated>(ct);
                        workspaceOverride = created?.Worktree;
                    }
                }
                catch { /* isolation is best-effort; fall through un-isolated */ }
            }

            using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, $"{candidate.PreferredEndpoint}/execute/assignment")
            {
                Content = JsonContent.Create(req with { WorkspaceOverride = workspaceOverride })
            };

            HttpResponseMessage upstreamResponse;
            try
            {
                upstreamResponse = await client.SendAsync(upstreamRequest, HttpCompletionOption.ResponseHeadersRead, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: $"Could not reach {candidate.Name}: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
            }

            using var _ = upstreamResponse;
            if (!upstreamResponse.IsSuccessStatusCode)
            {
                var body = await upstreamResponse.Content.ReadAsStringAsync(ct);
                var reason = ExtractErrorReason(body) ?? $"HTTP {(int)upstreamResponse.StatusCode}";
                return Results.Problem(detail: $"{candidate.Name} declined: {reason}", statusCode: StatusCodes.Status502BadGateway);
            }

            ctx.Response.Headers.CacheControl = "no-cache";
            ctx.Response.ContentType = "text/event-stream";
            // Lets the caller learn which Worker actually ran this (relevant
            // when it auto-picked rather than being pinned), so a client
            // orchestrating a multi-step plan can pin the REST of a sequential
            // group to this same Worker.
            await ctx.Response.WriteAsync(
                $"data: {JsonSerializer.Serialize(new { worker = new { id = candidate.Id, name = candidate.Name } })}\n\n", ct);
            await ctx.Response.Body.FlushAsync(ct);
            await using var upstreamStream = await upstreamResponse.Content.ReadAsStreamAsync(ct);
            await upstreamStream.CopyToAsync(ctx.Response.Body, ct);
            return Results.Empty;
        });

        // Turns a free-text goal into a reviewable list of agent subtasks
        // (see GoalPlanner) - a separate step from actually running them
        // (POST /api/assignment, one call per subtask) so the operator sees
        // the plan before anything executes. Planning is a plain chat
        // completion, not file/command access, so any connected Worker can do
        // it - not just agent-enabled ones, which widens the pool and means
        // planning still works even on a cluster with no agent-mode Workers
        // yet (you'd just have nothing able to run the resulting plan).
        app.MapPost("/api/goal-plan", async (
            GoalPlanRequest req, WorkerRegistry registry, IHttpClientFactory httpFactory, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Goal))
                return Results.Problem(detail: "goal text is required", statusCode: StatusCodes.Status400BadRequest);

            var candidate = registry.AvailableWorkers().FirstOrDefault();
            if (candidate is null)
                return Results.Problem(detail: "No connected Worker is available to plan this goal.", statusCode: StatusCodes.Status503ServiceUnavailable);

            var client = httpFactory.CreateClient("cluster");
            var planner = new GoalPlanner(async (chatRequest, token) =>
            {
                using var upstreamRequest = new HttpRequestMessage(HttpMethod.Post, $"{candidate.PreferredEndpoint}/execute")
                {
                    Content = JsonContent.Create(chatRequest)
                };
                using var upstreamResponse = await client.SendAsync(upstreamRequest, token);
                if (!upstreamResponse.IsSuccessStatusCode)
                {
                    var body = await upstreamResponse.Content.ReadAsStringAsync(token);
                    var reason = ExtractErrorReason(body) ?? $"HTTP {(int)upstreamResponse.StatusCode}";
                    return ProviderResponse.Fail(ProviderOutcome.TransientError, reason);
                }

                var chat = await upstreamResponse.Content.ReadFromJsonAsync<ChatResponse>(cancellationToken: token);
                return chat is null
                    ? ProviderResponse.Fail(ProviderOutcome.FatalError, "empty response")
                    : ProviderResponse.Ok(chat);
            });

            var maxParts = Math.Clamp(req.MaxParts ?? 6, 1, 8);
            IReadOnlyList<PlannedSubtask>? plan;
            try
            {
                plan = await planner.PlanAsync(req.Goal, maxParts, ct);
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: $"Could not reach {candidate.Name}: {ex.Message}", statusCode: StatusCodes.Status502BadGateway);
            }

            if (plan is null)
                return Results.Problem(detail: $"{candidate.Name} could not produce a usable plan. Try rephrasing the goal, or write it as a single assignment instead.", statusCode: StatusCodes.Status502BadGateway);

            return Results.Ok(new
            {
                worker = new { candidate.Id, candidate.Name },
                subtasks = plan.Select(p => new { p.Title, p.Description, p.Independent })
            });
        });

        ScheduleApi.MapEndpoints(app);

        // Resume in-flight goals interrupted by a Host restart. They were
        // marked Paused (not Failed) in the TaskBoard constructor, so the
        // company keeps its work - we just re-plan and re-dispatch them.
        app.Lifetime.ApplicationStarted.Register(async () =>
        {
            try
            {
                var board = app.Services.GetRequiredService<TaskBoard>();
                var interrupted = board.InterruptedTasks();
                if (interrupted.Count == 0) return;

                var reg = app.Services.GetRequiredService<WorkerRegistry>();
                var httpFactory = app.Services.GetRequiredService<IHttpClientFactory>();
                var providers = app.Services.GetRequiredService<FallbackChatProvider>();
                var broker = app.Services.GetRequiredService<WorkerSlotBroker>();
                var hub = app.Services.GetRequiredService<TaskStreamHub>();
                var cancellationRegistry = app.Services.GetRequiredService<TaskCancellationRegistry>();
                var settings = app.Services.GetRequiredService<NodeSettings>();
                var log = app.Services.GetRequiredService<ILogger<TaskBoard>>();

                foreach (var root in interrupted)
                {
                    root.State = TaskState.Running;
                    board.Save();
                    var req = new SubmitTaskRequest(root.Prompt, root.System, root.Parallelism);
                    _ = Task.Run(() => PlanAndDispatchAsync(root, req, root.ContextMessages, board, reg,
                        httpFactory, providers, broker, hub, cancellationRegistry, settings,
                        CancellationToken.None, log));
                }
            }
            catch (Exception ex)
            {
                app.Logger.LogWarning(ex, "could not auto-resume interrupted goals");
            }
        });
    }

    private static object BuildStats(TaskBoard board)
    {
        var all = board.All;
        var today = DateTimeOffset.UtcNow.Date;
        var completed = all.Where(t => t.State == TaskState.Completed).ToList();
        var todayCompleted = completed.Where(t => t.CompletedAt?.UtcDateTime.Date == today).ToList();
        return new { today = Aggregate(todayCompleted), allTime = Aggregate(completed) };

        static object Aggregate(IEnumerable<AgentTask> tasks)
        {
            var list = tasks.ToList();
            return new
            {
                tasks = list.Count,
                inputTokens = list.Sum(t => t.Usage.InputTokens),
                outputTokens = list.Sum(t => t.Usage.OutputTokens),
                costUsd = list.Sum(t => t.EstimatedCostUsd ?? 0),
                byProvider = list.Where(t => t.Provider is not null)
                    .GroupBy(t => t.Provider!)
                    .Select(g => new { provider = g.Key, tasks = g.Count(), costUsd = g.Sum(t => t.EstimatedCostUsd ?? 0) })
                    .OrderByDescending(g => g.costUsd)
            };
        }
    }

    private static IResult CancelTask(string id, TaskBoard board, TaskCancellationRegistry cancellationRegistry)
    {
        var task = board.Get(id);
        if (task is null)
            return Results.NotFound(new { error = "task not found" });
        if (task.State is TaskState.Completed or TaskState.Failed or TaskState.Cancelled)
            return Results.Ok(new { cancelled = false, reason = "task already finished" });

        var cancelled = cancellationRegistry.Cancel(id);
        foreach (var child in board.ChildrenOf(id))
            cancelled |= cancellationRegistry.Cancel(child.Id);

        return Results.Ok(new { cancelled });
    }

    private static async Task StreamTask(string id, HttpContext ctx, TaskStreamHub hub, TaskBoard board, CancellationToken ct)
    {
        ctx.Response.Headers.CacheControl = "no-cache";
        ctx.Response.ContentType = "text/event-stream";

        var subscription = hub.Subscribe(id);
        if (subscription is not null)
        {
            try
            {
                await foreach (var delta in subscription.WithCancellation(ct))
                {
                    await ctx.Response.WriteAsync($"data: {JsonSerializer.Serialize(new { delta })}\n\n", ct);
                    await ctx.Response.Body.FlushAsync(ct);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        var task = board.Get(id);
        var payload = JsonSerializer.Serialize(new
        {
            done = true,
            state = task?.State.ToString(),
            result = task?.Result,
            error = task?.Error
        });
        await ctx.Response.WriteAsync($"data: {payload}\n\n", ct);
        await ctx.Response.Body.FlushAsync(ct);
    }

    private static IResult BuildTopology(
        NodeSettings settings,
        PersistentSettingsStore persistentSettings,
        WorkerRegistry registry)
    {
        var hostId = $"host-{persistentSettings.NodeId}";
        var operatorId = "overseer-local";
        var nodes = new List<object>
        {
            new
            {
                id = operatorId,
                name = "Overseer",
                role = NodeRole.Overseer.ToString(),
                status = "Online",
                endpoint = (string?)null,
                activeTasks = 0,
                skills = Array.Empty<string>()
            },
            new
            {
                id = hostId,
                name = settings.NodeName,
                role = NodeRole.Host.ToString(),
                status = "Online",
                endpoint = $"http://127.0.0.1:{settings.Port}",
                activeTasks = 0,
                skills = Array.Empty<string>()
            }
        };
        var edges = new List<object>
        {
            new { source = operatorId, target = hostId }
        };

        foreach (var worker in registry.All)
        {
            nodes.Add(new
            {
                id = worker.Id,
                name = worker.Name,
                role = worker.Role.ToString(),
                status = worker.Status.ToString(),
                endpoint = worker.Endpoint,
                activeTasks = worker.ActiveTasks,
                skills = worker.Skills
            });
            edges.Add(new { source = hostId, target = worker.Id });
        }

        return Results.Ok(new { nodes, edges });
    }

    private static async Task<IResult> ProxyWorkerSettings(
        string id,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct) =>
        await ProxyGet(id, "/api/settings", reg, httpFactory, ct);

    private static async Task<IResult> ProxyWorkerRuntime(
        string id,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct) =>
        await ProxyGet(id, "/runtime", reg, httpFactory, ct);

    private static async Task<IResult> UpdateWorkerSettings(
        string id,
        SettingsUpdate update,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        if (reg.Get(id) is not { } node)
            return Results.NotFound(new { error = "worker not found" });

        try
        {
            var client = httpFactory.CreateClient("cluster");
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.PutAsJsonAsync($"{node.Endpoint}/api/settings", update, ct);
            return await ForwardResponse(response, ct);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    /// <summary>Pulls a human-readable reason out of a failed response body -
    /// either a ProblemDetails "detail" field or a plain {"error": "..."}
    /// shape (both are used across this app's endpoints) - mirrors
    /// WorkerRole's own copy of the same small helper.</summary>
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

    private static async Task<IResult> PullWorkerRuntime(
        string id,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        if (reg.Get(id) is not { } node)
            return Results.NotFound(new { error = "worker not found" });

        try
        {
            var client = httpFactory.CreateClient("cluster");
            client.Timeout = TimeSpan.FromHours(2);
            using var response = await client.PostAsync($"{node.Endpoint}/runtime/pull", null, ct);
            return await ForwardResponse(response, ct);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> SetupWorkerRuntime(
        string id,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        if (reg.Get(id) is not { } node)
            return Results.NotFound(new { error = "worker not found" });

        try
        {
            var client = httpFactory.CreateClient("cluster");
            client.Timeout = TimeSpan.FromHours(2);
            using var response = await client.PostAsync($"{node.Endpoint}/runtime/setup", null, ct);
            return await ForwardResponse(response, ct);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ProxyGet(
        string id,
        string path,
        WorkerRegistry reg,
        IHttpClientFactory httpFactory,
        CancellationToken ct)
    {
        if (reg.Get(id) is not { } node)
            return Results.NotFound(new { error = "worker not found" });

        try
        {
            var client = httpFactory.CreateClient("cluster");
            client.Timeout = TimeSpan.FromSeconds(15);
            using var response = await client.GetAsync($"{node.Endpoint}{path}", ct);
            return await ForwardResponse(response, ct);
        }
        catch (Exception ex)
        {
            return Results.Problem(ex.Message, statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static async Task<IResult> ForwardResponse(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        var contentType = response.Content.Headers.ContentType?.ToString() ?? "application/json";
        return Results.Content(body, contentType, statusCode: (int)response.StatusCode);
    }

    private static IResult SubmitTask(SubmitTaskRequest req, TaskBoard board, WorkerRegistry reg,
        IHttpClientFactory httpFactory, FallbackChatProvider providers, WorkerSlotBroker broker,
        TaskStreamHub streamHub, TaskCancellationRegistry cancellationRegistry, NodeSettings settings,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return Results.BadRequest(new { error = "prompt is required" });

        var log = loggerFactory.CreateLogger("host");
        var submission = SubmitGoal(req, null, board, reg, httpFactory, providers, broker, streamHub,
            cancellationRegistry, settings, log);
        return Results.Ok(new
        {
            submission.Root.Id,
            state = submission.Root.State.ToString(),
            error = submission.Root.Error,
            subtasks = submission.Subtasks,
            workers = submission.WorkerNames
        });
    }

    private static IResult SubmitChat(SubmitTaskRequest req, ChatBoard chat, TaskBoard board, WorkerRegistry reg,
        IHttpClientFactory httpFactory, FallbackChatProvider providers, WorkerSlotBroker broker,
        TaskStreamHub streamHub, TaskCancellationRegistry cancellationRegistry, NodeSettings settings,
        ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(req.Prompt))
            return Results.BadRequest(new { error = "message is required" });

        var log = loggerFactory.CreateLogger("host");
        var contextMessages = chat.RecentHistoryAsMessages(settings.Host.ChatHistoryWindow, board);
        var user = chat.AddUser(req.Prompt);
        var submission = SubmitGoal(req, contextMessages, board, reg, httpFactory, providers, broker, streamHub,
            cancellationRegistry, settings, log);
        var assistant = chat.AddAssistant(
            submission.Root.State == TaskState.Failed ? submission.Root.Error ?? "Task failed." : "Queued...",
            submission.Root.Id);

        return Results.Ok(new
        {
            userId = user.Id,
            assistantId = assistant.Id,
            taskId = submission.Root.Id,
            state = submission.Root.State.ToString(),
            subtasks = submission.Subtasks,
            workers = submission.WorkerNames
        });
    }

    internal static GoalSubmission SubmitGoal(
        SubmitTaskRequest req, IReadOnlyList<ChatMessage>? contextMessages, TaskBoard board, WorkerRegistry reg,
        IHttpClientFactory httpFactory, FallbackChatProvider providers, WorkerSlotBroker broker,
        TaskStreamHub streamHub, TaskCancellationRegistry cancellationRegistry, NodeSettings settings, ILogger log)
    {
        var workers = reg.AvailableWorkers();
        var root = board.Create(req.Prompt, req.System, "Goal");
        root.Parallelism = req.Parallelism;

        if (workers.Count == 0)
        {
            root.State = TaskState.Failed;
            root.Error = "no workers available";
            root.CompletedAt = DateTimeOffset.UtcNow;
            board.Save();
            return new GoalSubmission(root, 0, []);
        }

        // The Host acts as a manager: plan + delegate happen off the request
        // thread so the caller gets an immediate ack and the workers do the work.
        root.State = TaskState.Running;
        board.Save();
        var rootCts = cancellationRegistry.GetOrCreate(root.Id);
        _ = Task.Run(() => PlanAndDispatchAsync(root, req, contextMessages, board, reg, httpFactory, providers,
            broker, streamHub, cancellationRegistry, settings, rootCts.Token, log));
        return new GoalSubmission(root, 0, workers.Select(w => w.Name).Distinct().ToList());
    }

    /// <summary>
    /// Manager loop: decompose the goal (AI planner, heuristic fallback), then
    /// claim an actual worker capacity slot for each unit of work (queueing if
    /// the cluster is at capacity instead of overloading a busy worker).
    /// </summary>
    private static async Task PlanAndDispatchAsync(
        AgentTask root, SubmitTaskRequest req, IReadOnlyList<ChatMessage>? contextMessages, TaskBoard board,
        WorkerRegistry reg, IHttpClientFactory httpFactory, FallbackChatProvider providers, WorkerSlotBroker broker,
        TaskStreamHub streamHub, TaskCancellationRegistry cancellationRegistry, NodeSettings settings,
        CancellationToken ct, ILogger log)
    {
        var hostSettings = settings.Host;
        try
        {
            var workers = reg.AvailableWorkers();
            if (workers.Count == 0)
            {
                root.State = TaskState.Failed;
                root.Error = "no workers available";
                root.CompletedAt = DateTimeOffset.UtcNow;
                board.Save();
                return;
            }

            var maxParts = Math.Clamp(req.Parallelism ?? workers.Count, 1, Math.Min(workers.Count, 8));

            IReadOnlyList<PlannedWorkItem> plan;
            if (maxParts >= 2)
            {
                var planner = new AiTaskPlanner(providers, settings.Providers.MaxTokens);
                plan = await planner.PlanAsync(req.Prompt, maxParts, ct) ?? SimpleTaskPlanner.Plan(req, workers.Count);
            }
            else
            {
                plan = [new PlannedWorkItem("Task", req.Prompt)];
            }

            // Single unit of work: run the goal itself on the best-matched worker.
            if (plan.Count <= 1)
            {
                var item = plan.Count == 1 ? plan[0] : new PlannedWorkItem("Task", req.Prompt);
                root.Complexity = item.Complexity;
                root.OriginalComplexity = item.Complexity;
                root.RequiredSkill = item.Skill;
                root.ContextMessages = contextMessages?.Count > 0 ? contextMessages.ToList() : null;
                root.State = TaskState.Queued;
                board.Save();

                var requirement = new WorkRequirement(item.Skill, item.Complexity);
                var match = await TryClaimAsync(root, broker, requirement, board, log, ct);
                if (match is null) return;

                await DispatchWithRetryAsync(root, match, req.ModelHint, req.ProviderOrder, board, broker,
                    httpFactory, streamHub, hostSettings, requirement, cancellationRegistry, log);
                return;
            }

            log.LogInformation("planned {Count} subtasks across {Workers} workers", plan.Count, workers.Count);

            // Shared objective state: every child is told the overall goal and
            // the full plan (all subtask titles) so the models pull toward the
            // same objective and each knows how its piece fits the whole -
            // collaboration on one goal instead of N blind parallel chats.
            var objectiveBriefing = BuildObjectiveBriefing(req.Prompt, plan);

            // Claim hardest work first so it gets first pick of the best-suited
            // worker; claiming is fast in-memory bookkeeping (not network I/O),
            // so doing it sequentially here doesn't serialize the actual dispatch
            // calls below, which fire without being awaited.
            var ordered = plan.OrderByDescending(p => p.Complexity).ToList();
            var children = new List<AgentTask>();
            foreach (var item in ordered)
            {
                var child = board.Create(item.Prompt, CombineSystem(req.System, objectiveBriefing), item.Title, root.Id);
                child.Complexity = item.Complexity;
                child.OriginalComplexity = item.Complexity;
                child.RequiredSkill = item.Skill;
                child.State = TaskState.Queued;
                board.Save();
                children.Add(child);

                var requirement = new WorkRequirement(item.Skill, item.Complexity);
                var match = await TryClaimAsync(child, broker, requirement, board, log, ct);
                if (match is null) continue;

                _ = Task.Run(() => DispatchWithRetryAsync(child, match, req.ModelHint, req.ProviderOrder, board,
                    broker, httpFactory, streamHub, hostSettings, requirement, cancellationRegistry, log));
            }

            await CompleteParentAsync(root, children, board, reg, req, httpFactory, broker, streamHub,
                hostSettings, cancellationRegistry, log);
        }
        catch (Exception ex)
        {
            root.State = TaskState.Failed;
            root.Error = ex.Message;
            root.CompletedAt = DateTimeOffset.UtcNow;
            board.Save();
            log.LogWarning("planning failed: {Message}", ex.Message);
        }
    }

    private static async Task<WorkerMatch?> TryClaimAsync(
        AgentTask task, WorkerSlotBroker broker, WorkRequirement requirement, TaskBoard board, ILogger log,
        CancellationToken ct)
    {
        try
        {
            return await broker.ClaimAsync(
                requirement,
                onQueued: () => log.LogInformation("task {Id} queued, waiting for worker capacity", task.Id),
                ct);
        }
        catch (OperationCanceledException)
        {
            task.State = TaskState.Cancelled;
            task.Error = "Cancelled by operator.";
            task.CompletedAt = DateTimeOffset.UtcNow;
            board.Save();
            return null;
        }
    }

    private static async Task DispatchWithRetryAsync(
        AgentTask task, WorkerMatch initialMatch, string? modelHint, List<string>? providerOrder, TaskBoard board,
        WorkerSlotBroker broker, IHttpClientFactory httpFactory, TaskStreamHub streamHub, HostSettings hostSettings,
        WorkRequirement requirement, TaskCancellationRegistry cancellationRegistry, ILogger log)
    {
        var cts = cancellationRegistry.GetOrCreate(task.Id);
        var match = initialMatch;
        try
        {
            while (true)
            {
                await DispatchOnceAsync(task, match, modelHint, providerOrder, board, broker, httpFactory, streamHub,
                    hostSettings, cts.Token, log);

                if (task.State != TaskState.Failed)
                    return;

                // Exhausted cheap-model retries. Before giving up, escalate to a
                // stronger model: bump complexity one notch (capped at 5) so
                // ForTask routes to a bigger model, then retry from scratch.
                if (task.RetryCount >= hostSettings.MaxRetries)
                {
                    if (!TryEscalate(task, hostSettings, log))
                        return;

                    task.State = TaskState.Queued;
                    task.Error = null;
                    board.Save();
                }
                else
                {
                    task.RetryCount++;
                    task.State = TaskState.Queued;
                    task.Error = null;
                    board.Save();
                    log.LogInformation("retrying task {Id} (attempt {Attempt}/{Max})",
                        task.Id, task.RetryCount, hostSettings.MaxRetries);
                }

                var retryMatch = await TryClaimAsync(task, broker, requirement, board, log, cts.Token);
                if (retryMatch is null) return;
                match = retryMatch;
            }
        }
        finally
        {
            cancellationRegistry.Complete(task.Id);
        }
    }

    /// <summary>Escalate a failed task to a stronger model: bump its complexity
    /// one notch (capped at 5, the strongest tier) and reset the retry budget so
    /// the dispatch loop retries from scratch on the bigger model. Returns false
    /// (and leaves the task untouched) once we've escalated MaxEscalations times
    /// or already hit complexity 5 - then the caller gives up for real.</summary>
    internal static bool TryEscalate(AgentTask task, HostSettings hostSettings, ILogger log)
    {
        var canEscalate = task.EscalationCount < hostSettings.MaxEscalations
            && (task.Complexity ?? 3) < 5;
        if (!canEscalate)
            return false;

        task.EscalationCount++;
        task.Complexity = Math.Min(5, (task.Complexity ?? 3) + 1);
        task.RetryCount = 0;
        log.LogInformation(
            "escalating task {Id} to stronger model (escalation {N}, complexity -> {C})",
            task.Id, task.EscalationCount, task.Complexity);
        return true;
    }

    private static async Task DispatchOnceAsync(
        AgentTask task, WorkerMatch match, string? modelHint, List<string>? providerOrder, TaskBoard board,
        WorkerSlotBroker broker, IHttpClientFactory httpFactory, TaskStreamHub streamHub, HostSettings hostSettings,
        CancellationToken taskCt, ILogger log)
    {
        var worker = match.Worker.Node;
        task.AssignedWorkerId = worker.Id;
        task.WorkerName = worker.Name;
        task.WorkerTier = match.Worker.Tier;
        task.WorkerCapability = match.Worker.Capability;
        task.AssignmentReason = match.Reason;
        task.State = TaskState.Running;
        board.Save();

        var startedAt = DateTimeOffset.UtcNow;
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(hostSettings.DispatchTimeoutSeconds));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(taskCt, timeoutCts.Token);

        // Set when the failure means the Worker machine itself is unreachable
        // (connection refused/reset - the home-lab box was shut down or dropped
        // off the network), as opposed to an app-level error the Worker reported.
        // An unreachable node is flagged Offline immediately in the finally so
        // the retry loop fails over to a live Worker at once, instead of being
        // able to re-pick this dead node until MarkStale notices ~45s later. Its
        // next heartbeat clears the flag automatically once it's back.
        var nodeUnreachable = false;

        try
        {
            await RunStreamingDispatchAsync(task, worker, modelHint, providerOrder, httpFactory, streamHub, linked.Token);
            RecordHealth(worker, success: true, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
        }
        catch (WorkerCallFailedException ex)
        {
            task.State = TaskState.Failed;
            task.Error = ex.Message;
            RecordHealth(worker, success: false, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            log.LogWarning("dispatch to {Worker} failed: {Message}", worker.Name, ex.Message);
        }
        catch (OperationCanceledException) when (taskCt.IsCancellationRequested)
        {
            task.State = TaskState.Cancelled;
            task.Error = "Cancelled by operator.";
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            task.State = TaskState.Failed;
            task.Error = $"Timed out after {hostSettings.DispatchTimeoutSeconds}s.";
            RecordHealth(worker, success: false, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            log.LogWarning("dispatch to {Worker} timed out after {Seconds}s", worker.Name, hostSettings.DispatchTimeoutSeconds);
        }
        catch (Exception ex)
        {
            task.State = TaskState.Failed;
            task.Error = ex.Message;
            nodeUnreachable = IsNodeUnreachable(ex);
            RecordHealth(worker, success: false, (DateTimeOffset.UtcNow - startedAt).TotalMilliseconds);
            log.LogWarning("dispatch failed: {Message}{Unreachable}", ex.Message,
                nodeUnreachable ? $" (worker {worker.Name} unreachable - flagged offline)" : "");
        }
        finally
        {
            streamHub.Complete(task.Id);
            task.CompletedAt = DateTimeOffset.UtcNow;
            if (worker.ActiveTasks > 0) worker.ActiveTasks--;
            worker.Status = nodeUnreachable
                ? NodeStatus.Offline
                : worker.ActiveTasks > 0 ? NodeStatus.Busy : NodeStatus.Idle;
            board.Save();
            broker.Release();
        }
    }

    /// <summary>
    /// Calls the Worker's SSE execute endpoint, publishing each delta to
    /// <see cref="TaskStreamHub"/> as it arrives (so a live dashboard tab sees
    /// it immediately) while also accumulating the full result into
    /// <paramref name="task"/> exactly as a buffered call would.
    /// </summary>
    private static async Task RunStreamingDispatchAsync(
        AgentTask task, NodeInfo worker, string? modelHint, List<string>? providerOrder,
        IHttpClientFactory httpFactory, TaskStreamHub streamHub, CancellationToken ct)
    {
        // No explicit model hint from the operator (or the assignment
        // request): pick a (provider, model) for this task from the Worker's
        // per-skill tiers, so a writing task goes to ChatGPT, coding to Claude,
        // private/data to a local Ollama model, etc. - multiple models
        // collaborating on the goal instead of everything hitting one provider.
        var (provider, tieredModel) = worker.ModelTiers.ForTask(task.RequiredSkill, task.Complexity ?? 3);
        var model = modelHint ?? tieredModel;
        var chat = new ChatRequest
        {
            System = task.System,
            ModelHint = model,
            PreferredProvider = provider,
            ProviderOrder = providerOrder
        };
        if (task.ContextMessages is { Count: > 0 } context)
            chat.Messages.AddRange(context);
        chat.Messages.Add(new ChatMessage("user", task.Prompt));

        var client = httpFactory.CreateClient("cluster");
        // The caller already computed the real deadline (DispatchTimeoutSeconds,
        // linked with the operator-cancel token) into `ct` - the "cluster"
        // client's own inherited HttpClient.Timeout (100s default) is a
        // SEPARATE timer that races it independently and can fire first, since
        // .NET enforces HttpClient.Timeout regardless of what token the caller
        // passed in. A large local model that takes >100s to load on first
        // inference would spuriously fail here well before the configured
        // (often much longer) dispatch timeout the operator actually set.
        client.Timeout = Timeout.InfiniteTimeSpan;
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{worker.PreferredEndpoint}/execute/stream")
        {
            Content = JsonContent.Create(chat)
        };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new WorkerCallFailedException(SummarizeWorkerError(response.StatusCode, body));
        }

        var text = new System.Text.StringBuilder();
        ChatResponse? final = null;
        string? failError = null;

        await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(responseStream);
        while (true)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            var json = line[5..].Trim();
            if (json.Length == 0) continue;

            JsonDocument? doc = null;
            try { doc = JsonDocument.Parse(json); }
            catch { /* skip malformed SSE frame */ }
            if (doc is null) continue;

            using (doc)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("delta", out var deltaEl))
                {
                    var piece = deltaEl.GetString() ?? "";
                    if (piece.Length > 0)
                    {
                        text.Append(piece);
                        streamHub.Publish(task.Id, piece);
                    }
                }
                else if (root.TryGetProperty("final", out var finalEl))
                {
                    var success = finalEl.TryGetProperty("success", out var s) && s.GetBoolean();
                    if (success && finalEl.TryGetProperty("response", out var respEl) && respEl.ValueKind != JsonValueKind.Null)
                        final = respEl.Deserialize<ChatResponse>();
                    else
                        failError = finalEl.TryGetProperty("error", out var e) ? e.GetString() : "worker stream failed";
                }
            }
        }

        if (final is null)
            throw new WorkerCallFailedException(failError ?? "worker stream ended without a result");

        task.Result = final.Content.Length > 0 ? final.Content : text.ToString();
        task.Provider = final.Provider;
        task.Model = final.Model;
        task.IsLocal = final.IsLocal;
        task.Usage = final.Usage;
        task.EstimatedCostUsd = ModelCatalog.EstimateCost(final.Model, final.Usage.InputTokens, final.Usage.OutputTokens);
        task.State = TaskState.Completed;
    }

    private static void RecordHealth(NodeInfo worker, bool success, double latencyMs)
    {
        if (success) worker.SuccessCount++; else worker.FailureCount++;
        // Exponential moving average so recent behavior dominates without one
        // slow/fast sample swinging the average wildly.
        worker.AvgLatencyMs = worker.AvgLatencyMs <= 0 ? latencyMs : worker.AvgLatencyMs * 0.7 + latencyMs * 0.3;
    }

    /// <summary>True when the exception means the Worker machine is unreachable
    /// (connection refused/reset/timeout at the socket level, or a stream that
    /// dropped mid-response) rather than an application error the Worker sent
    /// back. Used to flag a dead home-lab node Offline immediately so failover
    /// doesn't keep re-picking it. Walks the inner-exception chain because
    /// HttpClient wraps the real SocketException a couple of layers down.</summary>
    private static bool IsNodeUnreachable(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
        {
            if (e is System.Net.Sockets.SocketException) return true;
            if (e is System.Net.Http.HttpRequestException) return true;
            if (e is IOException) return true;
        }
        return false;
    }

    private static string SummarizeWorkerError(System.Net.HttpStatusCode statusCode, string body)
    {
        var fallback = $"worker returned {(int)statusCode}";
        if (string.IsNullOrWhiteSpace(body))
            return fallback;

        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.TryGetProperty("detail", out var detail) && detail.GetString() is { Length: > 0 } d)
                return d;
            if (root.TryGetProperty("title", out var title) && title.GetString() is { Length: > 0 } t)
                return t;
            if (root.TryGetProperty("error", out var error) && error.GetString() is { Length: > 0 } e)
                return e;
        }
        catch
        {
            // Fall through to raw body summary.
        }

        var compact = body.ReplaceLineEndings(" ").Trim();
        if (compact.Length == 0) return fallback;
        return compact.Length > 240 ? compact[..240] : compact;
    }

    private static async Task CompleteParentAsync(
        AgentTask parent,
        IReadOnlyCollection<AgentTask> children,
        TaskBoard board,
        WorkerRegistry registry,
        SubmitTaskRequest request,
        IHttpClientFactory httpFactory,
        WorkerSlotBroker broker,
        TaskStreamHub streamHub,
        HostSettings hostSettings,
        TaskCancellationRegistry cancellationRegistry,
        ILogger log)
    {
        while (children.Any(c => c.State is TaskState.Pending or TaskState.Queued or TaskState.Dispatched or TaskState.Running))
            await Task.Delay(TimeSpan.FromMilliseconds(500));

        if (children.Count > 0 && children.All(c => c.State == TaskState.Cancelled))
        {
            parent.State = TaskState.Cancelled;
            parent.Error = "Cancelled by operator.";
            parent.CompletedAt = DateTimeOffset.UtcNow;
            board.Save();
            return;
        }

        if (children.Any(c => c.State == TaskState.Failed))
        {
            // If every child failed (even after escalation), the goal failed.
            // But if some children succeeded, deliver what we have - a partial
            // result beats losing the whole week's work. The operator sees
            // which pieces failed and can re-run just those.
            var anySucceeded = children.Any(c => c.State == TaskState.Completed && !string.IsNullOrWhiteSpace(c.Result));
            if (!anySucceeded)
            {
                parent.State = TaskState.Failed;
                parent.Error = string.Join(Environment.NewLine, children
                    .Where(c => c.State is TaskState.Failed or TaskState.Cancelled)
                    .Select(c => $"{c.Title ?? c.Id}: {c.Error ?? c.State.ToString()}"));
                board.Save();
                return;
            }

            parent.Error = string.Join(Environment.NewLine, children
                .Where(c => c.State is TaskState.Failed or TaskState.Cancelled)
                .Select(c => $"{c.Title ?? c.Id}: {c.Error ?? c.State.ToString()}"));
            log.LogWarning("goal {TaskId} partially failed - delivering completed subtasks", parent.Id);
        }

        var usableChildren = children.Where(c => c.State != TaskState.Cancelled).ToList();
        var combined = string.Join(Environment.NewLine + Environment.NewLine, usableChildren.Select(c =>
            $"## {c.Title ?? c.Id}{Environment.NewLine}{c.Result}"));
        var usage = usableChildren.Aggregate(TokenUsage.Zero,
            (sum, c) => new TokenUsage(sum.InputTokens + c.Usage.InputTokens, sum.OutputTokens + c.Usage.OutputTokens));
        var cost = usableChildren.Sum(c => c.EstimatedCostUsd ?? 0);

        // A Worker performs the final synthesis as a separate job. The Host
        // coordinates state and routing, but does not spend its own compute
        // budget rewriting the Workers' output.
        var workers = registry.AvailableWorkers();
        if (workers.Count > 0)
        {
            var synthesis = board.Create(
                BuildSynthesisPrompt(parent.Prompt, usableChildren),
                request.System,
                "Final synthesis",
                parent.Id);
            synthesis.Complexity = 4;
            synthesis.RequiredSkill = "writing";
            synthesis.State = TaskState.Queued;
            board.Save();

            var requirement = new WorkRequirement("writing", 4);
            var match = await TryClaimAsync(synthesis, broker, requirement, board, log, CancellationToken.None);
            if (match is not null)
            {
                await DispatchWithRetryAsync(synthesis, match, request.ModelHint, request.ProviderOrder, board,
                    broker, httpFactory, streamHub, hostSettings, requirement, cancellationRegistry, log);
            }

            if (synthesis.State == TaskState.Completed && !string.IsNullOrWhiteSpace(synthesis.Result))
            {
                parent.Result = synthesis.Result;
                usage = new TokenUsage(
                    usage.InputTokens + synthesis.Usage.InputTokens,
                    usage.OutputTokens + synthesis.Usage.OutputTokens);
                cost += synthesis.EstimatedCostUsd ?? 0;
            }
            else
            {
                parent.Result = combined;
                log.LogWarning(
                    "synthesis task {TaskId} did not complete; returning merged worker output: {Error}",
                    synthesis.Id,
                    synthesis.Error);
            }
        }
        else
        {
            parent.Result = combined;
        }

        parent.State = TaskState.Completed;
        parent.Usage = usage;
        parent.EstimatedCostUsd = cost > 0 ? cost : null;
        log.LogInformation(
            "parent task {TaskId} completed from {Count} subtasks",
            parent.Id,
            children.Count);

        parent.CompletedAt = DateTimeOffset.UtcNow;
        board.Save();
    }

    private static string BuildSynthesisPrompt(
        string originalGoal,
        IReadOnlyCollection<AgentTask> children)
    {
        var sections = children.Select(child =>
        {
            var result = child.Result ?? "";
            if (result.Length > 12000)
                result = result[..12000] + Environment.NewLine + "[truncated]";
            return $"### {child.Title ?? child.Id}{Environment.NewLine}{result}";
        });

        return $"""
        Original goal:
        {originalGoal}

        Worker outputs:
        {string.Join(Environment.NewLine + Environment.NewLine, sections)}

        Produce the final answer for the original goal. Reconcile contradictions,
        remove duplication, and keep the strongest concrete details. Do not mention
        the internal worker process unless the user explicitly asked for it.
        """;
    }

    /// <summary>Shared objective state handed to every subtask so the models
    /// collaborate on one goal: the overall goal plus the full plan (all sibling
    /// subtask titles), with a nudge to stay consistent with the shared
    /// objective and not duplicate a sibling's job.</summary>
    private static string BuildObjectiveBriefing(string goal, IReadOnlyList<PlannedWorkItem> plan)
    {
        var steps = string.Join(Environment.NewLine, plan.Select((p, i) => $"  {i + 1}. {p.Title}"));
        return $"""
        You are one worker in a cluster of AI models collaborating on a single shared goal.

        Overall goal:
        {goal}

        The full plan (each step handled by a worker, possibly a different model):
        {steps}

        Focus on your own assigned task below, but keep your output consistent with
        the overall goal and don't redo another step's work. Your result will be
        merged with the others into one final answer.
        """;
    }

    /// <summary>Prepends the shared objective briefing to any caller-supplied
    /// system prompt so both survive into the child's request.</summary>
    private static string CombineSystem(string? system, string briefing) =>
        string.IsNullOrWhiteSpace(system)
            ? briefing
            : $"{briefing}{Environment.NewLine}{Environment.NewLine}{system}";
}
