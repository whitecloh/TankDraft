using TankDraft.Server.Persistence;
using Xunit;

namespace TankDraft.Server.Persistence.Tests;

public sealed class ShutdownFlushTests
{
    [Fact]
    public void Flush_persists_idle_draft_time_and_reopens_with_exact_hash_and_deadline()
    {
        using var fixture = new PersistenceFixture();
        var caller = fixture.Caller(0, TimeSpan.FromHours(1));
        string stateHash;
        DateTimeOffset serverNow;
        DateTimeOffset? deadline;
        using (var match = fixture.Open())
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            _ = match.Pump();
            Assert.Equal(0, match.CommittedSequence);
            var before = match.Capture(caller);
            stateHash = match.GetStateHash();
            serverNow = before.ServerNow;
            deadline = before.DeadlineAt;
            Assert.True(match.Flush());
            Assert.Equal(1, match.CommittedSequence);
        }
        using var reopened = fixture.Open();
        var recovered = reopened.Capture(caller);
        Assert.Equal(stateHash, reopened.GetStateHash());
        Assert.Equal(serverNow, recovered.ServerNow);
        Assert.Equal(deadline, recovered.DeadlineAt);
    }

    [Fact]
    public void Failed_flush_faults_live_actor_and_preserves_prior_committed_head()
    {
        using var fixture = new PersistenceFixture();
        var caller = fixture.Caller(0, TimeSpan.FromHours(1));
        using (var match = fixture.Open(storeHook: point =>
        {
            if (point == StoreFaultPoint.BeforeCommit) throw new InvalidOperationException("injected flush failure");
        }))
        {
            fixture.Clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.Throws<InvalidOperationException>(() => match.Flush());
            Assert.Throws<InvalidOperationException>(() => match.Capture(caller));
            Assert.Throws<InvalidOperationException>(() => match.Pump());
        }
        using var reopened = fixture.Open();
        Assert.Equal(0, reopened.CommittedSequence);
    }
}
