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
> 404-sond där rollen saknar dem. Kvar av avgränsningarna: verifierad
> Android-APK (kräver SDK), prissatta bildanrop, automatisk återupptagning.

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
