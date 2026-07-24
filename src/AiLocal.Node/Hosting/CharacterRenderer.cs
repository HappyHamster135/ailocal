namespace AiLocal.Node.Hosting;

/// <summary>En pose som DATA. Tidigare var poserna inbakade i en switch inne
/// i ritfunktionen; nu kan klipp byggas av poser utan att motorn ändras.</summary>
public readonly record struct Pose(string Clip, int WalkPhase, int Bob);

/// <summary>Posuppsättningar och klippen de bildar. <see cref="AnimsFor"/>
/// RÄKNAR StartFrame ur faktiskt index - den gamla koden bokförde index på
/// två ställen (frame-listan och SpriteAnim-konstruktorn), vilket tyst går
/// isär så fort en pose läggs till.</summary>
public static class PoseLib
{
    /// <summary>Etapp 1 håller kontraktet oförändrat: idle 0-1, walk 2-5,
    /// samma namn och fps som PixelAnimator gav - därför behöver inget
    /// kit-GDScript röras.</summary>
    public static readonly Pose[] Standard =
    [
        new("idle", -1, 0),
        new("idle", -1, 1),
        new("walk", 0, 0),
        new("walk", 1, 1),
        new("walk", 2, 0),
        new("walk", 3, 1),
    ];

    private static readonly Dictionary<string, (double Fps, bool Loop)> ClipInfo = new()
    {
        ["idle"] = (3, true),
        ["walk"] = (10, true),
    };

    public static IReadOnlyList<SpriteAnim> AnimsFor(IReadOnlyList<Pose> poses)
    {
        var anims = new List<SpriteAnim>();
        var i = 0;
        while (i < poses.Count)
        {
            var clip = poses[i].Clip;
            var start = i;
            while (i < poses.Count && poses[i].Clip == clip) i++;
            var (fps, loop) = ClipInfo.TryGetValue(clip, out var info) ? info : (8, true);
            anims.Add(new SpriteAnim(clip, start, i - start, fps, loop));
        }
        return anims;
    }
}

/// <summary>
/// v2.29: EN parametrisk ritmotor för alla karaktärer. Ritar en
/// <see cref="CharacterSpec"/> i en given <see cref="Pose"/>. Eftersom varje
/// bild av en karaktär går genom samma spec kan två renderingar av samma
/// figur inte skilja sig - det är identitetsgarantin i praktiken.
///
/// Bevarar teknik och utseende från PixelAnimator: ljus från vänster-topp,
/// benen står på marken (bob flyttar bara överkroppen så figuren "andas" i
/// stället för att sväva), och ett avslutande INNERLINE-pass som ger den
/// slutna mörka silhuettlinjen som är pixelartens signatur.
/// </summary>
public static class CharacterRenderer
{
    private readonly record struct Layout(int TorsoX0, int TorsoX1, int ArmL, int ArmR);

    private static Layout LayoutFor(string body) => body switch
    {
        "slim" => new Layout(9, 14, 7, 16),
        "broad" => new Layout(7, 16, 5, 18),
        _ => new Layout(8, 15, 6, 17),
    };

