using Newtonsoft.Json.Linq;
using TankDraft.Server.Progression;
using TankDraft.Server.Settlement;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed partial class RemoteMetaIntegrationTests
{
    [Fact]
    public async Task ProgressionThroughGatewayUsesAuthenticatedAccountAndStablePurchaseReceipt()
    {
        await using var fixture = await Fixture.StartAsync(true, progressionEnabled: true);
        using var a = new QaPeer(fixture.Endpoint, "acctA");
        using var b = new QaPeer(fixture.Endpoint, "acctB");
        await a.OkAsync("Lobby", new { OperationId = "progression-a" });
        await b.OkAsync("Lobby", new { OperationId = "progression-b" });
        var context = new { fixture.Service.InstanceId, fixture.Service.ContentVersion };
        var initial = await a.OkAsync("ProgressionGet", context);
        Assert.True(initial.Value<bool>("Enabled"));
        Assert.Equal(0, initial["State"]!.Value<long>("AppliedSequence"));
        Assert.Null(initial["Receipt"]);
        var operation = Guid.NewGuid().ToString("N");
        var purchase = new { fixture.Service.InstanceId, fixture.Service.ContentVersion,
            Kind = "BuyOffer", TargetId = "pack.silver", OperationId = operation, ExpectedSequence = 0L };
        var bought = await a.OkAsync("ProgressionExecute", purchase);
        Assert.Equal("Completed", bought["Receipt"]!.Value<string>("Status"));
        Assert.Equal(3, ((JObject)bought["State"]!["Units"]!).Count);
        var repeated = await a.OkAsync("ProgressionExecute", purchase);
        Assert.True(JToken.DeepEquals(bought, repeated));
        Assert.Empty((JObject)(await b.OkAsync("ProgressionGet", context))["State"]!["Units"]!);
        var stale = await a.RawAsync("ProgressionExecute", new { fixture.Service.InstanceId, fixture.Service.ContentVersion,
            Kind = "BuyOffer", TargetId = "pack.silver", OperationId = Guid.NewGuid().ToString("N"), ExpectedSequence = 0L });
        Assert.Equal(409, stale.Value<int>("Status"));
        await a.OkAsync("Join", new { fixture.Service.InstanceId, fixture.Service.ContentVersion, OperationId = "queue-progression" });
        var active = await a.RawAsync("ProgressionExecute", new { fixture.Service.InstanceId, fixture.Service.ContentVersion,
            Kind = "BuyOffer", TargetId = "pack.silver", OperationId = Guid.NewGuid().ToString("N"), ExpectedSequence = 1L });
        Assert.Equal(409, active.Value<int>("Status"));
    }

    [Fact]
    public async Task CompletedHumanMatchAwardsFrozenParticipantXpExactlyOnce()
    {
        await using var fixture = await Fixture.StartAsync(metaEnabled: true, progressionEnabled: true, settlementEnabled: true);
        var a = fixture.Service.LoginFromPhotonGateway("acctA", "progression-settlement-a");
        var b = fixture.Service.LoginFromPhotonGateway("acctB", "progression-settlement-b");
        await fixture.Service.GetProfileAsync(a.LobbyToken, fixture.Service.ContentVersion, CancellationToken.None);
        await fixture.Service.GetProfileAsync(b.LobbyToken, fixture.Service.ContentVersion, CancellationToken.None);
        await fixture.Service.JoinAsync(a.LobbyToken, "progression-join-a", fixture.Service.ContentVersion, CancellationToken.None);
        await fixture.Service.JoinAsync(b.LobbyToken, "progression-join-b", fixture.Service.ContentVersion, CancellationToken.None);
        var done = false;
        for (var step = 0; step < 120 && !done; step++)
        {
            fixture.Clock!.Advance(TimeSpan.FromSeconds(10));
            for (var pump = 0; pump < 128; pump++) fixture.Service.Pump();
            var session = fixture.Service.ExchangeFromPhotonGateway("acctA", fixture.Service.ContentVersion, "progression-step-" + step, CancellationToken.None);
            var snapshot = fixture.Service.UseAccess(session.AccessToken, (match, actor) => match.Runtime.Capture(actor.Caller));
            done = snapshot.Phase == "MatchResult";
        }
        Assert.True(done);
        Assert.Equal(2, await fixture.Service.DispatchRewardsAsync(CancellationToken.None));
        Assert.Equal(0, await fixture.Service.DispatchRewardsAsync(CancellationToken.None));
        foreach (var account in new[] { "acctA", "acctB" })
        {
            var reward = Assert.Single(fixture.Settlements!.ReadParticipantRewards(account));
            var state = await fixture.Progression!.GetAsync(account, CancellationToken.None);
            Assert.True(reward.Applied);
            Assert.Equal(4, reward.UnitIds.Length);
            Assert.Equal(reward.Won ? 10 : 5, reward.MasteryXp);
            Assert.All(reward.UnitIds, id => Assert.True(state.Units.TryGetValue(id, out var unit) && unit.MasteryXp == reward.MasteryXp));
        }
        Assert.Equal(0, await fixture.Service.DispatchRewardsAsync(CancellationToken.None));
        Assert.All(fixture.Settlements!.ReadParticipantRewards(), reward => Assert.True(reward.Applied));
    }

    sealed class ProgressionWallet : IProgressionWallet
    {
        public Task<int> ReadAsync(string account, string currency, CancellationToken ct) => Task.FromResult(10000);
        public Task DebitAsync(string account, string currency, int amount, CancellationToken ct) => Task.CompletedTask;
        public Task CreditAsync(string account, string currency, int amount, CancellationToken ct) => Task.CompletedTask;
    }
}
