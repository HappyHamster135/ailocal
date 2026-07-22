using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.92: opt-in auto-återupptagning. Låser kandidatvalet (BARA
/// omstartsdödade byggen - exakta markeringen, inte grind-underkännanden;
/// känd projektmapp; färskhet 48h; ETT enda = nyaste) och resume-promptens
/// kedjnings-skydd (en redan-återupptagen prompt sveps inte in igen).</summary>
public class AutoResumeTests
{
    private static AssignmentLogEntry Entry(
        string state = "Failed", string? final = null, string? rel = "spelet", double ageHours = 1) => new()
    {
        State = state,
        FinalAnswer = final ?? AssignmentLog.RestartMarker,
        ProjectRel = rel,
        Prompt = "bygg ett plattformsspel",
        StartedAt = DateTimeOffset.UtcNow.AddHours(-ageHours)
    };

    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public void PickResumable_TarNyasteOmstartsdodade()
    {
        var nyast = Entry(ageHours: 1);
        var aldre = Entry(ageHours: 5);
        Assert.Same(nyast, AutoResumeService.PickResumable([nyast, aldre], Now));   // nyast-först-listan
    }

    [Fact]
    public void PickResumable_HopparOverGrindUnderkanda()
    {
        // Ett bygge som UNDERKÄNDES (annat slutsvar än omstartsmarkeringen)
        // får ALDRIG auto-återupptas - bara omstartsdödade är säkra.
        var underkand = Entry(final: "grinden underkände: playtest kraschade");
        Assert.Null(AutoResumeService.PickResumable([underkand], Now));
    }

    [Fact]
    public void PickResumable_KraverProjektmappOchFarskhet()
    {
        Assert.Null(AutoResumeService.PickResumable([Entry(rel: null)], Now));        // ingen mapp
        Assert.Null(AutoResumeService.PickResumable([Entry(ageHours: 72)], Now));     // för gammal (48h-tak)
        Assert.Null(AutoResumeService.PickResumable([Entry(state: "Completed")], Now));
        Assert.Null(AutoResumeService.PickResumable([], Now));
    }

    [Fact]
    public void ResumePrompt_SviperInUrsprunget_MenKedjarAldrigOm()
    {
        var first = AutoResumeService.ResumePrompt("bygg ett racingspel");
        Assert.StartsWith("Återuppta det avbrutna bygget", first);
        Assert.Contains("bygg ett racingspel", first);
        Assert.Contains("DESIGN.md", first);

        // En andra omstart på den redan-återupptagna körningen får INTE ge
        // "Återuppta... Ursprungligt uppdrag: Återuppta..." - den återanvänds.
        Assert.Equal(first, AutoResumeService.ResumePrompt(first));
    }

    [Fact]
    public void ResumePrompt_TrunkerarLangaUrsprung()
    {
        var lang = new string('x', 5000);
        var prompt = AutoResumeService.ResumePrompt(lang);
        Assert.True(prompt.Length < 1200, $"prompten är {prompt.Length} tecken - trunkeringen läckte");
    }
}
