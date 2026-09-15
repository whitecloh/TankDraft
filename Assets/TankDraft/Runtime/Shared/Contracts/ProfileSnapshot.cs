using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TankDraft.Contracts
{
    public sealed class ProfileSnapshot
    {
        public const int MaximumRecentResults = 20;
        private readonly ReadOnlyCollection<string> ownedIds;
        private readonly ReadOnlyCollection<string> unitIds;
        private readonly ReadOnlyCollection<string> orderIds;
        private readonly ReadOnlyCollection<RecentMatchResult> recentResults;

        public ProfileSnapshot(string playerName, int commanderLevel, int arenaLevel, int arenaProgress, int energy, int gems, int coins, int mastery, string[] ownedIds, string[] unitIds, string[] orderIds, RecentMatchResult[] recentResults = null)
        {
            if (string.IsNullOrWhiteSpace(playerName)) throw new ArgumentException("Player name is required.", nameof(playerName));
            if (commanderLevel < 1) throw new ArgumentOutOfRangeException(nameof(commanderLevel));
            if (arenaLevel < 1) throw new ArgumentOutOfRangeException(nameof(arenaLevel));
            if (arenaProgress < 0) throw new ArgumentOutOfRangeException(nameof(arenaProgress));
            if (energy < 0) throw new ArgumentOutOfRangeException(nameof(energy));
            if (gems < 0) throw new ArgumentOutOfRangeException(nameof(gems));
            if (coins < 0) throw new ArgumentOutOfRangeException(nameof(coins));
            if (mastery < 0) throw new ArgumentOutOfRangeException(nameof(mastery));

            PlayerName = playerName;
            CommanderLevel = commanderLevel;
            ArenaLevel = arenaLevel;
            ArenaProgress = arenaProgress;
            Energy = energy;
            Gems = gems;
            Coins = coins;
            Mastery = mastery;
            this.ownedIds = CopyIds(ownedIds, false, nameof(ownedIds));
            this.unitIds = CopyIds(unitIds, false, nameof(unitIds));
            this.orderIds = CopyIds(orderIds, true, nameof(orderIds));
            this.recentResults = CopyRecentResults(recentResults);
        }

        public string PlayerName { get; }
        public int CommanderLevel { get; }
        public int ArenaLevel { get; }
        public int ArenaProgress { get; }
        public int Energy { get; }
        public int Gems { get; }
        public int Coins { get; }
        public int Mastery { get; }
        public IReadOnlyList<string> OwnedIds { get { return ownedIds; } }
        public IReadOnlyList<string> UnitIds { get { return unitIds; } }
        public IReadOnlyList<string> OrderIds { get { return orderIds; } }
        public IReadOnlyList<RecentMatchResult> RecentResults { get { return recentResults; } }

        public ProfileSnapshot WithLoadout(string[] units, string[] orders)
        {
            return new ProfileSnapshot(PlayerName, CommanderLevel, ArenaLevel, ArenaProgress, Energy, Gems, Coins, Mastery, ToArray(ownedIds), units, orders, ToArray(recentResults));
        }

        private static ReadOnlyCollection<RecentMatchResult> CopyRecentResults(RecentMatchResult[] source)
        {
            if (source == null) return new ReadOnlyCollection<RecentMatchResult>(Array.Empty<RecentMatchResult>());
            if (source.Length > MaximumRecentResults) throw new ArgumentOutOfRangeException(nameof(source));
            RecentMatchResult[] copy = new RecentMatchResult[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                RecentMatchResult result = source[index] ?? throw new ArgumentException("Recent results cannot contain null.", nameof(source));
                for (int earlier = 0; earlier < index; earlier++)
                {
                    if (copy[earlier].ResultId == result.ResultId) throw new ArgumentException("Recent results cannot contain duplicate ids.", nameof(source));
                }
                copy[index] = result;
            }
            return new ReadOnlyCollection<RecentMatchResult>(copy);
        }

        private static ReadOnlyCollection<string> CopyIds(string[] source, bool allowEmpty, string parameterName)
        {
            if (source == null) throw new ArgumentNullException(parameterName);
            string[] copy = new string[source.Length];
            for (int index = 0; index < source.Length; index++)
            {
                string id = source[index];
                if (id == null || (id.Length == 0 ? !allowEmpty : string.IsNullOrWhiteSpace(id)))
                    throw new ArgumentException("Ids cannot be null, empty, or whitespace.", parameterName);
                copy[index] = id;
            }
            return new ReadOnlyCollection<string>(copy);
        }

        private static string[] ToArray(IReadOnlyList<string> source)
        {
            string[] copy = new string[source.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = source[index];
            return copy;
        }

        private static RecentMatchResult[] ToArray(IReadOnlyList<RecentMatchResult> source)
        {
            RecentMatchResult[] copy = new RecentMatchResult[source.Count];
            for (int index = 0; index < copy.Length; index++) copy[index] = source[index];
            return copy;
        }
    }

    public enum RewardDeliveryState
    {
        Pending,
        Sending,
        Applied,
        NeedsReview,
        Skipped
    }

    // Server-only read projection. It is deliberately separate from profile persistence.
    public sealed class RecentMatchResult
    {
        public RecentMatchResult(string resultId, string matchId, int ownWins, int opponentWins, bool won, bool isBot, string currency, int amount, RewardDeliveryState state, string[] participantUnitIds = null, int masteryXpPerUnit = 0, bool masteryApplied = false)
        {
            if (!IsHex64(resultId)) throw new ArgumentException("Result id must be 64 hexadecimal characters.", nameof(resultId));
            if (string.IsNullOrWhiteSpace(matchId) || matchId.Length > 128) throw new ArgumentException("Match id is invalid.", nameof(matchId));
            if (ownWins < 0) throw new ArgumentOutOfRangeException(nameof(ownWins));
            if (opponentWins < 0) throw new ArgumentOutOfRangeException(nameof(opponentWins));
            if (string.IsNullOrWhiteSpace(currency) || currency.Length > 128) throw new ArgumentException("Currency is invalid.", nameof(currency));
            if (amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));

            ResultId = resultId;
            MatchId = matchId;
            OwnWins = ownWins;
            OpponentWins = opponentWins;
            Won = won;
            IsBot = isBot;
            Currency = currency;
            Amount = amount;
            State = state;
            if (masteryXpPerUnit < 0 || masteryXpPerUnit > 10000) throw new ArgumentOutOfRangeException(nameof(masteryXpPerUnit));
            var participants = participantUnitIds == null ? Array.Empty<string>() : (string[])participantUnitIds.Clone();
            if (participants.Length != 0 && participants.Length != 4) throw new ArgumentException("Four battle participants required.");
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (var id in participants) if (!ProgressionSnapshot.IsValidId(id) || !unique.Add(id)) throw new ArgumentException("Invalid battle participant.");
            ParticipantUnitIds = Array.AsReadOnly(participants);
            MasteryXpPerUnit = masteryXpPerUnit;
            MasteryApplied = masteryApplied;
        }

        public string ResultId { get; }
        public string MatchId { get; }
        public int OwnWins { get; }
        public int OpponentWins { get; }
        public bool Won { get; }
        public bool IsBot { get; }
        public string Currency { get; }
        public int Amount { get; }
        public RewardDeliveryState State { get; }
        public IReadOnlyList<string> ParticipantUnitIds { get; }
        public int MasteryXpPerUnit { get; }
        public bool MasteryApplied { get; }

        private static bool IsHex64(string value)
        {
            if (value == null || value.Length != 64) return false;
            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f') || (character >= 'A' && character <= 'F'))) return false;
            }
            return true;
        }
    }
}
