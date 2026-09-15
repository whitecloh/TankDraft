using System.Collections.Immutable;
using TankDraft.Server.Meta;
using TankDraft.Server.Progression;

namespace TankDraft.RemoteHost;

public sealed partial class RemoteMatchService
{
    ProgressionService? progression;
    ProgressionRules? progressionRules;
    IDisposable? progressionJournal;
    long nextProgressionRetry;
    public bool ProgressionProviderPending { get; private set; }
    IReadOnlyDictionary<string, TankDraft.Match.Domain.PersistentBattleBonus> ProgressionBonuses(ProgressionState state, IEnumerable<string> unitIds)
    {
        var result = new Dictionary<string, TankDraft.Match.Domain.PersistentBattleBonus>(StringComparer.Ordinal);
        foreach (var id in unitIds)
        {
            var unit = state.Units.GetValueOrDefault(id, new UnitProgress(1, 0, 0));
            var levels = progressionRules!.LevelSteps.Values.Where(x => x.FromLevel < unit.Level).ToArray();
            var mastery = progressionRules.MasterySteps.Values.Where(x => x.CumulativeXp <= unit.MasteryXp).ToArray();
            result.Add(id, new TankDraft.Match.Domain.PersistentBattleBonus(
                (levels.Sum(x => x.HpPermille) + mastery.Sum(x => x.HpPermille)) / 10,
                (levels.Sum(x => x.DamagePermille) + mastery.Sum(x => x.DamagePermille)) / 10,
                mastery.Sum(x => x.AttackSpeedPermille) / 10));
        }
        return result;
    }
    internal void ConfigureProgression(ProgressionService service, ProgressionRules rules, IDisposable? journal = null)
    {
        lock(sync)
        {
            if (lobbies.Count != 0 || progression is not null || meta is null) throw new InvalidOperationException("Configure progression after meta and before admission.");
            progression = service; progressionRules = rules; progressionJournal = journal;
        }
    }
    internal void EnablePlayFabProgression(PlayFabMetaTransport transport, string privateDirectory)
    {
        var rules = ProgressionContent.Load().Rules();
        var journal = new SqliteProgressionStore(Path.Combine(privateDirectory, "fusion-progression", "operations.sqlite"));
        try
        {
            var provider = new PlayFabProgressionStore(transport, resolveMetaIdentity!, allowlist);
            ConfigureProgression(new ProgressionService(journal, new PlayFabProgressionWallet(transport, allowlist), rules, provider), rules, journal);
        }
        catch { journal.Dispose(); throw; }
    }
    public async Task<object> GetProgressionAsync(string lobby, string version, CancellationToken ct)
    {
        string account = BeginMeta(lobby, version, false);
        try
        {
            if (progression is null) return new { Enabled = false };
            var state = await progression.GetAsync(account, ct);
            lock(sync) { if (UseLobby(lobby) != account) throw new UnauthorizedAccessException(); }
            return new { Enabled = true, State = state, RulesVersion = progressionRules!.Version };
        }
        finally { EndMeta(account); }
    }
    public async Task<object> ExecuteProgressionAsync(string lobby, string version, string kind, string targetId, string operationId, long expectedSequence, CancellationToken ct)
    {
        if (!Guid.TryParseExact(operationId, "N", out var id) || id == Guid.Empty || !Enum.TryParse<ProgressionRequestKind>(kind, false, out var action) || !Enum.IsDefined(action) || action.ToString() != kind)
            throw new MetaFailureException("invalid_operation");
        string account = BeginMeta(lobby, version, true);
        try
        {
            if (progression is null || meta is null) throw new MetaFailureException("progression_disabled");
            var actor = await resolveMetaIdentity!(account, ct);
            if (actor.AccountId != account) throw new UnauthorizedAccessException();
            var trusted = await meta.GetAsync(actor, ct);
            var receipt = await progression.ExecuteAsync(account, id, new(action, targetId), expectedSequence, trusted.Inventory.OwnedContentIds, trusted.Profile.ArenaLevel, ct);
            var current = await progression.GetAsync(account, ct);
            lock(sync) { if (UseLobby(lobby) != account) throw new UnauthorizedAccessException(); }
            return new { Enabled = true, State = current, RulesVersion = progressionRules!.Version,
                Receipt = new { receipt.OperationId, receipt.Sequence, Status = receipt.Status.ToString(), receipt.ResolvedUnitIds } };
        }
        catch (ProgressionConflictException) { throw new MetaFailureException("version_conflict"); }
        catch (InvalidOperationException error) when (error.Message is "stale_progression_sequence" or "insufficient_funds" or "insufficient_bits" or "insufficient_chips" or "unit_max_level" or "mastery_max_level" or "unknown_offer" or "unit_unavailable" or "milestone_unavailable" or "invalid_milestone" or "pack_unavailable" or "operation_conflict" or "progression_wallet_review_required" or "progression_pending" or "progression_capacity")
        { throw new MetaFailureException(error.Message); }
        finally { EndMeta(account); }
    }
    async Task DispatchProgressionAsync(CancellationToken ct)
    {
        if (progression is null) return;
        if (clock.GetTimestamp() < nextProgressionRetry) return;
        try
        {
        await progression.RecoverAsync(ct);
        if (settlements is null) return;
        foreach (var pending in settlements.ReadParticipantRewards(pendingOnly: true))
        {
            var receipt = await progression.ApplyBattleAsync(pending.AccountId, pending.ResultId, pending.UnitIds.ToImmutableArray(), pending.Won, ct, pending.MasteryXp);
            if (receipt.Status == ProgressionOperationStatus.Completed) settlements.MarkParticipantApplied(pending.ResultId, pending.AccountId);
        }
        ProgressionProviderPending = false;
        }
        catch (Exception error) when (error is HttpRequestException || error is TaskCanceledException && !ct.IsCancellationRequested || error is MetaFailureException failure && failure.Code is "provider_unavailable" or "busy")
        {
            // The durable obligation remains pending; a provider outage must not kill an active battle.
            ProgressionProviderPending = true;
            nextProgressionRetry = clock.GetTimestamp() + clock.TimestampFrequency * 5;
        }
    }
}
