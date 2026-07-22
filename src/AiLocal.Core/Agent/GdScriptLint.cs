namespace AiLocal.Core.Agent;

/// <summary>
/// v1.94: snabb JS-ism-detektor för GDScript-filer vid write_file - INTE en
/// full parser (det gör riktiga godot i verify/grinden), utan en tripwire för
/// de vanor svaga modeller tar med sig från JavaScript och som Godot vägrar
/// parsa. Sett live: "// Godot Script"-kommentarer, function-nyckelordet och
/// funcar utan kropp. Fel fångas vid SKRIVNINGEN med facit i felmeddelandet,
/// i stället för att bygget kraschar långt senare i verify.
/// </summary>
public static class GdScriptLint
{
    /// <summary>Första uppenbara felet med rättningsfacit, eller null när
    /// inget av tripwire-mönstren träffar (filen kan ändå ha andra fel -
    /// riktiga parsen sker i verify).</summary>
    public static string? Check(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//"))
                return $"rad {i + 1} börjar med \"//\" - GDScript använder \"#\" för kommentarer, inte \"//\".";
            if (trimmed.StartsWith("/*"))
                return $"rad {i + 1} använder \"/* */\" - GDScript använder \"#\" för kommentarer.";
            if (trimmed.StartsWith("function ") || trimmed.StartsWith("function("))
                return $"rad {i + 1} använder \"function\" - GDScript använder \"func\".";

            // Tom funktionskropp: "func x():" där nästa icke-tomma rad ligger
            // på SAMMA eller lägre indentering (= ingen kropp alls). Godot:
            // "Expected indented block". Facit: minst en rad, t.ex. pass.
            if (trimmed.StartsWith("func ") && trimmed.TrimEnd().EndsWith(":"))
            {
                var indent = lines[i].Length - trimmed.Length;
                var next = NextNonEmpty(lines, i + 1);
                if (next is null)
                    return $"rad {i + 1}: \"func\" utan kropp i slutet av filen - lägg minst \"pass\" (indenterad) i kroppen.";
                var (nextLine, nextIdx) = next.Value;
                var nextIndent = nextLine.Length - nextLine.TrimStart().Length;
                if (nextIndent <= indent)
                    return $"rad {i + 1}: \"func\" utan indenterad kropp (rad {nextIdx + 1} ligger på samma nivå) - lägg minst \"pass\" i kroppen.";
            }
        }
        return null;
    }

    /// <summary>v1.99: UX-tripwires - mönster som PARSAR men ser trasiga ut
    /// på skärmen (live-sett i en levererad build: HUD med rå "Omgang %d: %s"
    /// och synliga [color=white]-taggar). Separata från Check() eftersom
    /// facit-texten är en annan: Godot vägrar INTE parsa dessa.</summary>
    public static string? CheckUx(string content)
    {
        var lines = content.Replace("\r\n", "\n").Split('\n');
        var hasRichText = content.Contains("RichTextLabel");
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // .text = "...%d...%s..." där strängen AVSLUTAR satsen = ingen
            // %-operator någonsin -> spelaren ser platshållarna råa.
            // Bara direkta literaler flaggas (mallar i variabler som
            // formateras senare är legitima och lämnas ifred).
            var m = System.Text.RegularExpressions.Regex.Match(
                line, "\\.text\\s*\\+?=\\s*\"([^\"]*)\"\\s*$");
            if (m.Success)
            {
                var lit = m.Groups[1].Value;
                if (lit.Contains("%d") || lit.Contains("%s") || lit.Contains("%f"))
                    return $"rad {i + 1}: formatsträng tilldelas .text UTAN %-operator - spelaren ser rå \"%d/%s\" på skärmen. Skriv: nod.text = \"...\" % [värden].";
            }

            // BBCode-taggar i strängar utan RichTextLabel i filen: en vanlig
            // Label visar "[color=...]" RÅTT för spelaren.
            if (!hasRichText &&
                (line.Contains("[color=") || line.Contains("[/color]") || line.Contains("[font=") || line.Contains("[img]")))
                return $"rad {i + 1}: BBCode-taggar ([color=...]) i en sträng, men filen använder ingen RichTextLabel - en vanlig Label visar taggarna RÅTT. Använd RichTextLabel med bbcode_enabled=true, eller ta bort taggarna.";
        }
        return null;
    }

    private static (string Line, int Index)? NextNonEmpty(string[] lines, int from)
    {
        for (var i = from; i < lines.Length; i++)
        {
            // Kommentarsrader räknas INTE som kropp oavsett indentering -
            // Godot kräver en riktig sats: "func _ready():" följd av enbart
            // "    # Initialize game state" är fortfarande "Expected indented
            // block" (exakt den fil som sågs live).
            var t = lines[i].Trim();
            if (t.Length == 0 || t.StartsWith("#")) continue;
            return (lines[i], i);
        }
        return null;
    }
}
