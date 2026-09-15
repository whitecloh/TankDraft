using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using TankDraft.Contracts.Battle;

namespace TankDraft.Match.Domain
{
    public enum MatchPhase
    {
        Draft,
        Battle,
        RoundResult,
        MatchResult,
        ReviewRequired
    }

    public enum DraftActionKind
    {
        Add,
        Double,
        Upgrade
    }

    public sealed class MatchRules
    {
        public int WinsRequired { get; }
        public int NormalChoices { get; }
        public int MaxUnitsPerType { get; }
        public int UpgradeHpPercent { get; }
        public int UpgradeDamagePercent { get; }
        public int OrderCharges { get; }
        public int OrderArmorPercent { get; }
        public int AddWeight { get; }
        public int DoubleWeight { get; }
        public int UpgradeWeight { get; }

        public MatchRules(int winsRequired, int normalChoices, int maxUnitsPerType, int upgradeHpPercent, int upgradeDamagePercent, int orderCharges, int orderArmorPercent, int addWeight = 60, int doubleWeight = 28, int upgradeWeight = 12)
        {
            if (winsRequired < 1 || normalChoices != 3 || maxUnitsPerType < 1 || maxUnitsPerType > 128 || upgradeHpPercent < 0 || upgradeHpPercent > 1000000 || upgradeDamagePercent < 0 || upgradeDamagePercent > 1000000 || orderCharges < 0 || orderCharges > 128 || orderArmorPercent < 0 || orderArmorPercent > 1000000 || addWeight < 1 || doubleWeight < 1 || upgradeWeight < 1)
                throw new ArgumentOutOfRangeException(nameof(winsRequired));
            WinsRequired = winsRequired;
            NormalChoices = normalChoices;
            MaxUnitsPerType = maxUnitsPerType;
            UpgradeHpPercent = upgradeHpPercent;
            UpgradeDamagePercent = upgradeDamagePercent;
            OrderCharges = orderCharges;
            OrderArmorPercent = orderArmorPercent;
            AddWeight = addWeight;
            DoubleWeight = doubleWeight;
            UpgradeWeight = upgradeWeight;
        }
    }

    public sealed class MatchUnitDefinition
    {
        public string Id { get; }
        public int AddCount { get; }
        public bool CanFightAlone { get; }

        public MatchUnitDefinition(string id, int addCount, bool canFightAlone)
        {
            if (string.IsNullOrWhiteSpace(id) || addCount < 1)
                throw new ArgumentOutOfRangeException(nameof(id));
            Id = id;
            AddCount = addCount;
            CanFightAlone = canFightAlone;
        }
    }

    public sealed class MatchOffer
    {
        public string Id { get; }
        public string UnitId { get; }
        public DraftActionKind Kind { get; }
        public int Amount { get; }

