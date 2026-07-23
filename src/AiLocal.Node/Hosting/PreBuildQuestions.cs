using System.Text.Json;
using AiLocal.Core.Contracts;
using AiLocal.Core.Providers;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.18: FÖRHANDSFRÅGORNA - innan scaffolden ställer noden 2-3 riktade
/// följdfrågor om uppdraget ("simulator eller arkad? öppen värld eller
/// banor?") i samma kort som demorundorna. Rätt riktning FRÅN START är den
/// billigaste kvalitetshöjningen: felgissad riktning är det dyraste felet
/// (upptäcks först efter halva bygget). Fail-open: ingen fråga vid fel,
/// och 10 minuters tystnad = regissören väljer själv.
/// </summary>
public static class PreBuildQuestions
{
    public const int MaxQuestions = 3;

    internal static string Prompt(string assignment) =>
        "Uppdrag från en operatör till en spelstudio:\n\"" + Trunc(assignment, 600) + "\"\n\n" +
        "Innan bygget startar får operatören svara på 2-3 KORTA klargörande frågor. " +
        "Ställ BARA frågor vars svar ändrar bygget i grunden (inriktning, skala, läge) - " +
        "aldrig detaljer studion kan avgöra själv. Ge varje fråga 2-4 förslag i parentes. " +
        "Exempel på bra frågor: \"Simulator eller arkadkänsla? (realistisk / arkad)\", " +
        "\"Öppen värld eller banbaserat? (öppen värld / banor)\", \"En spelare eller flera? (solo / lokal multiplayer)\". " +
        "Frågorna ska vara på svenska. Svara ENBART med JSON: {\"questions\": [\"...\", \"...\"]}. " +
        "Är uppdraget redan glasklart: {\"questions\": []}.";

    public static async Task<IReadOnlyList<string>> GenerateAsync(
        string assignment,
        Func<ChatRequest, CancellationToken, Task<ProviderResponse>> complete,
        CancellationToken ct,
        string? modelHint = null)
    {
        try
        {
            var response = await complete(new ChatRequest
            {
                System = "Du är en spelstudios producent som ställer få men avgörande frågor innan ett bygge.",
                Messages = [new ChatMessage("user", Prompt(assignment))],
                ModelHint = modelHint,
                MaxTokens = 300
            }, ct);
            if (!response.IsSuccess || string.IsNullOrWhiteSpace(response.Response!.Content)) return [];
            return ParseQuestions(response.Response.Content);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return []; }
    }

    /// <summary>JSON-tolkning med fail-open (tom lista). Public for test.</summary>
    public static IReadOnlyList<string> ParseQuestions(string reply)
    {
        try
        {
            var start = reply.IndexOf('{');
            var end = reply.LastIndexOf('}');
            if (start < 0 || end <= start) return [];
            using var doc = JsonDocument.Parse(reply[start..(end + 1)]);
            if (!doc.RootElement.TryGetProperty("questions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];
            return arr.EnumerateArray()
                .Select(e => e.ValueKind == JsonValueKind.String ? e.GetString() : null)
                .Where(s => !string.IsNullOrWhiteSpace(s) && s!.Length > 8)
                .Select(s => s!.Trim())
                .Take(MaxQuestions)
                .ToList();
        }
        catch { return []; }
    }

    static string Trunc(string s, int max) =>
        string.IsNullOrEmpty(s) ? "" : s.Length <= max ? s : s[..max] + "…";
}
