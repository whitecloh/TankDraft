namespace TankDraft.Server.Match.Tests;

internal sealed class ManualTimeProvider : TimeProvider
{
    private long _timestamp;
    private DateTimeOffset _utcNow;

    public ManualTimeProvider(DateTimeOffset initialUtc)
    {
        _utcNow = initialUtc;
    }

    public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    public override DateTimeOffset GetUtcNow() => _utcNow;
    public override long GetTimestamp() => _timestamp;

    public void AdvanceMonotonic(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(elapsed));
        _timestamp = checked(_timestamp + elapsed.Ticks);
        _utcNow = _utcNow.Add(elapsed);
    }

    public void JumpUtc(TimeSpan offset) => _utcNow = _utcNow.Add(offset);
}
