using TankDraft.Match.Domain;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class ExpandedRosterTests
{
    [Fact]
    public void Extended_catalog_accepts_distinct_four_slot_decks_and_offers_only_their_content()
    {
        var units = Enumerable.Range(0, 13).Select(i => new MatchUnitDefinition("unit." + i, 1, i != 0)).ToArray();
        var first = new[] { "unit.0", "unit.7", "unit.10", "unit.12" };
        var second = new[] { "unit.1", "unit.3", "unit.8", "unit.11" };
        Assert.True(MatchService.IsSupportedDeck(first, units));
        Assert.True(MatchService.IsSupportedDeck(second, units));
        var match = new MatchService(new MatchRules(4, 3, 64, 20, 20, 1, 15), units, first, second, "", 43);
        for (int choice = 0; choice < 3; choice++)
        {
            Assert.All(match.GetOffers(0), offer => Assert.Contains(offer.UnitId, first));
            Assert.All(match.GetOffers(1), offer => Assert.Contains(offer.UnitId, second));
            long token = match.ChoiceToken;
            Assert.True(match.TryChoose(0, token, 0, out var firstReason), firstReason);
            Assert.True(match.TryChoose(1, token, 0, out var secondReason), secondReason);
        }
        Assert.All(match.GetArmy(0), unit => Assert.Contains(unit.UnitId, first));
        Assert.All(match.GetArmy(1), unit => Assert.Contains(unit.UnitId, second));
    }

    [Fact]
    public void Catalog_size_does_not_expand_army_slots_or_allow_duplicate_and_unknown_entries()
    {
        var units = Enumerable.Range(0, 13).Select(i => new MatchUnitDefinition("unit." + i, 1, i > 1)).ToArray();
        Assert.False(MatchService.IsSupportedDeck(units.Select(x => x.Id).ToArray(), units));
        Assert.False(MatchService.IsSupportedDeck(new[] { "unit.2", "unit.2", "unit.3", "unit.4" }, units));
        Assert.False(MatchService.IsSupportedDeck(new[] { "unit.2", "unit.3", "unit.4", "unknown" }, units));
        Assert.False(MatchService.IsSupportedDeck(new[] { "unit.0", "unit.1", "unit.2", "unit.3" }, units));
    }
}
