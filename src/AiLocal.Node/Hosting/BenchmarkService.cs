using System.Diagnostics;
using System.Text.Json;
using AiLocal.Core.Configuration;

namespace AiLocal.Node.Hosting;

public sealed record BenchmarkPromptResult(
    string Prompt, string Engine, bool Success, bool VerifyPass, int Issues, int Files, double DurationSec, int Score);

public sealed record BenchmarkRun(
    string Version, DateTimeOffset StartedAt, double TotalScore, List<BenchmarkPromptResult> Results);

/// <summary>
/// Self-measurement: runs a fixed set of weak standard prompts through THIS
/// node's own assignment engine (loopback POST /api/assignment with a scratch
/// WorkspaceOverride per prompt), scores each outcome with the quality gate's
/// machinery, and persists the run per app version. That turns "did the last
/// releases actually make results better?" from a feeling into a number you
/// can read in Inställningar. Deliberately local-only and sequential - it
/// measures ONE node's build capability with its configured models.
/// </summary>
public sealed class BenchmarkService
{
    /// <summary>Fixed across releases on purpose - changing the prompts would
    /// invalidate every historical comparison, so only ever APPEND. Mix: engine
    /// default (godot platformer), three html5 kit genres, the app path, and a
    /// Godot management build that exercises the full new chain (kit -> grind ->
    /// window probe -> exe) as a fresh series from v1.49 onward.</summary>
    public static readonly string[] StandardPrompts =
    [
        "bygg ett 2d plattformsspel",
        "bygg ett snake-spel som webbspel",
        "bygg ett breakout-spel som webbspel",
        "bygg ett quiz-spel som webbspel",
        "bygg ett enkelt budgetverktyg i python",
        "bygg ett fotbollsmanager-spel",
    ];

    private readonly object _lock = new();
    private readonly string _historyPath = Path.Combine(SettingsPaths.DataDirectory, "benchmark-history.json");
    private List<BenchmarkRun> _history;

    public BenchmarkService() => _history = Load(_historyPath);

    public bool Running { get; private set; }
    public List<string> Progress { get; } = [];

    public IReadOnlyList<BenchmarkRun> History
    {
        get { lock (_lock) return [.. _history]; }
    }

    /// <summary>Starts a run in the background. False when one is already
    /// going - a benchmark occupies the node's model for a long while.</summary>
    public bool TryStart(int count, int port, string version)
    {
        lock (_lock)
        {
            if (Running) return false;
            Running = true;
            Progress.Clear();
        }
        _ = Task.Run(() => RunAsync(Math.Clamp(count, 1, StandardPrompts.Length), port, version));
        return true;
    }

