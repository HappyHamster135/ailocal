using System.Globalization;
using System.Text;

namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.31 punkt 2: ETT GENERERAT UI-TEMA. Mätningen över alla 21 kit gav noll
/// förekomster av Theme, StyleBoxFlat och NinePatch - varenda knapp i varenda
/// genererat spel var en rå Godot-standardknapp. Det är den tydligaste
/// prototypsignalen som finns, och den syns på FÖRSTA bildrutan i varje spel.
///
/// Samtidigt nådde <see cref="ArtBible"/>-paletten bara sprites och
/// molnpromptar - UI:t stod helt utanför projektets färgvärld, så meny och
/// spel såg ut att komma från olika produkter.
///
/// Temat skrivs som en riktig Theme-resurs och kopplas via projekt-
/// inställningen gui/theme/custom. Då ärver VARJE Control i spelet det
/// automatiskt - inget kit behöver ändras, och agentbyggd UI får det gratis.
/// </summary>
public static class UiTheme
{
    /// <summary>Bygger theme.tres ur projektets art-bibel: knappar, paneler,
    /// fält och etiketter i samma palett som spelets grafik.</summary>
    public static string Build(ArtBible bible)
    {
        var outline = ArtBible.Hex(bible.OutlineHex);
        var accent = ArtBible.Hex(bible.AccentRampHex[1]);
        var accentLight = ArtBible.Hex(bible.AccentRampHex[2]);
        var accentDark = ArtBible.Hex(bible.AccentRampHex[0]);

        // Panelytorna byggs ur konturfärgen så de sitter ihop med spelets
        // svarta silhuettlinjer i stället för att vara neutralgrå.
        var panel = Mix(outline, (14, 12, 20), 0.55);
        var normal = Mix(outline, accentDark, 0.28);
        var hover = Mix(outline, accent, 0.42);
        var pressed = Mix(outline, accentDark, 0.55);
        var disabled = Mix(outline, (60, 58, 68), 0.5);
        byte text = 242, textG = 244, textB = 250;

        var sb = new StringBuilder();
        // load_steps = antal sub_resources + 1
        sb.Append("[gd_resource type=\"Theme\" load_steps=7 format=3]\n\n");

        Box(sb, "btn_normal", normal, outline, 2, 8);
        Box(sb, "btn_hover", hover, accentLight, 2, 8);
        Box(sb, "btn_pressed", pressed, accentLight, 2, 8);
        Box(sb, "btn_disabled", disabled, outline, 1, 8);
        Box(sb, "btn_focus", normal, accentLight, 3, 8);
        Box(sb, "panel_bg", panel, outline, 2, 10);

        sb.Append("[resource]\n");
        // ---- Button: det som syns mest i varje spel ----
        sb.Append("Button/styles/normal = SubResource(\"btn_normal\")\n");
        sb.Append("Button/styles/hover = SubResource(\"btn_hover\")\n");
        sb.Append("Button/styles/pressed = SubResource(\"btn_pressed\")\n");
        sb.Append("Button/styles/disabled = SubResource(\"btn_disabled\")\n");
        sb.Append("Button/styles/focus = SubResource(\"btn_focus\")\n");
        sb.Append($"Button/colors/font_color = {Col(text, textG, textB)}\n");
        sb.Append($"Button/colors/font_hover_color = {Col(255, 255, 255)}\n");
        sb.Append($"Button/colors/font_pressed_color = {Col(accentLight)}\n");
        sb.Append($"Button/colors/font_disabled_color = {Col(150, 148, 158)}\n");
        // Kontur på knapptexten: läsbar mot vilken bakgrund som helst - flera
        // kit ritar spelvärlden RAKT BAKOM menyn.
        sb.Append($"Button/colors/font_outline_color = {Col(outline)}\n");
        sb.Append("Button/constants/outline_size = 4\n");
        sb.Append("Button/font_sizes/font_size = 20\n");

        // ---- Paneler: fanns inte alls - UI:t svävade fritt över spelet ----
        sb.Append("Panel/styles/panel = SubResource(\"panel_bg\")\n");
        sb.Append("PanelContainer/styles/panel = SubResource(\"panel_bg\")\n");
        sb.Append("PopupPanel/styles/panel = SubResource(\"panel_bg\")\n");

        // ---- Etiketter ----
        sb.Append($"Label/colors/font_color = {Col(text, textG, textB)}\n");
        sb.Append($"Label/colors/font_outline_color = {Col(outline)}\n");
        sb.Append("Label/constants/outline_size = 5\n");

        // ---- Reglage (volym i options-skärmen) ----
        sb.Append($"HSlider/colors/grabber_area = {Col(accent)}\n");
        sb.Append($"ProgressBar/colors/font_color = {Col(text, textG, textB)}\n");

        // ---- Kryssrutor och fält ----
        sb.Append($"CheckBox/colors/font_color = {Col(text, textG, textB)}\n");
        sb.Append($"CheckButton/colors/font_color = {Col(text, textG, textB)}\n");
        sb.Append("LineEdit/styles/normal = SubResource(\"btn_normal\")\n");
        sb.Append("LineEdit/styles/focus = SubResource(\"btn_focus\")\n");
        return sb.ToString();
    }

    private static void Box(StringBuilder sb, string id,
        (byte R, byte G, byte B) bg, (byte R, byte G, byte B) border, int borderW, int radius)
    {
        sb.Append($"[sub_resource type=\"StyleBoxFlat\" id=\"{id}\"]\n");
        sb.Append($"content_margin_left = 14.0\ncontent_margin_top = 8.0\n");
        sb.Append($"content_margin_right = 14.0\ncontent_margin_bottom = 8.0\n");
        sb.Append($"bg_color = {Col(bg)}\n");
        sb.Append($"border_width_left = {borderW}\nborder_width_top = {borderW}\n");
        sb.Append($"border_width_right = {borderW}\nborder_width_bottom = {borderW}\n");
        sb.Append($"border_color = {Col(border)}\n");
        sb.Append($"corner_radius_top_left = {radius}\ncorner_radius_top_right = {radius}\n");
        sb.Append($"corner_radius_bottom_right = {radius}\ncorner_radius_bottom_left = {radius}\n\n");
    }

    private static string Col((byte R, byte G, byte B) c) => Col(c.R, c.G, c.B);

    private static string Col(byte r, byte g, byte b) =>
        string.Create(CultureInfo.InvariantCulture,
            $"Color({r / 255.0:0.####}, {g / 255.0:0.####}, {b / 255.0:0.####}, 1)");

    private static (byte, byte, byte) Mix((byte R, byte G, byte B) a, (byte R, byte G, byte B) b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return ((byte)(a.R + (b.R - a.R) * t),
                (byte)(a.G + (b.G - a.G) * t),
                (byte)(a.B + (b.B - a.B) * t));
    }
}
