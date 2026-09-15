using TankDraft.Match.Domain;
using TankDraft.Server.Settlement;

namespace TankDraft.RemoteHost;

public sealed partial class RemoteMatchService
{
    SqliteSettlementStore? settlements;
    SettlementDispatcher? rewards;
    bool settlementFault;
    int rewardDispatching;
    public bool TestRewardsEnabled => rewards is not null;

    // Owned by this runtime. Configure before accepting identities; persisted history is independent of InstanceId.
    internal void ConfigureSettlement(SqliteSettlementStore store, IRewardProvider provider)
    {
        lock (sync)
        {
            if (settlements is not null || lobbies.Count != 0) throw new InvalidOperationException("Configure settlement before admission.");
            settlements = store;
            rewards = new SettlementDispatcher(store, provider);
        }
    }

    internal async Task<int> DispatchRewardsAsync(CancellationToken token)
    {
        if (rewards is null || Interlocked.CompareExchange(ref rewardDispatching, 1, 0) != 0) return 0;
        try { var count = await rewards.RunOnceAsync(token); await DispatchProgressionAsync(token); return count; }
        finally { Volatile.Write(ref rewardDispatching, 0); }
    }

    internal bool CanFinishDrain
    {
        get
        {
            lock (sync) return !settlementFault && matches.Values.All(m => m.Domain.Phase == MatchPhase.MatchResult)
                && (settlements?.ReadPending(1).Count ?? 0) == 0 && (progression is null || (settlements?.ReadParticipantRewards(pendingOnly: true).Count ?? 0) == 0) && Volatile.Read(ref rewardDispatching) == 0;
        }
    }

    internal object[] ReadResultHistory(string lobby)
    { lock (sync) return SettlementHistory(UseLobby(lobby)); }

    void PersistCompletion(Match match)
    {
        if (settlementFault) throw new InvalidOperationException("settlement_storage_unavailable");
        if (settlements is null || match.ResultPersisted || match.Domain.Phase != MatchPhase.MatchResult) return;
        try
        {
            var state = match.Runtime.ReadCompletion() ?? throw new InvalidDataException("Final result required.");
            settlements.Record(new CompletedMatch(SqliteSettlementStore.ResultIdFor(match.Id, ContentVersion),
                match.Id, ContentVersion, match.Side0, match.HasBot ? null : match.Side1,
                state.Wins0, state.Wins1, state.Winner, state.Revision),
                progression is null ? null : match.Deck0, progression is null || match.HasBot ? null : match.Deck1,
                progressionRules?.WinMasteryXp, progressionRules?.LossMasteryXp, progressionRules?.Version);
            match.ResultPersisted = true;
        }
        catch
        {
            // Do not publish an uncommitted final state/ACK or accept another match after storage failure.
            settlementFault = true;
            draining = true;
            throw;
        }
    }

    object[] SettlementHistory(string account)
    {
        if (settlements is null) return [];
        var receipts = settlements.ReadRewards(account).ToDictionary(x => x.Grant.ResultId, StringComparer.Ordinal);
        var participants = settlements.ReadParticipantRewards(account).ToDictionary(x => x.ResultId, StringComparer.Ordinal);
        return settlements.ReadResults(account, 20).Select(result =>
        {
            var side = result.Account0 == account ? 0 : 1;
            var receipt = receipts.GetValueOrDefault(result.ResultId);
            var participant = participants.GetValueOrDefault(result.ResultId);
            // No opponent/account identifiers or provider response payloads leave the authority.
            return (object)new { result.ResultId, result.MatchId, result.ContentVersion, result.Wins0, result.Wins1,
                result.Winner, Side = side, OpponentKind = result.Account1 is null ? "Bot" : "Human",
                ParticipantReward = participant is null ? null : new { participant.UnitIds, participant.Applied,
                    participant.MasteryXp, participant.RulesVersion },
                Reward = receipt is null ? null : new { receipt.Grant.Currency, receipt.Grant.Amount,
                    State = receipt.State.ToString(), receipt.Reason } };
        }).ToArray();
    }
}
