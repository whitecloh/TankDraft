using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace TankDraft.Server.Progression;

public sealed class ProgressionService
{
    readonly SqliteProgressionStore store; readonly IProgressionStore profileStore; readonly IProgressionWallet wallet; readonly ProgressionRules rules; readonly SemaphoreSlim global;
    public ProgressionService(SqliteProgressionStore store, IProgressionWallet wallet, ProgressionRules rules, IProgressionStore? profileStore = null, int maxConcurrent = 1)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store)); this.profileStore = profileStore ?? store; this.wallet = wallet ?? throw new ArgumentNullException(nameof(wallet)); this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
        if (maxConcurrent != 1) throw new ArgumentOutOfRangeException(nameof(maxConcurrent)); global = new SemaphoreSlim(1, 1);
    }
    public async Task<ProgressionState> GetAsync(string accountId, CancellationToken ct)
    {
        await global.WaitAsync(ct).ConfigureAwait(false); try { return ((await profileStore.ReadAsync(accountId, ct).ConfigureAwait(false)).State ?? ProgressionState.Empty).Validate(); } finally { global.Release(); }
    }
    public IReadOnlyList<ProgressionReview> ReadReviews() => store.ReadReviews();
    public async Task<ProgressionReceipt> ResolveReviewAsync(string accountId, Guid operationId, ProgressionReviewAction action, string evidence, CancellationToken ct)
    {
        if (operationId == Guid.Empty || !Enum.IsDefined(action) || string.IsNullOrWhiteSpace(evidence) || evidence.Length > 2000) throw new ArgumentException("Invalid review resolution.");
        await global.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var operation = store.ReadOperation(accountId, operationId) ?? throw new InvalidOperationException("Missing progression operation.");
            if (operation.Status != ProgressionOperationStatus.NeedsReview) throw new InvalidOperationException("Operation does not require review.");
            switch (action)
            {
                case ProgressionReviewAction.ConfirmApplied:
                    store.ResolveReview(accountId, operationId, action, evidence);
                    return Replay(operation with { Status = ProgressionOperationStatus.Completed }, operation.Fingerprint);
                case ProgressionReviewAction.ForgiveDebit:
                    if (operation.WalletDelta <= 0) throw new InvalidOperationException("Forgiveness requires a debit operation.");
                    store.ResolveReview(accountId, operationId, action, evidence);
                    return Replay(operation with { Status = ProgressionOperationStatus.Completed }, operation.Fingerprint);
                case ProgressionReviewAction.CompensateCreditOnce:
                    if (operation.WalletDelta >= 0) throw new InvalidOperationException("Compensation requires a credit operation.");
                    store.BeginCreditCompensation(accountId, operationId, evidence);
                    try
                    {
                        await DispatchWallet(operation, true).ConfigureAwait(false);
                    }
                    catch
                    {
                        store.SetStatus(accountId, operationId, ProgressionOperationStatus.NeedsReview);
                        return Replay(operation with { Status = ProgressionOperationStatus.NeedsReview }, operation.Fingerprint);
                    }
                    store.SetStatus(accountId, operationId, ProgressionOperationStatus.Completed);
                    return Replay(operation with { Status = ProgressionOperationStatus.Completed }, operation.Fingerprint);
                default: throw new ArgumentException("Invalid review resolution.");
            }
        }
        finally { global.Release(); }
    }
    public async Task<ProgressionReceipt> ExecuteAsync(string accountId, Guid operationId, ProgressionRequest request, long expectedSequence, ImmutableHashSet<string> ownedIds, int arena, CancellationToken ct)
    {
        if (operationId == Guid.Empty) throw new ArgumentException("Invalid operation id."); ArgumentNullException.ThrowIfNull(request); ArgumentNullException.ThrowIfNull(ownedIds); if (expectedSequence < 0 || arena < 0) throw new ArgumentOutOfRangeException(nameof(expectedSequence)); ValidateText(request.TargetId, 128);
        var fingerprint = Fingerprint($"v1|{request.Kind}|{request.TargetId}|{expectedSequence}");
        await global.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var duplicate = store.ReadOperation(accountId, operationId);
            if (duplicate is not null) { Replay(duplicate, fingerprint); return await Resume(duplicate, ct).ConfigureAwait(false); }
            if (store.Pending().Any(x => x.AccountId == accountId)) throw new InvalidOperationException("progression_wallet_review_required");
            var loaded = await profileStore.ReadAsync(accountId, ct).ConfigureAwait(false); var state = (loaded.State ?? ProgressionState.Empty).Validate();
            if (state.AppliedSequence != expectedSequence) throw new InvalidOperationException("stale_progression_sequence");
            var resolution = Resolve(state, request, ownedIds, arena);
            if (resolution.Debit > 0 && await wallet.ReadAsync(accountId, resolution.Currency, ct).ConfigureAwait(false) < resolution.Debit) throw new InvalidOperationException("insufficient_funds");
            var prepared = resolution.State with { AppliedSequence = state.AppliedSequence + 1, AppliedFingerprint = fingerprint };
            var operation = new JournalOperation(accountId, operationId, fingerprint, prepared.AppliedSequence, prepared, resolution.Currency, resolution.Debit, ProgressionOperationStatus.Prepared, string.Join("|", resolution.Resolved));
            store.Prepare(operation);
            return await Resume(operation, ct).ConfigureAwait(false);
        }
        finally { global.Release(); }
    }
    public Task<ProgressionReceipt> ApplyBattleAsync(string accountId, string resultId, ImmutableArray<string> participantUnitIds, bool won, CancellationToken ct, int? frozenXp = null)
    {
        ValidateText(resultId, 64); if (participantUnitIds.Length != 4 || participantUnitIds.Distinct(StringComparer.Ordinal).Count() != 4 || participantUnitIds.Any(x => string.IsNullOrWhiteSpace(x) || x.Length > 128 || !rules.Units.ContainsKey(x))) throw new ArgumentException("Battle participants must be four distinct unit ids.");
        return ApplyBattleCore(accountId, resultId, participantUnitIds, won, ct, frozenXp);
    }
    async Task<ProgressionReceipt> ApplyBattleCore(string accountId, string resultId, ImmutableArray<string> ids, bool won, CancellationToken ct, int? frozenXp)
    {
        await global.WaitAsync(ct).ConfigureAwait(false); try
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes("battle|" + resultId)); var operationId = new Guid(hash.AsSpan(0, 16)); var fingerprint = Fingerprint("battle|" + resultId + "|" + won + "|" + string.Join(',', ids));
            var old = store.ReadOperation(accountId, operationId); if (old is not null) { Replay(old, fingerprint); return await Resume(old, ct).ConfigureAwait(false); }
            if (store.Pending().Any(x => x.AccountId == accountId && x.Status != ProgressionOperationStatus.NeedsReview)) throw new InvalidOperationException("progression_pending");
            var loaded = await profileStore.ReadAsync(accountId, ct).ConfigureAwait(false); var state = (loaded.State ?? ProgressionState.Empty).Validate(); int xp = frozenXp ?? (won ? rules.WinMasteryXp : rules.LossMasteryXp); if (xp is < 0 or > 10000) throw new ArgumentOutOfRangeException(nameof(frozenXp));
            var units = state.Units; foreach (var id in ids) { var value = units.GetValueOrDefault(id, new UnitProgress(1, 0, 0)); units = units.SetItem(id, value with { MasteryXp = AddMastery(value.MasteryXp, xp) }); }
            var prepared = state with { Units = units, AppliedSequence = state.AppliedSequence + 1, AppliedFingerprint = fingerprint };
            var journal = new JournalOperation(accountId, operationId, fingerprint, prepared.AppliedSequence, prepared, "", 0, ProgressionOperationStatus.Prepared, string.Join("|", ids)); store.Prepare(journal);
            return await Resume(journal, ct).ConfigureAwait(false);
        } finally { global.Release(); }
    }
    public async Task RecoverAsync(CancellationToken ct)
    {
        await global.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            foreach (var operation in store.Pending())
            {
                ct.ThrowIfCancellationRequested();
                if (operation.Status == ProgressionOperationStatus.NeedsReview) continue;
                await Resume(operation, ct).ConfigureAwait(false);
            }
        }
        finally { global.Release(); }
    }
    async Task<ProgressionReceipt> Resume(JournalOperation operation, CancellationToken ct)
    {
        if (operation.Status is ProgressionOperationStatus.Completed or ProgressionOperationStatus.NeedsReview)
            return Replay(operation, operation.Fingerprint);
        if (operation.Status == ProgressionOperationStatus.WalletSending)
        {
            store.SetStatus(operation.AccountId, operation.OperationId, ProgressionOperationStatus.NeedsReview);
            return Replay(operation with { Status = ProgressionOperationStatus.NeedsReview }, operation.Fingerprint);
        }
        if (operation.Status == ProgressionOperationStatus.Prepared)
        {
            await ApplyState(operation, ct).ConfigureAwait(false);
            store.SetStatus(operation.AccountId, operation.OperationId, ProgressionOperationStatus.ProfileApplied);
        }
        if (operation.WalletDelta != 0)
        {
            store.SetStatus(operation.AccountId, operation.OperationId, ProgressionOperationStatus.WalletSending);
            try
            {
                await DispatchWallet(operation, false).ConfigureAwait(false);
            }
            catch
            {
                store.SetStatus(operation.AccountId, operation.OperationId, ProgressionOperationStatus.NeedsReview);
                return Replay(operation with { Status = ProgressionOperationStatus.NeedsReview }, operation.Fingerprint);
            }
        }
        store.SetStatus(operation.AccountId, operation.OperationId, ProgressionOperationStatus.Completed);
        return Replay(operation with { Status = ProgressionOperationStatus.Completed }, operation.Fingerprint);
    }
    async Task DispatchWallet(JournalOperation operation, bool creditOnly)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        if (creditOnly || operation.WalletDelta < 0)
            await wallet.CreditAsync(operation.AccountId, operation.Currency, -operation.WalletDelta, timeout.Token).ConfigureAwait(false);
        else
            await wallet.DebitAsync(operation.AccountId, operation.Currency, operation.WalletDelta, timeout.Token).ConfigureAwait(false);
    }
    async Task ApplyState(JournalOperation operation, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var stored = await profileStore.ReadAsync(operation.AccountId, ct).ConfigureAwait(false);
            var current = (stored.State ?? ProgressionState.Empty).Validate();
            if (current.AppliedSequence == operation.Sequence && current.AppliedFingerprint == operation.Fingerprint) return;
            if (current.AppliedSequence != operation.Sequence - 1) throw new ProgressionConflictException();
            try
            {
                await profileStore.WriteAsync(operation.AccountId, operation.PreparedState, stored.Version, ct).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch
            {
                // CAS permits safe read-after-uncertain-write. Never overwrite a newer progression sequence.
                var check = ((await profileStore.ReadAsync(operation.AccountId, ct).ConfigureAwait(false)).State ?? ProgressionState.Empty).Validate();
                if (check.AppliedSequence == operation.Sequence && check.AppliedFingerprint == operation.Fingerprint) return;
                if (check.AppliedSequence != operation.Sequence - 1) throw new ProgressionConflictException();
                if (attempt == 2) throw;
            }
        }
        throw new ProgressionConflictException();
    }
    int AddMastery(int current, int delta)
    {
        int cap = rules.MasterySteps.Count == 0 ? int.MaxValue : rules.MasterySteps.Values.Max(x => x.CumulativeXp);
        return (int)Math.Min(cap, (long)current + delta);
    }
    Resolution Resolve(ProgressionState state, ProgressionRequest request, ImmutableHashSet<string> owned, int arena)
    {
        switch (request.Kind)
        {
            case ProgressionRequestKind.UpgradeUnit:
                var unit = Eligible(request.TargetId, owned, arena); var progress = state.Units.GetValueOrDefault(unit.Id, new UnitProgress(1, 0, 0)); if (!rules.LevelSteps.TryGetValue(progress.Level, out var step)) throw new InvalidOperationException("unit_max_level"); if (progress.Bits < step.BitsCost) throw new InvalidOperationException("insufficient_bits");
                return new Resolution(state with { Units = state.Units.SetItem(unit.Id, progress with { Level = progress.Level + 1, Bits = progress.Bits - step.BitsCost }) }, "CO", step.CoinsCost, ImmutableArray.Create(unit.Id));
            case ProgressionRequestKind.BuyOffer:
                if (!rules.Offers.TryGetValue(request.TargetId, out var offer)) throw new InvalidOperationException("unknown_offer"); var resolved = ResolveOffer(offer, owned, arena); var afterOffer = AddBits(state, resolved); return new Resolution(afterOffer, offer.Currency, offer.Price, resolved.Select(x => x.Id.Id).ToImmutableArray());
            case ProgressionRequestKind.BoostMastery:
                var boosted = Eligible(request.TargetId, owned, arena); if (state.MasteryChips < rules.MasteryBoostChipCost) throw new InvalidOperationException("insufficient_chips"); var boost = state.Units.GetValueOrDefault(boosted.Id, new UnitProgress(1, 0, 0)); if (AddMastery(boost.MasteryXp, rules.MasteryBoostXp) == boost.MasteryXp) throw new InvalidOperationException("mastery_max_level"); return new Resolution(state with { MasteryChips = state.MasteryChips - rules.MasteryBoostChipCost, Units = state.Units.SetItem(boosted.Id, boost with { MasteryXp = AddMastery(boost.MasteryXp, rules.MasteryBoostXp) }) }, "", 0, ImmutableArray.Create(boosted.Id));
            case ProgressionRequestKind.BuyMasteryLevel:
                var masteryUnit = Eligible(request.TargetId, owned, arena); var masteryProgress = state.Units.GetValueOrDefault(masteryUnit.Id, new UnitProgress(1, 0, 0)); var next = rules.MasterySteps.Values.Where(x => x.CumulativeXp > masteryProgress.MasteryXp).OrderBy(x => x.Level).FirstOrDefault(); if (next is null || !rules.BuyMasteryLevelGemCosts.TryGetValue(next.Level, out var gems)) throw new InvalidOperationException("mastery_max_level"); return new Resolution(state with { Units = state.Units.SetItem(masteryUnit.Id, masteryProgress with { MasteryXp = next.CumulativeXp }) }, "GM", gems, ImmutableArray.Create(masteryUnit.Id));
            case ProgressionRequestKind.ClaimMilestone:
                var pair = request.TargetId.Split(':'); if (pair.Length != 2 || !int.TryParse(pair[1], out var milestone)) throw new InvalidOperationException("invalid_milestone"); var milestoneUnit = Eligible(pair[0], owned, arena); var key = milestoneUnit.Id + ":" + milestone; if (state.ClaimedMilestones.Contains(key) || !rules.MasterySteps.TryGetValue(milestone, out var mastery) || mastery.RewardKind == MasteryRewardKind.Cosmetic || mastery.RewardAmount <= 0 || state.Units.GetValueOrDefault(milestoneUnit.Id, new UnitProgress(1, 0, 0)).MasteryXp < mastery.CumulativeXp) throw new InvalidOperationException("milestone_unavailable"); return Claim(state, milestoneUnit.Id, key, mastery);
            default: throw new InvalidOperationException("unknown_progression_request");
        }
    }
    Resolution Claim(ProgressionState state, string id, string key, MasteryStep step)
    {
        var marked = state with { ClaimedMilestones = state.ClaimedMilestones.Add(key) };
        return step.RewardKind switch { MasteryRewardKind.Bits => new Resolution(AddBits(marked, ImmutableArray.Create<(UnitDefinition Id, int Bits)>((rules.Units[id], step.RewardAmount))), "", 0, ImmutableArray.Create(id)), MasteryRewardKind.Chips => new Resolution(marked with { MasteryChips = checked(marked.MasteryChips + step.RewardAmount) }, "", 0, ImmutableArray.Create(id)), MasteryRewardKind.Coins => new Resolution(marked, "CO", -step.RewardAmount, ImmutableArray.Create(id)), MasteryRewardKind.Cosmetic => new Resolution(marked, "", 0, ImmutableArray.Create(id)), _ => throw new InvalidOperationException() };
    }
    ImmutableArray<(UnitDefinition Id, int Bits)> ResolveOffer(OfferDefinition offer, ImmutableHashSet<string> owned, int arena)
    {
        if (offer.Price < 0 || offer.Bits < 0) throw new InvalidOperationException("invalid_offer"); if (offer.PackId is null) return ImmutableArray.Create((Eligible(offer.UnitId, owned, arena), offer.Bits)); if (!rules.Packs.TryGetValue(offer.PackId, out var pack)) throw new InvalidOperationException("unknown_pack");
        var available = rules.Units.Values.Where(x => owned.Contains(x.Id) && x.Arena <= arena).ToList(); var chosen = ImmutableArray.CreateBuilder<(UnitDefinition, int)>(); for (var draw = 0; draw < pack.DrawCount; draw++) { var possible = pack.Rarities.Where(r => available.Any(x => x.Rarity == r.Rarity && !chosen.Any(y => y.Item1.Id == x.Id))).ToArray(); if (possible.Length == 0) throw new InvalidOperationException("pack_unavailable"); var roll = RandomNumberGenerator.GetInt32(possible.Sum(x => x.Weight)); var rarity = possible.First(x => (roll -= x.Weight) < 0); var candidates = available.Where(x => x.Rarity == rarity.Rarity && !chosen.Any(y => y.Item1.Id == x.Id)).ToArray(); var item = candidates[RandomNumberGenerator.GetInt32(candidates.Length)]; chosen.Add((item, RandomNumberGenerator.GetInt32(rarity.MinBits, rarity.MaxBits + 1))); } return chosen.ToImmutable();
    }
    static ProgressionState AddBits(ProgressionState state, ImmutableArray<(UnitDefinition Id, int Bits)> entries) { foreach (var entry in entries) { var progress = state.Units.GetValueOrDefault(entry.Id.Id, new UnitProgress(1, 0, 0)); state = state with { Units = state.Units.SetItem(entry.Id.Id, progress with { Bits = checked(progress.Bits + entry.Bits) }) }; } return state; }
    UnitDefinition Eligible(string id, ImmutableHashSet<string> owned, int arena) { if (!rules.Units.TryGetValue(id, out var unit) || !owned.Contains(id) || unit.Arena > arena) throw new InvalidOperationException("unit_unavailable"); return unit; }
    static ProgressionReceipt Replay(JournalOperation operation, string fingerprint) { if (operation.Fingerprint != fingerprint) throw new InvalidOperationException("operation_conflict"); return new ProgressionReceipt(operation.OperationId, operation.Sequence, operation.Status, operation.PreparedState, operation.ResolvedJson.Length == 0 ? ImmutableArray<string>.Empty : operation.ResolvedJson.Split('|').ToImmutableArray()); }
    static string Fingerprint(string text) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static void ValidateText(string? value, int max) { if (string.IsNullOrWhiteSpace(value) || value.Length > max) throw new ArgumentException("Invalid text."); }
    sealed record Resolution(ProgressionState State, string Currency, int Debit, ImmutableArray<string> Resolved);
}
