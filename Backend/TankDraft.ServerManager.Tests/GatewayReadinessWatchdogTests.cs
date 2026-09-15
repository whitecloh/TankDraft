using TankDraft.ServerManager;
using Xunit;

public sealed class GatewayReadinessWatchdogTests
{
    [Fact] public void RunningServerGetsRecoveryGraceEvenLongAfterStartupDeadline()
    {
        var guard = new GatewayReadinessWatchdog(0);
        Assert.False(guard.TimedOut(true, 5));
        Assert.False(guard.TimedOut(false, 500));
        Assert.False(guard.TimedOut(false, 514));
        Assert.True(guard.TimedOut(false, 515));
    }
    [Fact] public void RecoveryResetsContinuousFailureWindow()
    {
        var guard = new GatewayReadinessWatchdog(0);
        Assert.False(guard.TimedOut(true, 1)); Assert.False(guard.TimedOut(false, 100));
        Assert.False(guard.TimedOut(true, 110)); Assert.False(guard.TimedOut(false, 112));
        Assert.False(guard.TimedOut(false, 120)); Assert.True(guard.TimedOut(false, 127));
    }
    [Fact] public void NeverReadyServerStillFailsStartupDeadline()
    {
        var guard = new GatewayReadinessWatchdog(10);
        Assert.False(guard.TimedOut(false, 69)); Assert.True(guard.TimedOut(false, 70));
    }
}
