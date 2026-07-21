> **ARKIVERAD (2026-07-21).** Den levande roadmapen är nu
> [`docs/ROADMAP.md`](ROADMAP.md), och överlämningen till nya sessioner är
> [`CLAUDE.md`](../CLAUDE.md). Den här filen behålls bara som historik
> ("Done"-sektionerna nedan är load-bearing) — lägg inte till ny backlog här.

# AiLocal Plan

This file tracks what exists, what's broken, and what's next. "Done" sections
are load-bearing history - don't re-litigate them. Everything else is a
backlog, roughly ordered by priority within each section.

## Status snapshot (what already works)

- Single exe, three cluster roles (Host/Worker/Overseer) plus a Launcher/desktop
  shell, all sharing one ASP.NET Core process model.
- LAN discovery (UDP multicast) with explicit `--host` override.
- Provider fallback chain: Anthropic -> Gemini -> Ollama, with cooldowns on
  quota/auth/rate-limit failures.
- Hardware inspection and local-model recommendation.
- Capability-based task delegation: the Host scores Workers by skill, hardware,
  provider access, and load, and assigns the hardest subtasks to the
  strongest, least-busy Workers.
- AI task planner (goal -> rated subtasks) with a heuristic fallback.
- Worker-side synthesis step so the Host stays a coordinator, not a compute node.
- Dedicated network topology tab (Overseer -> Host -> Worker), click-through
  node settings/history/removal.
- Durable Host state (nodes, tasks, chat, blocked memberships) with atomic
  writes and backup recovery; survives a crash or reboot.
- Secure-by-default cluster pairing: auto-generated token, mandatory on all
  node-to-node calls, UI to view/copy/regenerate, Quickstart auto-pairs its
  co-located Worker.
- One-click local AI setup (install Ollama, start it, pull the recommended
  model) and an optional "start with Windows" toggle per node.
- Light/dark mode, responsive layout, encrypted API keys and cluster token at
  rest (ASP.NET Data Protection).
