using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.ServerClient.Tests;
public sealed class FusionQueueClientTests
{
    [Fact]
    public async Task ExpiredLobbyIsRefreshedOnceAndRepeatedDenialDoesNotLoop()
    {
        int lobbies = 0, statuses = 0;
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, data) =>
        {
            bool lobby = (string)FusionQaProtocol.ReadRequest(data)["Operation"] == "Lobby";
            if (lobby) lobbies++; else statuses++;
            channel.Receive(id, Encoding.UTF8.GetBytes(new JObject { ["Status"] = lobby ? 200 : 403, ["Body"] = new JObject() }.ToString(Formatting.None)));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var queue = new FusionQueueClient(new FusionQaRequests(new FusionQaClient(channel)), "instance", new string('a',64));
            await Assert.ThrowsAsync<FusionAuthorityException>(() => queue.Send("Status", null, default));
            Assert.Equal(2, lobbies); Assert.Equal(2, statuses);
        }
    }
    [Fact]
    public async Task SemanticJoinDenialDoesNotRefreshLobby()
    {
        int lobbies = 0, joins = 0;
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, data) =>
        {
            string operation = (string)FusionQaProtocol.ReadRequest(data)["Operation"];
            bool lobby = operation == "Lobby";
            if (lobby) lobbies++; else joins++;
            var body = lobby ? new JObject() : new JObject { ["Code"] = "unsupported_battle_loadout" };
            channel.Receive(id, Encoding.UTF8.GetBytes(new JObject { ["Status"] = lobby ? 200 : 403, ["Body"] = body }.ToString(Formatting.None)));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var queue = new FusionQueueClient(new FusionQaRequests(new FusionQaClient(channel)), "instance", new string('a',64));
            var error = await Assert.ThrowsAsync<FusionAuthorityException>(() => queue.Send("Join", null, default));
            Assert.Equal("unsupported_battle_loadout", error.Code);
            Assert.Equal(1, lobbies); Assert.Equal(1, joins);
        }
    }
    static JObject State(string state) => new JObject { ["InstanceId"] = "instance", ["State"] = state, ["TicketId"] = "ticket", ["RemainingSeconds"] = 10 };
    [Fact]
    public async Task UncertainJoinReusesIntentButConfirmedCancelAllowsNewJoin()
    {
        var joins = new List<string>(); bool fail = true;
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, data) =>
        {
            var request = FusionQaProtocol.ReadRequest(data); string op = (string)request["Operation"];
            if (op == "Join") joins.Add((string)request["Body"]["OperationId"]);
            int status = op == "Join" && fail ? 503 : 200;
            fail = op == "Join" ? false : fail;
            var body = op == "Lobby" ? new JObject() : State(op == "Join" ? "Searching" : "Idle");
            channel.Receive(id, Encoding.UTF8.GetBytes(new JObject { ["Status"] = status, ["Body"] = body }.ToString(Formatting.None)));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var queue = new FusionQueueClient(new FusionQaRequests(new FusionQaClient(channel)), "instance", new string('a',64));
            await Assert.ThrowsAsync<FusionAuthorityException>(() => queue.Send("Join", null, default));
            await queue.Send("Join", null, default);
            await queue.Send("Cancel", "ticket", default);
            await queue.Send("Join", null, default);
            Assert.Equal(joins[0], joins[1]); Assert.NotEqual(joins[1], joins[2]);
        }
    }
    [Fact]
    public async Task MatchedResponseToCancelRemainsAssignedAndWrongInstanceIsRejected()
    {
        var response = State("Matched"); response["Side"] = 1; response["MatchId"] = "match"; response["OpponentKind"] = "Human";
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, data) =>
        {
            var request = FusionQaProtocol.ReadRequest(data);
            channel.Receive(id, Encoding.UTF8.GetBytes(new JObject { ["Status"] = 200, ["Body"] = (string)request["Operation"] == "Lobby" ? new JObject() : response }.ToString(Formatting.None)));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var queue = new FusionQueueClient(new FusionQaRequests(new FusionQaClient(channel)), "instance", new string('a',64));
            Assert.Equal("Matched", (await queue.Send("Cancel", "ticket", default)).Value<string>("State"));
            response["InstanceId"] = "other";
            await Assert.ThrowsAsync<InvalidDataException>(() => queue.Send("Status", null, default));
        }
    }
}
