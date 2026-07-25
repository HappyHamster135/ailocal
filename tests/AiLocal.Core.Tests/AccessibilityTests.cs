using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>
/// v2.36: tillgängligheten. Kiten kodar mening i FÄRG — röd fiende, grön sak,
/// grön hälsomätare. För ungefär var tolfte man är rött och grönt samma färg,
/// och då är spelet inte svårare utan oläsligt. Skärmskakning är den andra
/// halvan: den utlöser illamående hos rörelsekänsliga spelare.
///
/// Poängen med båda fixarna är att de sitter i ETT ställe var: färgfiltret i
/// Art (som alla ritningar går genom sedan v2.35) och rörelseskalan i det
/// enda uttrycket som tar ut skakningen.
/// </summary>
public class AccessibilityTests
{
    static string Src(string fil) => File.ReadAllText(Path.Combine(
        RepoRoot(), "src", "AiLocal.Node", "Hosting", fil)).Replace("\r\n", "\n");

    [Fact]
    public void Fargfiltret_GarGenomVarjeRitfunktion()
    {
        var s = Src("GameScaffoldService.cs");
        Assert.Contains("static var cb_mode := 0", s);
        Assert.Contains("static func cb(col: Color) -> Color:", s);
        // Varje ritfunktion som tar en farg maste filtrera den - annars blir
        // laget en halvmesyr dar delar av spelet byter palett och delar inte.
        foreach (var sig in new[] { "static func bg(", "static func panel(",
            "static func orb(", "static func bar(", "static func connect_path(" })
        {
            var i = s.IndexOf(sig, StringComparison.Ordinal);
            Assert.True(i > 0, $"{sig} saknas");
            var body = s[i..(i + 700)];
            Assert.True(body.Contains("cb("), $"{sig} filtrerar inte fargen genom cb()");
        }
    }

    [Fact]
    public void FormSkiljerFiendeFranForemalOchSpelare()
    {
        // Det VIKTIGASTE greppet: mening far aldrig bara pa farg allena. En
        // rod cirkel och en gron cirkel ar samma cirkel for den fargblinde.
        var s = Src("GameScaffoldService.cs");
        Assert.Contains("static func threat(", s);
        var kits = Src("GameScaffoldService.GodotKits.cs");
        // Spelare = ringad token, fiende = taggig threat, foremal = slat orb.
        Assert.True(kits.Split("Art.threat(self").Length - 1 >= 3,
            "fienderna ar fortfarande vanliga orbar - formen skiljer dem inte at");
        Assert.Contains("Art.token(self", kits);
        Assert.Contains("Art.orb(self", kits);
    }

    [Fact]
    public void Rorelseskalan_TackerVARJEStalleSomTarUtSkakning()
    {
        var kits = Src("GameScaffoldService.GodotKits.cs") + Src("GameScaffoldService.GodotIso.cs");
        // Karnan: skakningen tas ut pa exakt ett satt i alla kit, sa en enda
        // faktor racker. Blir det kvar ett oskalat uttag skakar det spelet
        // anda och flaggan blir en lognaktig kryssruta.
        Assert.DoesNotContain("randf_range(-shake, shake)", kits);
        var skalade = kits.Split("randf_range(-shake * Shell.motion, shake * Shell.motion)").Length - 1;
        Assert.True(skalade >= 30, $"bara {skalade} skakningsuttag ar skalade");
        Assert.Contains("static var motion := 1.0", Src("GameScaffoldService.cs"));
    }

    [Fact]
    public void ValenSparasOchGallerFranForstaRutan()
    {
        var s = Src("GameScaffoldService.cs");
        Assert.Contains("\"colorblind\": 0", s);
        Assert.Contains("\"shake\": true", s);
        // apply_settings anropas av startup() - annars kor spelet en ruta med
        // fel palett varje gang det startar.
        var i = s.IndexOf("static func apply_settings(", StringComparison.Ordinal);
        var body = s[i..(i + 1400)];
        Assert.Contains("Art.cb_mode =", body);
        Assert.Contains("motion = 1.0 if", body);
        // Och de maste GA ATT STALLA - annars ar de dolda for spelaren.
        Assert.Contains("cb_pick.item_selected.connect", s);
        Assert.Contains("shake.toggled.connect", s);
    }

    /// <summary>
    /// Fällan som kostade en runda i v2.36: Art.gd är MELLANSLAGS-indenterad
    /// och Shell.gd TABB-indenterad. Godot vägrar blanda ("Used space
    /// character for indentation instead of tab as used before in the file")
    /// och HELA filen faller — vilket i sin tur välte varje kit som
    /// preloadar den. Ingen C#-kompilator ser det.
    /// </summary>
    [Fact]
    public void IngenGeneradGDScript_BlandarTabbOchMellanslag()
    {
        var brister = new List<string>();
        foreach (var fil in Directory.GetFiles(Path.Combine(
            RepoRoot(), "src", "AiLocal.Node", "Hosting"), "GameScaffoldService*.cs"))
        {
            var src = File.ReadAllText(fil).Replace("\r\n", "\n");
            foreach (System.Text.RegularExpressions.Match block in
                System.Text.RegularExpressions.Regex.Matches(src, "\"\"\"\n(.*?)\n\"\"\"",
                    System.Text.RegularExpressions.RegexOptions.Singleline))
            {
                var gd = block.Groups[1].Value;
                if (!gd.Contains("\nfunc ") && !gd.Contains("\nstatic func ")) continue;
                var rader = gd.Split('\n');
                var tabb = rader.Count(l => l.StartsWith('\t'));
                var mellan = rader.Count(l => l.StartsWith("    "));
                if (tabb > 0 && mellan > 0)
                {
                    var namn = rader.FirstOrDefault(l => l.StartsWith("class_name ") || l.StartsWith("extends ")) ?? "?";
                    brister.Add($"{Path.GetFileName(fil)} ({namn.Trim()}): {tabb} tabbrader + {mellan} mellanslagsrader");
                }
            }
        }
        Assert.True(brister.Count == 0,
            "Godot vagrar blandad indentering och HELA filen faller:\n  " + string.Join("\n  ", brister));
    }

    static string RepoRoot()
    {
        var d = AppContext.BaseDirectory;
        for (var i = 0; i < 8 && d is not null; i++)
        {
            if (Directory.Exists(Path.Combine(d, "src", "AiLocal.Node"))) return d;
            d = Path.GetDirectoryName(d);
        }
        throw new DirectoryNotFoundException("hittar inte repo-roten");
    }
}
