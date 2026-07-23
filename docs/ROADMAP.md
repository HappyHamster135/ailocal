# ROADMAP — finslipning och nästa horisonter

Skriven 2026-07-21 vid v1.48.0 som överlämning. Två delar: (A) finslipning
av det som finns — konkret, prioriterad, med rotorsak; (B) vad appen hade
kunnat behöva härnäst. Historiken per release ligger i git-taggarnas
releasenotiser (v1.20.0–v1.48.0).

## A. Finslipningsplan (prioriterad)

> **STATUS (uppdaterad 2026-07-22, v1.63.0).** Nästan hela Del A är gjord — läs
> detta innan du gräver i punkterna nedan (de flesta är redan avbockade).
> - **A1** KLART (README omskriven kring "prompt → spelbar exe", v1.49).
> - **A2** KLART (v1.63.0): de sista två varningarna (CS8604 i ScaffoldGodot)
>   borta; NU1510/CS0649/testvarningarna var redan fixade tidigare. `dotnet
>   build` är varningsfri (verifierat med ren ombyggnad).
> - **A3** KLART (kövisibilitet: QueuedCount i NodeInfo/AssignmentQueue/
>   Dashboard, v1.49).
> - **A4** KLART (sjätte benchmarkprompten "bygg ett fotbollsmanager-spel" kör
>   hela godot-kedjan, v1.49).
> - **A5** KLART (gammal-nod-tooltip i klustervyn, v1.49).
> - **A6** git-sidan REN (worktree avregistrerad, ingen `claude/*`-gren) men den
>   FYSISKA mappen `.claude/worktrees/happy-noyce-ecc19f` är låst av en process
>   ("Device or resource busy" — ingen process med sökväg dit hittades, trolig
>   CWD/mount från en parallell session). Kräver omstart av datorn för att tas
>   bort. Ofarlig — påverkar varken app eller repo.
> - **A7** KLART: README dokumenterar Unity som best-effort (rekommendation (a)).
> - **A8** ÖPPET men rör inte förrän Dashboard.cs skaver på riktigt.
> - **A9** KLART: `_remote_assignment.log` borttagen, `docs/PLAN.md` arkiverad
>   (banner → ROADMAP), SfxrGenerator äkta deterministisk (seedades via
>   randomiserad `string.GetHashCode` — nu kategori-index) + LpfSweep inkopplad
>   (v1.63.0).
>
> **Del B (uppdaterad v1.68.0): HELA listan levererad.** B1 (fler godot-kit),
> B7 (webexport), B8 (studiominne) klara sedan v1.55–v1.62. Nya denna omgång:
> **B2** vidareutveckla-knapp (v1.64.0), **B3** speltest-repris som animerad
> PNG (v1.65.0), **B4** delbar leverans med skärmdump + versionsnamn (v1.66.0),
> **B5** öppen kostnadsredovisning (v1.67.0), **B6** klustergalleri (v1.68.0).
> De två uppskjutna uppföljningarna är också KLARA: **B5:s hårda per-uppdrags-
> gräns** ("Max $"-fält i composern → AgentLoop stoppar vid taket, HitCostCap,
> v1.69.0) och **HTML5-reprisen** (CDP-sonden fångar canvas-RGBA via
> getImageData → APNG, v1.70.0). HELA ROADMAP Del A + Del B är därmed
> levererad; enda öppna posterna är A6:s låsta worktree-mapp (kräver omstart)
> och A8 (Dashboard-storlek, parkerad tills det skaver).

### A1. README är 48 releaser gammal (störst synlighet, minst risk)
README.md beskriver appen som "intentionally small" och nämner inget om
uppdragspipelinen, kvalitetsgrinden, genrekiten, spelbara exe-leveranser,
uppdragskön eller flottuppdateringen. Skriv om den kring vad appen ÄR idag:
svag prompt → spelbar Godot-exe, med en skärmdumpssektion och en ärlig
"så funkar kedjan"-översikt. Källa: releasenotiserna v1.33→v1.48.

### A2. Byggvarningarna (30 min, städar varje framtida byggutskrift)
- `NU1510` Microsoft.Win32.Registry i AiLocal.Node.csproj — paketet är
  onödigt i net10.0 (Registry ingår) → ta bort referensen.
- `CS0649` SfxrGenerator.Params (FreqDeltaSlide/DutySweep/EnvAttack aldrig
  satta) → fälten används inte av någon kategori: ta bort dem eller låt en
  kategori använda dem (bättre ljud gratis).
- Testvarningarna: `CS8619` GameBuilderTests-tupeln, `CS8602`
  GameScaffoldHtml5Tests, `xUnit1031` (.Result-blockering i
  GameScaffoldProductionTests) → mekaniska fixar.

### A3. Kövisibilitet i klustervyn (litet, hög vardagsnytta)
AssignmentQueue köar per nod men klustervyn visar bara Busy/aktiva.
Lägg WaitingCount i heartbeaten (NodeInfo) och visa "bygger + N köade"
på workerkortet, så "ställ tre spel på kö" syns utifrån.

### A4. Benchmark-sviten saknar Godot-prompt
De 5 fasta promptarna (ändra ALDRIG befintliga = jämförbarhet) är
HTML5-orienterade. LÄGG TILL en sjätte fast Godot-managementprompt som ny
serie — då mäts hela nya kedjan (kit→grind→sond→exe) per release framåt.

