using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiLocal.Node.Hosting;

/// <summary>Resultat från en spelsession: vad som hände, prestanda, problem.</summary>
public sealed record PlaytestResult(
    bool Success,
    string Summary,
    List<string> Issues,
    double AverageFps,
    double PeakMemoryMb,
    TimeSpan Duration,
    string? ScreenshotPath,
    string? TitleScreenshotPath = null);

/// <summary>
/// Automatiserad speltestning: kör spelet, skickar simulerade inputs,
/// övervakar prestanda (FPS, minne) och rapporterar problem.
/// Fungerar främst för HTML5-spel (kan öppnas i en headless browser)
/// och för fristående .exe-filer (process-övervakning).
/// </summary>
public sealed class GamePlaytester
{
    private readonly IHttpClientFactory? _httpFactory;
    private readonly ILogger<GamePlaytester>? _logger;
    private readonly Func<string, string, CancellationToken, Task<(bool Success, string Text)>>? _visionReview;
    private readonly BrowserScreenshotter _screenshotter;

    public GamePlaytester(
        IHttpClientFactory? httpFactory = null,
        Func<string, string, CancellationToken, Task<(bool Success, string Text)>>? visionReview = null,
        ILogger<GamePlaytester>? logger = null,
        BrowserScreenshotter? screenshotter = null)
    {
        _httpFactory = httpFactory;
        _visionReview = visionReview;
        _logger = logger;
        _screenshotter = screenshotter ?? new BrowserScreenshotter();
    }

