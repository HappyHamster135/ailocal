using System.Text.RegularExpressions;
using AiLocal.Core.Agent;
using AiLocal.Core.Configuration;
using AiLocal.Core.Contracts;
using AiLocal.Core.Roles;
using AiLocal.Node.Hosting;
using AiLocal.Node.Roles;
using Microsoft.AspNetCore.DataProtection;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v1.39.0-ytorna: tema-vakten (rapport: "växtspel i stället för fotboll"),
/// plan-i-stället-för-utförande-vakten (rapport: "gjorde bara en plan,
/// utförde aldrig uppgiften"), sport-genren, ikontäckningen i dashboarden
/// och moln-pseudo-workers.
/// </summary>
public class PolishAndThemeGuardTests
{
    // ---- Tema-vakten: tokenextraktion --------------------------------------

    [Fact]
    public void TopicTokens_PlockarTemaordMenInteGenreOchFyllnad()
    {
        var tokens = ProjectContext.TopicTokens(
            "bygg ett 2d fotbollsspel med tre svårighetsgrader och banor");
        Assert.Contains("fotboll", tokens);
        Assert.DoesNotContain("svårighetsgrader", tokens);
        Assert.DoesNotContain("banor", tokens);
    }

    [Fact]
    public void TopicTokens_RenGenrepromptGerIngaTokens()
    {
        Assert.Empty(ProjectContext.TopicTokens("bygg ett enkelt plattformsspel"));
    }

    [Fact]
    public void TopicTokens_UppfoljningsordRaknasAldrigSomTema()
    {
        Assert.Empty(ProjectContext.TopicTokens("gör spelet svårare och snyggare"));
    }

    // ---- Tema-vakten: helprojektskravet ------------------------------------

    [Theory]
    [InlineData("bygg ett 2d fotbollsspel", true)]
    [InlineData("bygg ett 2d fotball manegmeant simulator spel där man väljer ett lag", true)]
    [InlineData("build a space game with three levels", true)]
    [InlineData("gör spelet svårare", false)]
    [InlineData("lägg till powerups och en boss", false)]
    [InlineData("fortsätt där du slutade", false)]
    public void NamesAWholeProject_KraverObestamdArtikelPlusSpelord(string prompt, bool expected)
    {
        Assert.Equal(expected, ProjectContext.NamesAWholeProject(prompt));
    }

    // ---- Tema-vakten: hela bedömningen mot ett projekt på disk -------------

