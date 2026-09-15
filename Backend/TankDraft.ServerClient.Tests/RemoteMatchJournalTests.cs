using System.Reflection;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class RemoteMatchJournalTests
{
    [Fact]
    public void New_match_has_its_own_stream_but_rejoining_retains_the_exact_pending_command()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftMatchJournals", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using var firstCredentials = Credentials("match-a", directory);
            using var first = Transport(firstCredentials);
            var a = Journal(first);
            a.BindStream("stream-a", 1);
            a.Begin(new JObject { ["MatchId"] = "match-a", ["OperationId"] = "pending-a", ["Sequence"] = 1 });

            using var secondCredentials = Credentials("match-b", directory);
            using var second = Transport(secondCredentials);
            var b = Journal(second);
            b.BindStream("stream-b", 1);
            Assert.Null(b.Pending);
            Assert.Equal(0, b.Sequence);
            b.Begin(new JObject { ["MatchId"] = "match-b", ["OperationId"] = "pending-b", ["Sequence"] = 1 });

            using var returningCredentials = Credentials("match-a", directory);
            using var returning = Transport(returningCredentials);
            var restored = Journal(returning);
            restored.BindStream("stream-a", 2); // Server may have committed before its ACK was lost.
            Assert.True(JToken.DeepEquals(a.Pending, restored.Pending));
            Assert.Equal("pending-a", restored.Pending.Value<string>("OperationId"));
            Assert.Throws<InvalidDataException>(() => restored.BindStream("unrelated-stream", 1));
        }
        finally { Directory.Delete(directory, true); }
    }

    private static PlayFabMatchCredentials Credentials(string match, string directory) =>
        new(new Uri("wss://remote.example/v1/socket"), match, 0, "content-v1", directory, new Tickets());
    private static ServerClientTransport Transport(IMatchCredentials credentials) => new(credentials, "tankdraft-server-v2", "content-v1", 100, 100, 1000, 8192, 8);
    private static ClientIntentJournal Journal(ServerClientTransport transport) =>
        (ClientIntentJournal)typeof(ServerClientTransport).GetField("_journal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(transport)!;
    private sealed class Tickets : IPlayFabSessionSource
    {
        public Task<string> AcquireSessionTicketAsync(CancellationToken token) => throw new InvalidOperationException("This journal test must not contact a provider.");
    }
}
