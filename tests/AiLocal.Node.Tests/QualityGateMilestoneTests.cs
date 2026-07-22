using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Node.Tests;

/// <summary>C5 (milstolpe-drivet bygge): en ren TEKNISK miss (contractUnmet &lt; 0)
/// behåller det snäva fixtaket; en KONTRAKTS-/milstolpe-miss får fortsätta upp
/// till milstolpe-taket SÅ LÄNGE ouppfyllda punkter minskar (framsteg), annars
/// stannar den av - bygget konvergerar alltid, aldrig runaway.</summary>
public class QualityGateMilestoneTests
{
    [Theory]
    // Teknisk miss (-1): snäva taket (maxFixRounds=2).
    [InlineData(0, -1, 999, true)]   // runda 0 < 2 -> fortsätt
    [InlineData(1, -1, 999, true)]   // runda 1 < 2 -> fortsätt
    [InlineData(2, -1, 999, false)]  // runda 2 >= 2 -> stanna
    // Milstolpe-miss (>0): förlängt tak (maxMilestoneRounds=4) MEDAN framsteg görs.
    [InlineData(0, 3, 9999, true)]   // framsteg (3 < 9999) -> fortsätt
    [InlineData(2, 2, 3, true)]      // framsteg (2 < 3), runda 2 - förbi maxFixRounds men milstolpen tillåter
    [InlineData(3, 1, 2, true)]      // framsteg (1 < 2) -> fortsätt
    [InlineData(4, 1, 2, false)]     // runda 4 >= 4 -> stanna (rundtak)
    [InlineData(2, 3, 3, false)]     // inget framsteg (3 >= 3) -> stanna
    [InlineData(2, 4, 3, false)]     // gick BAKÅT (4 > 3) -> stanna
    public void ShouldContinueFixing_TekniskSnavt_MilstolpeForlangtMedanFramsteg(
        int round, int contractUnmet, int prevUnmet, bool expected)
    {
        Assert.Equal(expected, AssignmentQualityGate.ShouldContinueFixing(round, contractUnmet, prevUnmet, 2, 4));
    }
}