    [Fact]
    public void SeemsUnrelated_FotbollMotBondgardsprojekt_SantMenUppfoljningFortsatter()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ailocal-theme-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "DESIGN.md"),
                "# Simulator / Bondgard (HTML5)\n\nEn odlingssimulator dar man planterar, vattnar och skordar grodor for guld.");

            // Rotorsaksfallet: fotbollsprompt mot ett bondgårdsprojekt.
            Assert.True(ProjectContext.SeemsUnrelated(dir, "bygg ett 2d fotbollsspel i godot"));

            // Uppföljningar och samma tema ska ALDRIG trigga vakten.
            Assert.False(ProjectContext.SeemsUnrelated(dir, "gör spelet svårare"));
            Assert.False(ProjectContext.SeemsUnrelated(dir, "bygg ett bondgårdsspel med fler grödor"));
            // Genreprompt utan tema-ord: hellre fortsätta än börja om.
            Assert.False(ProjectContext.SeemsUnrelated(dir, "bygg ett enkelt plattformsspel"));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* städning */ }
        }
    }

    [Fact]
    public void SeemsUnrelated_MappnamnetRaknasSomUnderlag()
    {
        var parent = Path.Combine(Path.GetTempPath(), "ailocal-theme-" + Guid.NewGuid().ToString("n"));
        var dir = Path.Combine(parent, "fotboll-management-simulator");
        Directory.CreateDirectory(dir);
        try
        {
            // Dokumenten skriver engelska "football" men mappnamnet är svenskt -
            // en fotbollsprompt ska matcha via mappnamnet och fortsätta.
            File.WriteAllText(Path.Combine(dir, "DESIGN.md"), "# Football Manager\nSeason, squad, transfers.");
            Assert.False(ProjectContext.SeemsUnrelated(dir, "bygg ett fotbollsspel med bättre menyer"));
        }
        finally
        {
            try { Directory.Delete(parent, recursive: true); } catch { /* städning */ }
        }
    }

    // ---- Plan-i-stället-för-utförande-vakten -------------------------------

    [Fact]
    public void PlanOnlyDetector_LovfraganFranTranskriptet_Traffas()
    {
        var final = "**ACCEPTANCE CRITERIA**: The game will be considered complete...\n\n" +
                    "Here is my plan:\n1. Create a new Godot project\n2. Implement the core mechanics\n" +
                    "Let me know if this plan meets your expectations!";
        Assert.True(PlanOnlyDetector.LooksUnexecuted(final));
    }

    [Fact]
    public void PlanOnlyDetector_AviseradMenEjUtfordSistaMening_Traffas()
    {
        var final = "Här är en plan:\n1. Modifiera GameManager.gd\n2. Modifiera Game.cs\n" +
                    "Jag börjar med att anpassa `GameManager.gd`.";
        Assert.True(PlanOnlyDetector.LooksUnexecuted(final));
    }

    [Fact]
    public void PlanOnlyDetector_SkaJagFortsatta_Traffas()
    {
        Assert.True(PlanOnlyDetector.LooksUnexecuted("Grunden är klar. Ska jag fortsätta med resten?"));
    }

    [Theory]
    [InlineData("Klart! Spelet har fem banor, ljud och menyer. Verify och playtest passerar.")]
    [InlineData("Bygget är färdigt och verifierat. Jag började med datalagret och avslutade med UI-polish.")]
    [InlineData("")]
    [InlineData(null)]
    public void PlanOnlyDetector_FardigaLeveranser_TraffasAldrig(string? final)
    {
        Assert.False(PlanOnlyDetector.LooksUnexecuted(final));
    }

    // ---- Sport-genren ------------------------------------------------------

    [Theory]
    [InlineData("bygg ett 2d fotbollsmanager spel", "management")]
    [InlineData("bygg ett 2d fotball manegmeant simulator spel där man väljer ett lag", "management")]
    [InlineData("hockey tycoon med tre divisioner", "management")]
    [InlineData("bygg ett bondgårdsspel där man odlar", "simulator")]
    public void DetectGenre_SportPlusStyrord_BlirManagementAldrigBondgard(string prompt, string expected)
    {
        Assert.Equal(expected, GameScaffoldService.DetectGenre(prompt));
    }

    // ---- Ikontäckning i dashboarden ----------------------------------------

    [Fact]
    public void Dashboard_VarjeDataIconHarEnGlyfIIconsObjektet()
    {
        var html = Dashboard.Html;
        var usedNames = Regex.Matches(html, "data-icon=\"([a-z-]+)\"")
            .Select(m => m.Groups[1].Value)
            .Distinct()
            .ToList();
        Assert.NotEmpty(usedNames);

        var start = html.IndexOf("const ICONS = {", StringComparison.Ordinal);
        Assert.True(start >= 0, "ICONS-objektet hittades inte i dashboarden.");
        var block = html[start..html.IndexOf("};", start, StringComparison.Ordinal)];
        var keys = Regex.Matches(block, @"^\s+'?([a-z-]+)'?:", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value)
            .ToHashSet();

        var missing = usedNames.Where(n => !keys.Contains(n)).ToList();
        Assert.True(missing.Count == 0,
            "data-icon-namn utan glyf i ICONS (renderas som tom SVG): " + string.Join(", ", missing));
    }

    // ---- Moln-pseudo-workers ----------------------------------------------

    [Fact]
    public void CloudPseudoWorkers_EnRadPerKonfigureradNyckel_AldrigDispatchbar()
    {
        var rows = CloudPseudoWorkers.For(p => p is "openrouter" or "anthropic" ? "nyckel" : null).ToList();

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.StartsWith("cloud:", r.Id);
            Assert.Equal(AiLocal.Core.Agent.AgentAccessLevel.Off, r.AgentAccess);
            Assert.Equal(0, r.MaxConcurrentTasks);
            Assert.Null(r.ClusterToken);
        });
        Assert.Contains(rows, r => r.Id == "cloud:openrouter");
        Assert.Contains(rows, r => r.Id == "cloud:anthropic");
        Assert.Empty(CloudPseudoWorkers.For(_ => null));
    }
}