### A5. Gammal-nod-hjälp i klustervyn
Noder < v1.40 kan inte flottuppdateras (saknar /execute/self-update) och
offline-noder nås inte alls. Versionsetiketten vet redan versionen — visa
"för gammal för fjärruppdatering - uppdatera manuellt en gång" som tooltip
i stället för bara rött "(äldre)".

### A6. Kvarlämnad worktree
`.claude/worktrees/happy-noyce-ecc19f` (gren `claude/happy-noyce-ecc19f`)
är en parallellsessions rest; dess ärende (tokenmaskering) släpptes i
v1.42.0. Ta bort med `git worktree remove` + `git branch -D` efter ägarens ok.

### A7. Unity är andra klassens medborgare — bestäm ambitionen
Unity har kit på basnivå men ingen auto-provisionering (kräver installerad
Unity), inga genrekit och ingen testad exportkedja. Antingen (a) dokumentera
ärligt i README att Unity är best-effort, eller (b) lyft den som Godot
lyftes (v1.45–v1.47). Rekommendation: (a) nu, (b) bara om ägaren ber.

### A8. Dashboard.cs växer (~6500 rader)
Fungerar tack vare scripts/check-dashboard.js + diff-guards + ikon-testet,
men filen närmar sig ohanterlig. Om den ska delas: bryt ut JS:en till en
embedded resource-fil FÖRST (minsta risk, behåller single-file-exen), inte
en SPA-ombyggnad. Gör inget förrän det skaver på riktigt.

### A9. Smärre kända skavanker
- `_remote_assignment.log` i repo-roten — gammalt spår; gitignorera/radera.
- `docs/PLAN.md` är den ursprungliga planen — arkivera eller ersätt med
  denna fil som enda levande roadmap.
- SfxrGenerator: seed 7 överallt i godot-kiten — variera per kategori för
  rikare ljudbild (behåll determinism per kit).

## B. Nästa horisonter ("vad mer hade den kunnat behöva?")

I fallande ordning efter bedömt värde för produktmålet:

1. **Fler Godot-genrekit** — pussel och racing är största luckorna
   (promptar i de genrerna får idag plattformarkitet). Samma recept som
   Klubben/Gläntan: rent GDScript, programmatiskt UI, headless-verifierat.
2. **Iterationsknapp på levererade spel** — "gör svårare/lägg till X" på
   ett Projekt-kort som startar uppföljningsuppdrag med kontinuitetsbriefen;
   flödet finns, det saknas en knapp i Projekt-vyn.
3. **Speltest-replay** — sonden sparar redan dumpar; spela in en kort GIF
   (N dumpar över 10 s) och visa i uppdragsresultatet: "så här ser ditt
   spel ut när det spelas", utan att öppna något.
4. **Delbar leverans** — "Dela"-knapp som zip:ar exe + README + skärmdump
   till dist/ med versionsnamn; PackageService finns, saknar UI-flöde.
5. **Kostnadsbudget per uppdrag** — BudgetLimitUsd finns per dag/nod; en
   per-uppdrags-gräns ("max $2 för det här bygget") med öppen redovisning
   i slutresultatet.
6. **Projektgalleri över klustret** — Projekt-vyn visar lokala projekt;
   aggregera alla noders /api/projects i en klustervy med Spela-knappar
   (proxyn från v1.40 gör resten).
7. **Mobil-/webexport av Godot-spel** — godot kan exportera HTML5/Android;
   exportmallarna finns redan nedladdade. "Exportera till webb" per projekt.
8. **Långtidsminne över projekt** — ProjectMemoryEnabled är per arbetsyta;
   en studiogemensam lärdomsbank ("förra fotbollsspelet fick kritik för X")
   som regissören läser. Störst osäkerhet, prototypa smått.

> **STATUS: HELA Del B levererad** (v1.64.0–v1.70.0). B1/B7/B8 var redan klara.
> Del A likaså. Se statusrutan högst upp. Nästa arbete tas från Del C nedan.

## C. Produktionsstudio-horisonter (skiss v1.70.0)

Golvet är stabilt: svag prompt → fungerande, polerat spel. Gapet till
PRODUKTIONSNIVÅ + RIKTIG STUDIO ligger i fem saker; punkterna är grupperade
efter vilket gap de stänger (S/M/L = grov storlek).