    public static byte[] Draw(CharacterSpec spec, Pose pose, int frame)
    {
        var raw = new byte[frame * frame * 4];
        int S(int v) => v * frame / 24; // regionerna är författade för 24 px

        var p = spec.Palette;
        var outline = ArtBible.Hex(p.Outline);
        var skinD = ArtBible.Hex(p.SkinRamp[0]);
        var skin = ArtBible.Hex(p.SkinRamp[1]);
        var skinL = ArtBible.Hex(p.SkinRamp[2]);
        var shirtD = ArtBible.Hex(p.ShirtRamp[0]);
        var shirt = ArtBible.Hex(p.ShirtRamp[1]);
        var shirtL = ArtBible.Hex(p.ShirtRamp[2]);
        var pantsD = ArtBible.Hex(p.PantsRamp[0]);
        var pants = ArtBible.Hex(p.PantsRamp[1]);
        var hairD = ArtBible.Hex(p.HairRamp[0]);
        var hair = ArtBible.Hex(p.HairRamp[1]);
        var shoe = ArtBible.Hex(p.Shoe);
        var eye = ArtBible.Hex(p.Eye);
        var shine = ((byte)255, (byte)255, (byte)255);

        void Px(int x, int y, (byte, byte, byte) c)
        {
            if (x < 0 || y < 0 || x >= frame || y >= frame) return;
            var i = (y * frame + x) * 4;
            raw[i] = c.Item1; raw[i + 1] = c.Item2; raw[i + 2] = c.Item3; raw[i + 3] = 255;
        }
        void Clear(int x, int y)
        {
            if (x < 0 || y < 0 || x >= frame || y >= frame) return;
            raw[(y * frame + x) * 4 + 3] = 0;
        }
        void Rect(int x0, int y0, int x1, int y1, (byte, byte, byte) fill)
        {
            for (var y = y0; y <= y1; y++)
                for (var x = x0; x <= x1; x++)
                    Px(x, y, fill);
        }
        void Shaded(int x0, int y0, int x1, int y1,
            (byte, byte, byte) dark, (byte, byte, byte) baseCol, (byte, byte, byte) light)
        {
            Rect(x0, y0, x1, y1, baseCol);
            for (var y = y0; y <= y1; y++) Px(x0, y, light);
            for (var x = x0; x <= x1; x++) Px(x, y0, light);
            for (var y = y0 + 1; y <= y1; y++) Px(x1, y, dark);
            for (var x = x0 + 1; x <= x1; x++) Px(x, y1, dark);
        }

        var lay = LayoutFor(spec.Traits.Body);

        // ---- ben + skor (star alltid pa marken, paverkas ej av bob) --------
        int lx = 0, rx = 0, lLift = 0, rLift = 0;
        switch (pose.WalkPhase)
        {
            case 0: lx = -S(1); rx = S(1); rLift = S(2); break;
            case 1: rLift = S(1); break;
            case 2: lx = S(1); rx = -S(1); lLift = S(2); break;
            case 3: lLift = S(1); break;
        }
        var legTop = S(17);
        var shoeY = S(21);
        Rect(S(9) + lx, legTop, S(10) + lx, shoeY - 1 - lLift, pants);
        for (var y = legTop; y <= shoeY - 1 - lLift; y++) Px(S(10) + lx, y, pantsD);
        Rect(S(13) + rx, legTop, S(14) + rx, shoeY - 1 - rLift, pants);
        for (var y = legTop; y <= shoeY - 1 - rLift; y++) Px(S(14) + rx, y, pantsD);
        Rect(S(8) + lx, shoeY - lLift, S(10) + lx, S(22) - lLift, shoe);
        Rect(S(12) + rx, shoeY - rLift, S(14) + rx, S(22) - rLift, shoe);

        // ---- overkropp (bob = andning/steg) --------------------------------
        var dy = pose.Bob;
        var armSwing = pose.WalkPhase == 0 ? S(1) : pose.WalkPhase == 2 ? -S(1) : 0;
        // Armarna far basfargen med mork YTTERkant - med enbart shirtD
        // smalte de ihop med bålen och figuren sag armlos ut.
        Rect(S(lay.ArmL), S(11) + dy + armSwing, S(lay.ArmL + 1), S(15) + dy + armSwing, shirt);
        for (var y = S(11) + dy + armSwing; y <= S(15) + dy + armSwing; y++) Px(S(lay.ArmL), y, shirtD);
        Px(S(lay.ArmL), S(16) + dy + armSwing, skin);
        Px(S(lay.ArmL + 1), S(16) + dy + armSwing, skin);
        Rect(S(lay.ArmR - 1), S(11) + dy - armSwing, S(lay.ArmR), S(15) + dy - armSwing, shirt);
        for (var y = S(11) + dy - armSwing; y <= S(15) + dy - armSwing; y++) Px(S(lay.ArmR), y, shirtD);
        Px(S(lay.ArmR - 1), S(16) + dy - armSwing, skin);
        Px(S(lay.ArmR), S(16) + dy - armSwing, skin);

        // trojan med ramp-shading + balte
        Shaded(S(lay.TorsoX0), S(10) + dy, S(lay.TorsoX1), S(16) + dy, shirtD, shirt, shirtL);
        Rect(S(lay.TorsoX0), S(16) + dy, S(lay.TorsoX1), S(16) + dy, pantsD);

        // huvud (hud-ramp) med rundade horn - huvudet halls konstant sa
        // proportionerna forblir lasbara oavsett kroppstyp
        Shaded(S(8), S(2) + dy, S(15), S(9) + dy, skinD, skin, skinL);
        Clear(S(8), S(2) + dy); Clear(S(15), S(2) + dy);
        Clear(S(8), S(9) + dy); Clear(S(15), S(9) + dy);

        // ---- har (drag) ----------------------------------------------------
        if (spec.Traits.Hair != "bald")
        {
            Rect(S(8), S(2) + dy, S(15), S(4) + dy, hair);
            for (var x = S(8); x <= S(15); x++) Px(x, S(4) + dy, hairD);
            switch (spec.Traits.Hair)
            {
                case "long":
                    Rect(S(8), S(4) + dy, S(9), S(7) + dy, hair);
                    Rect(S(14), S(4) + dy, S(15), S(7) + dy, hairD);
                    break;
                case "spiky":
                    Px(S(9), S(1) + dy, hair); Px(S(11), S(0) + dy, hair);
                    Px(S(11), S(1) + dy, hair); Px(S(13), S(1) + dy, hair);
                    break;
                case "ponytail":
                    Rect(S(6), S(4) + dy, S(7), S(8) + dy, hairD);
                    break;
                default: // short
                    Rect(S(8), S(4) + dy, S(9), S(6) + dy, hair);
                    Rect(S(14), S(4) + dy, S(15), S(6) + dy, hairD);
                    break;
            }
            Clear(S(8), S(2) + dy); Clear(S(15), S(2) + dy);
        }

        // ---- markering (rad 0-1 ar den enda lediga ytan i 24 px) ----------
        switch (spec.Traits.Mark)
        {
            case "horns":
                Px(S(8), S(1) + dy, skinD); Px(S(8), S(0) + dy, skinD);
                Px(S(15), S(1) + dy, skinD); Px(S(15), S(0) + dy, skinD);
                break;
            case "ears":
                Px(S(7), S(5) + dy, skin); Px(S(7), S(6) + dy, skinD);
                Px(S(16), S(5) + dy, skin); Px(S(16), S(6) + dy, skinD);
                break;
        }

        // ---- ansikte -------------------------------------------------------
        if (spec.Traits.Face == "visor")
        {
            Rect(S(8), S(6) + dy, S(15), S(7) + dy, eye);
            Px(S(10), S(6) + dy, shine);
        }
        else
        {
            Px(S(10), S(7) + dy, eye); Px(S(13), S(7) + dy, eye);
            if (p.EyeGlint)
            {
                Px(S(10), S(6) + dy, shine); Px(S(13), S(6) + dy, shine);
            }
            if (spec.Traits.Face == "beard")
            {
                // Kindskagg + haka, INTE en heltackande rand: den gamla
                // varianten lade en mork bjalke over hela underansiktet och
                // lasstes som smuts i stallet for skagg. Munnen lamnas synlig.
                Px(S(9), S(7) + dy, hairD); Px(S(14), S(7) + dy, hairD);
                Px(S(9), S(8) + dy, hairD); Px(S(14), S(8) + dy, hairD);
                Rect(S(10), S(9) + dy, S(13), S(9) + dy, hairD);
            }
            Px(S(11), S(8) + dy, skinD); Px(S(12), S(8) + dy, skinD);
        }

        // ---- innerline: sluten mork kontur runt hela silhuetten ------------
        var snap = (byte[])raw.Clone();
        bool Solid(int x, int y) => x >= 0 && y >= 0 && x < frame && y < frame && snap[(y * frame + x) * 4 + 3] > 0;
        for (var y = 0; y < frame; y++)
            for (var x = 0; x < frame; x++)
            {
                if (!Solid(x, y)) continue;
                if (!Solid(x - 1, y) || !Solid(x + 1, y) || !Solid(x, y - 1) || !Solid(x, y + 1))
                    Px(x, y, outline);
            }
        return raw;
    }
}
