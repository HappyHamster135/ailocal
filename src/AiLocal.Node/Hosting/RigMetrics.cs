namespace AiLocal.Node.Hosting;

/// <summary>
/// v2.29 etapp 1: en 3D-karaktärs PROPORTIONER i världsenheter, härledda ur
/// samma <see cref="CharacterTraits"/> som driver 2D-spriten. Det är kopplingen
/// som gör att "player" känns igen oavsett om spelet är 2D eller 3D: samma
/// kroppstyp ger smal/normal/bred figur i båda.
///
/// Alla mått är i meter och summerar till <see cref="HeightM"/> - en figur får
/// aldrig sväva eller sjunka genom golvet, och kiten kan sluta hårdkoda
/// 1.6/0.9/0.42 som ögonhöjd och kapselmått.
/// </summary>
public sealed record RigMetrics(
    double HeightM,
    double UpperLeg, double LowerLeg,
    double TorsoH, double HeadH,
    double UpperArm, double LowerArm,
    double ShoulderX, double HipX,
    double BodyW, double BodyD,
    double LimbW, double HeadW)
{
    /// <summary>Höften sitter där benen slutar - ankaret all posering utgår
    /// från. Fötterna ligger per definition på y=0.</summary>
    public double HipY => UpperLeg + LowerLeg;
    public double ShoulderY => HipY + TorsoH;
    public double EyeY => ShoulderY + HeadH * 0.60;
    public double ChestY => HipY + TorsoH * 0.60;
    /// <summary>Kapselmått för kit som vill ha en kollisionsform som
    /// motsvarar figuren i stället för en godtycklig literal.</summary>
    public double CapsuleRadius => BodyW * 0.55;
    public double CapsuleHeight => HeightM;
}

public static class RigMetricsFactory
{
    public const double DefaultHeight = 1.70;

    /// <summary>Deterministisk: samma drag ger alltid samma proportioner.</summary>
    public static RigMetrics For(CharacterTraits traits, double heightM = DefaultHeight)
    {
        var h = Math.Clamp(heightM, 0.6, 4.0);
        // Stiliserade proportioner: större huvud än anatomiskt korrekt, för
        // att figuren ska vara läsbar när den är 40 px hög på skärmen.
        var legTotal = 0.45 * h;
        var torso = 0.32 * h;
        var head = 0.23 * h;

        var (bodyW, limbW, headW) = (traits?.Body ?? "normal") switch
        {
            "slim" => (0.190 * h, 0.056 * h, 0.160 * h),
            "broad" => (0.270 * h, 0.080 * h, 0.180 * h),
            _ => (0.230 * h, 0.068 * h, 0.170 * h),
        };

        return new RigMetrics(
            HeightM: h,
            UpperLeg: legTotal * 0.55,
            LowerLeg: legTotal * 0.45,
            TorsoH: torso,
            HeadH: head,
            UpperArm: 0.165 * h,
            LowerArm: 0.150 * h,
            // Armarna hängs precis utanför bålen (0.42 i stället för 0.5 av
            // lemtjockleken - annars syns en glipa mellan axel och bål).
            ShoulderX: bodyW * 0.5 + limbW * 0.42,
            HipX: bodyW * 0.26,
            BodyW: bodyW,
            BodyD: bodyW * 0.62,
            LimbW: limbW,
            HeadW: headW);
    }

    /// <summary>GDScript-literal för Cast3D.gd. Skrivs som data i stället för
    /// att räknas om i GDScript - annars finns proportionerna på två ställen
    /// och glider isär tyst.</summary>
    public static string ToGd(RigMetrics m) =>
        "{" +
        $"\"height\": {F(m.HeightM)}, \"upper_leg\": {F(m.UpperLeg)}, \"lower_leg\": {F(m.LowerLeg)}, " +
        $"\"torso_h\": {F(m.TorsoH)}, \"head_h\": {F(m.HeadH)}, " +
        $"\"upper_arm\": {F(m.UpperArm)}, \"lower_arm\": {F(m.LowerArm)}, " +
        $"\"shoulder_x\": {F(m.ShoulderX)}, \"hip_x\": {F(m.HipX)}, " +
        $"\"body_w\": {F(m.BodyW)}, \"body_d\": {F(m.BodyD)}, " +
        $"\"limb_w\": {F(m.LimbW)}, \"head_w\": {F(m.HeadW)}, " +
        $"\"hip_y\": {F(m.HipY)}, \"shoulder_y\": {F(m.ShoulderY)}, " +
        $"\"eye_y\": {F(m.EyeY)}, \"chest_y\": {F(m.ChestY)}, " +
        $"\"cap_r\": {F(m.CapsuleRadius)}, \"cap_h\": {F(m.CapsuleHeight)}" +
        "}";

    private static string F(double v) =>
        v.ToString("0.####", System.Globalization.CultureInfo.InvariantCulture);
}
