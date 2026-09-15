namespace TankDraft.ServerManager;

// Monotonic seconds supplied by the supervisor. A transient status gap is not a failed startup.
public sealed class GatewayReadinessWatchdog(double startedAt)
{
    bool wasReady;
    double? unhealthySince;
    public bool TimedOut(bool healthy, double now)
    {
        if (healthy) { wasReady = true; unhealthySince = null; return false; }
        unhealthySince ??= now;
        return wasReady ? now - unhealthySince.Value >= 15 : now - startedAt >= 60;
    }
}
