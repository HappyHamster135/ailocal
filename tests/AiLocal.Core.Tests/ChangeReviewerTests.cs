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
}
