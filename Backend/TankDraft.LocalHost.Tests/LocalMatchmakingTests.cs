using TankDraft.LocalHost;
using Xunit;

namespace TankDraft.LocalHost.Tests;

public sealed class LocalMatchmakingTests
{
    [Fact]
    public void CompatiblePlayersPairBeforeDeadline()
    {
        using var fixture = new Fixture();
        var a = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        var b = fixture.Queue.Join("account-b", "request-b", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        Assert.Equal(LocalQueueState.Searching, a.State);
        Assert.Equal(LocalQueueState.Matched, b.State);
        var statusA = fixture.Queue.Status("account-a");
        Assert.Equal(LocalQueueState.Matched, statusA.State); Assert.Equal(statusA.MatchId, b.MatchId); Assert.Equal(0, statusA.Side); Assert.Equal(1, b.Side);
    }

    [Fact]
    public void TimeoutAtExactDeadlineCreatesBotMatch()
    {
        using var fixture = new Fixture();
        var ticket = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(9)); fixture.Queue.Pump(); Assert.Equal(LocalQueueState.Searching, fixture.Queue.Status("account-a").State);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); fixture.Queue.Pump();
        var status = fixture.Queue.Status("account-a"); Assert.Equal(LocalQueueState.Matched, status.State); Assert.Equal(LocalOpponentKind.Bot, status.OpponentKind); Assert.Equal(ticket.TicketId, status.TicketId);
    }

    [Fact]
    public void WallClockJumpDoesNotChangeMonotonicSearchDeadline()
    {
        using var fixture = new Fixture();
        fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Clock.ShiftUtcOnly(TimeSpan.FromHours(1)); fixture.Queue.Pump();
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Status("account-a").State);
        fixture.Clock.Advance(TimeSpan.FromSeconds(9)); fixture.Queue.Pump(); Assert.Equal(LocalQueueState.Searching, fixture.Queue.Status("account-a").State);
        fixture.Clock.Advance(TimeSpan.FromSeconds(1)); fixture.Queue.Pump(); Assert.Equal(LocalQueueState.Matched, fixture.Queue.Status("account-a").State);
    }

    [Fact]
    public void CancelIsIdempotentAndRemovesOnlySearchingTicket()
    {
        using var fixture = new Fixture();
        var ticket = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        Assert.Equal(ticket.TicketId, fixture.Queue.Cancel("account-a", "wrong-ticket").TicketId);
        Assert.Equal(LocalQueueState.Idle, fixture.Queue.Cancel("account-a", ticket.TicketId!).State);
        Assert.Equal(LocalQueueState.Idle, fixture.Queue.Cancel("account-a", ticket.TicketId!).State);
    }

    [Fact]
    public void CancelAfterExactDeadlineReturnsMatchedStatus()
    {
        using var fixture = new Fixture();
        var ticket = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10)); fixture.Queue.Pump();
        var status = fixture.Queue.Cancel("account-a", ticket.TicketId!);
        Assert.Equal(LocalQueueState.Matched, status.State); Assert.Equal(LocalOpponentKind.Bot, status.OpponentKind);
    }

    [Fact]
    public void CompletedBotLeaveIsIdempotentAndAllowsNextJoin()
    {
        using var fixture = new Fixture(fast: true);
        var matched = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10)); fixture.Queue.Pump();
        var match = fixture.Queue.Status("account-a"); Assert.Equal(LocalQueueState.Matched, match.State);
        Assert.True(CompleteAndLeave(fixture, "account-a", match.MatchId!));
        Assert.True(fixture.Queue.LeaveCompleted("account-a", match.MatchId!));
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Join("account-a", "request-next", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId).State);
    }

    [Fact]
    public void FirstHumanLeaveAllowsNextJoinAndSecondLeavePreservesIt()
    {
        using var fixture = new Fixture(fast: true);
        fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Queue.Join("account-b", "request-b", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        var match = fixture.Queue.Status("account-a"); Assert.Equal(LocalQueueState.Matched, match.State);
        Assert.True(CompleteAndLeave(fixture, "account-a", match.MatchId!));
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Join("account-a", "request-next", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId).State);
        Assert.True(fixture.Queue.LeaveCompleted("account-b", match.MatchId!));
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Status("account-a").State);
    }

    [Fact]
    public void RetentionExpiryLeaveIsNoOpAndPreservesNewTicket()
    {
        using var fixture = new Fixture(fast: true);
        fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        fixture.Clock.Advance(TimeSpan.FromSeconds(10)); fixture.Queue.Pump();
        var match = fixture.Queue.Status("account-a"); Assert.Equal(LocalQueueState.Matched, match.State);
        AdvanceToCompletionWithoutLeaving(fixture);
        fixture.Clock.Advance(TimeSpan.FromSeconds(61)); fixture.Queue.Pump();
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Join("account-a", "request-next", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId).State);
        Assert.True(fixture.Queue.LeaveCompleted("account-a", match.MatchId!));
        Assert.Equal(LocalQueueState.Searching, fixture.Queue.Status("account-a").State);
    }

    static bool CompleteAndLeave(Fixture fixture, string account, string matchId)
    {
        for (var step = 0; step < 4000; step++)
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(500)); fixture.Queue.Pump();
            if (fixture.Queue.LeaveCompleted(account, matchId)) return true;
        }
        return false;
    }

    static void AdvanceToCompletionWithoutLeaving(Fixture fixture)
    {
        // Four rounds use ordinary authoritative auto-draft; no test-only match transition.
        for (var step = 0; step < 4000; step++) { fixture.Clock.Advance(TimeSpan.FromMilliseconds(500)); fixture.Queue.Pump(); }
    }

    [Fact]
    public void JoinRequestIsIdempotentButCannotReplaceOutstandingTicket()
    {
        using var fixture = new Fixture();
        var first = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        var retry = fixture.Queue.Join("account-a", "request-a", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId);
        Assert.Equal(first.TicketId, retry.TicketId);
        Assert.Throws<InvalidOperationException>(() => fixture.Queue.Join("account-a", "request-b", fixture.Version, fixture.Content.Deck, fixture.Content.OrderId));
    }

    [Fact]
    public void InvalidDeckIsRejectedBeforeTicketCreation()
    {
        using var fixture = new Fixture();
        Assert.Throws<ArgumentException>(() => fixture.Queue.Join("account-a", "request-a", fixture.Version, ["bad", "bad", "bad", "bad"], ""));
        Assert.Equal(LocalQueueState.Idle, fixture.Queue.Status("account-a").State);
    }

    private sealed class Fixture : IDisposable
    {
        public FakeClock Clock { get; } = new();
        public AuthoredContent Content { get; }
        public string Version { get; }
        public LocalMatchmaking Queue { get; }
        public Fixture(bool fast = false)
        {
            (Content, Version) = AuthoredContent.Load();
            var host = new HostSettings { Mode = "LocalOnly", ListenUrl = "http://127.0.0.1:18781", MaxRequestBodyBytes = 8192, MaxConcurrentConnections = 8, RequestsPerWindow = 120, RateWindowSeconds = 60, SessionTtlSeconds = 600, MaxSessionCount = 4, MaxCommandReceipts = 512, MaxSimulationTicksPerRound = 10000, DraftChoiceSeconds = fast ? .1 : 20, RoundResultSeconds = fast ? .1 : 2, SchedulerPollMilliseconds = 20, MaxStepsPerPump = 256, EventCapacity = 1024, JournalCapacity = 256 };
            var settings = new LocalQueueSettings("LocalOnly", 10, 8, 4, 60, 500, Content.Deck, Content.OrderId);
            Queue = new LocalMatchmaking(Content, Version, host, settings, Clock);
        }
        public void Dispose() => Queue.Dispose();
    }

    private sealed class FakeClock : TimeProvider
    {
        private DateTimeOffset _utc = DateTimeOffset.UnixEpoch;
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void Advance(TimeSpan value) { _utc += value; _ticks += value.Ticks; }
        public void ShiftUtcOnly(TimeSpan value) => _utc += value;
    }
}