    /// <summary>
    /// Testar ett HTML5-spel genom att öppna det och analysera JS-konsolen
    /// efter fel. Kan inte simulera inputs — det kräver en riktig browser-driver.
    /// Returnerar en statisk analys + eventuella JS-fel.
    /// </summary>
    public async Task<PlaytestResult> TestHtml5Async(string indexPath, TimeSpan duration, CancellationToken ct)
    {
        if (!File.Exists(indexPath))
            return new PlaytestResult(false, $"index.html hittades inte: {indexPath}",
                [$"Sökväg saknas: {indexPath}"], 0, 0, TimeSpan.Zero, null);

        var issues = new List<string>();
        var html = await File.ReadAllTextAsync(indexPath, ct);

        // Statisk analys av HTML5-spelet
        var analysis = AnalyzeHtml5Game(html, indexPath);
        issues.AddRange(analysis.Issues);

        // Headless RUNTIME-korning: exekvera skripten mot en stubbad DOM och
        // pumpa spel-loopen ~2 sekunder. Fangar krascher som parsning inte
        // ser (ReferenceError, null-deref i loopen, id-drift) - utan browser.
        var smoke = new GameRuntimeSmokeTester().Run(html);
        foreach (var error in smoke.Errors)
            issues.Add($"RUNTIME-FEL (headless-korning): {error}");
        foreach (var warning in smoke.Warnings)
            issues.Add($"Runtime-varning: {warning}");

        var summary = new StringBuilder();
        summary.AppendLine($"## Speltest: {Path.GetFileName(Path.GetDirectoryName(indexPath))}");
        summary.AppendLine();
        summary.AppendLine($"- **Typ:** HTML5");
        summary.AppendLine($"- **Headless-körning:** {(smoke.Errors.Count == 0 ? $"{smoke.FramesPumped} frames utan fel ✓" : $"{smoke.Errors.Count} runtime-fel ✗")}");
        summary.AppendLine($"- **Storlek:** {html.Length} tecken");
        summary.AppendLine($"- **Canvas:** {(html.Contains("<canvas") ? "Ja ✓" : "Nej ✗ (spelet har ingen canvas!)")}");
        summary.AppendLine($"- **Game loop:** {(html.Contains("requestAnimationFrame") || html.Contains("setInterval") ? "Hittad ✓" : "Saknas ✗")}");
        summary.AppendLine($"- **Input-hantering:** {(html.Contains("keydown") || html.Contains("keyup") || html.Contains("mousedown") ? "Hittad ✓" : "Saknas ✗")}");
        summary.AppendLine($"- **Ljud:** {(html.Contains("Audio(") || html.Contains("AudioContext") ? "Hittat ✓" : "Inget ljud")}");
        summary.AppendLine();

        if (issues.Count > 0)
        {
            summary.AppendLine("### Problem hittade:");
            foreach (var issue in issues)
                summary.AppendLine($"- ✗ {issue}");
        }
        else
        {
            summary.AppendLine("### ✅ Inga uppenbara problem hittade i koden.");
            summary.AppendLine();
            summary.AppendLine("> **Notera:** Detta är en statisk kodanalys. För full speltestning krävs en browser-automation.");
        }

        // Försök uppskatta prestanda från kodmönster
        var fps = EstimateHtml5Fps(html);
        var mem = EstimateHtml5Memory(html);

        return new PlaytestResult(true, summary.ToString(), issues, fps, mem, duration, null);
    }

    /// <summary>
    /// Testar ett .exe-spel: startar processen, övervakar prestanda och tar
    /// en fönsterdump (screenshotDir) som vision-granskningen kan bedöma -
    /// motorspel får därmed samma visuella kvalitetsöga som HTML5-spelen.
    /// arguments låter godot-binären köra ett projekt direkt (--path ...).
    /// </summary>
    public async Task<PlaytestResult> TestExeAsync(
        string exePath, TimeSpan duration, CancellationToken ct,
        string? arguments = null, string? screenshotDir = null)
    {
        if (!File.Exists(exePath))
            return new PlaytestResult(false, $".exe hittades inte: {exePath}",
                [$"Sökväg saknas: {exePath}"], 0, 0, TimeSpan.Zero, null);

        var issues = new List<string>();
        var summary = new StringBuilder();
        var dir = Path.GetDirectoryName(exePath) ?? ".";

        try
        {
            var psi = new ProcessStartInfo(exePath)
            {
                Arguments = arguments ?? "",
                WorkingDirectory = dir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = false
            };

            using var proc = new Process { StartInfo = psi };
            var started = proc.Start();
            if (!started)
            {
                return new PlaytestResult(false, $"Kunde inte starta {Path.GetFileName(exePath)}",
                    ["Processen startade inte."], 0, 0, TimeSpan.Zero, null);
            }

            var startTime = DateTimeOffset.UtcNow;
            var memorySamples = new List<long>();
            var fpsSamples = new List<double>();

            // Övervaka processen under duration
            using var timeoutCts = new CancellationTokenSource(duration);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

            // Interaktiv QA parallellt med övervakningen, medan spelet lever:
            // sonden väntar in fönstret, SPELAR (tangenttryck via PostMessage),
            // pixeljämför före/efter och lämnar en dump till vision-granskningen
            // - motorspelens motsvarighet till HTML5-spelens CDP-sond.
            var probeTask = screenshotDir is null
                ? null
                : GodotWindowProbe.PlayAsync(
                    proc, Path.Combine(screenshotDir, "playtest-window.png"), linked.Token);

            try
            {
                while (!linked.Token.IsCancellationRequested && !proc.HasExited)
                {
                    try
                    {
                        proc.Refresh();
                        memorySamples.Add(proc.WorkingSet64);
                        fpsSamples.Add(EstimateFps(proc)); // grov uppskattning
                    }
                    catch { /* process kan ha avslutats */ }
                    await Task.Delay(500, linked.Token);
                }
            }
            catch (OperationCanceledException) { /* timeout — normalt */ }

            var elapsed = DateTimeOffset.UtcNow - startTime;

            string? screenshotPath = null;
            WindowProbeResult? probe = null;
            if (probeTask is not null)
            {
                probe = await probeTask;
                screenshotPath = probe.ScreenshotPath;
            }

            // Läses FÖRE Kill: efter Kill är ExitCode alltid -1, vilket gav
            // ett falskt "avslutade med felkod"-issue för varje spel som
            // överlevde hela testet (dvs. de som fungerar).
            var exitedOnItsOwn = proc.HasExited;

            // Samla eventuell stdout/stderr
            var stdout = exitedOnItsOwn ? await proc.StandardOutput.ReadToEndAsync() : "";
            var stderr = exitedOnItsOwn ? await proc.StandardError.ReadToEndAsync() : "";

            if (!proc.HasExited)
            {
                try { proc.Kill(); } catch { /* redan död */ }
            }

            var avgMem = memorySamples.Count > 0 ? memorySamples.Average() / (1024.0 * 1024.0) : 0;
            var avgFps = fpsSamples.Count > 0 ? fpsSamples.Where(f => f > 0).DefaultIfEmpty(60).Average() : 0;
            var peakMem = memorySamples.Count > 0 ? memorySamples.Max() / (1024.0 * 1024.0) : 0;

            summary.AppendLine($"## Speltest: {Path.GetFileName(exePath)}");
            summary.AppendLine();
            summary.AppendLine($"- **Körtid:** {elapsed.TotalSeconds:F1}s");
            summary.AppendLine($"- **Exit:** {(exitedOnItsOwn ? $"felkod {proc.ExitCode}" : "körde hela testet (avslutades av testet)")}");
            summary.AppendLine($"- **Genomsnittlig FPS:** {avgFps:F0}");
            summary.AppendLine($"- **Genomsnittligt minne:** {avgMem:F1} MB");
            summary.AppendLine($"- **Högsta minne:** {peakMem:F1} MB");
            if (probe is not null)
                summary.AppendLine($"- **Interaktiv QA (fönster):** {probe.Notes}");
            summary.AppendLine();

            if (exitedOnItsOwn && proc.ExitCode != 0)
                issues.Add($"Spelet avslutade med felkod {proc.ExitCode}");

            // Samma felklass som CDP-sondens "reagerar inte": ser rätt ut men
            // svarar inte på spelaren - grindens fixrundor får jobba på det.
            if (probe is { Ran: true, Responded: false, ContinuouslyAnimating: false })
                issues.Add("Interaktiv QA: spelet reagerar inte på spelarens tangenttryck - fönstret är oförändrat efter piltangenter/WASD/Enter/Space. Spelaren KOMMER INTE FÖRBI STARTSKÄRMEN - koppla Enter/Space (eller ge startknappen fokus med grab_focus) så spelet faktiskt startar.");

            if (!string.IsNullOrWhiteSpace(stderr))
            {
                var errLines = stderr.Split('\n').Where(l => !string.IsNullOrWhiteSpace(l)).Take(10).ToList();
                if (errLines.Count > 0)
                {
                    issues.AddRange(errLines.Select(l => $"Stderr: {l.Trim()}"));
                    summary.AppendLine("### Stderr (första 10 raderna):");
                    foreach (var line in errLines)
                        summary.AppendLine($"  - {line.Trim()}");
                }
            }

            if (avgFps < 30)
                issues.Add($"Låg FPS ({avgFps:F0}) — sikta på ≥60 FPS");

            if (peakMem > 500)
                issues.Add($"Hög minnesanvändning ({peakMem:F0} MB) — optimera resurser");

            if (elapsed.TotalSeconds < 2)
                issues.Add("Spelet avslutades mycket snabbt — kan vara en krasch direkt vid start");

            return new PlaytestResult(true, summary.ToString(), issues, avgFps, peakMem, elapsed,
                screenshotPath, probe?.TitleScreenshotPath);
        }
        catch (Exception ex)
        {
            return new PlaytestResult(false, $"Test misslyckades: {ex.Message}",
                [ex.Message], 0, 0, TimeSpan.Zero, null);
        }
    }

    /// <summary>Kör ett fullt speltest: start → spela → screenshots → avsluta → analysera.</summary>
    public async Task<PlaytestResult> FullTestAsync(string projectRoot, string engine, TimeSpan duration, CancellationToken ct)
    {
        var summary = new StringBuilder();
        var allIssues = new List<string>();

        // Hitta spelet. Motorbyggen doper .exe:n efter projektmappen
        // (GameBuilder.DeriveExeName), sa leta upp vad som än ligger i build/.
        var buildDir = Path.Combine(projectRoot, "build");
        string? gamePath = engine.ToLowerInvariant() switch
        {
            "html5" => Path.Combine(projectRoot, "index.html"),
            "godot" or "unity" => Directory.Exists(buildDir)
                ? Directory.GetFiles(buildDir, "*.exe").FirstOrDefault()
                : null,
            _ => null
        };

        string? exeArguments = null;
        if (gamePath is null || !File.Exists(gamePath))
        {
            // Godot utan exporterad exe: kvalitetsgrinden kör FÖRE auto-
            // exporten, så första bygget saknar build/. Kör projektet direkt
            // via `godot --path` i stället - då får även förstagångsbyggen
            // fönsterdump + vision, och grindens fixrundor kan reagera på
            // det visuella precis som för HTML5-spel.
            if (engine.Equals("godot", StringComparison.OrdinalIgnoreCase)
                && File.Exists(Path.Combine(projectRoot, "project.godot"))
                && AiLocal.Core.Agent.ToolLocator.Find("godot") is { } godotExe
                && File.Exists(godotExe))
            {
                gamePath = godotExe;
                exeArguments = $"--path \"{projectRoot}\"";
            }
            else
            {
                // Försök hitta spelet rekursivt
                var candidates = Directory.GetFiles(projectRoot, "*.*", SearchOption.AllDirectories)
                    .Where(f => Path.GetExtension(f).ToLowerInvariant() is ".html" or ".exe")
                    .Take(5)
                    .ToList();
                if (candidates.Count > 0)
                    gamePath = candidates[0];
                else
                    return new PlaytestResult(false, "Hittade inget spelbart i projektet",
                        ["Ingen .html eller .exe hittades."], 0, 0, TimeSpan.Zero, null);
            }
        }

        var result = gamePath.EndsWith(".html")
            ? await TestHtml5Async(gamePath, duration, ct)
            : await TestExeAsync(gamePath, duration, ct, exeArguments,
                screenshotDir: Path.Combine(projectRoot, "screenshots"));

        // ---- Visuell nivå: riktig Chromium-rendering + AI-ögon ----------
        // Jint-smoken bevisar att koden KÖR; skärmdumpen bevisar att spelet
        // RITAR något en spelare ser, och vision-modellen (när nycklar finns)
        // bedömer om det ser ut som ett riktigt spel. En svart/tom canvas
        // blir ett issue som kvalitetsgrindens fixrundor tvingar agenten
        // att åtgärda - tidigare kunde ett "grönt" spel rendera ingenting.
        return await AddVisualReviewAsync(result, gamePath, projectRoot, ct);
    }

    private async Task<PlaytestResult> AddVisualReviewAsync(
        PlaytestResult result, string gamePath, string projectRoot, CancellationToken ct)
    {
        var screenshotPath = result.ScreenshotPath;
        var titleScreenshotPath = result.TitleScreenshotPath;
        var summary = new StringBuilder(result.Summary);
        var issues = new List<string>(result.Issues);

        // Interaktiv QA: SPELA spelet via DevTools-protokollet - tangenttryck
        // in, canvas-hash före/efter. Ett spel som ser rätt ut men inte
        // svarar på spelaren är felklassen inga tidigare kontroller såg.
        if (gamePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var probe = await new InteractiveProbe().PlayAsync(
                gamePath, Path.Combine(projectRoot, "screenshots", "playtest.png"), ct);
            if (probe.Ran)
            {
                summary.AppendLine();
                summary.AppendLine("### Interaktiv QA (riktig spelsession)");
                summary.AppendLine(probe.Notes);
                if (!probe.Responded)
                    issues.Add("Interaktiv QA: spelet reagerar inte på spelarens input - canvasen är oförändrad efter tangenttryck och klick.");
                // Sondens slutdump visar spelet MITT I en session - bättre
                // vision-underlag än en statisk startbild.
                screenshotPath ??= probe.FinalScreenshotPath;
                titleScreenshotPath ??= probe.TitleScreenshotPath;
            }
        }

        if (screenshotPath is null && gamePath.EndsWith(".html", StringComparison.OrdinalIgnoreCase))
        {
            var output = Path.Combine(projectRoot, "screenshots", "playtest.png");
            var shot = await _screenshotter.CaptureHtmlAsync(gamePath, output, TimeSpan.FromSeconds(5), ct);
            summary.AppendLine();
            summary.AppendLine($"### Visuell rendering (Chromium headless)");
            summary.AppendLine(shot.Output);
            if (shot.Success)
                screenshotPath = shot.ImagePath;
        }

        if (screenshotPath is not null && _visionReview is not null)
        {
            try
            {
                var (ok, text) = await _visionReview(screenshotPath,
                    "Detta är en skärmdump av ett spel några sekunder efter start. Bedöm kort på svenska: " +
                    "1) Ser det ut som ett riktigt spel med titelskärm/spelgrafik och läsbar UI? " +
                    "2) Är ytan tom, helt svart eller trasig (tecken på kraschad rendering)? " +
                    "3) Syns TEXTDEFEKTER i UI:t: råa formatplatshållare (\"%d\", \"%s\"), synliga taggar (\"[color=...]\") " +
                    "eller rådumpad data ('{ \"name\": ... }')? Om ja, skriv exakt ordet TEXTDEFEKT och citera vad du ser. " +
                    "4) Ge de två viktigaste konkreta visuella förbättringarna.", ct);
                if (ok && !string.IsNullOrWhiteSpace(text))
                {
                    summary.AppendLine();
                    summary.AppendLine("### Visuell granskning (AI)");
                    summary.AppendLine(text.Trim());
                    var lower = text.ToLowerInvariant();
                    if (lower.Contains("helt svart") || lower.Contains("tom canvas") || lower.Contains("tom yta")
                        || lower.Contains("ingenting renderas") || lower.Contains("completely black"))
                        issues.Add("Visuellt: skärmdumpen ser tom/svart ut - spelet renderar inget synligt vid start.");
                    // v1.99: exakt de fel som sågs i en levererad build - rå
                    // "Omgang %d: %s" i HUD, [color=...]-taggar, dumpad dict.
                    if (lower.Contains("textdefekt"))
                        issues.Add("Textdefekter i UI:t: råa formatsträngar/taggar/datadumpar syns för spelaren - hitta strängarna (sök %d, %s, [color=, '{ \"') och rätta dem.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Vision är ett extra öga, aldrig ett krav - utan nycklar/nät
                // står skärmdumpen och de andra kontrollerna för sanningen.
            }
        }
        else if (screenshotPath is not null)
        {
            summary.AppendLine("(Ingen vision-modell konfigurerad - skärmdumpen sparad utan AI-granskning.)");
        }

        // Titelskärmen granskas SEPARAT: mittspelsdumpen kan aldrig svara på
        // "finns spelnamn, startval och instruktioner?" - två dumpar över tid
        // ger AI-granskaren hela förloppet i stället för en stillbild.
        if (titleScreenshotPath is not null && File.Exists(titleScreenshotPath) && _visionReview is not null)
        {
            try
            {
                var (ok, text) = await _visionReview(titleScreenshotPath,
                    "Detta är spelets ALLRA FÖRSTA skärm (titel-/startskärmen). Bedöm kort på svenska: " +
                    "1) Finns spelnamn, startval och läsbara instruktioner? " +
                    "2) Är ytan tom, helt svart eller trasig? " +
                    "3) Syns TEXTDEFEKTER: råa \"%d\"/\"%s\", synliga \"[color=...]\"-taggar eller rådumpad data? " +
                    "Om ja, skriv exakt ordet TEXTDEFEKT. " +
                    "4) Ge den EN viktigaste förbättringen av startupplevelsen.", ct);
                if (ok && !string.IsNullOrWhiteSpace(text))
                {
                    summary.AppendLine();
                    summary.AppendLine("### Titelskärmen (AI)");
                    summary.AppendLine(text.Trim());
                    var lower = text.ToLowerInvariant();
                    if (lower.Contains("helt svart") || lower.Contains("tom yta") || lower.Contains("tom canvas")
                        || lower.Contains("ingen titelskärm") || lower.Contains("saknar titel")
                        || lower.Contains("ingenting renderas"))
                        issues.Add("Titelskärmen: dumpen ser tom/trasig ut eller saknar titel - spelaren möts inte av en riktig startskärm.");
                    if (lower.Contains("textdefekt"))
                        issues.Add("Titelskärmen: textdefekter (råa formatsträngar/taggar/datadumpar) syns - rätta strängarna före leverans.");
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Vision är ett extra öga, aldrig ett krav.
            }
        }

        // SPELDESIGN-pass: en speldesigner-bedömning av SPELBARHET och balans -
        // inte "ser det ut som ett spel" utan "GÅR det att spela, är det rimligt
        // svårt, fastnar man?". Skillnaden mellan en polerad prototyp och ett spel
        // värt att spela. Mittspelsdumpen (efter sondens tangenttryck) är underlaget.
        if (screenshotPath is not null && _visionReview is not null)
        {
            try
            {
                var (ok, text) = await _visionReview(screenshotPath,
                    "Bedöm detta som en SPELDESIGNER. Skärmdumpen är MITT I en spelsession, efter att en testare tryckt tangenter. Kort på svenska:\n" +
                    "1) Går det att förstå målet - vad ska spelaren göra?\n" +
                    "2) Ser det ut att gå att göra framsteg, eller ser det fruset/fast/omöjligt ut?\n" +
                    "3) Är svårigheten rimlig, eller FÖR SVÅRT (spelaren död / game over direkt) eller FÖR LÄTT/TOMT (inget händer, ingen utmaning)?\n" +
                    "Avsluta med EXAKT en av raderna: 'SPELBART: ja' eller 'SPELBART: nej - <kort orsak>'.", ct);
                if (ok && !string.IsNullOrWhiteSpace(text))
                {
                    summary.AppendLine();
                    summary.AppendLine("### Speldesign-bedömning (AI)");
                    summary.AppendLine(text.Trim());
                    issues.AddRange(DesignIssuesFrom(text));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // Speldesign-bedömningen är ett extra öga, aldrig ett krav.
            }
        }

        return result with
        {
            Summary = summary.ToString(),
            Issues = issues,
            ScreenshotPath = screenshotPath,
            TitleScreenshotPath = titleScreenshotPath
        };
    }

    /// <summary>Design/playability issues parsed from the SPELDESIGN vision pass.
    /// "SPELBART: nej" becomes a HARD "not playable" finding the gate escalates to
    /// a design-fix round; softer for-svårt/for-lätt notes steer balance. Pure and
    /// testable - no vision call needed.</summary>
    internal static IReadOnlyList<string> DesignIssuesFrom(string visionText)
    {
        var issues = new List<string>();
        var text = visionText ?? "";
        var lower = text.ToLowerInvariant();
        var idx = lower.IndexOf("spelbart: nej", StringComparison.Ordinal);
        if (idx >= 0)
        {
            var reason = text[(idx + "spelbart: nej".Length)..].TrimStart(' ', '-', ':', '\t');
            var firstLine = reason.Split('\n')[0].Trim();
            issues.Add("SPELDESIGN: spelet bedöms INTE spelbart som det är"
                + (firstLine.Length == 0 ? "." : " - " + firstLine));
            return issues; // ospelbart trumfar balansanmärkningar
        }
        if (lower.Contains("för svår") || lower.Contains("for svar") || lower.Contains("dör direkt")
            || lower.Contains("game over direkt") || lower.Contains("dog direkt"))
            issues.Add("SPELDESIGN: verkar för svårt - mjuka upp balansen (färre/långsammare fiender, mer HP, mildare start).");
        if (lower.Contains("för lätt") || lower.Contains("for latt") || lower.Contains("ingen utmaning")
            || lower.Contains("inget händer") || lower.Contains("för tomt"))
            issues.Add("SPELDESIGN: verkar för lätt/tomt - lägg till utmaning (fler/snabbare fiender, tydligt mål, tidspress).");
        return issues;
    }

    // ---- Statisk HTML5-analys -------------------------------------------------

    private static (List<string> Issues, string[] Warnings) AnalyzeHtml5Game(string html, string path)
    {
        var issues = new List<string>();

        // Riktig JS-parsning först (Acornima) - ett syntaxfel betyder svart
        // skärm oavsett hur bra allt annat ser ut, så det rapporteras överst.
        foreach (var error in AiLocal.Core.Agent.JsSyntaxChecker.CheckHtml(html))
            issues.Add($"JS-SYNTAXFEL (spelet startar inte): {error}");

        // Saknas canvas?
        if (!html.Contains("<canvas"))
            issues.Add("Ingen <canvas> hittades — spelet kan inte rendera grafik");

        // Ingen game loop?
        if (!html.Contains("requestAnimationFrame") && !html.Contains("setInterval"))
            issues.Add("Ingen game loop (requestAnimationFrame/setInterval) hittades");

        // Ohanterade fel?
        if (html.Contains("console.error") && !html.Contains("catch"))
            issues.Add("console.error används utan try/catch — risk för tysta fel");

        // Saknas input-hantering?
        if (!html.Contains("addEventListener") && !html.Contains("onkey"))
            issues.Add("Ingen event listener för input — spelet tar inte emot styrning");

        // För stora inline-scripts?
        var scriptCount = CountOccurrences(html, "<script");
        if (scriptCount > 10)
            issues.Add($"{scriptCount} script-taggar — överväg att dela upp i separata filer");

        // ---- Produktionspolish (produktmålet: även en svag prompt ska ge ett
        // spel på produktionsnivå). Dessa rapporteras som issues så att
        // agenten - vars systemprompt säger att varje playtest-issue är
        // arbete som återstår - åtgärdar dem innan den kallar sig klar. ----
        if (!html.Contains("Audio(") && !html.Contains("AudioContext"))
            issues.Add("Produktionspolish: inga ljudeffekter hittades — lägg till WebAudio-SFX för hopp/träff/plock/vinst/förlust (inga externa filer behövs)");

        // requestAnimationFrame är själva game-loopen och innehåller både
        // "anim" och "frame" - maska bort den innan letandet, annars räknas
        // varje spel som animerat.
        var withoutLoop = html.Replace("requestAnimationFrame", "", StringComparison.OrdinalIgnoreCase);
        if (!ContainsAnyOrdinal(withoutLoop, "frame", "anim", "sprite"))
            issues.Add("Produktionspolish: inga animationsmarkörer hittades — ge rörliga entiteter minst 2-frame-animation och feedback-animation vid skada/plock");

        if (!ContainsAnyOrdinal(html, "game over", "gameover", "game-over"))
            issues.Add("Produktionspolish: ingen game over-skärm hittades — spelet behöver tydliga game over- och vinst-skärmar med omstart");

        if (!ContainsAnyOrdinal(html, "localStorage"))
            issues.Add("Produktionspolish: ingen localStorage-användning hittades — spara highscore mellan sessioner");

        return (issues, []);
    }

    private static bool ContainsAnyOrdinal(string text, params string[] needles) =>
        needles.Any(n => text.Contains(n, StringComparison.OrdinalIgnoreCase));

    private static double EstimateHtml5Fps(string html)
    {
        // Grov heuristik baserad på komplexitet
        var complexity = CountOccurrences(html, "for(") + CountOccurrences(html, "while(");
        if (complexity > 50) return 30;
        if (complexity > 20) return 45;
        return 60;
    }

    private static double EstimateHtml5Memory(string html)
    {
        var size = html.Length;
        if (size > 100_000) return 50;
        if (size > 30_000) return 20;
        return 5;
    }

    private static double EstimateFps(Process proc)
    {
        // Grov uppskattning: CPU-användning korrelerar med FPS
        try
        {
            proc.Refresh();
            var cpuTime = proc.TotalProcessorTime.TotalMilliseconds;
            // Kan inte mäta faktisk FPS utan hook i spelet
            return cpuTime > 0 ? 60 : 0;
        }
        catch
        {
            return 0;
        }
    }

    private static int CountOccurrences(string text, string substring)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(substring, index, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            count++;
            index += substring.Length;
        }
        return count;
    }
}