> **STATUS: HELA Del C levererad** (v1.72.0–v1.83.0). Alla 13 punkter klara:
> C1 game-feel/juice (v1.72–v1.74, adversariellt granskad), C2 testad balans +
> C3 prestanda-koll (regissörens kriterier + sond), C4 producent-läge (v1.83.0 —
> sekventiell rollpipeline programmerare→konstnär→ljuddesigner med riktiga
> överlämningar, hårdad efter 11-agents-granskning: ärver kostnadstak +
> continuation-skydd, leveransen styrs av kärnbygget), C5 milstolpe-drivet bygge
> (v1.82.0), C6 regressionsskydd, C7 trailer/butikssida + C8 spelbar länk
> (PackageService), C9 Android-export (best-effort, kräver Android SDK), C10
> art-bibel (AssetStyle), C11–C13 djup/tillgänglighet/onboarding (produktions-
> baren). Multi-modell per roll/spår löst (konstnären + hårda team-spår → stark
> tier). Ärliga avgränsningar kvar: full checkpoint/återuppta-över-sessioner
> (C5+), verifierad Android-APK (SDK), fristående cross-modell KOD-granskare.
>
> **Femlistan efter genomgången (v1.84–v1.89, alla KLARA):** v1.84 revisionspass
> (team/regissör in i kostnadsbokföringen + Max$; Overseer proxar operatörs-
> panelerna), v1.85 Pixel Rush → GDScript med juice (ALLA sex Godot-kit nu
> headless-verifierade + juicade; mono-beroendet borta), v1.86 ärlig
> bild-/visionskostnad (räknas + redovisas, prissätts inte), v1.87 återuppta
> avbrutna byggen (loggen bär projektmappen från start; Återuppta-knapp; C5+-
> MVP:n LEVERERAD - operatörsdriven, konversationen återuppstås inte), v1.88
> cross-modell KODgranskare i grinden (läser HELA huvudkoden på starka tiern,
> fail-open - avgränsningen ovan LEVERERAD), v1.89 Host-panelerna göms via
> 404-sond där rollen saknar dem.
>
> **Restlistan (v1.90–v1.92, alla KLARA):** v1.90 VERIFIERAD Android-APK -
> självprovisionerande kedja (provision("android-sdk") = cmdline-tools +
> licenser + platform/build-tools + keystore + standard-Godot + standardmallar;
> mono-editorn BLOCKERAR Android headless), "Bygg APK"-knapp i Projekt-vyn,
> reservkedja zipalign+apksigner när Godots align-steg faller, och TRE grävda
> grundbuggar: cmd-citatstrippningen (>2 citat dödade ALLA godot-exporter i
> alla fem cmd-körare - nu /c "{cmd}"), tysta etc2/astc-valideringen (tomt
> felmeddelande), dubblerad force-adderad projektikon (apksigner-stopp).
> v1.91 visionsanropen PRISSÄTTS (usage+slug ur alla fyra leverantörssvaren →
> usageByModel + Max$; usage-lösa räknas kvar i fotnoten; bildGENERERING
> fortsatt oprissatt - ärligt). v1.92 auto-återupptagning som OPT-IN-inställning
> (default AV; ETT bygge, bara omstartsdödade, 48h-tak, kedjnings-skydd, alla
> 8 kopplingspunkter).
>
> **APK:n EMULATORTESTAD (2026-07-22):** Android 34-emulator provisionerades
> via samma SDK (sdkmanager emulator + system-image; AVD-configens
> image.sysdir behövde rättas - avdmanager i icke-kanonisk layout skriver
> dubblad väg). x86_64-APK:n installerades, startades och VERIFIERADES med
> skärmdumpar: titelskärm (namn/instruktioner/svårighetsknappar/Rekord: 0),
> tryck på "Starta: Lätt" → nivå 1 renderar exakt per nivådatan med HUD
> "HP: 5" (rätt svårighet), spelaren SVARAR på input och fienden patrullerar.
> ÄRLIGT FYND från testet: kitet är tangentbordsstyrt - på en riktig telefon
> utan tangentbord behövs TOUCHKONTROLLER (virtuella knappar) innan spelet är
> mobilspelbart; layouten är också desktop-formad (1152x648 utan stretch).
>
> **v1.93.0: TOUCHKONTROLLER LEVERERADE (ägarens princip: dator är baslinjen).**
> Runtime-gatade (is_touchscreen_available = existerar inte på dator; sonderna
> bevisar noll skillnad) i alla fem action-kit via TouchScreenButton.action →
> samma ui_-actions spelen redan läser (noll logikändringar utom plattformarens
> hopp-flagga); stretch canvas_items/keep (1:1 på dator, skalar på mobil).
> EMULATORVERIFIERAT med granskade skärmdumpar: kontroller syns, "<"/">"
> flyttar spelaren, HOPP ger luftdump MED hoppdamm-partiklarna; produktions-
> ribban fick Mobile-raden. Bonusfynd från skarpt speltest: one_way_collision
> på plattformarna (huvuddunk-fixen, klassisk plattformare). Kvar: riktig
> fysisk enhet, release-signerad butiks-APK, prissatt bildgenerering.
>
> **v1.94–v1.98 (alla KLARA):** v1.94 KOSTNADSSTYRNING - inget kodspår får
> auto-välja Anthropic/dyra modeller (billig-först-prioritet, healade lagrade
> rutter, openrouter-fallback); STÅENDE REGEL. v1.95 teamläget robust (worktree-
> stängsel även för run_command, capped-spår med commits mergas, [ADDRESS]-
> vakter). v1.96 sex luckor (relativa vägar dödar maskering vid källan, session-
> tips → delegera, GdScript-lint, placeholder-vakt, provisioneringslås, Bygg
> APK-knapp). v1.97 kreativ regissör 2.0 (genretänk + förebilder + kreativ
> vinkel per körning + genre-formad fallback). v1.98 KANONADEN - artillerikit
> (ShellShock Live/Worms-klassen): turbaserad duell mot AI-stege (Rekryten/
> Kaptenen/Generalen med minne + provskjutning), FÖRSTÖRBAR pixelterräng med
> kratrar, vind per tur, 3 vapen, fallskada, segersvit-highscore, touch. Första
> versus-kittet - stänger formgapet "alla kit var ensam progression". Visuellt
> verifierat i skarp körning: krater vid träff, AI träffar tillbaka, vind
> växlar. 7 Godot-kit totalt.
>
> **v1.99 KVALITETSPASSET (ägarens skärmdumpar av en levererad build som "inte
> gick att sälja"):** (1) edit_file radslutsoberoende + core.autocrlf=false i
> agentrepon - CRLF/LF-mismatchen brände ~40% av teambyggets tokens på
> feldiagnosen "edit_file klarar inte tabbar". (2) Granskaravslag som citerar
> [ADDRESS] failar open (PII-filtret maskade GILTIG kod i diffen granskaren
> såg - ett spår skrev medvetet sämre kod för att undvika filtret). (3) Tom
> exportfelrad diagnostiseras deterministiskt (presets/mallar) och slutsvaret
> döljer aldrig en exportmiss. (4) ALLA 7 Godot-kit på ENGELSKA (spelartext;
> namn: Club Manager/The Glade/Pixel Rush/The Circuit/Twenty48/The Cube/
> Cannonade) + stående regissörskriterium + AgentSystemPrompt-regel.
> (5) Tangentbordsfokus i alla kit (första knappen per skärm får fokus) -
> Enter fungerar OCH grindens sond kommer förbi titeln på musdrivna spel.
> (6) UX-tripwires i GdScriptLint (rå %d/%s till .text, BBCode i vanlig
> Label) + vision letar TEXTDEFEKT (råa formatsträngar/taggar/datadumpar).
> KVAR (medvetet): språkväljare i spelens settings (i18n-golv med TEXT-dict,
> sv/en-växel) - engelska är nu default; de 16 HTML5-kiten är kvar på
> ASCII-svenska (Godot är husmotorn; regissörskriteriet styr agenten till
> engelska även där).
>
> **v2.0.0 UTVECKLINGSRUNDORNA (ägarens fundamentala krav: "det som var
> finalprodukten ska vara PROTOTYPEN").** Grindens gröna leverans är inte
> längre slutet: PolishPass låter en kritikermodell (billiga Medium-tiern)
> granska det GODKÄNDA spelet - kod + grindens speltest-/visionsbevis - över
> exakt ägarens fyra axlar (STÖRRE innehåll, SNYGGARE, BÄTTRE LJUD, MINDRE
> BUGGIGT) och producera max 6 konkreta byggbara förbättringar; en byggrunda
> utför dem ovanpå historiken; grinden körs om; en försämrande runda
> ÅTERSTÄLLS från snapshot i stället för att skeppas (och grindens gröna
> räknas som lyckat även om modellturen slog i tak - v1.95-lärdomen).
> Inställningen "Utvecklingsrundor efter godkänd prototyp" (0-3, default 1,
> alla 8 kopplingspunkter). Kritik-done-signalen ("spelet håller redan
> måttet") och tomma kritiker avbryter ärligt; Max$-taket respekteras (80%-
> spärr före ny runda). KVAR: fler rundor som default när kostnadsbilden
> bevisat sig; kritik med skärmdumpsbilder som underlag (nu text-bevisen).
>
> **v2.1-v2.2 PARTYSKALAN (mot "riktigt Mario Party/Pummel Party": ärvda
> byggen + färdig-inkoppling).** v2.1 Board Bash (9:e kittet, party-genren):
> bräda 24 rutor/2 layouter, tärning, 4 spelare, 3 minispel, engelska.
> ÄRVDA FEL RÄTTADE FÖRE RELEASE: 6 riktiga GDScript-parse-fel (`:=` på
> otypad indexering + PackedVector2Array*int) som förra passets fokuserade
> tester ALDRIG såg (kitet låg inte i headless-listan - nu gör det det);
> fokus landade på brädval så Enter aldrig startade (nu Easy); "You's turn".
> v2.2 fem motorer - VARAV TRE VAR OINKOPPLAD DÖD KOD, nu riktigt wirade:
> GenreContracts in i grinden MED genre+prompt (nya RequestedMinigames:
> "15 minigames" i prompten => kravet ÄR 15; CountMinigames räknar RIKTIGA
> minispel: minigame_type-grenar, "# Minigame N"-rubriker, Mg*.gd-filer);
> PromptDecomposer in i TeamBuild (ritning i arkitektprompten + dekomposer-
> fallback som buntar minispel 3 per spår => "15 minigames" bygger 9-12 i
> EN teamkörning, resten via kontraktsräknaren i milstolpe-/utvecklings-
> rundorna; 10 minispelsmallar + "uppfinn eget"-uppgifter över mallarna);
> VisualStyleLib in i regissören (palettförslag per genre i prompten +
> konkret identitetskriterium i fallbacken); GameMechanicLibrary =>
> MECHANICS.md i varje Godot-scaffold (beprövade snuttar att klistra in);
> AntiPatternDb var redan wirad (rådgivande). Genreord kompletterade:
> brädspel/partyspel/sällskapsspel/tärning/pummel party/lego party.
>
> **v2.3 BOARD BASH 3D (målets 3D-ben: "riktigt Mario Party/Pummel Party
> i 3D").** 10:e kittet och det FÖRSTA FLERFILSKITTET: Main.gd (3D-bräda
> 24 rutor i ring, tärning, 4 kapselspelare, mynt/stjärnor, 6 ronder,
> kamerashake, 3D-partiklar) + TRE fristående minispel som EGNA filer
> (MgRace3D/MgFall3D/MgCollect3D) enligt kontraktet setup(main) →
> main.minigame_done(rankings). FILKONVENTIONEN ÄR SKALVÄGEN: fler minispel
> = fler Mg*.gd (CountMinigames räknar dem, teamspår bygger dem parallellt,
> DESIGN.md/README dokumenterar kontraktet för agenter). "3d mario party"
> routas hit (inte The Cube). OLIKA LJUD: 9 wav-filer (bas 4 + tärning +
> stjärna + ETT eget ljud per minispel, olika sfxr-kategori/seed).
> Pummel-/Lego-frön i idébanken (sabotage-items, myntstöld, battle-royale-
> minispel, byggminispel, elakt slumphjul, kosmetiska upplåsningar).
> Verifierat: riktig Godot parsar Main + alla tre Mg-filer (--check-only
> per fil - --quit lastar aldrig runtime-laddade skript!), egna skärmdumpar
> bevisar 3D-brädan i spel (tärningsrull, mynt, turordning). KVAR mot
> fullskala: 15-minispelskörningen är TEAM+RUNDOR-driven (golvet ger 3;
> kontraktsräknaren + dekomposern + utvecklingsrundorna bygger resten) -
> nästa naturliga steg är ett skarpt "15 minigames"-klusterbygge live.
>
> **v2.4 LJUD + SEENDE KRITIK (svar på "gör den bättre på att producera
> spel").** (1) BAKGRUNDSMUSIK I ALLA 10 KIT: ChiptuneComposer fanns sedan
> v1.36 men inget Godot-kit SPELADE musik - nu skrivs music.wav centralt i
> scaffold-wrappern (stämning per genre: party=victory, racing/artilleri=
> action, plattform/rpg=exploration, management=calm, pussel=ambient) och
> varje kit loopar den (finished->play, -14 dB under effekterna); testlåst
> i AssertKitComplete. (2) BILDKRITIK I UTVECKLINGSRUNDORNA: PolishPass tar
> sondens titel-/mittspelsdumpar genom visionsmodellen med ett art
> director-pass (tomma ytor, kontrast, saknad identitet) och lägger
> omdömet i kritikerns bevisunderlag - "ser tomt ut" upptäcks ur riktiga
> pixlar i stället för att gissas ur koden; testlåst (omdömet når
> prompten). (3) ATTRACT-AUTOPILOT i båda party-kiten: 8s idle på
> människans tur => spelet rullar självt - partyt stannar aldrig OCH
> grindens sond når hela loopen (bräda -> minispel) utan att kunna
> reglerna. KVAR: tredje sen sond-dump (mitt-i-minispel-bevis), banor/
> kartor-antal ur prompten som mätbart krav (minigames har det redan).
>
> **v2.5 FPS + DEMORUNDOR + LIVE-VY (ägarens godkända fyra).** (1) STRIKE
> ARENA (11:e kittet, genre "fps"): first person på riktigt - musfångst +
> piltangent-titt (sonden kan spela), matematisk siktkontroll (ingen fysik-
> raycast), jagande vågor, ammo/HP-pickups, crosshair, skadeblixt, rekyl;
> "60 fps"/"120fps" = prestandakrav, INTE genre (lookbehind); "3d fps"
> routas hit. Spelat + skärmdumpsverifierat. (2) DEMORUNDOR (inställning,
> default PÅ, lokala körningar): när prototypen är grind-grön webbexporteras
> den och visas som LIVE-VY (iframe) i byggbubblan med 2-3 riktade frågor;
> svaren = byggrunda med HÖGSTA prioritet; runda 2 efter utvecklingsrundorna
> = sista ändringspunkten; 10 min auto-fortsätt; återanvänder milstolps-
> registret/-endpointen (noll ny API-yta). Web-preseten exporterar nu UTAN
> trådar (threads kräver COOP/COEP och blir svartruta i iframe). (3) SEN
> SOND-DUMP: playtest-late.png 6 s senare (attract-autopiloten driver spelet
> djupare) → med i art director-kritiken (Take 3). (4) KARTOR/BANOR MÄTBART:
> RequestedBoards ("3 kartor") → BOARD_*-konstanter/"# Board N"-rubriker
> räknas som hårt krav. KVAR: demorundornas fulla kedja live-testad mot
> nod (endpoint/kort är milstolpskloner, men ett skarpt bygge är beviset).
>
> **v2.6 KRASCHEN + WEBBSPELSBUGGEN (ägarens partytranskript, två rot-
> orsaker).** (1) ALLA FEM providers klassade svarsparse-fel som FATALT -
> OpenRouter svarade 200 med icke-JSON ("The input does not contain any
> JSON tokens") och hela kedjan dog. Nu TRANSIENT (cooldown + nästa
> försök), samma klass som v1.95:s "no choices"; regressionstest med
> HTML-svar. (2) MOTORGARANTIN: icke-tom arbetsyta utan motorprojekt +
> tema-vakt som inte slog till => inget kit => DetectEngine "unknown" =>
> teamspårens filråd blev "egen js-fil" => TRE spår byggde WEBBSPEL i ett
> "i godot"-uppdrag. Nu: (a) WorkerRole scaffoldar genrekittet när ett
> spelbygge med motorprompt saknar motorgolv i roten (fjärde grenen före
> kontinuiteten), (b) TeamBuilds filråd följer PROMPTENS motorval när
> DetectEngine säger unknown. Testlåst: godot-prompt utan golv får aldrig
> js-råd. OBS: ägarens nod körde v2.3.0 under transkriptet - demorundor/
> FPS/musik fanns inte där.
>
> **v2.7-v2.12 ROBUSTHET + PREMIUMGOLVET (sammanfattat).** Säkerhetsnätet
> (varje grind-grönt läge snapshottas; misslyckad slutstatus levererar
> senast godkända bygget), kostnadstak-defaulter, e2e-beviskedjan (isolerad
> gratisnod → 84 MB spelbar exe), scaffold-wrapperns ljud-/konst-/sprite-
> golv i ALLA kit, genrekontraktens mätbara antal ("15 minigames" räknas),
> GENOMBROTTSFYNDET (spelläget importerar aldrig resurser - sonden kör
> `--headless --import` först; alla tidigare sond-/visionsbedömningar såg
> sprite-lösa spel) samt premium-Pixel Rush som arkadgolvets målnivå
> (lagrad värld, 5 designade banor, vittrande plattformar, studsplattor,
> medaljer, volym, övergångar).
>
> **v2.13 FÖRE/EFTER-VAKTEN + AUTOPILOTEN + RELEASE-CHECKLISTAN (steg 2-4
> av "små spel som faktiskt är bra"-planen).** (1) Varje godkänt läge
> sparar en referensdump; efter varje utvecklings-/demorunda dömer visionen
> referens-mot-nu sida vid sida - "SAMRE" ⇒ rundan förkastas och senast
> godkända bygget återställs (fail-open utan visionsnycklar). (2) Sonden
> sätter AILOCAL_AUTOPILOT=1; plattformarkitet spelar sig självt (väntar
> förbi titeldumpen, springer mot flaggan, strålkastar kanter, hoppar,
> omstart efter game over) - sonddumparna visar RIKTIGT spelande (bevisat
> nivå 4/5, 200 p på 19 s). Autopilotens FÖRSTA körning hittade en äkta
> kitbugg: root-nod-screenshake teleporterade fysiken + one-way-mark ⇒
> genomfall genom världen vid varje skak; fix = kameraskak + one-way bara
> på svävande plattor (75 s autonom körning: 0 genomfall). (3) Release-
> checklistan i grinden (rådgivande): omstart, volym/mute, paus, sparat
> highscore, riktig fönstertitel - fynden matas in i kritikrundorna.
> KVAR i planen: autopilot i fler kit (FPS/party har attract-lägen),
> kameraskak i övriga kit med root-shake, CC0-assetpaket via pinnad
> ToolProvisioner-katalog, språkväljare i settings.
>
> **v2.14 SJU ROTORSAKER UR CANDY PARTY-TEAMHAVERIET (ägarens v2.11-
> transkript, 8M tokens in → fail).** (1) COOLDOWN-KASKADEN (huvudfelet):
> transient-cooldown är 5 s men uttömd kedja gav FatalError direkt - alla
> tre redo-rundorna efter merge-konflikt + fixrunda 2 dog på "all providers
> failed: cooling down". Nu VÄNTAR FallbackChatProvider ut korta cooldowns
> (≤90 s, max 3 rundor, injicerbar klocka/delay för test) i Complete- OCH
> Stream-vägen; quota/auth (timmar/kvartar) failar ärligt direkt.
> (2) VÄGÖVERSÄTTNING: teamspår brände 3-6 famlande anrop var på ISOLERAT-
> fel - absoluta huvudrot-vägar översätts nu TYST till worktreen
> (syskonworktrees nekas fortfarande). (3) read_file offset UTAN limit
> int-overflowade ("Non-negative number required" ×3 live). (4) glob/
> search utan path sökte i "." = NODENS cwd (exe-katalogen!) - Full-agenter
> fick alltid "no files match"; default är nu arbetsytan, och utdatan
> relativiseras (maskningsskydd). (5) search med path=EN FIL gav "path not
> found" - söker nu i filen (utan textfilter för explicit fil).
> (6) run_command-MASKVAKT: ett spår "lagade" [ADDRESS] på disk via
> powershell -replace och skrev in trasiga värden - samma facit-block som
> write/edit. (7) ARKITEKTREGELN: tre spår byggde VAR SIN sprite-lösning
> och alla redigerade Main.gd → merge-konflikt ×3; prompten kräver nu att
> varje leverabel ägs av EXAKT ETT spår och att delade filer har en ägare.
> OBS: ägarens nod körde v2.11.0 - import-fyndet (v2.12), vakterna/
> autopiloten (v2.13) och dessa fixar fanns inte där.
>
> **v2.15 SPELSKALET (ägarens dom: "inte produktionsredo - startmeny,
> settings, välja gubbe/map/minigames, sånt som ALLA spel har").**
> Shell.gd = menyverkstaden i varje Godot-scaffold: Shell.menu (riktig
> navigerbar huvudmeny), Shell.options_panel (volym/mute/fullskärm som
> SPARAS till user:// - Shell.startup laddar), Shell.character_select.
> Board Bash har hela skalet (Play/Choose Character/Minigames/Options/
> Quit; sex valbara figurer vars val styr färg+namn och överlever
> omstart; practice-läge för enskilda minigames; setup-skärm; autopilot).
> Pixel Rush fick Options+Quit på titeln. Regissören har nytt stående
> SPELSKAL-kriterium och release-checklistan flaggar saknad options/
> fullskärm/quit. LÄRDOM: class_name registreras först vid IMPORT - kit
> som parsas före importen måste preload:a (const Shell = preload(...)).
> KVAR mot "spel folk vill spela": flera BRÄDEN med olika layout/tema i
> partygolvet, fler minigames i golvet (3 idag - kontraktet skalar med
> prompten), CC0-assetpaket, språkväljare.
>
> **v2.16 KONSTEN (ägarens riktning: "förbättra vår art - kan AI göra
> pixelart? animerade gubbar?").** SVARET INBYGGT: bildmodeller är bra på
> form/karaktär men kan INTE exakt pixelart (1024² RGB utan alfa, pseudo-
> pixlar) och INTE konsekventa animationsframes - så AI:n gör designen och
> deterministisk kod resten. (1) PixelArtPipeline: molnbild → ÄKTA pixelart
> (flood-fill-bakgrund→alfa, bounding-box, boxfilter-downsample till exakt
> grid, palettkvantisering N färger, 1px kontur). (2) SpriteAnimator: EN
> stillbild → idle(2)+walk(4) via puppet-transformer (bob/squash/lutning
> kring fot-ankare) → GodotSpriteFrames .tres. (3) generate_asset
> style:'pixelart': moln→pipeline→sheet+_frames.tres; UTAN nycklar
> levererar PixelAnimator-riggen animerat ändå - aldrig en stum platta;
> verktygsbeskrivningen varnar för AI-spritesheets. (4) Board Bash:
> riktiga AnimatedSprite2D-GUBBAR på brädet (player_frames.tres, färgade
> per spelare, lerp-rörelse + hopp-bob + flip + walk/idle; cirkel-fallback
> före import) och levande bakgrund (gradientband + konfetti i två
> djuplager + vinjett). Skärmdumpsverifierat med zoom: riktig pixelgubbe
> med huvud/kropp/ben på sin ruta. KVAR: samma token-lyft i 3D-partyt,
> AI-bakgrunder via pipelinen (type 'background' + palettlås mot
> AssetStyle), CC0-paket.
>
> **v2.17 RIKTIG PIXELART (ägarens referensbilder: "bara några färgade
> rutor - mindre likt gubbarna vi har nu; ser ut som 2004-webbspel").**
> Tre hantverkstekniker in i ritkoden: (1) PixelAnimator 2.0 - 24px-canvas,
> RAMPER (2-3 nyanser per material: hud/tröja/byxor/hår), hår med mörk
> kant, skor, ögon med vitor, bälte, INNERLINE-pass (solid pixel som
> grannar transparens → mörk konturfärg = sluten silhuettlinje), benen
> står på marken (bob flyttar bara överkroppen). (2) NEAREST-filter:
> default_texture_filter=0 i projektmallen + texture_filter i party/
> plattformar-_ready - sprites renderades LINJÄRT suddiga i alla kit
> (stor del av billig-looken). (3) Board Bash: PIXELART-BRICKOR
> (_make_tile_tex: rundad kvadrat, kontur, ljus topp/mörk botten,
> pixelsymboler +/−/gnista/prickar) via draw_texture_rect i stället för
> nakna cirklar. Pipelinen: despeckle-pass (kluster-städning: brusig
> pixel tar vanligaste grannfärgen, öar rensas) + molnprompten begär
> pixelart-STIL direkt (modellerna är bra på stilen i hög upplösning -
> nedskalningen blir ren). Skärmdumpsverifierat med zoom: konturerad
> gubbe med frisyr/shading vid konturerad bricka. KVAR: samma bricklyft
> i fler kit (Glade-tiles, 3D-partyt), AI-bakgrunder via pipelinen.
>
> **v2.18 FÖRHANDSVALEN + FÖRHANDSFRÅGORNA + STAFETTEN (ägarens tre:
> "välj allting innan", "så frågar vi på mer", "automatiska handovers").**
> (1) Composern: stilkort med SVG-miniatyrer (Auto/Pixelart/2.5D/3D/
> Vektor) + Omfång (litet/standard/stort) + "Fråga först". Skickas som
> ASCII-taggar i uppdragstexten ([STIL: x]) = inga API-ändringar;
> BuildDirectives parsar och gör dem till HÅRDA kontraktspunkter FÖRST
> i regissörens kontrakt (operatorCriteria-param). Omfånget styr pass-
> (3/4/6) och polishbudgetar. (2) PreBuildQuestions: 2-3 riktade följd-
> frågor FÖRE scaffold i demorundornas kort (title-fält återanvänder
> demo-steget); svaren väger tyngst; 10 min = auto; inställning
> PreBuildQuestions (8 kopplingspunkter). (3) RelayHandover-stafetten:
> vid iterationstak skriver passet HANDOVER.md (KLART/ÅTERSTÅR/KÄNDA
> PROBLEM/NÄSTA STEG - överlämningsturen körs med behörighet AV = ren
> text) och ett FÄRSKT pass med tom kontext tar över - konstant
> kontextstorlek i stället för 8M-token-svällningen; fail-open till
> historik-fortsättningen. KVAR: förhandsval även i Ny spel-dialogen,
> stil-förhandsbilder av riktiga kit-skärmdumpar i korten.
>
> **v2.19 PARTYDJUPET + TEAMSTAFETTEN.** (1) Stafetten även i teamspåren
> (TeamBuild-continuation → RelayHandover; HANDOVER.md per worktree) -
> teamkörningar var de som svällde värst. (2) Board Bash: 5 minispel
> (nya: Coin Grab - AI jagar fallande mynt med per-svårighets-precision;
> Quick Draw - reaktionsduell 3 omgångar med AI-reaktionstider), 3 bräden
> med egna teman (nya Spiralen inåt mot mitten; bakgrundston per bräde:
> natt-lila/djuphavs-teal/candy-rosa), practice-menyn med alla fem.
> SKARPT FYND+FIX: fristående knappar på CanvasLayer får inga pålitliga
> auto-fokusgrannar i Godot → piltangenter döda i setup/practice-menyer;
> _button länkar nu fokuskedjan explicit (focus_neighbor_top/bottom).
> Spiralvalet loggbevisat + skärmdumpar (menyn/Quick Draw/spiralbrädet).
> KVAR mot partymålet: duell-/teleportrutor, bonusrundor, minigame-
> intro-kort med regler, 2-4 mänskliga spelare hotseat.
>
> **v2.20 GLÄNTAN I PIXELSPRÅK + PARTYRUTORNA.** (1) The Glade: schack-
> rutigt pixelgräs (två nyanser, jordfläckar, grässtrån), 14 kontur-
> dekorer (blommor/stenar/buskar) deterministiskt utplacerade, mörk
> kontur runt gläntan, riktiga pixelmynt (kontur/glans), nearest i
> _ready, PRESET_CENTER-fixen även här. (2) Board Bash: TILE_DUEL
> (tärningsduell, vinnaren tar ≤5 mynt, resultat i HUD via event_text)
> + TILE_WARP (teleport 5-9 framåt, dubbelburst) - minst 2+2 per bräde;
> BONUSRUNDA var 3:e runda (_is_bonus: HUD-tagg, blå ×2, minigame-
> awards ×2; aldrig i practice). KVAR: 3D-partyts pixellyft, minigame-
> intro-kort, hotseat 2-4 spelare, Options/Quit i Gläntan.

### C-gap 1. Spelet KÄNNS produktionsklart (störst upplevt gap, verifierbart)
- **C1 Game-feel/juice-pass** (M): screenshake, partiklar, tweenade övergångar,
  hit-stop, easing - LEVERERAT och VERIFIERAT (sonden mäter rörelse/partikel-
  aktivitet mellan bildrutor). Själva prototyp→produktion-skillnaden.
- **C2 Testad balans** (M): en agent SPELAR på flera skicklighetsnivåer via
  sonden och flaggar ovinnbart/trivialt/trasig svårighetskurva.
- **C3 Prestanda-koll** (S): sonden mäter FPS, underkänner hackiga byggen.

### C-gap 2. Studion beter sig som en riktig studio (störst ambition)
- **C4 Producent + riktiga överlämningar** (L): producent planerar milstolpar;
  design→konst→kod→ljud→QA lämnar över arbete, producenten koordinerar och
  itererar på speltest-feedback. Rollerna GRANSKAR idag - detta gör dem till en
  pipeline. Se multi-modell-noten nedan - ägarens idé skärper just den här.
- **C5 Längre checkpoint-byggen** (L): överlever kostnads-/iterationstak,
  återupptar mot en milstolpe, synlig framdrift.
- **C6 Regressionsskydd vid iteration** (S): kör om gamla speltest-kriterier
  efter "vidareutveckla" så en ändring inte tyst bryter befintliga features.

### C-gap 3. Utdatan är en PRODUKT, inte bara ett bygge
- **C7 Trailer + butikssida från reprisen** (M): sy ihop repriser + titelkort +
  musik till en kort trailer; autogenererad butiksbeskrivning + skärmdumpar.
  Återanvänder allt repris-arbete (v1.65/v1.70).
- **C8 Spelbar länk** (M): paketera webbygget för hosting. OBS utåtriktat -
  bygg paketeringen, ÄGAREN publicerar (aldrig autopublicering).
- **C9 Mobilexport (Android APK)** (M): Godot kan exportera Android, mallarna
  finns.

### C-gap 4. Konst- & innehållsdjup
- **C10 Art-bibel / sammanhållen konstriktning** (M): palett/stil/stämning
  bestäms FÖRST; varje asset (sprites, TILESETS, BAKGRUNDER, UI) följer den -
  inte bara karaktärssprites som idag (AssetStyle).
- **C11 Procedurellt innehållsdjup** (M): fler banor/fiender/föremål för
  replayvärde, inte en skärm.

### C-gap 5. Räckvidd & finish
- **C12 Tillgänglighet + lokalisering** (M): ombindbara kontroller, färgblind-
  säkra paletter, extraherade strängar för översättning.
- **C13 In-game tutorial/onboarding** (S): produktionsspel lär spelaren spela.

**Rekommenderad start:** C1 (störst upplevt golv-lyft, verifierbart), sedan C7
(snabb visuell vinst som återanvänder repriserna), sedan det stora C4. ÄRLIGT:
C2 (fun/balans) har högst tak men är svårast - mät det som GÅR (vinnbarhet,
svårighetsspridning), lova ingen "fun-detektor".

### Multi-modell-samarbete på EN maskin (ägarens idé 2026-07-22)
Flera API-agenter (OLIKA modeller) som jobbar mot samma mål på samma dator.
FUNGERAR - och infran finns delvis: team-läget (TeamBuild) kör redan parallella
agenter på en maskin via git-worktrees + sekventiell merge. API-agenter är
I/O-bundna på API:et (inte lokal GPU), så en maskin kan köra flera samtidigt
billigt - "en dator per uppdrag"-regeln handlade om DELADE FILER + lokala
modeller, inte om compute.
- BÄTTRE när: (a) SPECIALISERING (stark modell som arkitekt/regissör/granskare +
  billig som byggare - delvis gjort via tier-eskaleringen), (b) KVALITET via
  korsgranskning (två OLIKA modeller fångar fler fel än en modell som granskar
  sig själv - olika felmoder).
- SÄMRE/risk: tätt kopplad spelkod → parallellt samma-mål ger merge-konflikter
  och agenter som ångrar varann; koordinationskostnad; ~2x kostnad för de
  samarbetande delarna. "Två identiska agenter som slåss om samma filer" är
  sämre än en fokuserad agent.
- SLUTSATS: sweet spot = ROLL-baserat samarbete med olika modeller (senior/
  junior, byggare/kritiker), inte en kapplöpning. Det är i praktiken C4 +
  per-roll-modellval. SAKNAS idag: (1) tilldela OLIKA modeller per team-spår/
  roll (arkitekten kör Complex men dev-spåren delar samma modelHint), (2) ett
  KOLLABORATIVT läge (inte bara dela-och-merga) där agenter korsgranskar
  varandras arbete cross-modell.
