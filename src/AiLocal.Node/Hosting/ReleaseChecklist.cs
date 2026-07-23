namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.13: RELEASE-CHECKLISTAN for smaspel - de sma sakerna som skiljer
/// "prototyp som rakar funka" fran "spel man vagar lagga pa itch.io":
/// omstart utan att starta om programmet, volym/mute, paus, sparat basta
/// resultat och en riktig fonstertitel. Allt ar RADGIVANDE (aldrig hart) -
/// fynden hamnar i grindens rapport dar utvecklingsrundornas kritiker
/// laser dem, sa checklistan driver polish i stallet for att falla bygget.
/// Ren grep, ingen LLM - samma filosofi som AntiPatternDb.
/// </summary>
public static class ReleaseChecklist
{
    public static IReadOnlyList<string> Review(string projectRoot, string? engine)
    {
        var notes = new List<string>();
        try
        {
            if (engine is not ("godot" or "unity" or "html5")) return notes;
            var src = new System.Text.StringBuilder();
            foreach (var f in Directory.EnumerateFiles(projectRoot, "*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".gd") || f.EndsWith(".cs") || f.EndsWith(".js") || f.EndsWith(".html"))
                .Take(40))
            {
                try { src.Append(File.ReadAllText(f)).Append('\n'); } catch { /* olasbar fil hoppar vi */ }
            }
            var all = src.ToString();
            if (all.Length == 0) return notes;
            bool Has(params string[] needles) =>
                needles.Any(n => all.Contains(n, StringComparison.OrdinalIgnoreCase));

            if (!Has("KEY_R", "restart", "play again", "new_game", "newGame", "resetGame"))
                notes.Add("RELEASE-CHECKLISTA: ingen omstart hittad - efter game over ska R eller en knapp " +
                          "starta en ny runda; spelaren ska aldrig behova starta om programmet.");
            if (!Has("set_bus_volume_db", "set_bus_mute", "KEY_M", "volume", "muted"))
                notes.Add("RELEASE-CHECKLISTA: ingen volymkontroll/mute hittad (M for mute och -/+ for niva ar arkadribban).");
            if (!Has("paused", "ui_cancel", "KEY_ESCAPE", "pause"))
                notes.Add("RELEASE-CHECKLISTA: ingen paus hittad - Esc ska pausa och ateruppta spelet.");
            if (!Has("user://", "localStorage"))
                notes.Add("RELEASE-CHECKLISTA: inget sparat framsteg/basta resultat hittat (user:// eller localStorage) - " +
                          "aven sma arkadspel ska minnas sitt highscore mellan korningar.");
            // v2.15 spelskalet: options-skarm och quit-val ar standard i ALLA
            // riktiga spel (agarens dom). Shell.gd-hjalparna gor det billigt.
            if (!Has("options_panel", "OptionsMenu", "\"Options\"", "\"Settings\"", "settings_menu"))
                notes.Add("RELEASE-CHECKLISTA: ingen Options/Settings-skarm hittad - volym, mute och fullskarm ska ga att " +
                          "stalla i spelet (Shell.options_panel ger den fardig).");
            if (!Has("fullscreen", "window_set_mode", "WINDOW_MODE"))
                notes.Add("RELEASE-CHECKLISTA: ingen fullskarmsvaxel hittad (DisplayServer.window_set_mode eller motsvarande).");
            if (!Has("quit()", ".quit", "\"Quit\""))
                notes.Add("RELEASE-CHECKLISTA: inget Quit-val hittat - spelet ska ga att avsluta fran menyn, inte bara via X.");

            if (engine == "godot")
            {
                var proj = Path.Combine(projectRoot, "project.godot");
                if (File.Exists(proj))
                {
                    var m = System.Text.RegularExpressions.Regex.Match(
                        File.ReadAllText(proj), "config/name=\"([^\"]*)\"");
                    var name = m.Success ? m.Groups[1].Value.Trim() : "";
                    if (name.Length == 0
                        || name.Equals("Game", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("New Game Project", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Godot", StringComparison.OrdinalIgnoreCase))
                        notes.Add($"RELEASE-CHECKLISTA: fonstertiteln ar generisk (\"{name}\") - satt spelets " +
                                  "riktiga namn i project.godot (application/config/name).");
                }
            }
        }
        catch { /* checklistan ar radgivande, aldrig ett krav */ }
        return notes;
    }
}
