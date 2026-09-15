using System.Collections.Immutable;

namespace TankDraft.Server.Progression;

public sealed record UnitProgress
{
    public int Level { get; init; } public int Bits { get; init; } public int MasteryXp { get; init; }
    public UnitProgress(int level, int bits, int masteryXp)
    {
        if (level < 1 || bits < 0 || masteryXp < 0) throw new ArgumentOutOfRangeException(nameof(level));
        Level = level; Bits = bits; MasteryXp = masteryXp;
    }
}
public sealed record ProgressionState
{
    public int SchemaVersion { get; init; } public long AppliedSequence { get; init; } public string AppliedFingerprint { get; init; }
    public ImmutableDictionary<string, UnitProgress> Units { get; init; } public int MasteryChips { get; init; } public ImmutableHashSet<string> ClaimedMilestones { get; init; }
    public ProgressionState(int schemaVersion, long appliedSequence, string appliedFingerprint, ImmutableDictionary<string, UnitProgress> units, int masteryChips, ImmutableHashSet<string> claimedMilestones)
    {
        if (schemaVersion != 1 || appliedSequence < 0 || string.IsNullOrWhiteSpace(appliedFingerprint) || masteryChips < 0) throw new ArgumentException("Invalid progression state.");
        ArgumentNullException.ThrowIfNull(units); ArgumentNullException.ThrowIfNull(claimedMilestones);
        SchemaVersion = schemaVersion; AppliedSequence = appliedSequence; AppliedFingerprint = appliedFingerprint; Units = units; MasteryChips = masteryChips; ClaimedMilestones = claimedMilestones;
    }
    public static ProgressionState Empty { get; } = new(1, 0, "initial", ImmutableDictionary<string, UnitProgress>.Empty, 0, ImmutableHashSet<string>.Empty);
    public ProgressionState Validate()
    {
        if (SchemaVersion != 1 || AppliedSequence < 0 || AppliedSequence == long.MaxValue || AppliedFingerprint is not { Length: > 0 and <= 128 } ||
            Units is null || Units.Count > 256 || MasteryChips < 0 || ClaimedMilestones is null || ClaimedMilestones.Count > 256 ||
            Units.Any(x => !SafeId(x.Key) || x.Value is null || x.Value.Level is < 1 or > 31 || x.Value.Bits < 0 || x.Value.MasteryXp < 0) ||
            ClaimedMilestones.Any(x => !SafeId(x))) throw new InvalidDataException("Invalid persisted progression.");
        return this;
    }
    static bool SafeId(string value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-' or ':');
}
public sealed record StoredProgression(int Version, ProgressionState? State);
public interface IProgressionStore
{
    Task<StoredProgression> ReadAsync(string accountId, CancellationToken ct);
    Task WriteAsync(string accountId, ProgressionState state, int expectedVersion, CancellationToken ct);
}
public interface IProgressionWallet
{
    Task<int> ReadAsync(string accountId, string currency, CancellationToken ct);
    Task DebitAsync(string accountId, string currency, int amount, CancellationToken ct);
    Task CreditAsync(string accountId, string currency, int amount, CancellationToken ct);
}
public enum ProgressionRequestKind { UpgradeUnit, BuyOffer, BoostMastery, BuyMasteryLevel, ClaimMilestone }
public sealed record ProgressionRequest(ProgressionRequestKind Kind, string TargetId);
public enum ProgressionOperationStatus { Prepared, ProfileApplied, WalletSending, Completed, NeedsReview }
public sealed record ProgressionReceipt(Guid OperationId, long Sequence, ProgressionOperationStatus Status, ProgressionState State, ImmutableArray<string> ResolvedUnitIds);
public enum ProgressionReviewAction { ConfirmApplied, ForgiveDebit, CompensateCreditOnce }
public sealed record ProgressionReview(string AccountId, Guid OperationId, long Sequence, string Currency, int WalletDelta, ImmutableArray<string> ResolvedUnitIds, bool CompensationAttempted);
public enum MasteryRewardKind { Bits, Coins, Chips, Cosmetic }
public sealed record UnitDefinition(string Id, string Rarity, int Arena);
public sealed record LevelStep(int FromLevel, int BitsCost, int CoinsCost, int HpPermille, int DamagePermille);
public sealed record MasteryStep(int Level, int CumulativeXp, MasteryRewardKind RewardKind, int RewardAmount, int HpPermille = 0, int DamagePermille = 0, int AttackSpeedPermille = 0, bool UnlocksFormation = false);
public sealed record OfferDefinition(string Sku, string Currency, int Price, string UnitId, int Bits, string? PackId);
public sealed record PackRarity(string Rarity, int Weight, int MinBits, int MaxBits);
public sealed record PackDefinition(string Id, int DrawCount, ImmutableArray<PackRarity> Rarities);
public sealed class ProgressionRules
{
    public string Version { get; }
    public ImmutableDictionary<string, UnitDefinition> Units { get; }
    public ImmutableDictionary<int, LevelStep> LevelSteps { get; }
    public ImmutableDictionary<int, MasteryStep> MasterySteps { get; }
    public int WinMasteryXp { get; } public int LossMasteryXp { get; }
    public int MasteryBoostChipCost { get; } public int MasteryBoostXp { get; }
    public ImmutableDictionary<int, int> BuyMasteryLevelGemCosts { get; }
    public ImmutableDictionary<string, OfferDefinition> Offers { get; }
    public ImmutableDictionary<string, PackDefinition> Packs { get; }
    public ProgressionRules(string version, IEnumerable<UnitDefinition> units, IEnumerable<LevelStep> levels, IEnumerable<MasteryStep> mastery, int winXp, int lossXp, int boostChipCost, int boostXp, IEnumerable<KeyValuePair<int, int>> masteryCosts, IEnumerable<OfferDefinition> offers, IEnumerable<PackDefinition> packs)
    {
        if (string.IsNullOrWhiteSpace(version) || winXp < 0 || lossXp < 0 || boostChipCost < 0 || boostXp < 0) throw new ArgumentException("Invalid progression rules.");
        Version = version; Units = ToMap(units, x => x.Id); LevelSteps = ToMap(levels, x => x.FromLevel); MasterySteps = ToMap(mastery, x => x.Level);
        BuyMasteryLevelGemCosts = masteryCosts?.ToImmutableDictionary(x => x.Key, x => x.Value) ?? throw new ArgumentNullException(nameof(masteryCosts)); Offers = ToMap(offers, x => x.Sku); Packs = ToMap(packs, x => x.Id);
        WinMasteryXp = winXp; LossMasteryXp = lossXp; MasteryBoostChipCost = boostChipCost; MasteryBoostXp = boostXp;
        if (Units.Count == 0 || LevelSteps.Any(x => x.Value.FromLevel < 1 || x.Value.BitsCost < 0 || x.Value.CoinsCost < 0) || MasterySteps.Any(x => x.Value.Level < 1 || x.Value.CumulativeXp < 0 || x.Value.RewardAmount < 0)) throw new ArgumentException("Invalid progression rules.");
        static bool Id(string value) => value is { Length: > 0 and <= 128 } && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');
        if (Units.Count > 256 || Units.Values.Any(x => !Id(x.Id) || !Id(x.Rarity) || x.Arena < 1) ||
            LevelSteps.Count > 30 || LevelSteps.Values.Any(x => x.BitsCost > 100000 || x.CoinsCost > 100000 || x.HpPermille is < 0 or > 10000 || x.DamagePermille is < 0 or > 10000) ||
            MasterySteps.Count > 30 || MasterySteps.Values.Any(x => !Enum.IsDefined(x.RewardKind) || x.RewardAmount > 100000 || x.HpPermille is < 0 or > 10000 || x.DamagePermille is < 0 or > 10000 || x.AttackSpeedPermille is < 0 or > 10000) ||
            BuyMasteryLevelGemCosts.Any(x => !MasterySteps.ContainsKey(x.Key) || x.Value is < 1 or > 100000) ||
            winXp > 10000 || lossXp > 10000 || boostChipCost < 1 || boostXp is < 1 or > 10000 ||
            Offers.Count > 100 || Offers.Values.Any(x => !Id(x.Sku) || x.Price is < 1 or > 100000 || x.Currency is not ("CO" or "GM") || x.Bits is < 0 or > 100000 || (x.PackId is null ? !Units.ContainsKey(x.UnitId) : !Packs.ContainsKey(x.PackId))) ||
            Packs.Count > 30 || Packs.Values.Any(x => !Id(x.Id) || x.DrawCount is < 1 or > 4 || x.Rarities.IsDefaultOrEmpty || x.Rarities.Length > 4 || x.Rarities.Select(r=>r.Rarity).Distinct().Count()!=x.Rarities.Length || x.Rarities.Any(r=>!Id(r.Rarity) || r.Weight is < 1 or > 10000 || r.MinBits < 1 || r.MaxBits < r.MinBits || r.MaxBits > 100000)))
            throw new ArgumentException("Progression rules exceed validated bounds.");
        var ordered = MasterySteps.Values.OrderBy(x => x.Level).ToArray();
        for (int i=1; i<ordered.Length; i++)
            if (ordered[i].Level != ordered[i-1].Level+1 || ordered[i].CumulativeXp <= ordered[i-1].CumulativeXp) throw new ArgumentException("Mastery thresholds must increase.");
    }
    static ImmutableDictionary<TKey, TValue> ToMap<TValue, TKey>(IEnumerable<TValue> values, Func<TValue, TKey> key) where TKey : notnull => values?.ToImmutableDictionary(key) ?? throw new ArgumentNullException(nameof(values));
}
