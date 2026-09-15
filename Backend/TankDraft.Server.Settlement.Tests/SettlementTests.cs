using TankDraft.Server.Settlement;
using Xunit;
using Microsoft.Data.Sqlite;

namespace TankDraft.Server.Settlement.Tests;

public sealed class SettlementTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "tankdraft-settlement-" + Guid.NewGuid().ToString("N"));
    private const string Account0 = "Player01";
    private const string Account1 = "Player02";
    private static readonly RewardPolicy Policy = new("qa-v1", "CO", 10, 3, 2);
    public SettlementTests() => Directory.CreateDirectory(_directory);
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }

    [Fact]
    public void Reopen_duplicate_is_noop_and_conflict_is_rejected()
    {
        var path = DatabasePath(); var match = Match("match-1", Account0, Account1, 0);
        using (var store = new SqliteSettlementStore(path, Policy)) { store.Record(match); store.Record(match); Assert.Single(store.ReadResults(Account0)); }
        using var reopened = new SqliteSettlementStore(path, Policy);
        reopened.Record(match);
        Assert.Throws<InvalidOperationException>(() => reopened.Record(match with { Wins1 = 1 }));
    }

    [Fact]
    public void Participant_snapshot_freezes_rewards_and_is_immutable_after_reopen()
    {
        var path = DatabasePath();
        var match = Match("participant-frozen", Account0, Account1, 0);
        var first = new[] { "mine", "tank", "destroyer", "artillery" };
        var second = new[] { "drone", "raider", "stealth", "engineer" };
        using (var store = new SqliteSettlementStore(path, Policy))
        {
            store.Record(match, first, second, winMasteryXp: 10, lossMasteryXp: 5, progressionRulesVersion: "progression-v1");
            var player0 = Assert.Single(store.ReadParticipantRewards(Account0));
            Assert.True(player0.Won); Assert.Equal(10, player0.MasteryXp); Assert.Equal("progression-v1", player0.RulesVersion);
            var player1 = Assert.Single(store.ReadParticipantRewards(Account1));
            Assert.False(player1.Won); Assert.Equal(5, player1.MasteryXp);
            store.Record(match, first, second, winMasteryXp: 10, lossMasteryXp: 5, progressionRulesVersion: "progression-v1");
            Assert.Throws<InvalidOperationException>(() => store.Record(match, first, second, winMasteryXp: 11, lossMasteryXp: 5, progressionRulesVersion: "progression-v1"));
            Assert.Throws<InvalidOperationException>(() => store.Record(match, first, second, winMasteryXp: 10, lossMasteryXp: 5, progressionRulesVersion: "progression-v2"));
        }
        using var reopened = new SqliteSettlementStore(path, Policy);
        var history = reopened.ReadParticipantRewards(Account0);
        Assert.Single(history); Assert.Equal(10, history[0].MasteryXp); Assert.Equal("progression-v1", history[0].RulesVersion);
    }

    [Fact]
    public void Participant_history_is_newest_first_but_pending_dispatch_is_oldest_first()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        var first = Match("participant-order-a", Account0, null, 0);
        var second = Match("participant-order-b", Account0, null, 0);
        var deck = new[] { "mine", "tank", "destroyer", "artillery" };
        store.Record(first, deck, null, 10, 5, "progression-v1");
        store.Record(second, deck, null, 10, 5, "progression-v1");
        Assert.Equal(new[] { second.ResultId, first.ResultId }, store.ReadParticipantRewards(Account0).Select(x => x.ResultId));
        Assert.Equal(new[] { first.ResultId, second.ResultId }, store.ReadParticipantRewards(pendingOnly: true).Select(x => x.ResultId));
    }

    [Fact]
    public void Both_humans_receive_rewards_but_bot_does_not()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("match-2", Account0, Account1, 0));
        store.Record(Match("match-3", Account0, null, 0));
        Assert.Equal(2, store.ReadRewards(Account0).Count);
        Assert.Single(store.ReadRewards(Account1));
        Assert.Empty(store.ReadResults("Player03"));
    }

    [Fact]
    public async Task Provider_throw_after_apply_leaves_needs_review_and_quarantines_account()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("match-4", Account0, null, 0));
        store.Record(Match("match-5", Account0, null, 0));
        var provider = new ThrowingProvider();
        using var dispatcher = new SettlementDispatcher(store, provider);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        var rewards = store.ReadRewards(Account0);
        Assert.Contains(rewards, x => x.State == RewardState.NeedsReview && x.Reason == "provider_uncertain");
        Assert.Contains(rewards, x => x.State == RewardState.Pending);
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public void Sending_is_quarantined_on_reopen_and_writer_is_exclusive()
    {
        var path = DatabasePath();
        using (var store = new SqliteSettlementStore(path, Policy))
        {
            store.Record(Match("match-6", Account0, null, 0));
            var grant = Assert.Single(store.ReadPending()).Grant;
            Assert.True(store.TryBegin(grant));
            Assert.Throws<IOException>(() => new SqliteSettlementStore(path, Policy));
        }
        using var reopened = new SqliteSettlementStore(path, Policy);
        Assert.Equal(RewardState.NeedsReview, Assert.Single(reopened.ReadRewards(Account0)).State);
    }

    [Fact]
    public async Task Cap_skips_later_obligation_and_success_is_applied_once()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("match-7", Account0, null, 0));
        store.Record(Match("match-8", Account0, null, 0));
        store.Record(Match("match-9", Account0, null, 0));
        Assert.Contains(store.ReadRewards(Account0), x => x.State == RewardState.Skipped && x.Reason == "qa_reward_cap");
        var provider = new RecordingProvider();
        using var dispatcher = new SettlementDispatcher(store, provider);
        Assert.Equal(2, await dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.All(store.ReadRewards(Account0).Where(x => x.State != RewardState.Skipped), x => Assert.Equal(RewardState.Applied, x.State));
        Assert.Equal(2, provider.Calls);
    }

    [Fact]
    public void Pending_query_skips_more_than_limit_blocked_accounts()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        for (var index = 0; index < 17; index++)
        {
            var account = "Block" + index.ToString("D2", System.Globalization.CultureInfo.InvariantCulture);
            store.Record(Match("blocked-a-" + index, account, null, 0));
            store.Record(Match("blocked-b-" + index, account, null, 0));
            Assert.True(store.TryBegin(store.ReadRewards(account)[0].Grant));
        }
        store.Record(Match("healthy-pending", "Healthy01", null, 0));
        var pending = store.ReadPending();
        Assert.Single(pending);
        Assert.Equal("Healthy01", pending[0].Grant.AccountId);
    }

    [Fact]
    public async Task Concurrent_runs_are_single_flight_and_cancellation_before_begin_keeps_pending()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("single-flight", Account0, null, 0));
        var provider = new GatedProvider();
        using var dispatcher = new SettlementDispatcher(store, provider);
        var first = dispatcher.RunOnceAsync(CancellationToken.None);
        await provider.Entered.Task;
        Assert.Equal(0, await dispatcher.RunOnceAsync(CancellationToken.None));
        provider.Release.SetResult();
        Assert.Equal(1, await first);

        store.Record(Match("cancel-before", Account1, null, 0));
        Assert.Equal(0, await dispatcher.RunOnceAsync(new CancellationToken(canceled: true)));
        Assert.Equal(RewardState.Pending, Assert.Single(store.ReadRewards(Account1)).State);
    }

    [Fact]
    public async Task Provider_application_then_throw_is_once_and_other_account_can_succeed()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("uncertain-account0", Account0, null, 0));
        store.Record(Match("success-account1", Account1, null, 0));
        var provider = new BalanceThenThrowProvider(Account0);
        using var dispatcher = new SettlementDispatcher(store, provider);
        Assert.Equal(1, await dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.Equal(10, provider.Balances[Account0]);
        Assert.Equal(10, provider.Balances[Account1]);
        Assert.Equal(RewardState.NeedsReview, Assert.Single(store.ReadRewards(Account0)).State);
        Assert.Equal(RewardState.Applied, Assert.Single(store.ReadRewards(Account1)).State);
    }

    [Fact]
    public void Existing_empty_corrupt_policy_and_relative_path_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new SqliteSettlementStore("settlement.sqlite", Policy));
        var empty = DatabasePath();
        File.WriteAllBytes(empty, []);
        Assert.Throws<InvalidDataException>(() => new SqliteSettlementStore(empty, Policy));

        var path = Path.Combine(_directory, "corrupt.sqlite");
        using (var store = new SqliteSettlementStore(path, Policy)) { store.Record(Match("corrupt-match", Account0, null, 0)); }
        using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "UPDATE Metadata SET Value='bad' WHERE Key='policy';";
            command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => new SqliteSettlementStore(path, Policy));
    }

    [Fact]
    public async Task Confirm_applied_after_lost_response_is_audited_without_another_provider_call()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("confirm-lost", Account0, null, 0));
        var provider = new BalanceThenThrowProvider(Account0, 1);
        using var dispatcher = new SettlementDispatcher(store, provider);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        var grant = Assert.Single(store.ReadRewards(Account0)).Grant;
        Assert.True(store.ResolveReview(grant, ReviewResolution.ConfirmApplied, "inventory-verify-001"));
        Assert.Equal(1, provider.Calls);
        Assert.Equal(10, provider.Balances[Account0]);
        var receipt = Assert.Single(store.ReadRewards(Account0));
        Assert.Equal(RewardState.Applied, receipt.State);
        Assert.Equal("verified_external", receipt.Reason);
        var audit = Assert.Single(store.ReadReviewResolutions(grant));
        Assert.Equal("provider_uncertain", audit.OriginalReason);
        Assert.Equal(0, await dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.Equal(1, provider.Calls);
    }

    [Fact]
    public async Task Compensation_success_is_limited_to_two_provider_grants_and_keeps_authorization_reason()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("compensate-success", Account0, null, 0));
        var provider = new BalanceThenThrowProvider(Account0, 1);
        using var dispatcher = new SettlementDispatcher(store, provider);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        var grant = Assert.Single(store.ReadRewards(Account0)).Grant;
        Assert.True(store.ResolveReview(grant, ReviewResolution.CompensateOnce, "inventory-verify-002"));
        Assert.Equal(1, await dispatcher.RunOnceAsync(CancellationToken.None));
        var receipt = Assert.Single(store.ReadRewards(Account0));
        Assert.Equal(RewardState.Applied, receipt.State);
        Assert.Equal("compensation_authorized", receipt.Reason);
        Assert.Equal(2, provider.Calls);
        Assert.Equal(20, provider.Balances[Account0]);
        Assert.Single(store.ReadReviewResolutions(grant));
    }

    [Fact]
    public async Task Compensation_lost_response_cannot_authorize_a_third_grant_and_can_be_confirmed()
    {
        using var store = new SqliteSettlementStore(DatabasePath(), Policy);
        store.Record(Match("compensate-lost", Account0, null, 0));
        var provider = new BalanceThenThrowProvider(Account0, 2);
        using var dispatcher = new SettlementDispatcher(store, provider);
        await dispatcher.RunOnceAsync(CancellationToken.None);
        var grant = Assert.Single(store.ReadRewards(Account0)).Grant;
        Assert.True(store.ResolveReview(grant, ReviewResolution.CompensateOnce, "inventory-verify-003a"));
        await dispatcher.RunOnceAsync(CancellationToken.None);
        Assert.Equal(RewardState.NeedsReview, Assert.Single(store.ReadRewards(Account0)).State);
        Assert.True(store.ResolveReview(grant, ReviewResolution.ConfirmApplied, "inventory-verify-003b"));
        Assert.Equal(0, await dispatcher.RunOnceAsync(CancellationToken.None));
        Assert.Equal(2, provider.Calls);
        Assert.Equal(20, provider.Balances[Account0]);
        Assert.Equal(2, store.ReadReviewResolutions(grant).Count);
    }

    [Fact]
    public void Review_resolution_is_idempotent_persistent_and_rejects_invalid_operator_commands()
    {
        var path = DatabasePath();
        RewardGrant grant;
        using (var store = new SqliteSettlementStore(path, Policy))
        {
            store.Record(Match("operator-resolution", Account0, null, 0));
            grant = Assert.Single(store.ReadRewards(Account0)).Grant;
            Assert.Throws<InvalidOperationException>(() => store.ResolveReview(grant, ReviewResolution.ConfirmApplied, "evidence-004"));
            Assert.Throws<ArgumentException>(() => store.ResolveReview(grant, ReviewResolution.ConfirmApplied, "unsafe evidence text"));
            Assert.Throws<InvalidOperationException>(() => store.ResolveReview(grant with { Amount = grant.Amount + 1 }, ReviewResolution.ConfirmApplied, "evidence-004"));
            Assert.True(store.TryBegin(grant));
            store.MarkNeedsReview(grant, "provider_uncertain");
            Assert.True(store.ResolveReview(grant, ReviewResolution.CompensateOnce, "evidence-004"));
            Assert.False(store.ResolveReview(grant, ReviewResolution.CompensateOnce, "evidence-004"));
            Assert.Throws<InvalidOperationException>(() => store.ResolveReview(grant, ReviewResolution.CompensateOnce, "evidence-005"));
        }
        using var reopened = new SqliteSettlementStore(path, Policy);
        Assert.Single(reopened.ReadReviewResolutions(grant));
        Assert.Equal(RewardState.Pending, Assert.Single(reopened.ReadRewards(Account0)).State);
    }

    [Fact]
    public void Database_without_the_additive_audit_table_reopens_without_changing_rewards()
    {
        var path = DatabasePath();
        RewardReceipt expected;
        using (var store = new SqliteSettlementStore(path, Policy))
        {
            store.Record(Match("legacy-schema", Account0, null, 0));
            expected = Assert.Single(store.ReadRewards(Account0));
        }
        using (var connection = new SqliteConnection("Data Source=" + path + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "DROP TABLE ReviewResolutions;"; command.ExecuteNonQuery();
        }
        using var reopened = new SqliteSettlementStore(path, Policy);
        Assert.Equal(expected, Assert.Single(reopened.ReadRewards(Account0)));
        Assert.Empty(reopened.ReadReviewResolutions(expected.Grant));
    }

    [Fact]
    public void Reopen_rejects_resolution_audit_state_mismatch_and_reserved_reason_without_audit()
    {
        var mismatchPath = Path.Combine(_directory, "audit-mismatch.sqlite");
        using (var store = new SqliteSettlementStore(mismatchPath, Policy))
        {
            store.Record(Match("audit-mismatch", Account0, null, 0));
            var grant = Assert.Single(store.ReadRewards(Account0)).Grant;
            Assert.True(store.TryBegin(grant));
            store.MarkNeedsReview(grant, "provider_uncertain");
            Assert.True(store.ResolveReview(grant, ReviewResolution.ConfirmApplied, "evidence-006a"));
        }
        using (var connection = new SqliteConnection("Data Source=" + mismatchPath + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE Rewards SET State=0, Reason=NULL;"; command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => new SqliteSettlementStore(mismatchPath, Policy));

        var reservedPath = Path.Combine(_directory, "reserved-reason.sqlite");
        using (var store = new SqliteSettlementStore(reservedPath, Policy)) store.Record(Match("reserved-reason", Account0, null, 0));
        using (var connection = new SqliteConnection("Data Source=" + reservedPath + ";Pooling=False"))
        {
            connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "UPDATE Rewards SET State=2, Reason='compensation_authorized';"; command.ExecuteNonQuery();
        }
        Assert.Throws<InvalidDataException>(() => new SqliteSettlementStore(reservedPath, Policy));
    }

    private string DatabasePath() => Path.Combine(_directory, "settlement.sqlite");
    private static CompletedMatch Match(string matchId, string account0, string? account1, int winner)
    {
        const string version = "content-v1";
        return new CompletedMatch(SqliteSettlementStore.ResultIdFor(matchId, version), matchId, version, account0, account1,
            winner == 0 ? 4 : 2, winner == 1 ? 4 : 2, winner, 9);
    }
    private sealed class ThrowingProvider : IRewardProvider
    {
        public int Calls { get; private set; }
        public Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken) { Calls++; throw new InvalidOperationException("applied-then-threw"); }
    }
    private sealed class RecordingProvider : IRewardProvider
    {
        public int Calls { get; private set; }
        public Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken) { Calls++; return Task.CompletedTask; }
    }
    private sealed class GatedProvider : IRewardProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken)
        {
            Entered.SetResult();
            await Release.Task.ConfigureAwait(false);
        }
    }
    private sealed class BalanceThenThrowProvider(string throwingAccount, int throwsRemaining = 1) : IRewardProvider
    {
        public Dictionary<string, int> Balances { get; } = new(StringComparer.Ordinal);
        public int Calls { get; private set; }
        public Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken)
        {
            Calls++;
            Balances.TryGetValue(grant.AccountId, out var balance);
            Balances[grant.AccountId] = balance + grant.Amount;
            if (grant.AccountId == throwingAccount && throwsRemaining-- > 0) throw new OperationCanceledException(cancellationToken);
            return Task.CompletedTask;
        }
    }
}
