using AiLocal.Node.Hosting;
using Xunit;

namespace AiLocal.Core.Tests;

/// <summary>v1.48.0: regissörens idéverkstad - genrebanker, fröslumpning per
/// körning och fallback-kontrakt som divergerar även utan modell.</summary>
public class IdeaBankTests
{
    [Theory]
    [InlineData("management", "ungdomsakademi")]
    [InlineData("simulator", "ungdomsakademi")]
    [InlineData("idle", "ungdomsakademi")]
    [InlineData("rpg", "boss")]
    [InlineData("roguelike", "boss")]
    [InlineData("platformer", "dubbelhopp")]
    [InlineData("quiz", "riskmekanik")]
    public void SeedsFor_RattBankPerGenre(string genre, string expectedFragment)
    {
        var bank = GenreIdeaBank.SeedsFor(genre);
        Assert.NotEmpty(bank);
        Assert.Contains(bank, s => s.Contains(expectedFragment, StringComparison.OrdinalIgnoreCase));
        Assert.Equal(bank.Count, bank.Distinct().Count());
    }

    [Fact]
    public void PickSeeds_RattAntalUrBanken_OchVarierarMellanKorningar()
    {
        var bank = GenreIdeaBank.SeedsFor("management").ToHashSet();
        var combos = new HashSet<string>();
        for (var i = 0; i < 20; i++)
        {
            var seeds = GenreIdeaBank.PickSeeds("management", 3);
            Assert.Equal(3, seeds.Count);
            Assert.Equal(3, seeds.Distinct().Count());
            Assert.All(seeds, s => Assert.Contains(s, bank));
            combos.Add(string.Join("|", seeds.OrderBy(s => s)));
        }
        // 12 frön ⇒ 220 möjliga kombinationer - 20 dragningar som ALLA är
        // identiska vore ett slumpfel av astronomisk osannolikhet.
        Assert.True(combos.Count >= 2, "fröslumpningen gav samma kombination 20 gånger i rad");
    }

    [Fact]
    public void FallbackContract_MedFron_DivergerarFranStandardkontraktet()
    {
        var seeds = new[] { "derbyn mot en namngiven rival", "vadereffekter som paverkar utfall" };
        var withSeeds = DirectorPass.FallbackContract("bygg ett fotbolls managerspel", seeds);
        var plain = DirectorPass.FallbackContract("bygg ett fotbolls managerspel");

        Assert.Contains(withSeeds.Criteria, c => c.Contains("derbyn"));
        Assert.Contains(withSeeds.Criteria, c => c.Contains("vadereffekter"));
        Assert.Contains("derbyn", withSeeds.Twist);
        Assert.Equal(plain.Criteria.Count + 2, withSeeds.Criteria.Count);
        // Baskriterierna (golvet) finns kvar orörda.
        Assert.All(plain.Criteria, c => Assert.Contains(c, withSeeds.Criteria));
    }
}
