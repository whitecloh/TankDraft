using System.Net.WebSockets;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

public sealed class FusionRunnerReplacementTests
{
    [Fact] public async Task DisconnectFailsPendingCommandAsNetworkFailureAndRejectsOldCallback()
    {
        int request = 0;
        var old = new FusionRequestChannel((id, _) => request = id, () => false, TimeSpan.FromSeconds(3), true);
        var client = new FusionQaClient(old);
        var pending = client.ExchangeAsync("Lobby", new Newtonsoft.Json.Linq.JObject { ["OperationId"] = "test" }, CancellationToken.None);
        client.ReplaceChannel(null);
        await Assert.ThrowsAsync<WebSocketException>(() => pending);
        Assert.False(old.Receive(request, Array.Empty<byte>()));
        await Assert.ThrowsAsync<WebSocketException>(() => client.ExchangeAsync("Lobby", null, CancellationToken.None));
    }
    [Fact] public async Task NewRunnerUsesNewChannelWithoutReplayingUncertainCommand()
    {
        var old = new FusionRequestChannel((_, _) => { }, () => false, TimeSpan.FromSeconds(3), true);
        var client = new FusionQaClient(old); client.ReplaceChannel(null);
        var calls = 0;
        FusionRequestChannel fresh = null!;
        fresh = new FusionRequestChannel((id, _) => { calls++; fresh!.Receive(id, System.Text.Encoding.UTF8.GetBytes("{\"Status\":200,\"Body\":{\"State\":\"Idle\"}}")); }, () => false, TimeSpan.FromSeconds(3), true);
        client.ReplaceChannel(fresh);
        var response = await client.ExchangeAsync("Lobby", new Newtonsoft.Json.Linq.JObject { ["OperationId"] = "test" }, CancellationToken.None);
        Assert.Equal("Idle", response.Value<string>("State")); Assert.Equal(1, calls);
    }
}
