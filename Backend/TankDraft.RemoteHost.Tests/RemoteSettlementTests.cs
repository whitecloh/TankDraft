using System.Text.Json;
using PlayFab;
using PlayFab.ServerModels;
using TankDraft.RemoteHost;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Settlement;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class RemoteSettlementTests
{
    static readonly RewardPolicy Policy = new("qa-r32-v1", "CO", 1, 1, 10);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineCompletionPersistsBeforePublicationAndSurvivesRetentionAndNewInstance(bool bot)
    {
        var folder = Path.Combine(Path.GetTempPath(), "td-settlement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "results.sqlite");
        var provider = new CountingProvider();
        try
        {
            string matchId;
            using (var fixture = new Fixture(path, provider))
            {
                var a = fixture.Service.LoginFromPhotonGateway("acctA", "login-a");
                fixture.Service.Join(a.LobbyToken, "join-a", fixture.Service.ContentVersion);
                if (!bot)
                {
                    var b = fixture.Service.LoginFromPhotonGateway("acctB", "login-b");
                    fixture.Service.Join(b.LobbyToken, "join-b", fixture.Service.ContentVersion);
                }
                else { fixture.Clock.Advance(10); fixture.Service.Pump(); }
                var access = fixture.Service.ExchangeFromPhotonGateway("acctA", fixture.Service.ContentVersion, "session-a", CancellationToken.None);
                matchId = access.MatchId;
                var done = false;
                for (var step = 0; step < 120 && !done; step++)
                {
                    fixture.Clock.Advance(10);
                    for (var settle = 0; settle < 128; settle++) fixture.Service.Pump();
                    var current = fixture.Service.ExchangeFromPhotonGateway("acctA", fixture.Service.ContentVersion, "step-" + step, CancellationToken.None);
                    var snapshot = fixture.Service.UseAccess(current.AccessToken, (match, actor) => match.Runtime.Capture(actor.Caller));
                    done = snapshot.Phase == "MatchResult";
                    if (done) Assert.Equal(snapshot.Revision, Assert.Single(fixture.Store.ReadResults("acctA")).FinalRevision);
                }
                Assert.True(done);
                Assert.Equal(bot ? 1 : 2, await fixture.Service.DispatchRewardsAsync(CancellationToken.None));
                Assert.Equal(0, await fixture.Service.DispatchRewardsAsync(CancellationToken.None));
                fixture.Clock.Advance(121); fixture.Service.Pump();
                var newLobby = fixture.Service.LoginFromPhotonGateway("acctA", "after-retention");
                Assert.Single(fixture.Service.ReadResultHistory(newLobby.LobbyToken));
            }
            var calls = provider.Calls;
            using (var restarted = new Fixture(path, provider))
            {
                Assert.Equal(0, await restarted.Service.DispatchRewardsAsync(CancellationToken.None));
                Assert.Equal(calls, provider.Calls);
                var a = restarted.Service.LoginFromPhotonGateway("acctA", "new-instance");
                var history = Assert.Single(restarted.Service.ReadResultHistory(a.LobbyToken));
                var json = JsonSerializer.Serialize(history);
                Assert.Contains(matchId, json);
                Assert.Contains("Applied", json);
                Assert.DoesNotContain("acctA", json); Assert.DoesNotContain("acctB", json);
                var outsider = restarted.Service.LoginFromPhotonGateway("acctC", "other");
                Assert.Empty(restarted.Service.ReadResultHistory(outsider.LobbyToken));
                Assert.Throws<UnauthorizedAccessException>(() => restarted.Service.ReadResultHistory("not-a-lobby"));
            }
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task ProviderFailureDoesNotBlockOtherPlayerOrEraseTheResult()
    {
        var folder = Path.Combine(Path.GetTempPath(), "td-settlement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var provider = new CountingProvider { FailAccount = "acctA" };
            using var fixture = new Fixture(Path.Combine(folder, "results.sqlite"), provider);
            var result = new CompletedMatch(SqliteSettlementStore.ResultIdFor("trusted-test-match", fixture.Service.ContentVersion),
                "trusted-test-match", fixture.Service.ContentVersion, "acctA", "acctB", 4, 2, 0, 10);
            fixture.Store.Record(result);
            await fixture.Service.DispatchRewardsAsync(CancellationToken.None);
            await fixture.Service.DispatchRewardsAsync(CancellationToken.None);
            Assert.Equal(2, provider.Calls);
            Assert.Equal(RewardState.NeedsReview, Assert.Single(fixture.Store.ReadRewards("acctA")).State);
            Assert.Equal(RewardState.Applied, Assert.Single(fixture.Store.ReadRewards("acctB")).State);
            Assert.Single(fixture.Store.ReadResults("acctA"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void FailedJournalCommitFencesFinalPublicationAndFurtherAdmission()
    {
        var folder = Path.Combine(Path.GetTempPath(), "td-settlement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var provider = new CountingProvider();
            using var fixture = new Fixture(Path.Combine(folder, "results.sqlite"), provider);
            var a = fixture.Service.LoginFromPhotonGateway("acctA", "login-a");
            fixture.Service.Join(a.LobbyToken, "join-a", fixture.Service.ContentVersion);
            fixture.Clock.Advance(10); fixture.Service.Pump();
            fixture.Store.Dispose(); // Simulates a journal that cannot accept the completion transaction.
            var failed = false;
            for (var step = 0; step < 120 && !failed; step++)
            {
                fixture.Clock.Advance(10);
                try { for (var settle = 0; settle < 128; settle++) fixture.Service.Pump(); }
                catch (ObjectDisposedException) { failed = true; }
            }
            Assert.True(failed);
            Assert.True(fixture.Service.IsDraining);
            Assert.Throws<InvalidOperationException>(() => fixture.Service.Pump());
            Assert.Equal(0, provider.Calls);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task SoftDrainWaitsForPendingAndInflightReward()
    {
        var folder = Path.Combine(Path.GetTempPath(), "td-settlement-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var provider = new BlockingProvider();
            using var fixture = new Fixture(Path.Combine(folder, "results.sqlite"), provider);
            fixture.Store.Record(new CompletedMatch(SqliteSettlementStore.ResultIdFor("drain-match", fixture.Service.ContentVersion),
                "drain-match", fixture.Service.ContentVersion, "acctA", null, 4, 1, 0, 10));
            fixture.Service.BeginDrain();
            Assert.False(fixture.Service.CanFinishDrain);
            var dispatch = fixture.Service.DispatchRewardsAsync(CancellationToken.None);
            await provider.Entered.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.False(fixture.Service.CanFinishDrain);
            provider.Release.SetResult();
            Assert.Equal(1, await dispatch);
            Assert.True(fixture.Service.CanFinishDrain);
        }
        finally { Directory.Delete(folder, true); }
    }

    sealed class BlockingProvider : IRewardProvider
    {
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task GrantAsync(RewardGrant grant, CancellationToken ct) { Entered.SetResult(); return Release.Task; }
    }

    sealed class CountingProvider : IRewardProvider
    {
        public int Calls;
        public string? FailAccount;
        public Task GrantAsync(RewardGrant grant, CancellationToken ct)
        { Calls++; return grant.AccountId == FailAccount ? Task.FromException(new IOException("Lost provider response")) : Task.CompletedTask; }
    }
    sealed class Fixture : IDisposable
    {
        public readonly TestClock Clock = new();
        public readonly SqliteSettlementStore Store;
        public readonly RemoteMatchService Service;
        readonly PlayFabIdentityAdapter identity;
        public Fixture(string path, IRewardProvider provider)
        {
            var (content, version) = AuthoredContent.Load();
            identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only", 4),
                _ => Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new() { IsSessionTicketExpired = false, UserInfo = new() { PlayFabId = "acctA" } } }), () => Clock.GetUtcNow());
            Service = new RemoteMatchService(content, version, identity,
                new RemoteHostSettings(new HashSet<string>(["acctA", "acctB", "acctC"])) { Policy = new RemotePolicy { LifetimeMinutes = 50 } }, Clock);
            Store = new SqliteSettlementStore(path, Policy);
            Service.ConfigureSettlement(Store, provider);
        }
        public void Dispose() { Service.Dispose(); identity.Dispose(); }
    }
    sealed class TestClock : TimeProvider
    {
        readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        long ticks = 1;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => start.AddTicks(ticks);
        public void Advance(int seconds) => ticks += TimeSpan.FromSeconds(seconds).Ticks;
    }
}
