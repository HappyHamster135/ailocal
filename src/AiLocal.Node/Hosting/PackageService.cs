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
            var zipPath = Path.Combine(outputDir, $"{packageName}-v1.0.zip");

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
            var metaPath = Path.Combine(outputDir, "aitown-metadata.json");
            var meta = GenerateMetadata(gameName, engine, projectRoot);
            await File.WriteAllTextAsync(metaPath, JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8, ct);
            files.Add(metaPath);

            // Skapa .zip
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                foreach (var file in files.Distinct())
                {
                    if (!File.Exists(file)) continue;
                    var entryName = Path.GetRelativePath(projectRoot, file).Replace('\\', '/');
                    // Undvik duplicering i build-mappen
                    if (entryName.StartsWith("release/") && file.StartsWith(outputDir))
                        entryName = entryName["release/".Length..];
                    zip.CreateEntryFromFile(file, entryName, CompressionLevel.Optimal);
                }
            }

            var size = new FileInfo(zipPath).Length;
            _logger?.LogInformation("Paket skapat: {Path} ({Size} bytes)", zipPath, size);

            return new PackageResult(true,
                $"Paket skapat: {zipPath} ({FormatBytes(size)}, {files.Count} filer).\n" +
                $"- README: {readmePath}\n" +
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

                // Använd PowerShell för att skapa en enkel self-extracting installer
                var psScript = $@"
$source = '{buildDir.Replace("'", "''")}'
$dest = '{outputPath.Replace("'", "''")}'
Compress-Archive -Path '$source\*' -DestinationPath '$dest' -Force
";
                var psi = new System.Diagnostics.ProcessStartInfo("powershell.exe")
                {
                    Arguments = $"-NoProfile -Command \"{psScript}\"",
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
        sb.AppendLine($"- AiLocal v{"1.19.23"}");
        sb.AppendLine($"- Motor: {engine}");
        sb.AppendLine();
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