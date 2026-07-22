using AiLocal.Core.Agent;
using AiLocal.Core.Providers;
using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.44.0: motorvalet ("unity eller godot" valde unity; "inte html" kunde
/// paradoxalt välja html5) och team-lägets tysta git-död (föll tillbaka utan
/// förklaring när git saknades på maskinen).
/// </summary>
public class EnginePickAndTeamGitTests
{
    [Theory]
    [InlineData("bygg ett riktigt spel i unity eller godot inte html", "godot")]
    [InlineData("2d fotboll manager i godot eller unity", "godot")]
    [InlineData("gör ett spel i unity", "unity")]
    [InlineData("3d rymdskjutare", "godot")]
    [InlineData("bygg ett 3d spel i unity", "unity")]
    [InlineData("ett webbspel som körs i webbläsaren", "html5")]
    [InlineData("riktigt spel, inte html", "godot")]
    [InlineData("ett spel, ej i html tack", "godot")]
    [InlineData("a real game, not html", "godot")]
    [InlineData("bygg ett plattformsspel", "godot")]
    public void PickEngine_GodotVinnerVidBadaOchNegeratWebbRaknasAldrig(string prompt, string expected)
    {
        Assert.Equal(expected, GameScaffoldService.PickEngine(prompt));
    }

    [Theory]
    // Genre-namngivna spel UTAN literalen "spel"/"game" - rotorsaken bakom att
    // "Football Manager Tycoon" blev en C#-konsolapp i stallet for Godot-kitet.
    [InlineData("Football Manager Tycoon", true)]
    [InlineData("football manager tycoon", true)]
    [InlineData("bygg ett fotbollsmanager-spel", true)]
    [InlineData("en roguelike med permadeath", true)]
    [InlineData("a platformer", true)]
    [InlineData("gör en tower defense", true)]
    [InlineData("ett spel i godot", true)]
    // Riktiga appar/verktyg far INTE klassas som spel (app-vagen ska leva kvar).
    [InlineData("bygg ett enkelt budgetverktyg i python", false)]
    [InlineData("skapa en rest-api i python", false)]
    [InlineData("ett verktyg som sorterar filer", false)]
    public void LooksLikeGame_GenreOrdRaknasSomSpel_MenInteVerktyg(string prompt, bool expected)
    {
        Assert.Equal(expected, GameScaffoldService.LooksLikeGame(prompt));
    }

    [Fact]
    public async Task IsGitAvailable_PaDenHarMaskinen_Sant()
    {
        // Dev/CI-maskinen har git (det här repot ÄR git) - beviset att
        // kontrollen svarar sant där git faktiskt finns.
        Assert.True(await new GitService().IsGitAvailableAsync());
    }

    private sealed class GitlessGitService : GitService
    {
        public override Task<bool> IsGitAvailableAsync(CancellationToken ct = default) => Task.FromResult(false);
        public override Task<bool> IsRepoAsync(string folderPath, CancellationToken ct = default) => Task.FromResult(false);
        public override Task<bool> InitAsync(string folderPath, CancellationToken ct = default) => Task.FromResult(false);
    }

    [Fact]
    public async Task TeamBuild_UtanGit_EmittarOrsakOchFallerTillbaka()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-teamgit-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        var steps = new List<AgentStep>();
        try
        {
            var git = new GitlessGitService();
            var result = await TeamBuild.RunAsync(
                "bygg ett spel", 2, dir, AgentAccessLevel.Full, null, "system",
                complete: (_, _) => throw new InvalidOperationException("modellen ska aldrig anropas när git-grunden saknas"),
                executorFor: _ => null!,
                emit: step => { steps.Add(step); return Task.CompletedTask; },
                git, new GitIsolationService(git), CancellationToken.None);

            Assert.Null(result); // fallback till ensam agent
            Assert.Contains(steps, s => s.Kind == "tool_error" && s.Detail.Contains("git init misslyckades"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* städning */ }
        }
    }

    // ---- Multi-modell: per-spår-svårighet (olika modeller, samma mål) -------

    [Fact]
    public void ParseTracks_LaserSvarighetPerSpar()
    {
        var json = """
            {"tracks":[
              {"title":"Gameplay","description":"karnmekaniken","difficulty":"hard"},
              {"title":"Meny","description":"startskarm","difficulty":"simple"}
            ]}
            """;
        var tracks = TeamBuild.ParseTracks(json);
        Assert.NotNull(tracks);
        Assert.Equal(2, tracks!.Count);
        Assert.Equal("hard", tracks[0].Difficulty);
        Assert.Equal("simple", tracks[1].Difficulty);
    }

    [Theory]
    [InlineData("hard", "hard")]
    [InlineData("HARD", "hard")]
    [InlineData("simple", "simple")]
    [InlineData("medium", "medium")]
    [InlineData("nonsens", "medium")]   // okänt -> medium (standardmodellen)
    [InlineData(null, "medium")]
    public void NormalizeDifficulty_KlamparTillGiltiga(string? raw, string expected)
    {
        Assert.Equal(expected, TeamBuild.NormalizeDifficulty(raw));
    }

    [Fact]
    public void FallbackTracks_HarBadeHardaOchEnklaSpar()
    {
        // Poängen med multi-modell: fallbacken måste variera svårighet så
        // per-spår-modellvalet har något att jobba med (inte allt "medium").
        var tracks = TeamBuild.FallbackTracks("bygg ett spel", Path.GetTempPath());
        Assert.Contains(tracks, t => t.Difficulty == "hard");
        Assert.Contains(tracks, t => t.Difficulty == "simple");
    }
}
