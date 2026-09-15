using System.Net.WebSockets;
using System.Text;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class FusionMatchSocketTests
{
    [Fact]
    public async Task SessionAndFragmentedSocketKeepAccessCredentialsOffWire()
    {
        FusionRequestChannel channel = null; var sent = new List<string>();
        using (channel = new FusionRequestChannel((id, bytes) =>
        {
            sent.Add(Encoding.UTF8.GetString(bytes)); var request = JObject.Parse(sent[^1]);
            var body = request.Value<string>("Operation") == "Session"
                ? new JObject { ["MatchId"] = "match", ["Side"] = 0, ["SessionId"] = "session", ["StreamId"] = "stream", ["Generation"] = 1, ["RefreshAfterSeconds"] = 30 }
                : new JObject { ["Kind"] = "Reauthenticated" };
            channel.Receive(id, Encoding.UTF8.GetBytes(new JObject { ["Status"] = 200, ["Body"] = body }.ToString()));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true))
        {
            var directory = Path.Combine(Path.GetTempPath(), "td-fusion-socket-" + Guid.NewGuid().ToString("N"));
            try
            {
                using var credentials = new FusionMatchCredentials(new FusionQaRequests(new FusionQaClient(channel)), "match", 0, new string('a', 64), directory);
                var access = await credentials.AcquireAsync(CancellationToken.None);
                Assert.Equal("gateway-owned", access.Token);
                using var socket = await credentials.ConnectAsync(access, CancellationToken.None);
                var message = Encoding.UTF8.GetBytes("{\"Kind\":\"Reauthenticate\",\"AccessToken\":\"gateway-owned\"}");
                await socket.SendAsync(new ArraySegment<byte>(message), WebSocketMessageType.Text, true, CancellationToken.None);
                using var result = new MemoryStream(); var buffer = new byte[3]; WebSocketReceiveResult part;
                do { part = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None); result.Write(buffer, 0, part.Count); } while (!part.EndOfMessage);
                Assert.Equal("Reauthenticated", JObject.Parse(Encoding.UTF8.GetString(result.ToArray())).Value<string>("Kind"));
                foreach (var frame in sent) { Assert.DoesNotContain("AccessToken", frame); Assert.DoesNotContain("gateway-owned", frame); }
                Assert.StartsWith("fusion-intent-0-", credentials.JournalFileName);
            }
            finally { Directory.Delete(directory); }
        }
    }
}