- Crash logging to `%LOCALAPPDATA%\AiLocal\logs\`.

---

## P0 - Correctness bugs (fix before anything else)

These aren't missing features, they're wrong behavior today.

1. **Chat has no memory.** Every chat message becomes a brand-new,
   context-free `AgentTask` (`chat.Messages.Add(new ChatMessage("user",
   task.Prompt))` is the *only* message ever sent - see `HostRole.DispatchAsync`).
   The UI looks like a conversation, but the model never sees prior turns.
   Follow-up questions ("what did you just say about X") silently fail.
   **Fix:** thread the ChatBoard's prior turns (or a summarized window of
   them) into every new task's `ChatRequest.Messages`.

2. **No task queueing - cluster overload silently reassigns instead of
   waiting.** `PlanAndDispatchAsync` picks
   `rankedMatches.FirstOrDefault(candidate => candidate.HasCapacity) ??
   rankedMatches[0]` - when *no* Worker has a free slot, it dispatches to the
   top-ranked Worker anyway, ignoring `MaxConcurrentTasks`. A burst of goals
   on a small cluster will pile work onto an already-saturated Worker instead
   of queueing until a slot frees up.
   **Fix:** add a pending-task queue; only dispatch when a Worker actually has
   capacity, and surface queue depth in the dashboard.

3. **No per-task timeout on the Worker side.** The Host enforces a 5-minute
   HTTP timeout on dispatch (`DispatchAsync`), but a Worker's own call into
   Ollama/Anthropic/Gemini has no explicit timeout (`HttpClient` default
   ~100s) and nothing kills a truly hung local model. A wedged Worker can
   occupy a capacity slot indefinitely with no way to reclaim it short of
   restarting the process.
   **Fix:** explicit, configurable per-provider timeout; a stuck task should
   fail and free the slot, not hang forever.

4. **Unbounded in-memory + on-disk history.** `TaskBoard`/`ChatBoard` never
   prune; every task and chat message ever created stays in memory and gets
   rewritten into `host-state.json` in full on *every single mutation*. For a
   cluster that runs for weeks this is a slow memory leak and an
   increasingly expensive fsync on every task state change.
   **Fix:** short-term - cap in-memory history with a rolling window and
   prune old completed tasks from the persisted file; real fix is the SQLite
   migration below.

## P1 - Production hardening

5. **Migrate JSON snapshot store to SQLite** once task/chat volume matters
   (indexed lookups, concurrent-safe writes, no full-file rewrite per
   mutation, cheap pruning/retention policies). *(carried over from before)*
6. **Retry policy per (sub)task.** A Worker crash or transient provider error
   currently fails that subtask permanently and poisons the whole goal's
   synthesis step. Add bounded automatic retry, ideally on a *different*
   Worker if the original one is now offline.
7. **Worker health scoring beyond "online/offline."** Track rolling
   success/failure rate and latency per Worker and factor it into
   `WorkerScorer` - a Worker that's technically Idle but has failed its last
   five tasks shouldn't outrank a slower, more reliable one.
8. **Explicit provider timeouts + circuit breaking**, tied to item 3 above.
9. **TLS / transport security.** Node traffic is authenticated (cluster
   token) but unencrypted. Fine for a trusted home LAN, not fine the moment
   someone runs this over a shared/office network. At minimum: self-signed
   cert + trust-on-first-use, or document an IPSec/VPN wrapper as the
   supported path.
10. **First-run setup wizard inside the exe** - detect Windows version, GPU,
    driver, RAM, admin rights; offer to install Ollama and (if applicable) a
    GPU driver instead of the current silent-if-you-click-the-button flow.
11. **Auto-update mechanism for the exe itself.** Right now "download an exe
    to each computer" has no update story - the user has to notice a new
    build exists, redistribute it, and rerun setup on every machine by hand.
    Even a simple "check a version endpoint, prompt to re-download" beats
    nothing.

## P2 - Missing core features

12. **Task cancellation / pause / resume from the UI.** Once a goal is
    submitted there's no way to stop it short of killing the Worker process.
13. **Streaming output.** Chat responses only appear once the entire task
    (plan -> dispatch -> synthesis) completes; the dashboard polls every 2s
    for a state snapshot. Real token streaming from Worker to Host to
    browser would make the tool feel alive instead of "submit and wait."
14. **Cost/usage tracking wired up.** `ModelCatalog.EstimateCost` exists and
    is *never called*. Every completed task already carries `TokenUsage` -
    surface running cost per task, per day, and per provider in the
    dashboard, plus an optional spending cap that forces fallback to Ollama
    once hit.
15. **File/image attachments in chat.** Everything is plain text today;
    vision-capable providers (Claude, Gemini) are wasted without image input.
16. **"Drain" mode for a Worker.** Let an operator mark a Worker as
    not-accepting-new-work (for planned maintenance/shutdown) without fully
    removing it from the cluster and losing its history/config.
17. **Result export.** Save a completed goal's final answer (and/or the full
    subtask trail) to a file - `.md`/`.docx`/`.pdf` - instead of copy-paste
    from the browser. *(carried over from before)*

## P3 - UX polish

18. Richer Overseer view: task tree per goal (parent + subtasks + synthesis,
    visually nested), live per-Worker logs, not just final state.
19. Desktop notifications (toast/tray) when a long-running goal finishes,
    since the app currently requires keeping the dashboard tab in view.
20. System tray icon for the desktop app (minimize instead of close) -
    `MainForm` today is just a plain window.
21. Saved prompt templates for recurring goals.
22. Replace the remaining browser-styled dialogs/notices with more
    desktop-native affordances where it's cheap to do. *(carried over)*

## P4 - Addons worth considering (bigger, optional)

23. **Cross-platform Workers.** .NET and Ollama both run on Linux/macOS, but
    `AutoStartManager`, the Ollama installer flow, and the desktop shell are
    Windows-only, and `AiLocal.App` targets `net10.0-windows`. A spare Linux
    box or Mac can't join as a Worker today even though nothing about the
    *protocol* requires Windows. Worth scoping: headless `ailocal.exe`
    equivalent for Linux/macOS (systemd/launchd autostart instead of the
    registry Run key), even if the desktop shell stays Windows-only.
24. **Scheduled/recurring goals** (cron-style: "every morning, summarize X").
25. **Role-based access control.** Right now the cluster token is all-or-
    nothing - anyone who has it can do anything (submit goals, remove
    Workers, rotate keys). No concept of separate operator identities or
    read-only viewers.
26. **Multi-Host failover.** The Host is a single point of failure per
    cluster segment; if it goes down, queued/in-flight goals are lost and
    Workers just sit idle until it comes back. Worth a design pass even if
    full HA is out of scope for a LAN hobby tool.
27. **Live resource graphs** (CPU/GPU/RAM over time per Worker), not just the
    one-time hardware snapshot taken at startup.
28. **Pluggable local runtimes beyond Ollama** (LM Studio, llama.cpp server,
    vLLM) behind the same `IChatProvider` abstraction - Ollama isn't the only
    game in town and some GPUs/models run meaningfully better elsewhere.
29. **Config export/import** for fast onboarding of many machines (provider
    priority, skills, token) instead of clicking through Settings on every box.

## P5 - Engineering hygiene (currently at zero)

30. **No automated tests exist anywhere in the repo.** Nothing covers
    `WorkerScorer`'s matching math, the fallback-provider cooldown logic, the
    task planner's JSON parsing/fallback path, or the settings-store
    encryption round-trip - all of which are exactly the kind of logic that
    silently breaks under refactoring. Start with a unit-test project for
    `AiLocal.Core` (pure logic, no ASP.NET/network dependencies to fake).
31. **No CI.** No GitHub Actions/pipeline builds the solution or runs tests
    on a push. Regressions are only caught by whoever happens to run
    `dotnet build` manually.
32. **No structured logging/metrics endpoint.** Console-only `ILogger`
    output today; a `/metrics` (or even a simple `/api/stats`) endpoint -
    uptime, tasks completed, tokens spent, current queue depth - would make
    the "is my cluster actually working" question answerable without staring
    at the dashboard.

---

## Suggested next milestone

Given where the app is, the highest-leverage next slice is **P0 fully, then
P1 items 6-8** - the correctness bugs are the kind of thing that erodes trust
fastest (a chat that forgets what you said, a cluster that silently
overloads one machine instead of queueing), and retry/health-scoring turns
"it usually works" into "it reliably works" before adding any more surface
area. Everything in P2-P4 is genuinely worth doing but is additive; P0/P1 is
fixing the foundation the additive stuff will stand on.
