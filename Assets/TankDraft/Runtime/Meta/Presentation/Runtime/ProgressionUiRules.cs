using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using TankDraft.Contracts;
using UnityEngine;

namespace TankDraft.UI
{
    // Display-only reading of the authored balance file. The server remains the authority
    // for all costs, grants, availability and random pack results.
    public sealed class ProgressionUiRules
    {
        [Serializable] private sealed class Document
        {
            public string Version;
            public Unit[] Units;
            public Level[] Levels;
            public Mastery[] Mastery;
            public int BoostChipCost;
            public int BoostXp;
            public Offer[] Offers;
        }

        [Serializable] public sealed class Unit { public string Id; public string Rarity; public int Arena; }
        [Serializable] public sealed class Level { public int FromLevel; public int BitsCost; public int CoinsCost; }
        [Serializable] public sealed class Mastery { public int Level; public int CumulativeXp; public int RewardKind; public int RewardAmount; }
        [Serializable] public sealed class Offer { public string Sku; public string Currency; public int Price; public string UnitId; public int Bits; public string PackId; }

        private readonly Dictionary<string, Unit> units;
        private readonly Dictionary<int, Level> levels;
        private readonly Dictionary<int, Mastery> mastery;
        private readonly Dictionary<string, Offer> offers;
        private readonly Dictionary<int, int> masteryCosts;

        private ProgressionUiRules(Document source, Dictionary<int, int> masteryCosts)
        {
            if (source == null || string.IsNullOrWhiteSpace(source.Version) || source.Units == null || source.Levels == null || source.Mastery == null || source.Offers == null || source.BoostChipCost < 1 || source.BoostXp < 1)
                throw new InvalidOperationException("Progression rules are incomplete.");
            Version = source.Version;
            BoostChipCost = source.BoostChipCost;
            BoostXp = source.BoostXp;
            units = IndexUnits(source.Units);
            levels = IndexLevels(source.Levels);
            mastery = IndexMastery(source.Mastery);
            offers = IndexOffers(source.Offers);
            this.masteryCosts = masteryCosts ?? throw new ArgumentNullException(nameof(masteryCosts));
        }

        public string Version { get; }
        public int BoostChipCost { get; }
        public int BoostXp { get; }
        public IReadOnlyCollection<Offer> Offers => offers.Values;

        public static ProgressionUiRules Load(TextAsset authoredJson)
        {
            if (authoredJson == null || string.IsNullOrWhiteSpace(authoredJson.text)) throw new ArgumentException("Progression rules asset is required.", nameof(authoredJson));
            return new ProgressionUiRules(JsonUtility.FromJson<Document>(authoredJson.text), ParseMasteryCosts(authoredJson.text));
        }

        public bool TryGetUnit(string id, out Unit value) => units.TryGetValue(id, out value);
        public bool TryGetLevel(int fromLevel, out Level value) => levels.TryGetValue(fromLevel, out value);
        public bool TryGetMasteryCost(int level, out int value) => masteryCosts.TryGetValue(level, out value);
        public bool TryGetOffer(string sku, out Offer value) => offers.TryGetValue(sku, out value);

        public bool TryGetNextMastery(int xp, out Mastery value)
        {
            value = null;
            foreach (Mastery candidate in mastery.Values)
                if (candidate.CumulativeXp > xp && (value == null || candidate.Level < value.Level)) value = candidate;
            return value != null;
        }

        public bool TryGetClaimable(string unitId, int xp, IReadOnlyList<string> claimed, out Mastery value)
        {
            value = null;
            foreach (Mastery candidate in mastery.Values)
            {
                if (candidate.CumulativeXp > xp || candidate.RewardKind == 3 || candidate.RewardAmount <= 0 || Contains(claimed, unitId + ":" + candidate.Level)) continue;
                if (value == null || candidate.Level < value.Level) value = candidate;
            }
            return value != null;
        }

        private static bool Contains(IReadOnlyList<string> source, string value)
        {
            if (source == null) return false;
            for (int index = 0; index < source.Count; index++) if (source[index] == value) return true;
            return false;
        }

        private static Dictionary<string, Unit> IndexUnits(IEnumerable<Unit> source)
        {
            var result = new Dictionary<string, Unit>();
            foreach (Unit item in source)
            {
                if (item == null || !ProgressionSnapshot.IsValidId(item.Id) || item.Arena < 1 || result.ContainsKey(item.Id)) throw new InvalidOperationException("Invalid progression unit rules.");
                result.Add(item.Id, item);
            }
            if (result.Count == 0) throw new InvalidOperationException("Progression unit rules are empty.");
            return result;
        }

        private static Dictionary<int, Level> IndexLevels(IEnumerable<Level> source)
        {
            var result = new Dictionary<int, Level>();
            foreach (Level item in source)
            {
                if (item == null || item.FromLevel < 1 || item.BitsCost < 0 || item.CoinsCost < 0 || result.ContainsKey(item.FromLevel)) throw new InvalidOperationException("Invalid progression level rules.");
                result.Add(item.FromLevel, item);
            }
            if (result.Count == 0) throw new InvalidOperationException("Progression level rules are empty.");
            return result;
        }

        private static Dictionary<int, Mastery> IndexMastery(IEnumerable<Mastery> source)
        {
            var result = new Dictionary<int, Mastery>();
            foreach (Mastery item in source)
            {
                if (item == null || item.Level < 1 || item.CumulativeXp < 0 || result.ContainsKey(item.Level)) throw new InvalidOperationException("Invalid progression mastery rules.");
                result.Add(item.Level, item);
            }
            if (result.Count == 0) throw new InvalidOperationException("Progression mastery rules are empty.");
            return result;
        }

        private static Dictionary<string, Offer> IndexOffers(IEnumerable<Offer> source)
        {
            var result = new Dictionary<string, Offer>();
            foreach (Offer item in source)
            {
                if (item == null || !ProgressionSnapshot.IsValidId(item.Sku) || item.Price < 1 || (item.Currency != "CO" && item.Currency != "GM") || result.ContainsKey(item.Sku)) throw new InvalidOperationException("Invalid progression offer rules.");
                result.Add(item.Sku, item);
            }
            if (result.Count == 0) throw new InvalidOperationException("Progression offer rules are empty.");
            return result;
        }

        private static Dictionary<int, int> ParseMasteryCosts(string json)
        {
            Match block = Regex.Match(json, "\\\"MasteryCosts\\\"\\s*:\\s*\\{(?<values>.*?)\\}", RegexOptions.Singleline);
            if (!block.Success) throw new InvalidOperationException("Progression mastery costs are missing.");
            var result = new Dictionary<int, int>();
            MatchCollection entries = Regex.Matches(block.Groups["values"].Value, "\\\"(?<level>[0-9]+)\\\"\\s*:\\s*(?<price>[0-9]+)");
            foreach (Match entry in entries)
            {
                if (!int.TryParse(entry.Groups["level"].Value, out int level) || !int.TryParse(entry.Groups["price"].Value, out int price) || level < 1 || price < 1 || result.ContainsKey(level))
                    throw new InvalidOperationException("Progression mastery costs are invalid.");
                result.Add(level, price);
            }
            if (result.Count == 0) throw new InvalidOperationException("Progression mastery costs are empty.");
            return result;
        }
    }
}
