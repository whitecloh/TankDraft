using System;

namespace TankDraft.Match.Presentation
{
    public enum MatchRoundBanner
    {
        None = 0,
        Round = 1,
        Fight = 2
    }

    // A pure projection policy. Its input is an elapsed presentation age supplied by
    // the client projection; it never owns a clock or changes match state.
    public static class MatchRoundPresentation
    {
        public const double RoundLabelSeconds = 1d;
        public const double FightLabelSeconds = .8d;
        public const double TotalLabelSeconds = RoundLabelSeconds + FightLabelSeconds;

        public static MatchRoundBanner ClassifyBanner(string phaseName, bool presentationReset, double battleElapsedSeconds,
            double roundLabelSeconds = RoundLabelSeconds, double fightLabelSeconds = FightLabelSeconds)
        {
            if (!IsPositiveFinite(roundLabelSeconds) || !IsPositiveFinite(fightLabelSeconds))
                return MatchRoundBanner.None;
            if (presentationReset || !string.Equals(phaseName, "Battle", StringComparison.Ordinal) ||
                double.IsNaN(battleElapsedSeconds) || double.IsInfinity(battleElapsedSeconds) ||
                battleElapsedSeconds < 0d || battleElapsedSeconds >= TotalLabelSecondsFor(roundLabelSeconds, fightLabelSeconds))
                return MatchRoundBanner.None;

            return battleElapsedSeconds < roundLabelSeconds ? MatchRoundBanner.Round : MatchRoundBanner.Fight;
        }

        public static double TotalLabelSecondsFor(double roundLabelSeconds, double fightLabelSeconds) => roundLabelSeconds + fightLabelSeconds;

        private static bool IsPositiveFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;
    }
}
