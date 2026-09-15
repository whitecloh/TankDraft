using PlayFab;
using PlayFab.ServerModels;
using TankDraft.RemoteHost;
using TankDraft.Server.PlayFab.Identity;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class RemotePolicyTests
{
    [Fact]
    public async Task ActiveMatchAndQueuedPlayerCapsAlsoApplyToTimedOutBots()
    {
        using var f = new Fixture();
        var tokens = new List<string>();
        for (var i = 0; i < 81; i++) tokens.Add((await f.Login(i)).LobbyToken);
        for (var i = 0; i < 40; i++) f.Service.Join(tokens[i], "join", f.Service.ContentVersion);
        var ids = new HashSet<string>();
        for (var i = 0; i < 40; i++) ids.Add(f.Service.Status(tokens[i]).MatchId!);
        Assert.Equal(20, ids.Count);
        for (var i = 40; i < 80; i++) Assert.Equal("Searching", f.Service.Join(tokens[i], "join", f.Service.ContentVersion).State);
        Assert.Throws<InvalidOperationException>(() => f.Service.Join(tokens[80], "join", f.Service.ContentVersion));
        f.Clock.Advance(TimeSpan.FromSeconds(10)); f.Service.Pump();
        Assert.Equal("Searching", f.Service.Status(tokens[40]).State);
    }
    [Fact]
    public async Task CannotLeaveAnUnfinishedMatchOrCancelANewerTicket()
    {
        using var f = new Fixture(); var a = await f.Login(0); var b = await f.Login(1);
        var first = f.Service.Join(a.LobbyToken, "first", f.Service.ContentVersion);
        f.Service.Cancel(a.LobbyToken, first.TicketId);
        var second = f.Service.Join(a.LobbyToken, "second", f.Service.ContentVersion);
        Assert.Equal(second.TicketId, f.Service.Cancel(a.LobbyToken, first.TicketId).TicketId);
        f.Service.Join(b.LobbyToken, "pair", f.Service.ContentVersion);
        var match = f.Service.Status(a.LobbyToken).MatchId!;
        Assert.Throws<InvalidOperationException>(() => f.Service.Leave(a.LobbyToken, match));
        Assert.Equal(match, f.Service.Status(a.LobbyToken).MatchId);
    }
    [Fact]
    public async Task ProviderBudgetFailsBeforeCallingPlayFabAgain()
    {
        using var f = new Fixture(new RemotePolicy { MaxIdentityCalls = 2 });
        await f.Login(0); await f.Login(1);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Login(2));
        Assert.Equal(2, f.ProviderCalls);
    }
    [Fact]
    public async Task DrainsBeforeExpiryAndNewProcessRejectsOldTokens()
    {
        using var f = new Fixture(); var a = await f.Login(0);
        using var next = new Fixture();
        Assert.NotEqual(f.Service.InstanceId, next.Service.InstanceId);
        Assert.Throws<UnauthorizedAccessException>(() => next.Service.Status(a.LobbyToken));
        f.Clock.Advance(TimeSpan.FromMinutes(45)); f.Service.Pump(); Assert.True(f.Service.IsDraining);
        a = await f.Login(0, "new-login");
        Assert.Throws<InvalidOperationException>(() => f.Service.Join(a.LobbyToken, "join", f.Service.ContentVersion));
        f.Clock.Advance(TimeSpan.FromMinutes(5)); Assert.True(f.Service.IsExpired);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Status(a.LobbyToken));
    }
    [Fact]
    public async Task StaleLoginOperationCannotRotateBackToOldCredentials()
    {
        using var f = new Fixture(); var first = await f.Login(0, "login-one");
        var again = await f.Login(0, "login-one"); Assert.Equal(first.LobbyToken, again.LobbyToken);
        var next = await f.Login(0, "login-two"); Assert.NotEqual(first.LobbyToken, next.LobbyToken);
        Assert.Throws<UnauthorizedAccessException>(() => f.Service.Status(first.LobbyToken));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Login(0, "login-one"));
    }
    [Fact]
    public void InvalidConfigCannotLiftPrototypeLimits()
    {
        Assert.Throws<InvalidDataException>(() => new RemotePolicy { MaxActiveMatches = 21 }.Validate());
        Assert.Throws<InvalidDataException>(() => new RemotePolicy { MaxIdentityCalls = 501 }.Validate());
        Assert.Throws<InvalidDataException>(() => new RemotePolicy { LifetimeMinutes = 60 }.Validate());
    }
    sealed class Fixture : IDisposable
    {
        public TestClock Clock { get; } = new();
        public int ProviderCalls;
        readonly PlayFabIdentityAdapter identity;
        public RemoteMatchService Service { get; }
        public Fixture(RemotePolicy? policy = null)
        {
            var (content, version) = AuthoredContent.Load();
            identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "test-secret-only", 4), request =>
            {
                ProviderCalls++;
                return Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = false, UserInfo = new UserAccountInfo { PlayFabId = request.SessionTicket } } });
            }, () => Clock.GetUtcNow());
            Service = new RemoteMatchService(content, version, identity, new RemoteHostSettings(Enumerable.Range(0, 100).Select(i => "acct" + i.ToString("D3")).ToHashSet()) { Policy = policy ?? new RemotePolicy() }, Clock);
        }
        public Task<LobbyIssue> Login(int i, string operation = "login") => Service.LoginAsync("acct" + i.ToString("D3"), operation, CancellationToken.None);
        public void Dispose() { Service.Dispose(); identity.Dispose(); }
    }
    sealed class TestClock : TimeProvider
    {
        long ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(ticks);
        public void Advance(TimeSpan value) => ticks += value.Ticks;
    }
}
