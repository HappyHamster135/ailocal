# CLAUDE.md — överlämning till AI-assistenter som jobbar i detta repo

Läs detta FÖRST. Det är den kondenserade erfarenheten från v1.20.0→v1.48.0
(49 releaser byggda av en AI-session tillsammans med ägaren, juli 2026).

## Vad appen är och vad den ska bli

AiLocal är en Windows-app (.NET 10, en enda exe) för ett LAN-kluster av
AI-noder. En exe, fyra roller: **Host** (delegerar, registrerar workers),
**Worker** (kör agentbyggen), **Overseer** (operatörsvy som proxar till
Hosts), **Launcher** (skrivbordsappen, kör noden in-process).

**Produktmålet** (ägarens ord): en svag enrads-prompt ("bygg ett 2d
plattformsspel") ska ge ett resultat på PRODUKTIONSNIVÅ — ljud, animationer,
menyer, polish, spelbar exe — via autonoma agenter, som en riktig spelstudio.
Godot är husmotorn. HTML5 = webbleksaker, tillåts bara när användaren ber om
webb. Ägaren skriver svenska; all UI-text, releasenotiser och commits är på
svenska (commits i ASCII-svenska, utan åäö).

## Arkitekturkartan (var saker bor)

- `src/AiLocal.Node/Roles/WorkerRole.cs` — HJÄRTAT: RunAssignmentAsync är
  hela byggpipelinen, i ordning: uppdragskö (AssignmentQueue, ETT bygge/nod)
  → SSE-keepalive (": ping"/15s, skrivlås) → modellroutning (TaskComplexity
  → ModelTiers.ForTask) → förskaffold (tom yta) ELLER tema-vakt
  (ProjectContext.SeemsUnrelated → nytt projekt i undermapp) ELLER
  kontinuitetsbrief → regissören (DirectorPass + GenreIdeaBank-frön +
  spelkänsla-kriterier) → ev. milstolpsgodkännande → agentkörning
  (RunWithContinuationsAsync: iterationstak som kontrollpunkt + PlanOnly-
  Detector-knuffar) eller TeamBuild → auto-provision godot vid Godot-projekt
  → kvalitetsgrind (AssignmentQualityGate: verify + GamePlaytester +
  regissörens avbockning; max 2 fixrundor; eskalering till stark tier) →
  Godot-autoexport (build_game) → preview-/artefaktvägar → final-frame.
- `src/AiLocal.Node/Roles/Dashboard.cs` — HELA UI:t i en fil (~6500 rader):
  markup + CSS + JS i en raw-sträng. Ändringar HÄR kräver
  `node scripts/check-dashboard.js` (syntax + id-drift) före commit.
- `src/AiLocal.Node/Roles/HostRole.cs` — dispatch (AvailableWorkers sorterar
  på ActiveTasks = least-busy), /api/assignment SSE-forward med final-frame-
  omskrivning (ClusterDelivery), nodlist-endpoints (ALLTID NodeInfo.Redacted()
  — klustertoken får aldrig serialiseras till operatörsnivå), flottuppdatering.
- `src/AiLocal.Node/Roles/OverseerRole.cs` — proxar ALLT dashboarden anropar:
  varje ny Host-endpoint som UI:t använder MÅSTE proxas här, annars 404 för
  Overseer-användare (vanligaste buggklassen historiskt).
- `src/AiLocal.Node/Hosting/` — motorerna: GameScaffoldService(.Genres,
  .GodotKits) med 16 HTML5-kit + 7 Godot-kit (kit = deterministiskt GOLV;
  all variation bor i regissörskontraktet), GamePlaytester (Jint-smoke →
  CDP-sond för webb / GodotWindowProbe för motorspel → vision med titel- +
  mittspelsdump), ToolProvisioner (pinnad katalog; ALDRIG url:er från
  anropare), ToolLocator (rekursiv sökning i tools-katalogen — toppnivå-
  sökning var en riktig bugg), SelfUpdater, TeamBuild, DirectorPass,
  GenreIdeaBank, PersistentSettingsStore (8 kopplingspunkter per fält —
  se mönstret i filen; missad LoadInto = inställning som tyst dör vid
  omstart), AssignmentQueue, AssignmentLog, ClusterDelivery, WindowCapturer.
- `src/AiLocal.Core/` — provider-kedjan, AgentLoop/AgentToolExecutor,
  NodeSettings/ModelTiers, ClusterSecurity (adminnivå = klustertoken,
  operatörsnivå = operatörstoken; /cluster|/execute|/runtime = nod-token).

## Arbetskonventioner (följ EXAKT)

**Releaseprocessen** (varje kodändring som påverkar binärerna):
1. Full verifiering: `dotnet test tests/AiLocal.Core.Tests` +
   `tests/AiLocal.Node.Tests` GRÖNA (Core-sviten två gånger i rad vid
   flaky-misstanke); `node scripts/check-dashboard.js` vid Dashboard-ändring.
2. Bumpa `<Version>` i `Directory.Build.props` (SelfUpdater jämför mot
   GitHub-releasetaggen — fel version = trasig uppdateringskoll).
3. Publicera BÅDA exe: Node → `publish/node/ailocal.exe`, App →
   `publish/app/ailocal-app.exe` (Release, win-x64, self-contained,
   PublishSingleFile, IncludeNativeLibrariesForSelfExtract). Verifiera
   FileVersion på båda.
4. Svensk commit via `git commit -F <fil>` (ALDRIG -m med citattecken i
   PowerShell), avsluta meddelandet med Co-Authored-By-raden för AI:n.
5. `git tag vX.Y.Z` + `git push origin master --tags`.
6. GitHub-release via gh CLI med svensk `--notes-file` + BÅDA exe-filerna.
   Repot MÅSTE förbli publikt (anonym självuppdatering).
7. Påminn ägaren att uppdatera alla noder (pilknappen i klustervyn; noder
   äldre än v1.40 måste uppdateras manuellt).

**Verifieringsdisciplin** ("falskt grönt"-lärdomarna):
- Ett miljöberoende test som passerar på millisekunder har SKIPPAT sig
  självt — verifiera att det faktiskt körde (tidskoll) och inspektera
  artefakter (PNG-innehåll, riktig utdata) själv.
- Riktad körning grön + full svit röd = leta parallellkrock (en RADERARE
  bland egna tester, delade portar/Chromium-profiler), inte logikfel.
- Test-cleanup får ALDRIG härleda målkatalog via GetDirectoryName — spara
  den skapade katalogen explicit (en cleanup raderade en gång hela %TEMP%).
- Godot finns provisionerad i `%LOCALAPPDATA%\AiLocal\tools` på ägarens
  dev-maskin — gated godot-tester kör SKARPT där.

**PowerShell 5.1-fällor** (skalen på denna maskin):
- Inga `&&`; inga ternaries; `git commit -m` med inbäddade citattecken
  spricker (använd -F); testa streaming-proxies med curl.exe (Invoke-
  RestMethod ger falska 400 via Expect: 100-continue); döda scratch-noder
  via port-PID (netstat), ALDRIG `taskkill /IM`.
- Långa kommandon: skriv utdata till fil OCH läs filen (parallella
  processer kan radera harness-utdatafiler).
- Release-PUBLICERINGEN måste köras i PowerShell, ALDRIG via bash-verktyget:
  git-bash manglar `/p:`-switcharna (MSYS-vägkonvertering => MSB1008 "Only one
  project can be specified") och lämnar då TYST kvar gamla exe i publish/ =>
  releasen får fel FileVersion. Verifiera ALLTID FileVersion på båda exe efter
  publicering, före taggen (annars shippas gammal binär med ny version).

**Kod/UI-konventioner:**
- UI: svenska, INGA emoji (SVG-ikonuppsättningen ICONS i Dashboard.cs —
  varje `data-icon` MÅSTE ha en nyckel; testlåst), platt inte blockigt,
  transitions på nya interaktioner, native OS-pickers före egna fält.
- Kit-innehåll (spelkoden i C#-strängar): ASCII-svenska (inga åäö).
- Nya Worker-inställningar: ALLA 8 kopplingspunkter (SettingsUpdate,
  StoredNodeSettings, LoadInto, Read, Update, CopyCurrentIntoStored,
  markup, applySettingsData+saveSettings) — annars dör värdet tyst.
- Delade nod-endpoints (alla roller) mappas i NodeWebHost, inte WorkerRole
  (Launcher mappar inte /execute/assignment).
- Vid buggrapport: identifiera ROLLEN först (App=Launcher in-process;
  Overseer = proxy — saknad proxy är förstamisstanken vid 404).

## Ägarens arbetssätt

Ägaren rapporterar buggar som skärmdumpar + klistrade transkript och svarar
"yes kör X" på förslagslistor. Leverera: rotorsak → fix → test → release →
kort svensk sammanfattning med rotorsaken förklarad. Ärlighet före allt:
degradera öppet (emit-steg som förklarar), hellre "kunde inte" än falskt
grönt. Roadmap och finslipningsplan: `docs/ROADMAP.md`.
