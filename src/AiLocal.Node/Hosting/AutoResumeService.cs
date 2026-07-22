using System.Net.Http.Json;
using AiLocal.Core.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v1.92 (opt-in, AV som default): automatisk återupptagning vid nodstart.
/// När inställningen är PÅ och noden startar om efter ett avbrutet bygge
/// återupptas det SENASTE omstartsdödade bygget - ETT enda (aldrig en storm),
/// bara om det dog av en OMSTART (exakta RestartMarker-markeringen, inte
/// grind-underkännanden) och är färskt (48h). Körningen går via loopback-POST
/// till nodens egen /api/assignment (Benchmark-mönstret) så den tar EXAKT
/// samma väg som Återuppta-knappen: kontinuitetsbrief + befintligt kontrakt +
/// milstolpe-loop. Default AV är en ärlighetsprincip: en omstartad nod ska
/// aldrig börja spendera tokens utan att operatören valt det.
/// </summary>
public static class AutoResumeService
{
    internal static readonly TimeSpan MaxAge = TimeSpan.FromHours(48);

    /// <summary>Kandidaten: nyaste posten som (1) dog av en omstart - exakta
    /// markeringen, INTE grind-underkännanden, (2) har en känd projektmapp och
    /// (3) är färsk. Null = inget att återuppta.</summary>
    internal static AssignmentLogEntry? PickResumable(IReadOnlyList<AssignmentLogEntry> newestFirst, DateTimeOffset now) =>
        newestFirst.FirstOrDefault(e =>
            e.State == "Failed"
            && e.FinalAnswer == AssignmentLog.RestartMarker
            && e.ProjectRel is { Length: > 0 }
            && now - e.StartedAt <= MaxAge);

    /// <summary>Resume-prompten. Redan-återupptagna prompter kedjas INTE om
    /// (annars växer "Återuppta... Ursprungligt uppdrag: Återuppta..." för
    /// varje omstart) - de återanvänds som de är.</summary>
    internal static string ResumePrompt(string originalPrompt)
    {
        if (originalPrompt.StartsWith("Återuppta det avbrutna bygget", StringComparison.Ordinal))
            return originalPrompt;
        var orig = originalPrompt.Length <= 600 ? originalPrompt : originalPrompt[..600] + "…";
        return "Återuppta det avbrutna bygget i den här projektmappen: läs DESIGN.md (leveranskontraktet) "
            + "och den befintliga koden, kör verify, och slutför det som återstår mot kontraktet - "
            + "börja INTE om från noll.\n\nUrsprungligt uppdrag: " + orig;
    }

    /// <summary>Kopplas in vid nodstart (Worker/Launcher). Väntar tills appen
    /// är uppe + en settle-paus, och skickar sedan EN loopback-körning om en
    /// kandidat finns. Fel här får aldrig sänka noden - ren bekvämlighet.</summary>
    public static void ScheduleOnStart(WebApplication app)
    {
        app.Lifetime.ApplicationStarted.Register(() => _ = Task.Run(async () =>
        {
            try
            {
                var settings = app.Services.GetRequiredService<NodeSettings>();
                if (!settings.Worker.AutoResume) return;
                if (settings.Role.ToString() is not ("Worker" or "Launcher")) return;   // bara motorroller
                var log = app.Services.GetService<AssignmentLog>();
                if (log is null) return;

                var entry = PickResumable(log.Snapshot(), DateTimeOffset.UtcNow);
                if (entry is null) return;

                await Task.Delay(TimeSpan.FromSeconds(10));   // låt noden sätta sig

                // Loopback till nodens egen motor - Benchmark-mönstret (v1.35):
                // exakt samma väg som ett riktigt uppdrag, inklusive kön.
                using var http = new HttpClient { Timeout = TimeSpan.FromHours(2) };
                await http.PostAsJsonAsync(
                    $"http://127.0.0.1:{settings.Port}/api/assignment",
                    new { assignment = ResumePrompt(entry.Prompt), projectRel = entry.ProjectRel });
            }
            catch
            {
                // Bekvämlighet - en misslyckad återupptagning loggas av
                // körningen själv (eller inte alls) men får aldrig störa noden.
            }
        }));
    }
}
