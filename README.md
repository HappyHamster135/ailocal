# AiLocal

AiLocal is a Windows desktop app for a LAN AI cluster where one executable can run as:

## Download

Grab the latest build from the [Releases](../../releases) page - no build step required:

- `ailocal-app.exe` - the desktop app (recommended). Just run it.
- `ailocal.exe` - headless/server build, same engine, browser-based UI.

Copy the file to any Windows PC and run it; each machine picks its role (Host, Worker, or Overseer) from the app itself.

- `Launcher` - desktop UI with three buttons for starting Host, Worker, or Overseer.
- `Host` - registers Workers, tracks tasks, and delegates work.
- `Worker` - executes delegated AI requests through a provider fallback chain.
- `Overseer` - provides an operator view for submitting goals and watching the cluster.

The current version is intentionally small, but it already has the core shape:

- LAN discovery with UDP multicast.
- HTTP APIs between nodes.
- Worker heartbeat registration.
- Provider fallback: Anthropic -> Gemini -> Ollama by default.
- Desktop UI for role selection, chat, cluster status, tasks, and provider order.
- Live Host/Overseer view of every registered Worker, including online state, active tasks, hardware, model, and provider chain.
- Remote Worker settings for node name, discovery, provider order, models, Ollama, token limits, and API keys.
- Persistent role-specific settings and stable Worker identity across restarts.
- API keys encrypted at rest with ASP.NET Core Data Protection.
- Local hardware inspection and Ollama model recommendation.
- One-click local AI setup on a Worker: install Ollama (winget or official installer), start it, and pull the recommended model.
- Company-style delegation: each Worker has editable specialties and a concurrency limit; the Host matches skill, difficulty, hardware, providers, and current load.
- AI task planner: the Host splits a goal into independently executable subtasks with skill and complexity requirements (heuristic bullet/`---` split as fallback).
- Worker-side final synthesis: after parallel work completes, a suitable Worker acts as editor and produces the final answer so the Host remains an orchestrator.
- Click a Worker to see its full task history chain, each item expandable to the result or error.
- Dedicated network topology view: Overseer -> Host -> connected Workers, with clickable nodes, settings, history, and removal controls.
- Persistent multi-Host registry: one Overseer aggregates every discovered Host and routes Worker operations to the Host that owns it.
- Optional shared cluster token for node-to-node calls, encrypted at rest and never returned by the settings API.
- Durable Host state for Worker inventory, blocked memberships, tasks, and chat, written atomically with a backup for crash recovery.
- One-click Quickstart from the Launcher: start a Host and a Worker joined to it, then open the Host UI.
- Light and dark mode with a toggle (remembers your choice, follows the OS on first run).
- Secure-by-default cluster pairing: a fresh Host mints its own cluster token automatically; every node-to-node call requires it. Quickstart hands the token to its co-located Worker automatically, so the one-click flow keeps working unchanged.
- View, copy, and regenerate the current cluster token from Settings; a Worker/Overseer joins by pasting that token into its own Settings (or into the launch form when starting one manually).
- Optional "start automatically when you log in" toggle per node, so a reboot after a power outage brings the cluster back without you re-launching anything by hand.
- Day-to-day logs (one rolling file per role, 14-day retention) and unhandled errors are both written to `%LOCALAPPDATA%\AiLocal\logs\` instead of only ever living in a console window nobody's looking at - Host/Worker/Overseer are routinely launched with no console at all (desktop app, autostart-at-login).
- Conversation memory: chat-originated goals include recent turns as context, not just the latest message.
- Real work queue: an overloaded cluster queues a task until a worker has a free capacity slot, instead of force-assigning it.
- Configurable dispatch/provider timeouts and bounded automatic retries, with reassignment to a different worker on retry.
- Worker health scoring (rolling success rate and average latency), not just online/offline, factored into who gets the next task.
- Optional TLS for node-to-node cluster traffic (self-signed, auto-generated certificate; loopback dashboard access is never affected).
- Cancel a queued or in-flight task from the dashboard.
- Streaming responses for single-worker chat goals - tokens appear as they're generated instead of polling.
- Cost tracking: per-task and daily estimated USD cost, visible in the dashboard.
- Scheduled/recurring goals (interval-based or a daily time-of-day), managed from a dedicated dashboard tab.
- Two-tier cluster tokens: an admin token (full control) and an optional operator token (submit/view goals, chat, cancel - no node management or settings).
- Cross-platform Worker engine: `AiLocal.Core`/`AiLocal.Node` build and run on Linux/macOS too, including an automatic Ollama install path for each OS (Windows via winget/installer, Linux via the official install script, macOS via Homebrew). The desktop shell (`AiLocal.App`) is Windows-only.
- Automated tests (`tests/AiLocal.Core.Tests`) and a GitHub Actions CI workflow that builds and tests on both Windows and Linux.
- Click-to-pair: a Host lists Workers it sees on the LAN before they're even paired; connecting requires an explicit click on both sides, and the cluster token is only exchanged after both have agreed - no copy-pasting required for same-LAN setups. A Worker that was previously removed from the cluster (and would otherwise stay permanently blocked) is automatically un-blocked the moment both sides re-consent this way.
- A Worker's own dashboard shows a live, accurate connected/not-connected indicator for its Host - not just "is an endpoint configured", but the real outcome of its most recent registration attempt, including the specific reason (wrong cluster token, Host unreachable, ...) when it isn't connected.

## Build

```powershell
dotnet build .\src\AiLocal.Node\AiLocal.Node.csproj
```

## Run as a desktop app

Build the Windows desktop shell:

```powershell
dotnet build .\src\AiLocal.App\AiLocal.App.csproj
```

Run it:

```powershell
dotnet run --project .\src\AiLocal.App\AiLocal.App.csproj
```

This opens a normal AiLocal desktop window. The UI is hosted inside the app with WebView2, so you do not need to open a browser or work in a terminal.

The three role buttons start background node processes from the same app executable:

- `Host` starts the coordinator.
- `Worker` starts a local worker and registers it with the Host.
- `Overseer` opens the operator view.

## Run locally with the browser UI

Start the launcher:

```powershell
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj
```

Open:

```text
http://127.0.0.1:5088
```

Use the three buttons at the top to start `Host`, `Worker`, or `Overseer`.

The Host and Overseer interfaces both include:

- a Codex-style message panel for sending goals to the cluster,
- a live Worker list with hardware, status, model, and provider details,
- recent task state,
- provider order controls for `Claude`, `Gemini`, and `Local`,
- local and remote node configuration.

Worker configuration includes free-form specialties such as `coding`, `research`, `writing`, `analysis`, `data`, and `vision`, plus a maximum number of concurrent tasks. Explicit specialties take priority over raw hardware when the Host assigns work.

The provider order is sent with each message. Examples:

- `Claude -> Local`
- `Gemini -> Claude -> Local`
- `Local` only

## Run roles directly

Start a Host:

```powershell
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Host
```

Start a Worker in another terminal:

```powershell
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Worker --host http://127.0.0.1:5080 --port 5081
```

Start an Overseer dashboard:

```powershell
dotnet run --project .\src\AiLocal.Node\AiLocal.Node.csproj -- --role Overseer --host http://127.0.0.1:5080 --port 5082
```

Open the Overseer:

```text
http://127.0.0.1:5082
```

## API keys

API keys can be entered under `Inställningar` in the desktop app. They are encrypted on the computer that runs the configured role. Environment variables are also supported and take priority:

```powershell
$env:ANTHROPIC_API_KEY="..."
$env:GEMINI_API_KEY="..."
```

If remote providers fail because of auth, rate limits, or quota, the fallback chain moves to the next provider. Ollama is the local safety net.

## Ollama runtime

Check a Worker's local runtime status:

```powershell
Invoke-RestMethod http://127.0.0.1:5081/runtime
```

Pull the recommended local model on that Worker:

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:5081/runtime/pull
```

