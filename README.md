# AiLocal

**AiLocal turns a one-line prompt into a playable game.** Type *"build a 2D
platformer"* and a cluster of autonomous AI agents — running on your own Windows
PCs — scaffolds, designs, builds, playtests, and exports a real
[Godot](https://godotengine.org/) game to a downloadable `.exe`, the way a small
game studio would: sound, animation, menus, difficulty levels, and a quality
gate the build has to pass before it's handed back to you.

It is a single Windows executable that can run as any of four roles, so one
download becomes a whole LAN render-farm for AI work:

- **Launcher** — the desktop app: pick a role and start it with one click.
- **Host** — registers Workers, queues and delegates jobs, aggregates the fleet.
- **Worker** — runs the actual build pipeline (this is where games get made).
- **Overseer** — an operator view that proxies to every Host it can see.

Godot is the house engine — it's the one with the full verify → playtest →
export chain. HTML5 mini-games are produced only when you explicitly ask for a
*web* game; Unity is supported at a best-effort level (see
[Engines & honesty](#engines--honesty)).

## Download

Grab the latest build from the [Releases](../../releases) page — no build step
required:

- `ailocal-app.exe` — the desktop app (recommended). Just run it.
- `ailocal.exe` — headless/server build, same engine, browser-based UI.

Copy the file to any Windows PC and run it; each machine picks its role from the
app itself. Nodes keep themselves current: a running node compares its version
against the latest GitHub release and can update the whole fleet with one click
(see [Keeping the fleet current](#keeping-the-fleet-current)).

## Screenshots

<!-- Drop PNGs into docs/img/ and reference them here:
     docs/img/dashboard.png          - the operator dashboard with the worker list
     docs/img/build-in-progress.png  - a build streaming its step-by-step progress
     docs/img/delivered-game.png     - a finished game card with the Play / Download buttons -->

_Screenshots live in `docs/img/`. See the three suggested shots in the comment
above._

## How the studio works

When you send a goal like *"gör ett fotbollsmanager-spel"*, the Host picks the
least-busy Worker and that Worker runs one build at a time through this pipeline
(the heart of it is `WorkerRole.RunAssignmentAsync`):

1. **Queue** — each node builds one game at a time. Send three at once and the
   extra two wait in the node's queue instead of trampling each other's files.
2. **Keepalive** — long silent phases (queueing, a 10-minute Godot import, the
   quality gate) emit a `: ping` on the stream every 15 s so proxies and
   browsers don't drop a build that's still working.
3. **Model routing** — the task's estimated complexity chooses the model tier,
   so a throwaway prototype doesn't pay for the strongest model and a hard build
   can escalate to it.
4. **Engine & theme guard** — Godot is chosen for games by default; an
   unrelated new prompt starts a fresh project in its own subfolder rather than
   overwriting the last one, while a follow-up ("make it harder") continues the
   existing game with a continuity brief.
5. **The director** — before any code, a director pass writes a creative
   contract for the game. It's seeded with random picks from a curated
   **idea bank** (so the same prompt never produces the same game twice) and
   with standing **game-feel criteria** — sound per action, visible feedback,
   smooth transitions, difficulty levels that actually feel different — that the
   build is later held to.
6. **Scaffold** — a genre kit lays down a clean, known-good floor (16 HTML5
   kits, 3 Godot kits). The kit is deterministic on purpose; all the variety
   lives in the director's contract, which the agent then builds out.
7. **Build** — an agent (or a small multi-agent team) implements the game,
   auto-provisioning whatever the machine is missing along the way — git, the
   Godot editor, export templates — instead of failing silently.
8. **Quality gate** — the build must pass before it's delivered: the code has to
   compile/parse, a playtester actually *runs* the game (a headless smoke test,
   then a real window/browser probe that presses keys and checks the game
   reacts), and a vision review looks at title and mid-game screenshots. The
   director signs off that its own contract was met. Failures trigger up to two
   fix rounds and can escalate to a stronger model.
9. **Export & deliver** — a passing Godot project is exported to a standalone
   `.exe`, and the game shows up with **Play** and **Download** buttons that
   work from anywhere in the cluster, not just the machine that built it.

Every delivered project is also snapshotted, so a follow-up that makes the game
*worse* can be rolled back from the **Projects** view.

## The cluster

Everything above runs on top of a small, self-contained LAN cluster:

- **Zero-config pairing.** Hosts and Workers discover each other over UDP
  multicast. Click **Anslut** on one side, approve on the other — no token to
  copy. (Manual token pairing is there for cross-subnet setups.)
- **Provider fallback chain.** Each request walks a configurable order —
  Anthropic → Gemini → OpenRouter → local Ollama by default — so a rate-limited
  or down provider degrades to the next instead of failing the build. Ollama is
  the local safety net; a Worker can install it and pull a recommended model in
  one click.
- **Skill-aware delegation.** Workers advertise specialties and a concurrency
  limit; the Host matches skill, difficulty, hardware, provider chain, and
  current load, and queues work when the cluster is full.
- **Live fleet view.** Host and Overseer show every Worker with its online
  state, current task, hardware, model, provider chain, and version.
- **Projects view.** A portfolio of finished games per node — Play, Continue,
  Package, open the folder, restore an earlier snapshot, or delete.
- **Cost tracking** (per-task and daily USD estimates), **scheduled/recurring
  goals**, and **two-tier tokens** (an admin token for full control, an optional
  operator token that can submit and watch goals but not manage nodes).
- **Durable, crash-recoverable Host state** and **stable node identity** across
  restarts, plus optional **autostart at login** so a reboot brings the cluster
  back on its own.

## Keeping the fleet current

A running node compares its version against the latest GitHub release. When a
Worker is behind the Host, the cluster view shows an update arrow: click it and
the Host rolls the update out to every idle node in parallel (busy nodes are
reported and skipped, never interrupted mid-build). Nodes older than the
fleet-update feature (v1.40) have to be updated by hand once.

## Running it locally

The published `ailocal-app.exe` is all most people need. To run from source you
need the [.NET 10 SDK](https://dotnet.microsoft.com/).

Desktop app (WebView2 UI, no browser needed):

```powershell
dotnet run --project .\src\AiLocal.App\AiLocal.App.csproj
```

Browser UI (headless node — open <http://127.0.0.1:5088> and use the three
role buttons at the top):

```powershell
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj
```

Or start a specific role directly:

```powershell
# Host on 5080, then a Worker joined to it, then an Overseer
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Host
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Worker --host http://127.0.0.1:5080 --port 5081
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Overseer --host http://127.0.0.1:5080 --port 5082
```

### API keys

Enter keys under `Inställningar` in the app, or via environment variable
(environment variables take priority and are handy for unattended launches):

```powershell
$env:ANTHROPIC_API_KEY="..."
$env:GEMINI_API_KEY="..."
$env:OPENROUTER_API_KEY="..."
```

Keys are encrypted at rest with ASP.NET Core Data Protection on the machine that
runs the role. If every remote provider fails, the chain falls back to local
Ollama.

### Publish a single exe

```powershell
# Desktop app -> dist\win-x64-desktop\ailocal-app.exe
dotnet publish .\src\AiLocal.App\AiLocal.App.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64-desktop

# Headless/server exe -> dist\win-x64\ailocal.exe
dotnet publish .\src\AiLocal.Node\AiLocal.Node.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64
```

Each resulting exe runs on its own when copied elsewhere; the other files in the
publish folder are optional debugging companions.

## Cluster security

A fresh Host mints its own cluster token the first time it runs — the cluster is
paired, not open, by default. Every node-to-node call (registration, dispatch,
runtime control, remote build delivery, self-update) requires that token; each
machine can still reach its own local dashboard without it. Rotate the token any
time with `Generera ny` in Settings; it's encrypted at rest and never returned
by the settings API or written to logs. An operator token can never read the
admin token back out.

Node-to-node traffic is plain HTTP by default (the token travels in a header),
with **optional** opportunistic self-signed TLS. Treat this as
**LAN-trusted-network** security, not Internet-facing security.

## Engines & honesty

- **Godot** is the house engine and the only one with the complete chain: genre
  kits, self-provisioning of the editor and export templates, a playtester that
  drives the real game window, and a verified export to a playable `.exe`.
- **HTML5** kits exist for quick *web* toys and are only chosen when you ask for
  a web/browser game explicitly.
- **Unity** is **best-effort**: there's a base-level kit, but no
  auto-provisioning (it needs Unity already installed), no genre kits, and no
  tested export chain. Ask for Unity by name (or a 3D game) and you'll get the
  base kit — not the polished Godot pipeline.

A few more things kept deliberately honest:

- The vision review **fails open** without API keys — it still saves the
  screenshots, but it can't judge them, so it won't block a build on looks
  alone when it can't see.
- The `AiLocal.Core`/`AiLocal.Node` engine builds and passes CI on Linux/macOS,
  but has only been exercised on real **Windows** hardware. The desktop shell
  (`AiLocal.App`) is Windows-only.
- Host state is file-backed and crash-recoverable; a database migration is still
  the right move for very large clusters.
- Remote provider model IDs and pricing should be verified before you lean on
  the cost estimates in production.

## Troubleshooting

**A Worker never gets a connect request, or shows "host not connected" after
pairing.** Almost always the Windows network profile. If either machine's
network is **Public**, Windows Firewall silently drops the inbound connection.
Set both to **Private** under Settings → Network & Internet → (your network).

**"invalid or missing cluster token" / "Unauthorized".** The Worker's stored
token no longer matches the Host's — usually because the Host regenerated it.
Re-pair (click-to-pair again, or paste the Host's current token into the
Worker's settings).

**Nothing seems to be happening and there's no window.** Host/Worker/Overseer
usually run without a console (desktop app, autostart). Each role writes a
rolling daily log to `%LOCALAPPDATA%\AiLocal\logs\`, plus a separate
`crash-yyyyMMdd.log` for unhandled exceptions.

## For contributors and AI sessions

- **`CLAUDE.md`** — the condensed handover for anyone (human or AI) working in
  this repo: the product goal, the architecture map, the release process, and
  the hard-won verification and PowerShell lessons. Read it first.
- **`docs/ROADMAP.md`** — the living roadmap: what to polish next and the
  candidate features beyond that.
- **`scripts/check-dashboard.js`** — run before committing any change to
  `Dashboard.cs` (JS syntax + id-drift check).

Tests live in `tests/AiLocal.Core.Tests` and `tests/AiLocal.Node.Tests`; a
GitHub Actions workflow builds and tests on Windows and Linux.