        public MatchOffer(string id, string unitId, DraftActionKind kind, int amount)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(unitId) || amount < 1 || !Enum.IsDefined(typeof(DraftActionKind), kind))
                throw new ArgumentException("Invalid offer.");
            Id = id;
            UnitId = unitId;
            Kind = kind;
            Amount = amount;
        }
    }

    public sealed class ArmyEntry
    {
        public string UnitId { get; }
        public int Count { get; }
        public int UpgradeLevel { get; }

        public ArmyEntry(string unitId, int count, int upgradeLevel)
        {
            UnitId = unitId;
            Count = count;
            UpgradeLevel = upgradeLevel;
        }
    }

    public sealed class PersistentBattleBonus
    {
        public int HpPercent { get; }
        public int DamagePercent { get; }
        public int AttackSpeedPercent { get; }

        public PersistentBattleBonus(int hpPercent, int damagePercent, int attackSpeedPercent)
        {
            if (hpPercent < 0 || hpPercent > 10000 || damagePercent < 0 || damagePercent > 10000 || attackSpeedPercent < 0 || attackSpeedPercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(hpPercent));
            HpPercent = hpPercent;
            DamagePercent = damagePercent;
            AttackSpeedPercent = attackSpeedPercent;
        }
    }

    public sealed class MatchService
    {
        const string ReinforceArmorId = "order.reinforce_armor";
        readonly MatchRules _rules;
        readonly Dictionary<string, MatchUnitDefinition> _units = new Dictionary<string, MatchUnitDefinition>();
        readonly string[][] _decks = new string[2][];
        readonly Dictionary<string, PersistentBattleBonus>[] _persistentBonuses =
        {
            new Dictionary<string, PersistentBattleBonus>(StringComparer.Ordinal),
            new Dictionary<string, PersistentBattleBonus>(StringComparer.Ordinal)
        };
        readonly Dictionary<string, ArmyValue>[] _armies =
        {
            new Dictionary<string, ArmyValue>(),
            new Dictionary<string, ArmyValue>()
        };
        readonly List<MatchOffer>[] _offers =
        {
            new List<MatchOffer>(),
            new List<MatchOffer>()
        };
        readonly Pending[] _pending = new Pending[2];
        readonly bool[] _armorNextBattle = new bool[2];
        readonly int[] _wins = new int[2];
        readonly bool[] _orderEnabled = new bool[2];
        uint _rng;
        long _choiceToken;
        int _choiceNumber;
        int _bonusSide = -1;
        int _lastWinner = -1;
        int _roundNumber = 1;
        readonly int[] _orderCharges = new int[2];
        struct ArmyValue
        {
            public int Count, Upgrade;
        }

        struct Pending
        {
            public bool Committed, Order;
            public MatchOffer Offer;
        }

        public MatchService(MatchRules rules, MatchUnitDefinition[] units, string[] playerDeck, string[] enemyDeck, string playerOrderId, uint seed, string enemyOrderId = "", IReadOnlyDictionary<string, PersistentBattleBonus> playerPersistentBonuses = null, IReadOnlyDictionary<string, PersistentBattleBonus> enemyPersistentBonuses = null)
        {
            _rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (units == null || units.Length < 4)
                throw new ArgumentException("At least four supported unit types are required.", nameof(units));
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null || _units.ContainsKey(units[i].Id))
                    throw new ArgumentException("Unit types must be unique.", nameof(units));
                _units.Add(units[i].Id, units[i]);
            }

            _decks[0] = ValidateDeck(playerDeck, nameof(playerDeck));
            _decks[1] = ValidateDeck(enemyDeck, nameof(enemyDeck));
            CopyPersistentBonuses(playerPersistentBonuses, _persistentBonuses[0], nameof(playerPersistentBonuses));
            CopyPersistentBonuses(enemyPersistentBonuses, _persistentBonuses[1], nameof(enemyPersistentBonuses));
            _orderEnabled[0] = string.Equals(playerOrderId, ReinforceArmorId, StringComparison.Ordinal);
            _orderEnabled[1] = string.Equals(enemyOrderId, ReinforceArmorId, StringComparison.Ordinal);
            _orderCharges[0] = _orderEnabled[0] ? rules.OrderCharges : 0;
            _orderCharges[1] = _orderEnabled[1] ? rules.OrderCharges : 0;
            _rng = seed == 0 ? 1u : seed;
            BeginChoices(-1);
        }

        public MatchPhase Phase { get; private set; } = MatchPhase.Draft;
        public int RoundNumber => _roundNumber;
        public int ChoiceNumber => _choiceNumber;
        public bool IsComeback => _bonusSide >= 0;
        public int BonusSide => _bonusSide;
        public long ChoiceToken => _choiceToken;
        public int OrderCharges => _orderCharges[0];
        public int LastWinner => _lastWinner;

        public int Wins(int side)
        {
            ValidateSide(side);
            return _wins[side];
        }

        public int Charges(int side)
        {
            ValidateSide(side);
            return _orderCharges[side];
        }

        public bool HasCommitted(int side)
        {
            ValidateSide(side);
            return _pending[side].Committed;
        }

        public bool CanUseOrder(int side)
        {
            ValidateSide(side);
            return Phase == MatchPhase.Draft && _bonusSide < 0 && !_pending[side].Committed && _orderEnabled[side] && _orderCharges[side] > 0 && !_armorNextBattle[side] && !_pending[side].Order && HasMobileUnit(side);
        }

        public static bool IsSupportedDeck(string[] deck, MatchUnitDefinition[] units)
        {
            if (deck == null || units == null || deck.Length != 4 || units.Length < 4)
                return false;
            var supported = new Dictionary<string, MatchUnitDefinition>();
            for (int i = 0; i < units.Length; i++)
            {
                if (units[i] == null || supported.ContainsKey(units[i].Id))
                    return false;
                supported.Add(units[i].Id, units[i]);
            }

            var selected = new HashSet<string>();
            int mobiles = 0;
            for (int i = 0; i < deck.Length; i++)
            {
                if (!supported.ContainsKey(deck[i]) || !selected.Add(deck[i]))
                    return false;
                if (supported[deck[i]].CanFightAlone)
                    mobiles++;
            }

            return mobiles >= 3;
        }

        public IReadOnlyList<ArmyEntry> GetArmy(int side)
        {
            ValidateSide(side);
            var copy = new List<ArmyEntry>();
            foreach (var pair in _armies[side])
                copy.Add(new ArmyEntry(pair.Key, pair.Value.Count, pair.Value.Upgrade));
            copy.Sort((a, b) => string.CompareOrdinal(a.UnitId, b.UnitId));
            return new ReadOnlyCollection<ArmyEntry>(copy);
        }

        public IReadOnlyList<MatchOffer> GetOffers(int side)
        {
            ValidateSide(side);
            if (Phase != MatchPhase.Draft)
                return new ReadOnlyCollection<MatchOffer>(new List<MatchOffer>());
            return new ReadOnlyCollection<MatchOffer>(new List<MatchOffer>(_offers[side]));
        }

        public bool TryChoose(int side, long token, int offerIndex, out string reason)
        {
            ValidateSide(side);
            if (!CanCommit(side, token, out reason))
                return false;
            if (offerIndex < 0 || offerIndex >= _offers[side].Count)
            {
                reason = "offer-index";
                return false;
            }

            _pending[side] = new Pending
            {
                Committed = true,
                Offer = _offers[side][offerIndex]
            };
            Apply(side);
            ResolveBarrier();
            reason = "";
            return true;
        }

        public bool TryUseOrder(int side, long token, out string reason)
        {
            ValidateSide(side);
            if (!CanCommit(side, token, out reason))
                return false;
            if (!CanUseOrder(side))
            {
                reason = "order-unavailable-or-immobile";
                return false;
            }

            _pending[side] = new Pending
            {
                Committed = true,
                Order = true
            };
            Apply(side);
            ResolveBarrier();
            reason = "";
            return true;
        }

        public void ResolveBattle(BattleOutcome outcome)
        {
            if (Phase != MatchPhase.Battle)
                throw new InvalidOperationException("Battle is not pending.");
            if (outcome == BattleOutcome.Running)
                throw new ArgumentException("Battle outcome is required.", nameof(outcome));
            if (outcome == BattleOutcome.ReviewRequired)
            {
                Phase = MatchPhase.ReviewRequired;
                return;
            }

            int winner = outcome == BattleOutcome.Side0Won ? 0 : outcome == BattleOutcome.Side1Won ? 1 : throw new ArgumentOutOfRangeException(nameof(outcome));
            _wins[winner] = checked(_wins[winner] + 1);
            _lastWinner = winner;
            _armorNextBattle[0] = false;
            _armorNextBattle[1] = false;
            Phase = _wins[winner] >= _rules.WinsRequired ? MatchPhase.MatchResult : MatchPhase.RoundResult;
        }

        public void Continue()
        {
            if (Phase == MatchPhase.ReviewRequired)
                throw new InvalidOperationException("Review is required.");
            if (Phase != MatchPhase.RoundResult)
                throw new InvalidOperationException("Round result is required.");
            if (_wins[_lastWinner] >= _rules.WinsRequired)
            {
                Phase = MatchPhase.MatchResult;
                return;
            }

            _roundNumber = checked(_roundNumber + 1);
            BeginChoices(1 - _lastWinner);
        }

        public BattleScenarioDefinition CreateScenario()
        {
            if (Phase != MatchPhase.Battle && Phase != MatchPhase.Draft)
                throw new InvalidOperationException("Scenario is not available.");
            var stacks = new List<BattleArmyStack>();
            for (int side = 0; side < 2; side++)
            {
                if (Phase == MatchPhase.Battle && !HasMobileUnit(side))
                    throw new InvalidOperationException("Battle army has no mobile non-mine unit.");
                foreach (var pair in _armies[side])
                {
                    _persistentBonuses[side].TryGetValue(pair.Key, out var persistent);
                    int hp = AddBoundedPercent(AddBoundedPercent(BoundedPercent(pair.Value.Upgrade, _rules.UpgradeHpPercent), _armorNextBattle[side] ? _rules.OrderArmorPercent : 0), persistent?.HpPercent ?? 0);
                    int damage = AddBoundedPercent(BoundedPercent(pair.Value.Upgrade, _rules.UpgradeDamagePercent), persistent?.DamagePercent ?? 0);
                    stacks.Add(new BattleArmyStack(side, pair.Key, pair.Value.Count, hp, damage, persistent?.AttackSpeedPercent ?? 0));
                }
            }

            return new BattleScenarioDefinition("match.round." + _roundNumber, "Match round " + _roundNumber, MixSeed(_rng, (uint)_roundNumber), stacks.ToArray(), allowIncomplete: Phase == MatchPhase.Draft);
        }

        bool CanCommit(int side, long token, out string reason)
        {
            if (Phase != MatchPhase.Draft)
            {
                reason = "draft-not-active";
                return false;
            }

            if (token != _choiceToken)
            {
                reason = "stale-token";
                return false;
            }

            if (_pending[side].Committed)
            {
                reason = "already-committed";
                return false;
            }

            if (_bonusSide >= 0 && side != _bonusSide)
            {
                reason = "bonus-not-for-side";
                return false;
            }

            reason = "";
            return true;
        }

        void ResolveBarrier()
        {
            if (_bonusSide >= 0)
            {
                if (!_pending[_bonusSide].Committed)
                    return;
                BeginChoices(-1);
                return;
            }

            if (!_pending[0].Committed || !_pending[1].Committed)
                return;
            if (_choiceNumber >= _rules.NormalChoices)
            {
                if (!HasMobileUnit(0) || !HasMobileUnit(1))
                    throw new InvalidOperationException("Last choice did not create a mobile army.");
                Phase = MatchPhase.Battle;
                return;
            }

            BeginChoices(-1);
        }

        void Apply(int side)
        {
            Pending p = _pending[side];
            if (p.Order)
            {
                _orderCharges[side]--;
                _armorNextBattle[side] = true;
                return;
            }

            ArmyValue value;
            _armies[side].TryGetValue(p.Offer.UnitId, out value);
            if (p.Offer.Kind == DraftActionKind.Add)
                value.Count = Math.Min(_rules.MaxUnitsPerType, value.Count + p.Offer.Amount);
            else if (p.Offer.Kind == DraftActionKind.Double)
                value.Count = Math.Min(_rules.MaxUnitsPerType, value.Count * 2);
            else
                value.Upgrade = checked(value.Upgrade + 1);
            _armies[side][p.Offer.UnitId] = value;
        }

        void BeginChoices(int bonusSide)
        {
            Phase = MatchPhase.Draft;
            _bonusSide = bonusSide;
            if (bonusSide >= 0)
                _choiceNumber = 0;
            else
                _choiceNumber = checked(_choiceNumber + 1);
            _pending[0] = default(Pending);
            _pending[1] = default(Pending);
            _offers[0].Clear();
            _offers[1].Clear();
            if (bonusSide >= 0)
            {
                _offers[bonusSide].AddRange(GenerateOffers(bonusSide, false));
            }
            else
                for (int side = 0; side < 2; side++)
                    _offers[side].AddRange(GenerateOffers(side, _choiceNumber == _rules.NormalChoices && !HasMobileUnit(side)));
            _choiceToken = checked(_choiceToken + 1);
        }

        List<MatchOffer> GenerateOffers(int side, bool forceMobileAdd)
        {
            var candidates = new List<MatchOffer>();
            for (int i = 0; i < _decks[side].Length; i++)
            {
                string id = _decks[side][i];
                ArmyValue value;
                _armies[side].TryGetValue(id, out value);
                if (forceMobileAdd)
                {
                    if (_units[id].CanFightAlone && value.Count < _rules.MaxUnitsPerType)
                        candidates.Add(new MatchOffer("candidate." + id, id, DraftActionKind.Add, Math.Min(_units[id].AddCount, _rules.MaxUnitsPerType - value.Count)));
                    continue;
                }

                if (value.Count < _rules.MaxUnitsPerType)
                    candidates.Add(new MatchOffer("candidate." + id + ".add", id, DraftActionKind.Add, Math.Min(_units[id].AddCount, _rules.MaxUnitsPerType - value.Count)));
                if (value.Count > 0 && value.Count <= _rules.MaxUnitsPerType / 2)
                    candidates.Add(new MatchOffer("candidate." + id + ".double", id, DraftActionKind.Double, value.Count));
                if (value.Count > 0)
                    candidates.Add(new MatchOffer("candidate." + id + ".upgrade", id, DraftActionKind.Upgrade, 1));
            }

            var result = new List<MatchOffer>();
            while (result.Count < 3 && candidates.Count > 0)
            {
                int total = 0;
                for (int i = 0; i < candidates.Count; i++)
                    total = checked(total + Weight(candidates[i].Kind));
                int roll = (int)(NextRandom() % (uint)total), selected = 0;
                for (; selected < candidates.Count - 1; selected++)
                {
                    roll -= Weight(candidates[selected].Kind);
                    if (roll < 0)
                        break;
                }

                MatchOffer picked = candidates[selected];
                result.Add(new MatchOffer("r" + _roundNumber + ".c" + (_choiceNumber + 1) + ".s" + side + ".o" + result.Count, picked.UnitId, picked.Kind, picked.Amount));
                candidates.RemoveAll(x => string.Equals(x.UnitId, picked.UnitId, StringComparison.Ordinal));
            }

            if (result.Count != 3)
                throw new InvalidOperationException("Cannot generate three distinct legal offers.");
            return result;
        }

        string[] ValidateDeck(string[] deck, string name)
        {
            if (deck == null || deck.Length != 4)
                throw new ArgumentException("Each side requires four unit types.", name);
            var copy = (string[])deck.Clone();
            var seen = new HashSet<string>();
            int mobiles = 0;
            for (int i = 0; i < copy.Length; i++)
            {
                if (!_units.ContainsKey(copy[i]) || !seen.Add(copy[i]))
                    throw new ArgumentException("Deck has unknown or duplicate type.", name);
                if (_units[copy[i]].CanFightAlone)
                    mobiles++;
            }

            if (mobiles < 3)
                throw new ArgumentException("Deck needs three mobile non-mine types.", name);
            return copy;
        }

        void CopyPersistentBonuses(IReadOnlyDictionary<string, PersistentBattleBonus> source, Dictionary<string, PersistentBattleBonus> target, string name)
        {
            if (source == null)
                return;
            foreach (var pair in source)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || !_units.ContainsKey(pair.Key) || pair.Value == null)
                    throw new ArgumentException("Persistent bonus contains an unknown unit.", name);
                target.Add(pair.Key, new PersistentBattleBonus(pair.Value.HpPercent, pair.Value.DamagePercent, pair.Value.AttackSpeedPercent));
            }
        }

        bool HasAnyUnit(int side)
        {
            foreach (var pair in _armies[side])
                if (pair.Value.Count > 0)
                    return true;
            return false;
        }

        bool HasMobileUnit(int side)
        {
            foreach (var pair in _armies[side])
                if (pair.Value.Count > 0 && _units[pair.Key].CanFightAlone)
                    return true;
            return false;
        }

        int Weight(DraftActionKind kind)
        {
            return kind == DraftActionKind.Add ? _rules.AddWeight : kind == DraftActionKind.Double ? _rules.DoubleWeight : _rules.UpgradeWeight;
        }

        uint NextRandom()
        {
            _rng ^= _rng << 13;
            _rng ^= _rng >> 17;
            _rng ^= _rng << 5;
            return _rng;
        }

        static uint MixSeed(uint value, uint round)
        {
            return unchecked(value ^ (round * 0x9e3779b9u));
        }

        static int BoundedPercent(int level, int perLevel)
        {
            long value = (long)level * perLevel;
            return value > 1000000 ? 1000000 : (int)value;
        }

        static int AddBoundedPercent(int first, int second)
        {
            long value = (long)first + second;
            return value > 1000000 ? 1000000 : (int)value;
        }

        static void ValidateSide(int side)
        {
            if (side != 0 && side != 1)
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }
}
