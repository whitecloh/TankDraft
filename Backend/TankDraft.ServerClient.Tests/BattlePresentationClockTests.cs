using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;
public sealed class BattlePresentationClockTests
{
    [Fact]
    public void SlowSnapshotArrivalHasMuchLessBufferStarvationThanFixed200ms()
    {
        int CountStarvation(double maximum)
        {
            var clock = new BattlePresentationClock(.2, maximum);
            var arrivals = new List<double> { 0 }; clock.Observe(0, true);
            double next = .38; int held = 0;
            for (int step = 0; step < 1800; step++)
            {
                double now = step / 60d;
                if (now >= next) { arrivals.Add(next); clock.Observe(next, false); next += arrivals.Count % 5 == 0 ? .48 : .38; }
                double cursor = clock.Advance(now);
                if (now > 5 && cursor >= arrivals[^1]) held++;
            }
            return held;
        }
        Assert.True(CountStarvation(.8) < CountStarvation(.2) / 4);
    }
    [Fact]
    public void IncreasedDelayNeverRewindsAndOutageCannotGrowBufferWithoutBound()
    {
        var clock = new BattlePresentationClock(.2, .8);
        clock.Observe(0, true); double previous = clock.Advance(0);
        for (int step = 1; step < 1200; step++)
        {
            double now = step / 60d;
            if (step % 60 == 0) clock.Observe(now, false);
            var cursor = clock.Advance(now);
            Assert.True(cursor >= previous); Assert.True(cursor <= now);
            Assert.InRange(clock.Delay, .2, .8); previous = cursor;
        }
        clock.Observe(99, true); Assert.Equal(.2, clock.Delay);
        Assert.Equal(99.8, clock.Advance(100), 6);
    }
}
