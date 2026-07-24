using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.31: mätningen över alla 21 kit gav NOLL Theme, StyleBoxFlat och
/// NinePatch - varenda knapp var en rå Godot-standardknapp, den tydligaste
/// prototypsignalen som finns och den syns på FÖRSTA bildrutan. Samtidigt
/// nådde art-bibelns palett bara sprites, så meny och spel såg ut att komma
/// från olika produkter.
/// </summary>
public class UiThemeTests
{
    [Fact]
    public void Temat_ByggsUrBibelnsPalett()
    {
        var bible = ArtBibleStore.Derive("party", "ett partyspel");
        var tres = UiTheme.Build(bible);

        Assert.StartsWith("[gd_resource type=\"Theme\"", tres);
        // Knappen ar det som syns mest - alla fyra tillstand plus fokus.
        foreach (var s in new[] { "btn_normal", "btn_hover", "btn_pressed", "btn_disabled", "btn_focus" })
            Assert.Contains($"id=\"{s}\"", tres);
        foreach (var s in new[] { "Button/styles/normal", "Button/styles/hover",
            "Button/styles/pressed", "Button/styles/focus" })
            Assert.Contains(s, tres);
        // Panelytor fanns inte alls i kiten - UI:t svavade fritt over spelet.
        Assert.Contains("Panel/styles/panel", tres);
        Assert.Contains("PanelContainer/styles/panel", tres);
        // Kontur pa text: flera kit ritar spelvarlden RAKT BAKOM menyn.
        Assert.Contains("Button/colors/font_outline_color", tres);
        Assert.Contains("Label/constants/outline_size", tres);
        // Rundade horn - den enskilt tydligaste skillnaden mot standardknappen.
        Assert.Contains("corner_radius_top_left", tres);
    }

    [Fact]
    public void OlikaProjekt_GerOlikaTeman_MenSammaStruktur()
    {
        var a = UiTheme.Build(ArtBibleStore.Derive("party", "partyspel"));
        var b = UiTheme.Build(ArtBibleStore.Derive("platformer", "plattformsspel"));
        Assert.NotEqual(a, b);                       // palett foljer projektet
        Assert.Equal(CountOf(a, "StyleBoxFlat"), CountOf(b, "StyleBoxFlat"));
    }

    [Fact]
    public void Temat_ArDeterministiskt_OchKulturoberoende()
    {
        var bible = ArtBibleStore.Derive("rpg", "ett aventyr");
        Assert.Equal(UiTheme.Build(bible), UiTheme.Build(bible));
        // En svensk nod far INTE skriva "0,5" i resursfilen - Godot laser
        // punkt som decimaltecken och filen blir tyst trasig.
        Assert.DoesNotContain(",5,", UiTheme.Build(bible).Replace(", ", "|"));
    }

    [Theory]
    [InlineData("bygg ett mario party liknande spel i godot med minigames")]
    [InlineData("bygg ett 2d plattformsspel i godot")]
    [InlineData("bygg ett snake spel i godot")]
    [InlineData("bygg ett litet fps spel i godot")]
    public void VarjeGodotProjekt_FarTemaOchRegistrerarDet(string prompt)
    {
        var dir = Directory.CreateTempSubdirectory("ailocal-theme-").FullName;
        try
        {
            new GameScaffoldService().Scaffold("auto", prompt, dir);
            var root = AiLocal.Core.Agent.ProjectRootDetector.Detect(dir) ?? dir;
            Assert.True(File.Exists(Path.Combine(root, "theme.tres")), "theme.tres saknas");
            // Registreringen ar poangen: via gui/theme/custom arver VARJE
            // Control temat, sa inget kit behover andras och agentbyggd UI
            // far det gratis.
            var proj = File.ReadAllText(Path.Combine(root, "project.godot"));
            Assert.Contains("theme/custom=\"res://theme.tres\"", proj);
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    private static int CountOf(string s, string needle)
    {
        var n = 0;
        for (var i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = s.IndexOf(needle, i + 1, StringComparison.Ordinal)) n++;
        return n;
    }
}
