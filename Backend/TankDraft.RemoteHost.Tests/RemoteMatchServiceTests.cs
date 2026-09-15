using PlayFab;
using PlayFab.ServerModels;
using Xunit;
using TankDraft.RemoteHost;
using TankDraft.Server.Admission;
using TankDraft.Server.PlayFab.Identity;

namespace TankDraft.RemoteHost.Tests;

public sealed class RemoteMatchServiceTests
{
    [Fact]
    public async Task TwoAllowedLobbiesPairAndCancelledEntryDoesNotReturn()
    {
        var (service, _) = Create(); using (service)
        {
            var a = await service.LoginAsync("ticket-player-a", "login-a", CancellationToken.None); var b = await service.LoginAsync("ticket-player-b", "login-b", CancellationToken.None);
            var joined = service.Join(a.LobbyToken, "join-a", service.ContentVersion); Assert.Equal("Searching", joined.State);
            Assert.Equal("Idle", service.Cancel(a.LobbyToken, joined.TicketId).State);
            Assert.Equal("Idle", service.Join(a.LobbyToken, "join-a", service.ContentVersion).State);
            Assert.Equal("Searching", service.Join(a.LobbyToken, "join-a2", service.ContentVersion).State);
            Assert.Equal("Matched", service.Join(b.LobbyToken, "join-b", service.ContentVersion).State);
            Assert.Equal("Matched", service.Status(a.LobbyToken).State);
        }
    }
    [Fact]
    public async Task ForeignAccountAndContentAreDenied()
    {
        var (service, _) = Create(); using (service)
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.LoginAsync("ticket-foreign", "login", CancellationToken.None));
            var a = await service.LoginAsync("ticket-player-a", "login-a", CancellationToken.None);
            Assert.Throws<UnauthorizedAccessException>(() => service.Join(a.LobbyToken, "join", "wrong"));
        }
    }
    [Fact]
    public async Task OneQueuedPlayerReceivesServerBotAfterTenSeconds()
    {
        var (service, clock) = Create(); using (service)
        {
            var lobby = await service.LoginAsync("ticket-player-a", "login", CancellationToken.None);
            Assert.Equal("Searching", service.Join(lobby.LobbyToken, "join", service.ContentVersion).State);
            clock.Advance(TimeSpan.FromSeconds(10)); service.Pump();
            Assert.Equal("Matched", service.Status(lobby.LobbyToken).State);
        }
    }
    [Fact]
    public async Task OfflineMatchContinuesToResultAndReturningAccessUsesSameMatch()
    {
        var (service, clock) = Create(); using (service)
        {
            var a = await service.LoginAsync("ticket-player-a", "login-a", CancellationToken.None); var b = await service.LoginAsync("ticket-player-b", "login-b", CancellationToken.None);
            service.Join(a.LobbyToken, "join-a", service.ContentVersion); service.Join(b.LobbyToken, "join-b", service.ContentVersion);
            var first = await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-a", CancellationToken.None);
            var matchId = service.UseAccess(first.AccessToken, (match, _) => match.Id);
            var complete = false;
            // Random drafts can produce seven rounds. This is a virtual test watchdog,
            // not a gameplay timeout; allow the full offline flow before service drain.
            for (var step = 0; step < 120 && !complete; step++)
            {
                clock.Advance(TimeSpan.FromSeconds(20));
                for (var settle = 0; settle < 128; settle++) service.Pump();
                var probe = await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "offline-" + step, CancellationToken.None);
                complete = service.UseAccess(probe.AccessToken, (match, _) => match.Domain.Phase.ToString()) == "MatchResult";
            }
            Assert.True(complete, "Offline match did not finish within the 40-minute virtual watchdog.");
            var returned = await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "returned-a", CancellationToken.None);
            var final = service.UseAccess(returned.AccessToken, (match, access) => match.Runtime.Capture(access.Caller));
            Assert.Equal(matchId, final.MatchId); Assert.Equal("MatchResult", final.Phase); Assert.Null(final.Fault);
        }
    }
    [Fact]
    public async Task MatchSessionRenewalUsesOneProviderVerificationAndConsumesTheActualBudget()
    {
        var calls = 0;
        var clock = new TestClock(); var (content, version) = AuthoredContent.Load();
        using var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only", 4), request =>
        {
            calls++;
            var ticket = request.SessionTicket;
            var expired = ticket == "ticket-expired";
            var account = ticket switch { "ticket-player-a" => "acctA", "ticket-player-b" => "acctB", "ticket-player-c" => "acctC", _ => "foreign" };
            return Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult>
            {
                Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = expired, UserInfo = new UserAccountInfo { PlayFabId = account } }
            });
        }, () => clock.GetUtcNow());
        using var service = new RemoteMatchService(content, version, identity,
            new RemoteHostSettings(new HashSet<string>(["acctA", "acctB", "acctC"])) { Policy = new RemotePolicy { MaxIdentityCalls = 4 } }, clock);

        var a = await service.LoginAsync("ticket-player-a", "login-a", CancellationToken.None);
        var b = await service.LoginAsync("ticket-player-b", "login-b", CancellationToken.None);
        service.Join(a.LobbyToken, "join-a", service.ContentVersion);
        service.Join(b.LobbyToken, "join-b", service.ContentVersion);

        var first = await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-a", CancellationToken.None);
        var renewal = await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-b", CancellationToken.None);
        Assert.Equal(1, first.Generation);
        Assert.Equal(2, renewal.Generation);
        Assert.Equal(4, calls); // two logins plus one provider verification for each access issue.

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "over-budget", CancellationToken.None));
        Assert.Equal(4, calls);
    }

    [Fact]
    public async Task MatchSessionExchangeRejectsExpiredCrossAccountContentAndReplayedRequests()
    {
        var calls = 0;
        var clock = new TestClock(); var (content, version) = AuthoredContent.Load();
        using var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only", 4), request =>
        {
            calls++;
            var ticket = request.SessionTicket;
            var expired = ticket == "ticket-expired";
            var account = ticket switch { "ticket-player-a" => "acctA", "ticket-player-b" => "acctB", "ticket-player-c" => "acctC", _ => "foreign" };
            return Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult>
            {
                Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = expired, UserInfo = new UserAccountInfo { PlayFabId = account } }
            });
        }, () => clock.GetUtcNow());
        using var service = new RemoteMatchService(content, version, identity, new RemoteHostSettings(new HashSet<string>(["acctA", "acctB", "acctC"])), clock);

        var a = await service.LoginAsync("ticket-player-a", "login-a", CancellationToken.None);
        var b = await service.LoginAsync("ticket-player-b", "login-b", CancellationToken.None);
        service.Join(a.LobbyToken, "join-a", service.ContentVersion);
        service.Join(b.LobbyToken, "join-b", service.ContentVersion);

        await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-a", CancellationToken.None);
        await service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-b", CancellationToken.None);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExchangeMatchAsync("ticket-expired", service.ContentVersion, "expired", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExchangeMatchAsync("ticket-player-c", service.ContentVersion, "cross-account", CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => service.ExchangeMatchAsync("ticket-player-a", "other-content", "wrong-content", CancellationToken.None));
        await Assert.ThrowsAsync<AdmissionRejectedException>(() => service.ExchangeMatchAsync("ticket-player-a", service.ContentVersion, "session-a", CancellationToken.None));
        Assert.Equal(7, calls); // wrong content is rejected before provider work; all other requests cost one call.
    }
    static (RemoteMatchService Service, TestClock Clock) Create()
    {
        var clock = new TestClock(); var (content, version) = AuthoredContent.Load();
        var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only", 4), request => Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = false, UserInfo = new UserAccountInfo { PlayFabId = request.SessionTicket switch { "ticket-player-a" => "acctA", "ticket-player-b" => "acctB", _ => "foreign" } } } }), () => clock.GetUtcNow());
        return (new RemoteMatchService(content, version, identity, new RemoteHostSettings(new HashSet<string>(["acctA", "acctB"])), clock), clock);
    }
    sealed class TestClock : TimeProvider { readonly DateTimeOffset start = DateTimeOffset.UtcNow; long ticks = 1; public override long TimestampFrequency => TimeSpan.TicksPerSecond; public override long GetTimestamp()=>ticks; public override DateTimeOffset GetUtcNow()=>start.AddTicks(ticks); public void Advance(TimeSpan by) => ticks += by.Ticks; }
}