Automatic pulling is disabled by default and can be enabled per Worker in the desktop settings.

## Persistent settings

Each role has its own settings file:

```text
%LOCALAPPDATA%\AiLocal\host.settings.json
%LOCALAPPDATA%\AiLocal\worker.settings.json
%LOCALAPPDATA%\AiLocal\overseer.settings.json
%LOCALAPPDATA%\AiLocal\launcher.settings.json
```

This lets Host and Worker run on the same computer without overwriting each other's configuration.

## Cluster security

A fresh Host generates its own cluster token automatically the first time it runs - the cluster is paired, not open, by default. Node-to-node endpoints (registration, task dispatch, runtime control) always require that token; each computer can still reach its own local dashboard without it.

### Click-to-pair (recommended, no typing)

If the Host and Worker are on the same LAN, they can find each other automatically: a Host lists every not-yet-paired Worker it sees broadcasting on the network under "Upptäckta enheter" in the Cluster panel. Click **Anslut** next to one, and that Worker's own dashboard shows a "Host X vill ansluta" banner with **Anslut**/**Avvisa** buttons. Nothing is trusted until *both* sides click - no token to copy or type. Under the hood: the Host sends a connect request with a random one-time nonce; the cluster token is only ever handed to the Worker after it echoes that nonce back, which only happens once its own operator approves.

### Manual pairing (cross-subnet, or when discovery doesn't reach)

