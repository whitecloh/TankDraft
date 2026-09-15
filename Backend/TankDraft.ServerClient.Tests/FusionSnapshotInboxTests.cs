using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

public sealed class FusionSnapshotInboxTests
{
    [Fact] public void NativeRenderSequenceNeverRollsBackWithinAnAuthenticatedStream()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1);
        Assert.True(inbox.AcceptRenderState(1, 8)); Assert.True(inbox.AcceptRenderState(1, 8));
        Assert.False(inbox.AcceptRenderState(1, 7)); Assert.Equal(1, inbox.StaleNativeStates);
        Assert.False(inbox.AcceptRenderState(2, 99)); Assert.True(inbox.AcceptRenderState(1, 9));
        inbox.Disconnect(); Assert.False(inbox.AcceptRenderState(1, 10));
        inbox.Bind(1); Assert.True(inbox.AcceptRenderState(1, 1));
    }
    static JObject Frame(params long[] sequences) => new JObject { ["Kind"] = "Snapshot", ["Snapshot"] = new JObject { ["ResyncRequired"] = false, ["Events"] = new JArray(sequences.Select(s => new JObject { ["Sequence"] = s })) } };
    [Fact] public async Task SupersededSnapshotKeepsRingEventsAndDeduplicatesCursor()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1);
        inbox.Publish(1, Frame(1, 2)); inbox.Publish(1, Frame(1, 2, 3));
        var result = await inbox.ReadAsync(1, CancellationToken.None);
        Assert.Equal(new long[] { 2, 3 }, result["Snapshot"]["Events"].Select(e => e.Value<long>("Sequence")));
        Assert.False(result["Snapshot"].Value<bool>("ResyncRequired"));
    }
    [Fact] public async Task OverflowRequiresResyncRatherThanSilentlyDroppingEffects()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1); inbox.Publish(1, Frame(9, 10));
        Assert.True((await inbox.ReadAsync(3, CancellationToken.None))["Snapshot"].Value<bool>("ResyncRequired"));
    }
    [Fact] public async Task PriorStreamCannotSatisfyNewMatchRead()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1); inbox.Publish(1, Frame(1)); inbox.Bind(2);
        inbox.Publish(1, Frame(2));
        using var stop = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.ReadAsync(null, stop.Token));
        inbox.Publish(2, Frame(10)); Assert.Single((JArray)(await inbox.ReadAsync(null, CancellationToken.None))["Snapshot"]["Events"]);
    }
    [Fact] public async Task ClosingRunnerWakesBlockedSnapshotRead()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1); var read = inbox.ReadAsync(null, CancellationToken.None);
        inbox.Close(); await Assert.ThrowsAsync<IOException>(() => read);
    }
    [Fact] public async Task ReplacedRunnerRejectsOldStateAndResumesOnlyAfterWelcome()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(4);
        var pending = inbox.ReadAsync(null, CancellationToken.None); inbox.Disconnect();
        await Assert.ThrowsAsync<IOException>(() => pending);
        Assert.False(inbox.Accepts(4)); inbox.Publish(4, Frame(99));
        inbox.Bind(1); inbox.Publish(4, Frame(99)); inbox.Publish(1, Frame(5));
        Assert.Equal(5, (await inbox.ReadAsync(null, CancellationToken.None))["Snapshot"]["Events"][0].Value<int>("Sequence"));
    }
    [Fact] public async Task DelayedRefreshSignalCannotRotateAlreadyRenewedAccessAgain()
    {
        var inbox = new FusionSnapshotInbox(); inbox.Bind(1); inbox.AcknowledgeAccess(1);
        var hint = new JObject { ["Kind"] = "AccessRefreshRequired", ["NativeAccessGeneration"] = 1 };
        inbox.Publish(1, hint); inbox.AcknowledgeAccess(2); inbox.Publish(1, hint);
        using var stop = new CancellationTokenSource(50);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => inbox.ReadAsync(null, stop.Token));
        inbox.Publish(1, new JObject { ["Kind"] = "AccessRefreshRequired", ["NativeAccessGeneration"] = 2 });
        var control = await inbox.ReadAsync(null, CancellationToken.None);
        Assert.Single(control); Assert.Equal("AccessRefreshRequired", control.Value<string>("Kind"));
    }
}