    private async Task RunAsync(int count, int port, string version)
    {
        var run = new BenchmarkRun(version, DateTimeOffset.UtcNow, 0, []);
        try
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

            foreach (var (prompt, index) in StandardPrompts.Take(count).Select((p, i) => (p, i)))
            {
                Note($"[{index + 1}/{count}] kör: {prompt}");
                var workspace = Path.Combine(SettingsPaths.DataDirectory, "benchmarks", stamp, $"{index + 1}");
                Directory.CreateDirectory(workspace);
                var sw = Stopwatch.StartNew();
                var success = false;
                try
                {
                    // Loopback till nodens EGEN motor - exakt samma väg som
                    // ett riktigt uppdrag tar (förskaffold, grind, allt).
                    using var response = await http.PostAsJsonAsync(
                        $"http://127.0.0.1:{port}/api/assignment",
                        new { assignment = prompt, workspaceOverride = workspace });
                    var body = await response.Content.ReadAsStringAsync();
                    success = body.Contains("\"Success\":true", StringComparison.Ordinal);
                }
                catch (Exception ex)
                {
                    Note($"  körningen kraschade: {ex.Message}");
                }
                sw.Stop();

                var result = await ScoreWorkspaceAsync(prompt, workspace, success, sw.Elapsed.TotalSeconds);
                run.Results.Add(result);
                Note($"  klart: {result.Score}/100 (verify {(result.VerifyPass ? "OK" : "FEL")}, {result.Issues} anmärkningar, {result.Files} filer, {result.DurationSec:0}s)");
            }

            run = run with { TotalScore = run.Results.Count > 0 ? Math.Round(run.Results.Average(r => r.Score), 1) : 0 };
            lock (_lock)
            {
                _history.Add(run);
                while (_history.Count > 20) _history.RemoveAt(0);
                Save();
            }
            Note($"KLART - totalpoäng {run.TotalScore}/100 (v{version}).");
        }
        catch (Exception ex)
        {
            Note($"Benchmarken avbröts av ett fel: {ex.Message}");
        }
        finally
        {
            lock (_lock) Running = false;
        }
    }

    private async Task<BenchmarkPromptResult> ScoreWorkspaceAsync(
        string prompt, string workspace, bool success, double durationSec)
    {
        var findings = await AssignmentQualityGate.InspectAsync(
            workspace, buildIntent: true, DateTime.MinValue, RunCommandAsync,
            playtest: async (root, engine, ct) =>
            {
                // Utan vision: benchmarkpoängen ska vara deterministisk och
                // gratis - skärmdumpen tas ändå av playtestern.
                var r = await new GamePlaytester().FullTestAsync(root, engine, TimeSpan.FromSeconds(8), ct);
                return (r.Success, r.Summary, (IReadOnlyList<string>)r.Issues);
            },
            CancellationToken.None,
            gameExpected: GameScaffoldService.LooksLikeGame(prompt));

        var files = CountFiles(workspace);
        var score = Score(success, findings, files);
        return new BenchmarkPromptResult(
            prompt, findings.Engine ?? "-", success, !findings.HardFail, CountIssues(findings), files, durationSec, score);
    }

    /// <summary>0-100, ren och testbar: körningen lyckades (30), grinden utan
    /// hårda fel (40), helt ren (15), rimligt antal filer (15).</summary>
    public static int Score(bool success, QualityFindings findings, int files)
    {
        var score = 0;
        if (success) score += 30;
        if (!findings.HardFail) score += 40;
        if (findings.Clean) score += 15;
        if (files >= 2) score += 15;
        return score;
    }

    private static int CountIssues(QualityFindings findings) =>
        findings.Clean ? 0 : findings.Report.Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));

    private static int CountFiles(string workspace)
    {
        try
        {
            return Directory.EnumerateFiles(workspace, "*", SearchOption.AllDirectories)
                .Count(f => !f.Contains(".git", StringComparison.OrdinalIgnoreCase));
        }
        catch { return 0; }
    }

    private void Note(string line)
    {
        lock (_lock) Progress.Add($"{DateTime.Now:HH:mm:ss} {line}");
    }

    private static Task<(int ExitCode, string Output)> RunCommandAsync(string cmd, string dir, CancellationToken ct)
    {
        // Wrappad /c "{cmd}" - cmd.exe:s citatstrippning (v1.90).
        var psi = new ProcessStartInfo("cmd.exe", $"/c \"{cmd}\"")
        {
            WorkingDirectory = Directory.Exists(dir) ? dir : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        using var proc = Process.Start(psi)!;
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit((int)TimeSpan.FromMinutes(5).TotalMilliseconds);
        return Task.FromResult((proc.ExitCode, stdout + "\n" + stderr));
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_historyPath)!);
            File.WriteAllText(_historyPath, JsonSerializer.Serialize(_history));
        }
        catch
        {
            // Historik är en bekvämlighet - fylld disk får inte krascha noden.
        }
    }

    private static List<BenchmarkRun> Load(string path)
    {
        try
        {
            if (!File.Exists(path)) return [];
            return JsonSerializer.Deserialize<List<BenchmarkRun>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }
}
