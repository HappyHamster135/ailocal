using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AiLocal.Node.Hosting;

public sealed record PackageResult(bool Success, string Output, string? PackagePath, long SizeBytes);

/// <summary>
/// Paketerar ett byggt spel/app till ett distribuerbart paket (.zip med alla
/// assets + auto-genererad README + skärmdumpar) och förbereder för publicering
/// till plattformar som Itch.io och Steam.
/// </summary>
public sealed class PackageService
{
    private readonly IHttpClientFactory? _httpFactory;
    private readonly ILogger<PackageService>? _logger;

    public PackageService(IHttpClientFactory? httpFactory = null, ILogger<PackageService>? logger = null)
    {
        _httpFactory = httpFactory;
        _logger = logger;
    }

    /// <summary>Paketerar en komplett spelkatalog till ett .zip-arkiv.</summary>
    public async Task<PackageResult> PackageAsync(
        string projectRoot, string engine, string gameName, string? outputDir, CancellationToken ct)
    {
        try
        {
            if (!Directory.Exists(projectRoot))
                return new PackageResult(false, $"Projektmappen finns inte: {projectRoot}", null, 0);

            outputDir ??= Path.Combine(projectRoot, "release");
            Directory.CreateDirectory(outputDir);

            var packageName = SanitizeFileName(gameName);
            // Versionsnamn (B4): AiLocal-versionen som byggde paketet - spårbart
            // och stabilt per release i stället för den gamla hårdkodade v1.0.
            var version = typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "1.0";
            var zipPath = Path.Combine(outputDir, $"{packageName}-v{version}.zip");

            // Ta reda på vad som ska inkluderas
            var buildDir = FindBuildDirectory(projectRoot, engine);
            var files = CollectDistributionFiles(projectRoot, buildDir, engine);

            _logger?.LogInformation("Paketerar {Count} filer till {Path}", files.Count, zipPath);

            // Skapa README
            var readmePath = Path.Combine(outputDir, "README.md");
            var readme = GenerateReadme(gameName, engine, files);
            await File.WriteAllTextAsync(readmePath, readme, Encoding.UTF8, ct);
            files.Add(readmePath);

            // Skapa metadata
            var metaPath = Path.Combine(outputDir, "ailocal-metadata.json");
            var meta = GenerateMetadata(gameName, engine, projectRoot);
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, ct);
            files.Add(metaPath);

            // C7: butikssida (store.html) - en DELBAR produktsida med spelets
            // namn, den animerade reprisen som kort "trailer", skärmdumpar,
            // beskrivning (ur DESIGN.md) och hur man spelar. Bilderna bäddas in
            // som data-URI:er så sidan är helt fristående och funkar var som helst.
            var storePath = Path.Combine(outputDir, "store.html");
            await File.WriteAllTextAsync(storePath, GenerateStorePage(gameName, engine, projectRoot, files), Encoding.UTF8, ct);
            files.Add(storePath);

            // C8: spelbar länk - om spelet går att spela i webbläsaren (html5
            // eller godot-webexport), lägg en HOSTA.md med steg för att få en
            // delbar länk. Jag bygger paketet; publiceringen (utåtriktad) gör
            // ägaren själv.
            var webIndex = File.Exists(Path.Combine(projectRoot, "index.html")) ? "index.html"
                : File.Exists(Path.Combine(projectRoot, "build", "web", "index.html")) ? "build/web/index.html"
                : null;
            if (webIndex is not null)
            {
                var hostaPath = Path.Combine(outputDir, "HOSTA.md");
                await File.WriteAllTextAsync(hostaPath, GenerateHostingGuide(gameName, webIndex), Encoding.UTF8, ct);
                files.Add(hostaPath);
            }

            // Skapa .zip
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var outDirName = Path.GetFileName(outputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                foreach (var file in files.Distinct())
                {
                    if (!File.Exists(file)) continue;
                    var entryName = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                    // README/metadata skrivs i outputmappen (dist/ eller release/) -
                    // lägg dem i zip-roten där mottagaren förväntar sig dem i
                    // stället för nästlade under mappnamnet (bugg: strippade bara
                    // hårdkodat "release/", men Packa-knappen skickar "dist/").
                    if (file.StartsWith(outputDir) && entryName.StartsWith(outDirName + "/"))
                        entryName = entryName[(outDirName.Length + 1)..];
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            var size = new FileInfo(zipPath).Length;
            _logger?.LogInformation("Paket skapat: {Path} ({Size} bytes)", zipPath, size);

            return new PackageResult(true,
                $"Paket skapat: {zipPath} ({FormatBytes(size)}, {files.Count} filer).\n" +
                $"- README: {readmePath}\n" +
                $"- Butikssida: {storePath}\n" +
                $"- Metadata: {metaPath}",
                zipPath, size);
        }
        catch (Exception ex)
        {
            return new PackageResult(false, $"Paketering misslyckades: {ex.Message}", null, 0);
        }
    }

    /// <summary>Publicerar till Itch.io via Butler CLI (om installerat).</summary>
    public async Task<PackageResult> PublishToItchAsync(
        string zipPath, string itchUser, string itchGame, string channel, CancellationToken ct)
    {
        // Kontrollera att butler finns
        var butlerPath = FindButler();
        if (butlerPath is null)
            return new PackageResult(false,
                "Butler (Itch.io CLI) är inte installerat. Installera från https://itch.io/docs/butler/ och se till att 'butler' finns i PATH.",
                null, 0);

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo(butlerPath)
            {
                Arguments = $"push \"{zipPath}\" {itchUser}/{itchGame}:{channel}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null)
                return new PackageResult(false, "Kunde inte starta butler.", null, 0);

            var output = await proc.StandardOutput.ReadToEndAsync(ct);
            var error = await proc.StandardError.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
                return new PackageResult(false, $"Itch.io push misslyckades: {error}", null, 0);

            return new PackageResult(true,
                $"Publicerat till Itch.io: {itchUser}/{itchGame}:{channel}\n{output}", zipPath, 0);
        }
        catch (Exception ex)
        {
            return new PackageResult(false, $"Itch.io publicering misslyckades: {ex.Message}", null, 0);
        }
    }

    /// <summary>Laddar upp till Steam via SteamCMD (kräver konfiguration).</summary>
    public async Task<PackageResult> PublishToSteamAsync(
        string buildDir, string steamUser, string appId, string depotId, CancellationToken ct)
    {
        var steamCmd = FindSteamCmd();
        if (steamCmd is null)
            return new PackageResult(false,
                "SteamCMD är inte installerat. Installera från https://developer.valvesoftware.com/wiki/SteamCMD",
                null, 0);

        // SteamCMD kräver VDF-filer och korrekt konfiguration — ge instruktioner
        return new PackageResult(false,
            "Steam-uppladdning kräver manuell setup:\n" +
            "1. Skapa `steam_app_build.vdf` med depot-konfiguration\n" +
            "2. Kör: `steamcmd +login {steamUser} +run_app_build steam_app_build.vdf +quit`\n" +
            "3. Använd Steamworks SDK för att konfigurera butikssidan\n\n" +
            "Detta steg är inte helt automatiserat än — kontakta Steamworks för full pipeline.",
            null, 0);
    }

    /// <summary>Skapar en enkel NSIS/WiX-installer för Windows.</summary>
    public async Task<PackageResult> CreateInstallerAsync(
        string buildDir, string gameName, string outputPath, CancellationToken ct)
    {
        // Generera ett self-extracting zip eller använd PowerShell för MSI
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var installerDir = Path.Combine(buildDir, "installer");
                Directory.CreateDirectory(installerDir);

                // Kör via en temporär .ps1-fil i stället för -Command "..." -
                // det eliminerar två buggar i den gamla varianten: (1) enkla
                // citattecken expanderar inte $source/$dest i PowerShell, så
                // den arkiverade den bokstavliga sökvägen '$source\*'; (2)
                // inbäddade citattecken i ett flerradigt -Command-argument
                // krockar med kommandoradens egen quoting.
                var psScript = $@"
$source = '{buildDir.Replace("'", "''")}'
$dest = '{outputPath.Replace("'", "''")}'
Compress-Archive -Path ""$source\*"" -DestinationPath ""$dest"" -Force
";
                var scriptPath = Path.Combine(Path.GetTempPath(), $"ailocal-installer-{Guid.NewGuid():n}.ps1");
                await File.WriteAllTextAsync(scriptPath, psScript, ct);
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var proc = System.Diagnostics.Process.Start(psi);
                if (proc is null)
                    return new PackageResult(false, "Kunde inte starta PowerShell.", null, 0);

                var output = await proc.StandardOutput.ReadToEndAsync(ct);
                var error = await proc.StandardError.ReadToEndAsync(ct);
                await proc.WaitForExitAsync(ct);
                try { File.Delete(scriptPath); } catch { /* temp cleanup - best effort */ }

                if (proc.ExitCode != 0)
                    return new PackageResult(false, $"PowerShell misslyckades: {error}", null, 0);

                var size = File.Exists(outputPath) ? new FileInfo(outputPath).Length : 0;
                return new PackageResult(true,
                    $"Installer skapad: {outputPath} ({FormatBytes(size)})", outputPath, size);
            }
            catch (Exception ex)
            {
                return new PackageResult(false, $"Installer misslyckades: {ex.Message}", null, 0);
            }
        }