/// <summary>
/// Persistensrevisionen: fälten som sparades men aldrig lästes tillbaka
/// (AutoMergeIsolatedTasks, BudgetLimitUsd), trion som aldrig nådde disken
/// (CommandGuard, BlockedCommands, ProjectMemoryEnabled), rollistan och
/// skyddet mot att dashboard-spara nollställer ModelTiers.Routes.
/// </summary>
[Collection("EnvIsolated")]
public class SettingsPersistenceRoundtripTests : IDisposable
{
    private readonly string _dataDir;
    private readonly string? _previousDataDir;

    public SettingsPersistenceRoundtripTests()
    {
        _previousDataDir = Environment.GetEnvironmentVariable("AILOCAL_DATA_DIR");
        _dataDir = Path.Combine(Path.GetTempPath(), "ailocal-tests-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(_dataDir);
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _dataDir);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("AILOCAL_DATA_DIR", _previousDataDir);
        try { Directory.Delete(_dataDir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public void AllaRevideradeFaltOverleverEnOmstart()
    {
        var settings = new NodeSettings { Role = NodeRole.Host };
        var store = new PersistentSettingsStore(settings, new EphemeralDataProtectionProvider());
        var customRole = AgentRoles.Defaults()[0] with { Name = "Chefsarkitekten" };

        store.Update(new SettingsUpdate(
            AutoMergeIsolatedTasks: true,
            BudgetLimitUsd: 12.5m,
            CommandGuard: CommandGuardLevel.Warn,
            BlockedCommands: ["rm -rf", "format c:"],
            ProjectMemoryEnabled: true,
            Roles: [customRole]), new HostLocator());

        // "Omstart": en färsk NodeSettings hydreras enbart från disken.
        var fresh = new NodeSettings { Role = NodeRole.Host };
        PersistentSettingsStore.LoadInto(fresh);

        Assert.True(fresh.Worker.AutoMergeIsolatedTasks);
        Assert.Equal(12.5m, fresh.Worker.BudgetLimitUsd);
        Assert.Equal(CommandGuardLevel.Warn, fresh.Worker.CommandGuard);
        Assert.Equal(new List<string> { "rm -rf", "format c:" }, fresh.Worker.BlockedCommands);
        Assert.True(fresh.Worker.ProjectMemoryEnabled);
        Assert.Equal("Chefsarkitekten", Assert.Single(fresh.Host.Roles).Name);
    }

    [Fact]
    public void DashboardSpara_NollstallerInteModelTiersRoutes()
    {
        var settings = new NodeSettings { Role = NodeRole.Host };
        var store = new PersistentSettingsStore(settings, new EphemeralDataProtectionProvider());
        settings.Worker.ModelTiers.Routes =
            [new ModelRoute("coding", "openrouter", "custom/model", 1)];

        // Dashboarden POST:ar bara de tre tier-fälten - JSON-bindningen ger då
        // ett ModelTiers-objekt vars Routes är fabriksdefault.
        store.Update(new SettingsUpdate(
            ModelTiers: new ModelTiers { Simple = "a/b", Medium = "c/d", Complex = "e/f" }), new HostLocator());

        Assert.Equal("a/b", settings.Worker.ModelTiers.Simple);
        var route = Assert.Single(settings.Worker.ModelTiers.Routes);
        Assert.Equal("custom/model", route.Model);

        // Och de anpassade routes överlever omstarten.
        var fresh = new NodeSettings { Role = NodeRole.Host };
        PersistentSettingsStore.LoadInto(fresh);
        Assert.Equal("custom/model", Assert.Single(fresh.Worker.ModelTiers.Routes).Model);
    }
}
