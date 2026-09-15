namespace TankDraft.LocalHost;

// Only instantiated by --verify. No endpoint permits clients to advance authoritative time.
internal sealed class LocalVerificationClock : TimeProvider
{
    private readonly DateTimeOffset _start = DateTimeOffset.UtcNow;
    private long _ticks;
    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override long GetTimestamp() => Interlocked.Read(ref _ticks);
    public override DateTimeOffset GetUtcNow() => _start.AddTicks(GetTimestamp());
    public void Advance(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Interlocked.Add(ref _ticks, duration.Ticks);
    }
}