        return new PackageResult(false, "Installer-skapande stöds endast på Windows just nu.", null, 0);
    }

    // ---- Hjälpmetoder --------------------------------------------------------

    private static string FindBuildDirectory(string projectRoot, string engine)
    {
        var candidates = new[]
        {
            Path.Combine(projectRoot, "build"),
            Path.Combine(projectRoot, "Build"),
            Path.Combine(projectRoot, "dist"),
            Path.Combine(projectRoot, "release"),
        };

        foreach (var dir in candidates)
            if (Directory.Exists(dir))
                return dir;

        return projectRoot;
    }

    private static List<string> CollectDistributionFiles(string projectRoot, string buildDir, string engine)
    {
        var files = new List<string>();

        // Alla .exe och .dll i build-mappen
        if (Directory.Exists(buildDir))
        {
            files.AddRange(Directory.GetFiles(buildDir, "*.exe", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.GetFiles(buildDir, "*.dll", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.GetFiles(buildDir, "*.pck", SearchOption.TopDirectoryOnly));
            files.AddRange(Directory.GetFiles(buildDir, "*.txt", SearchOption.TopDirectoryOnly));

            // Undermappar med data
            foreach (var subdir in new[] { "Assets", "Data", "Content", "Resources" })
            {
                var path = Path.Combine(buildDir, subdir);
                if (Directory.Exists(path))
                    files.AddRange(Directory.GetFiles(path, "*", SearchOption.AllDirectories));
            }
        }

        // HTML5: inkludera index.html + assets
        if (engine == "html5")
        {
            var indexPath = Path.Combine(projectRoot, "index.html");
            if (File.Exists(indexPath))
                files.Add(indexPath);

            // Inkludera alla .js, .css, .png, .jpg, .wav i projektmappen
            var mediaExts = new[] { ".js", ".css", ".png", ".jpg", ".jpeg", ".gif", ".wav", ".mp3", ".ogg", ".svg" };
            foreach (var file in Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories))
                if (mediaExts.Contains(Path.GetExtension(file).ToLowerInvariant()))
                    files.Add(file);
        }

        // Steam-filer
        var steamAppId = Path.Combine(projectRoot, "steam_appid.txt");
        if (File.Exists(steamAppId))
            files.Add(steamAppId);

        // Skärmdumpar + ev. repris (B4): "så här ser spelet ut" följer med
        // paketet så mottagaren ser spelet utan att köra det.
        var shotsDir = Path.Combine(projectRoot, "screenshots");
        if (Directory.Exists(shotsDir))
            files.AddRange(Directory.GetFiles(shotsDir, "*.png", SearchOption.TopDirectoryOnly));

        return files.Where(f => File.Exists(f)).Distinct().ToList();
    }

    private static string GenerateReadme(string gameName, string engine, List<string> files)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# {gameName}");
        sb.AppendLine();
        sb.AppendLine($"> Auto-genererat av AiLocal | Motor: {engine}");
        sb.AppendLine();
        sb.AppendLine("## Spela");
        sb.AppendLine();
        if (engine == "html5")
            sb.AppendLine("Öppna `index.html` i en webbläsare.");
        else
            sb.AppendLine($"Kör `{gameName}.exe` på Windows.");
        sb.AppendLine();
        sb.AppendLine("## Systemkrav");
        sb.AppendLine("- Windows 10/11 (64-bit)");
        sb.AppendLine("- DirectX 11+");
        sb.AppendLine("- 4 GB RAM");
        sb.AppendLine();
        sb.AppendLine("## Byggd med");
        sb.AppendLine($"- AiLocal v{typeof(PackageService).Assembly.GetName().Version?.ToString(3) ?? "?"}");
        sb.AppendLine($"- Motor: {engine}");
        sb.AppendLine();
        var shots = files.Where(f => f.Replace('\\', '/').Contains("/screenshots/") && f.EndsWith(".png")).ToList();
        if (shots.Count > 0)
        {
            sb.AppendLine("## Skärmdumpar");
            sb.AppendLine();
            foreach (var s in shots)
                sb.AppendLine($"- screenshots/{Path.GetFileName(s)}");
            sb.AppendLine();
        }
        sb.AppendLine("## Innehåll");
        sb.AppendLine();
        var exeFiles = files.Where(f => f.EndsWith(".exe")).ToList();
        var dataFiles = files.Where(f => !f.EndsWith(".exe") && !f.EndsWith(".dll")).ToList();
        sb.AppendLine($"- {exeFiles.Count} körbara filer");
        sb.AppendLine($"- {dataFiles.Count} datafiler");
        sb.AppendLine($"- Total storlek: {FormatBytes(files.Where(File.Exists).Sum(f => new FileInfo(f).Length))}");
        return sb.ToString();
    }

    private static object GenerateMetadata(string gameName, string engine, string projectRoot)
    {
        return new
        {
            name = gameName,
            engine,
            version = "1.0.0",
            builtAt = DateTimeOffset.UtcNow.ToString("O"),
            builtBy = "AiLocal",
            platform = "Windows",
            genre = "auto-detected",
            tags = Array.Empty<string>(),
        };
    }

    // ---- C7: butikssida (store.html) ----------------------------------------

    private static string GenerateStorePage(string gameName, string engine, string projectRoot, List<string> files)
    {
        static string DataUri(string path)
        {
            try { return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(path)); }
            catch { return ""; }
        }
        var shotFiles = files
            .Where(f => f.Replace('\\', '/').Contains("/screenshots/") && f.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var replay = shotFiles.FirstOrDefault(f => Path.GetFileName(f).Equals("replay.png", StringComparison.OrdinalIgnoreCase));
        var stills = shotFiles.Where(f => !Path.GetFileName(f).Equals("replay.png", StringComparison.OrdinalIgnoreCase)).ToList();

        var description = ExtractConcept(projectRoot)
            ?? $"Ett {(engine == "html5" ? "webbspel" : "spel")} byggt med AiLocal.";
        var controls = engine == "html5"
            ? "Öppna index.html i en webbläsare."
            : $"Kör {HtmlEsc(gameName)}.exe på Windows.";

        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html><html lang=\"sv\"><head><meta charset=\"utf-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">");
        sb.AppendLine($"<title>{HtmlEsc(gameName)}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("body{margin:0;font-family:system-ui,Arial,sans-serif;background:#0e1116;color:#e6e6e6;line-height:1.6}");
        sb.AppendLine(".wrap{max-width:820px;margin:0 auto;padding:32px 20px}");
        sb.AppendLine("h1{font-size:2.4rem;margin:0 0 4px}.tag{color:#8aa0b4;margin:0 0 24px}");
        sb.AppendLine("img{max-width:100%;border-radius:10px;display:block;margin:0 auto}");
        sb.AppendLine(".hero{margin:0 0 24px}.shots{display:grid;grid-template-columns:1fr 1fr;gap:12px;margin:0 0 24px}");
        sb.AppendLine("h2{border-bottom:1px solid #223;padding-bottom:6px;margin-top:32px}");
        sb.AppendLine("footer{margin-top:40px;color:#66788a;font-size:.9rem}");
        sb.AppendLine("</style></head><body><div class=\"wrap\">");
        sb.AppendLine($"<h1>{HtmlEsc(gameName)}</h1><p class=\"tag\">{HtmlEsc(engine)}-spel · byggt med AiLocal</p>");
        if (replay is not null && DataUri(replay) is { Length: > 0 } r)
            sb.AppendLine($"<div class=\"hero\"><img src=\"{r}\" alt=\"Speltest-repris (trailer)\"></div>");
        if (stills.Count > 0)
        {
            sb.AppendLine("<div class=\"shots\">");
            foreach (var s in stills.Take(4))
                if (DataUri(s) is { Length: > 0 } d)
                    sb.AppendLine($"<img src=\"{d}\" alt=\"Skärmdump\">");
            sb.AppendLine("</div>");
        }
        sb.AppendLine($"<h2>Om spelet</h2><p>{HtmlEsc(description)}</p>");
        sb.AppendLine($"<h2>Spela</h2><p>{controls}</p>");
        sb.AppendLine("<footer>Byggd med AiLocal.</footer>");
        sb.AppendLine("</div></body></html>");
        return sb.ToString();
    }

    /// <summary>Första meningsfulla stycket ur DESIGN.md:s "Koncept"-avsnitt -
    /// spelets egen designvision blir butikssidans beskrivning. Null utan
    /// DESIGN.md eller koncept.</summary>
    private static string? ExtractConcept(string projectRoot)
    {
        try
        {
            var design = Path.Combine(projectRoot, "DESIGN.md");
            if (!File.Exists(design)) return null;
            var sb = new StringBuilder();
            var inConcept = false;
            foreach (var raw in File.ReadAllLines(design))
            {
                var line = raw.Trim();
                if (line.StartsWith("## "))
                {
                    if (inConcept) break;
                    inConcept = line.Contains("Koncept", StringComparison.OrdinalIgnoreCase);
                    continue;
                }
                if (inConcept && line.Length > 0 && !line.StartsWith('#'))
                    sb.Append(line.Replace("**", "")).Append(' ');
            }
            var concept = sb.ToString().Trim();
            if (string.IsNullOrWhiteSpace(concept)) return null;
            return concept.Length > 400 ? concept[..400] + "…" : concept;
        }
        catch { return null; }
    }

    private static string HtmlEsc(string s) =>
        (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    /// <summary>C8: steg för att förvandla ett webbygge till en delbar länk.
    /// Paketering görs här - publiceringen (utåtriktad) gör ägaren själv.</summary>
    private static string GenerateHostingGuide(string gameName, string webIndex)
    {
        var folder = webIndex.Contains('/') ? webIndex[..webIndex.LastIndexOf('/')] : "spelets rotmapp";
        var sb = new StringBuilder();
        sb.AppendLine($"# Få en spelbar länk till {gameName}");
        sb.AppendLine();
        sb.AppendLine("Spelet går att spela direkt i webbläsaren. För att dela det som en LÄNK,");
        sb.AppendLine("ladda upp webbfilerna till valfri gratis statisk host:");
        sb.AppendLine();
        sb.AppendLine("## Snabbast: itch.io (dra-och-släpp)");
        sb.AppendLine($"1. Zippa mappen med `{webIndex}`.");
        sb.AppendLine("2. Skapa ett projekt på itch.io, välj \"Kind of project: HTML\".");
        sb.AppendLine("3. Ladda upp zip:en, kryssa i \"This file will be played in the browser\".");
        sb.AppendLine("4. Publicera - du får en spelbar länk att skicka.");
        sb.AppendLine();
        sb.AppendLine("## Alternativ (alla gratis)");
        sb.AppendLine($"- **Netlify Drop** (https://app.netlify.com/drop): dra mappen `{folder}` dit -> direkt länk.");
        sb.AppendLine("- **GitHub Pages**: lägg filerna i ett repo, aktivera Pages -> länk.");
        sb.AppendLine("- **Cloudflare Pages / Vercel**: importera mappen -> länk.");
        sb.AppendLine();
        sb.AppendLine($"Filen som ska öppnas/spelas är `{webIndex}`.");
        sb.AppendLine();
        sb.AppendLine("> Byggt med AiLocal. Publiceringen gör du själv - det är en utåtriktad åtgärd.");
        return sb.ToString();
    }

    private static string? FindButler()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in paths)
        {
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "butler.exe" : "butler");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    private static string? FindSteamCmd()
    {
        var paths = Environment.GetEnvironmentVariable("PATH")?.Split(Path.PathSeparator) ?? [];
        foreach (var dir in paths)
        {
            var exe = Path.Combine(dir, OperatingSystem.IsWindows() ? "steamcmd.exe" : "steamcmd");
            if (File.Exists(exe)) return exe;
        }
        return null;
    }

    private static string SanitizeFileName(string name) =>
        string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Trim();

    private static string FormatBytes(long bytes) => bytes switch
    {
        >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
        >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
        >= 1_024 => $"{bytes / 1_024.0:F1} KB",
        _ => $"{bytes} B"
    };
}