using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>ParseVerdict is the pure core of the AI write-review: everything
/// around it (provider call, diff building) fails open by design, so the
/// verdict parsing is where correctness actually lives.</summary>
public class ChangeReviewerTests
{
    [Theory]
    [InlineData("GODKÄNN")]
    [InlineData("godkänn")]
    [InlineData("  GODKÄNN  ")]
    [InlineData("GODKANN")]
    [InlineData("APPROVE")]
    [InlineData("Godkänn - ser bra ut.")]
    public void ParseVerdict_Approvals(string reply)
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(reply);
        Assert.True(approved);
        Assert.Null(reason);
    }

    [Theory]
    [InlineData("AVVISA: filen är trasig JSON, stäng klammern.", "filen är trasig JSON, stäng klammern.")]
    [InlineData("avvisa: fel fil.", "fel fil.")]
    [InlineData("REJECT: wrong file.", "wrong file.")]
    public void ParseVerdict_Rejections_CarryTheReason(string reply, string expectedReason)
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(reply);
        Assert.False(approved);
        Assert.Equal(expectedReason, reason);
    }

    [Fact]
    public void ParseVerdict_RejectionWithoutColon_GetsFallbackReason()
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict("AVVISA");
        Assert.False(approved);
        Assert.False(string.IsNullOrWhiteSpace(reason));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("Jag är osäker på vad du menar?")]
    [InlineData("Som AI-modell kan jag inte...")]
    public void ParseVerdict_UnparseableReplies_FailOpen(string? reply)
    {
        // A broken reviewer must degrade to "no review", never block work -
        // see ChangeReviewer's class doc.
        var (approved, _) = ChangeReviewer.ParseVerdict(reply);
        Assert.True(approved);
    }

    [Theory]
    // v1.99, exakta live-avslagen: leverantörens PII-filter maskade GILTIG
    // kod (PackedVector2Array(), particle_burst, efternamnslistor) till
    // [ADDRESS] i diffen granskaren SÅG - och den avvisade korrekt kod som
    // "ogiltig syntax". Skrivvakterna garanterar att markörer aldrig når
    // disk, så ett avslag som citerar en markör är per definition ett
    // kanalartefakt -> fail open.
    [InlineData("AVVISA: `var buf := [ADDRESS]()` är inte giltig GDScript-syntax; använd PackedVector2Array().")]
    [InlineData("AVVISA: Diffen innehåller `[ADDRESS]`-platshållare som skapar syntaxfel i GDScript.")]
    [InlineData("REJECT: LAST_NAMES innehåller platshållaren \"[ADDRESS]\" som inte är ett riktigt efternamn.")]
    [InlineData("AVVISA: mönstret \\[ADDRESS\\] förekommer i animate_button.")]
    public void ParseVerdict_MaskningsartefaktIAvslaget_FailOpen(string reply)
    {
        var (approved, reason) = ChangeReviewer.ParseVerdict(reply);
        Assert.True(approved);
        Assert.Null(reason);
    }

    [Fact]
    public void ParseVerdict_RiktigtAvslag_PaverkasInteAvArtefaktvakten()
    {
        // Ett avslag UTAN markörer blockerar fortfarande - vakten är smal.
        var (approved, _) = ChangeReviewer.ParseVerdict("AVVISA: filen raderar hela nivådatan som uppgiften kräver.");
        Assert.False(approved);
    }
}
