using AiLocal.Core.Agent;
using AiLocal.Node.Hosting;
using AiLocal.Node.Hosting.GameModules;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.32: modulbibliotekets GDScript lämnas till agenter som DROP-IN-kod.
/// Parsar den inte är den värre än ingenting - agenten klistrar in något
/// trasigt och får felsöka husets kod i stället för att bygga spelet.
/// Ingen modul har någonsin parse-verifierats mot riktig Godot.
/// </summary>
public class GameModuleGodotTests
{
    public static TheoryData<string> GodotModuler() =>
    [
        "inventory", "dialog", "quest", "save", "combat",
        "progression", "enemyai", "particles", "shop", "achievements",
    ];

    [Fact]
    public void ButikOchPrestationer_FinnsIRegistret()
    {
        // v2.30 gav composern kryssrutor for dem, men biblioteket hade ingen
        // modul - kryssen var tomma loften agenten fick uppfylla fran noll.
        var names = GameModuleLibrary.List().Select(m => m.Name).ToList();
        Assert.Contains("ShopModule", names);
        Assert.Contains("AchievementModule", names);
        Assert.NotNull(GameModuleLibrary.GetCode("shop", "godot"));
        Assert.NotNull(GameModuleLibrary.GetCode("achievements", "godot"));
        // Och kryssrutorna maste peka pa dem.
        Assert.Equal("shop", BuildDirectives.Catalog["shop"].Module);
        Assert.Equal("achievements", BuildDirectives.Catalog["achievements"].Module);
    }

    [Fact]
    public void ButikenHarRiktigMekanik_InteBaraEnSkal()
    {
        var gd = GameModuleLibrary.GetCode("shop", "godot")!;
        // Det som skiljer en butik fran en meny: valuta, stigande priser,
        // takniva, sparning och en effekt som MARKS i spelet.
        foreach (var s in new[] { "func earn(", "func cost(", "func buy(", "func maxed(",
            "func multiplier(", "FileAccess.open(SAVE", "cost_step" })
            Assert.Contains(s, gd);
    }

    [Fact]
    public void PrestationerHarVillkorPopupOchLista()
    {
        var gd = GameModuleLibrary.GetCode("achievements", "godot")!;
        foreach (var s in new[] { "func unlock(", "func report(", "func panel(",
            "_popup", "FileAccess.open(SAVE", "\"goal\"" })
            Assert.Contains(s, gd);
        // Minst 8 mal - farre kanns som en eftertanke.
        var count = gd.Split("{\"id\":").Length - 1;
        Assert.True(count >= 8, $"bara {count} prestationer - for fa for att kannas som ett system");
    }

    [Theory]
    [MemberData(nameof(GodotModuler))]
    public async Task VarjeModulsGDScript_ParsarIRiktigGodot(string modul)
    {
        // Gated men SKARPT dar godot finns. Detta ar enda satt att veta att
        // koden vi ger agenterna faktiskt gar att anvanda.
        var godot = ToolLocator.Find("godot");
        if (godot is null || !File.Exists(godot)) return;

        var code = GameModuleLibrary.GetCode(modul, "godot");
        Assert.False(string.IsNullOrWhiteSpace(code), $"{modul}: ingen godot-kod");

        var dir = Directory.CreateTempSubdirectory("ailocal-mod-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(dir, "project.godot"),
                "config_version=5\n\n[application]\nconfig/name=\"ModTest\"\n"
                + "config/features=PackedStringArray(\"4.3\")\n");
            File.WriteAllText(Path.Combine(dir, "Mod.gd"), code!);

            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(godot)
            {
                ArgumentList = { "--headless", "--path", dir, "--check-only", "--script", "res://Mod.gd" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            })!;
            var so = proc.StandardOutput.ReadToEndAsync();
            var se = proc.StandardError.ReadToEndAsync();
            await proc.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromMinutes(2)).Token);
            var output = await so + "\n" + await se;
            Assert.False(
                output.Contains("Parse Error") || output.Contains("SCRIPT ERROR")
                || output.Contains("Compile Error"),
                $"modulen '{modul}' parsar INTE i Godot - agenten far trasig drop-in-kod:\n{output}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }
}
