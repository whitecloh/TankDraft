using TankDraft.Match.Presentation;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class MatchRoundPresentationTests
{
    [Theory]
    [InlineData("Battle", false, 0d, MatchRoundBanner.Round)]
    [InlineData("Battle", false, .8d, MatchRoundBanner.Round)]
    [InlineData("Battle", false, 1d, MatchRoundBanner.Fight)]
    [InlineData("Battle", false, 1.79d, MatchRoundBanner.Fight)]
    [InlineData("Battle", false, 1.8d, MatchRoundBanner.None)]
    [InlineData("Draft", false, 0d, MatchRoundBanner.None)]
    [InlineData("RoundResult", false, .2d, MatchRoundBanner.None)]
    public void Classifies_only_the_early_authoritative_battle_age(string phase, bool reset, double age, MatchRoundBanner expected)
    {
        Assert.Equal(expected, MatchRoundPresentation.ClassifyBanner(phase, reset, age));
    }

    [Fact]
    public void Rejects_reset_unknown_and_invalid_ages_without_replaying_a_banner()
    {
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", true, 0d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner(null!, false, 0d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, double.NaN));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, double.PositiveInfinity));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, -.001d));
    }

    [Fact]
    public void Uses_the_authored_durations_and_rejects_invalid_duration_overrides()
    {
        Assert.Equal(MatchRoundBanner.Round, MatchRoundPresentation.ClassifyBanner("Battle", false, .49d, .5d, .25d));
        Assert.Equal(MatchRoundBanner.Fight, MatchRoundPresentation.ClassifyBanner("Battle", false, .5d, .5d, .25d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, .75d, .5d, .25d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, 0d, 0d, .25d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, 0d, .5d, -1d));
        Assert.Equal(MatchRoundBanner.None, MatchRoundPresentation.ClassifyBanner("Battle", false, 0d, double.NaN, .25d));
    }
}