1. On the Host, open `Inställningar` -> `Klustersäkerhet` and copy the current cluster token (there's a show/hide toggle and a Copy button).
2. On the new node, open `Inställningar` and paste the token into "Klistra in en nyckel för att para ihop den här noden", or paste it into the "Klusternyckel" field before clicking `Worker`/`Overseer` to launch it.

Starting a Worker via the Launcher's one-click **Quickstart** button skips both of these entirely - the Launcher fetches the freshly-generated token from the Host it just started and hands it to the co-located Worker automatically.

Rotate the token any time with `Generera ny` in Settings; every node still using the old token is rejected until it is updated. The token is encrypted at rest and never appears in logs. It can also be set via environment variable or CLI flag (useful for scripted/unattended launches), both of which take priority over the stored value:

```powershell
$env:AILOCAL_CLUSTER_TOKEN="use-a-long-random-shared-secret"
# or, on first run only:
ailocal.exe --role Worker --host http://192.168.1.10:5080 --cluster-token "use-a-long-random-shared-secret"
```

Current LAN transport is still plain HTTP (the token travels in a request header, not over TLS), so treat this as LAN-trusted-network security, not Internet-facing security, until transport encryption is added.

An operator token can never read the raw admin cluster token back out of Settings, even though it's allowed to view most other settings - only the admin token itself can see or copy the admin token.

## Start automatically after a reboot

Each node (Host, Worker, Overseer, or the Launcher/desktop app) can register itself to start when you log in to Windows. Toggle "Starta automatiskt vid inloggning" in `Inställningar`. This writes a value under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run` pointing at the exe with its current role/port/name (and Host endpoint, for a Worker/Overseer); unchecking it removes the value. Combined with the durable Host state below, this means a power outage or reboot does not require you to manually restart or reconfigure anything - the machine comes back into the same role with the same cluster membership.

## Publish a single exe

Desktop app:

```powershell
dotnet publish .\src\AiLocal.App\AiLocal.App.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64-desktop
```

The resulting desktop executable is:

```text
dist\win-x64-desktop\ailocal-app.exe
```

That exe can run by itself when copied to another folder. The other files in the publish folder are useful for debugging/configuration, but not required for the default desktop launch.

Headless/server executable:

```powershell
dotnet publish .\src\AiLocal.Node\AiLocal.Node.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true -o .\dist\win-x64
```

The resulting executable is:

```text
dist\win-x64\ailocal.exe
```

## Troubleshooting

**A Worker on another computer never gets a connect request / a Worker shows "host not connected" even after pairing.**
Almost always Windows' network profile. If either machine's network is set to "Public" instead of "Private", Windows Firewall silently drops the inbound connection with no error on either side - it just looks like nothing happened. Check: Windows Settings -> Network & Internet -> (your network) -> Network profile type, and set it to **Private** on both machines. A Worker's own dashboard now shows the real reason it isn't connected (see the connection indicator above) - if it says "unreachable" rather than a token error, this is the most likely cause.

**"invalid or missing cluster token" / a Worker shows "Unauthorized".**
The Worker's stored cluster token doesn't match the Host's current one - usually because the token was regenerated on the Host after the Worker last paired (`Generera ny` in Settings invalidates every node still using the old token). Re-pair the Worker: either click-to-pair again from the Host's "Upptäckta enheter" list, or copy the Host's current token from `Inställningar` -> `Klustersäkerhet` and paste it into the Worker's settings.

**A Worker shows Offline and won't reappear in "Upptäckta enheter" to reconnect.**
Fixed as of the click-to-pair reliability pass - a Worker that goes offline (network blip, restart, expired token) now reappears in the Host's discovered-devices list on its next LAN announcement, ready to re-pair with one click. If you're on an older build, download the latest release.

**Nothing seems to be happening and there's no window to check.**
Host/Worker/Overseer are usually launched without a console window (the desktop app, or autostart-at-login). Check `%LOCALAPPDATA%\AiLocal\logs\` - each role writes its own rolling daily log file there (`host-yyyyMMdd.log`, `worker-...`, `overseer-...`), plus a separate `crash-yyyyMMdd.log` for unhandled exceptions.

## Current limitations

- When no model is reachable, the AI planner falls back to splitting explicit bullet lists or `---` sections.
- Host state is file-backed and crash-recoverable; a future database migration is still recommended for very large clusters.
- The "start automatically at login" toggle is Windows-only (registry Run key); Linux/macOS Workers need their own autostart mechanism (systemd/launchd).
- TLS between nodes is opportunistic and self-signed (transport encryption only, not real server-identity verification) - the shared cluster token remains the actual authentication boundary. Fine for a trusted LAN, not an Internet-facing deployment.
- A node created before cluster tokens existed stays unpaired (open) until you set one by hand - only a *brand-new* Host mints one automatically.
- The update-check mechanism (`/api/update-check`) only compares versions against a manifest URL you host yourself; there is no automatic download-and-replace of the running exe.
- Streaming is real for single-worker chat goals; multi-subtask fan-out goals still resolve via polling (the dashboard doesn't open a live stream for each parallel subtask).
- Cross-platform Worker support (Linux/macOS) builds and passes CI, but has not been exercised on real Linux/macOS hardware - only on Windows.
- Remote provider model ids and pricing should be verified before production use.
