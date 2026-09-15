using TankDraft.Server.Persistence;
using TankDraft.Server.Security;
using Xunit;

namespace TankDraft.Server.Persistence.Tests;

public sealed class CatchingUpPersistenceTests
{
    [Fact]
    public void Catching_up_command_is_journaled_without_a_receipt_and_replays_normally()
    {
        using var fixture = new PersistenceFixture(maxStepsPerPump: 1);
        var caller = fixture.Caller(0, TimeSpan.FromHours(1));
        CommandEnvelope command;
        using (var match = fixture.Open())
        {
            command = fixture.Choose(match, caller, "catching-up", sequence: 1);
            fixture.Clock.Advance(TimeSpan.FromSeconds(5));
            Assert.Equal("CatchingUp", match.Execute(caller, command).Code);
        }
        using (var checkStore = fixture.OpenStore())
        {
            Assert.Null(checkStore.FindReceipt("account-0", command.OperationId));
        }
        using var reopened = fixture.Open();
        for (var step = 0; step < 1_000 && reopened.Capture(caller).CatchingUp; step++)
            _ = reopened.Pump();
        Assert.False(reopened.Capture(caller).CatchingUp);
        Assert.NotEqual("UnexpectedSequence", reopened.Execute(caller, command).Code);
    }
}
