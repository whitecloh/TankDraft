using System.Reflection;
using Newtonsoft.Json.Linq;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class FusionColdResumeTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "TankDraftColdResume", Guid.NewGuid().ToString("N"));
    const string Version = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    FusionResumeStorage Store(string account = "account-a", string instance = "instance-a", string version = Version) => new(root, account, instance, version);
    [Fact] public void BindingSeparatesAccountsInstancesContentMatchesAndSides()
    {
        var original = Store().JournalPath("match", 0);
        Assert.Equal(original, Store().JournalPath("match", 0));
        Assert.NotEqual(original, Store("account-b").JournalPath("match", 0));
        Assert.NotEqual(original, Store(instance: "instance-b").JournalPath("match", 0));
        Assert.NotEqual(original, Store(version: new string('b', 64)).JournalPath("match", 0));
        Assert.NotEqual(original, Store().JournalPath("other-match", 0));
        Assert.NotEqual(original, Store().JournalPath("match", 1));
        var hostile = Store("../account").JournalPath("../../outside", 0);
        Assert.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, hostile);
        Assert.DoesNotContain("..", Path.GetRelativePath(root, hostile));
    }
    [Fact] public void Fresh_storage_root_for_same_authenticated_match_side_adopts_existing_server_stream()
    {
        var firstRoot = Path.Combine(root, "storage-first");
        var secondRoot = Path.Combine(root, "storage-second");
        var first = new FusionResumeStorage(firstRoot, "account-a", "instance-a", Version);
        var second = new FusionResumeStorage(secondRoot, "account-a", "instance-a", Version);
        var firstPath = first.JournalPath("match", 0);
        var secondPath = second.JournalPath("match", 0);

        Assert.NotEqual(firstPath, secondPath);
        var priorClient = new ClientIntentJournal(firstPath);
        priorClient.BindStream("server-stream", 7);

        var freshClient = new ClientIntentJournal(secondPath);
        freshClient.BindStream("server-stream", 7);
        freshClient.Begin(new JObject { ["MatchId"] = "match", ["OperationId"] = "fresh-root", ["Sequence"] = 7, ["Payload"] = "exact" });

        Assert.Equal(6, freshClient.Sequence);
        Assert.Equal(7, freshClient.Pending!.Value<long>("Sequence"));
        Assert.Equal("server-stream", new ClientIntentJournal(secondPath).StreamId);
    }
    [Fact] public async Task Fresh_storage_root_adopts_existing_stream_through_welcome_without_replaying_a_command()
    {
        var first = new FusionResumeStorage(Path.Combine(root, "storage-first"), "account-a", "instance-a", Version);
        new ClientIntentJournal(first.JournalPath("match", 0)).BindStream("server-stream", 7);
        var second = new FusionResumeStorage(Path.Combine(root, "storage-second"), "account-a", "instance-a", Version);
        var journalPath = second.JournalPath("match", 0);
        var kinds = new System.Collections.Concurrent.ConcurrentQueue<string>();
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, bytes) =>
        {
            var request = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var operation = request.Value<string>("Operation"); var kind = request["Body"]?.Value<string>("Kind");
            if (kind != null) kinds.Enqueue(kind);
            JObject response;
            if (operation == "Session") response = new JObject { ["MatchId"] = "match", ["Side"] = 0, ["SessionId"] = "session", ["StreamId"] = "server-stream", ["Generation"] = 1, ["RefreshAfterSeconds"] = 30 };
            else if (operation == "SocketOpen") response = new JObject();
            else if (kind == "Hello") response = new JObject { ["Kind"] = "Welcome", ["Protocol"] = "tankdraft-server-v2", ["ContentVersion"] = Version,
                ["MatchId"] = "match", ["Side"] = 0, ["SessionId"] = "session", ["StreamId"] = "server-stream", ["Generation"] = 1, ["NextSequence"] = 7 };
            else return;
            channel.Receive(id, System.Text.Encoding.UTF8.GetBytes(new JObject { ["Status"] = 200, ["Body"] = response }.ToString()));
        }, () => false, TimeSpan.FromSeconds(5), true))
        {
            using var credentials = new FusionMatchCredentials(new FusionQaRequests(new FusionQaClient(channel)), "match", 0, Version, Path.Combine(root, "logs-second"), journalPath);
            using var transport = Transport(credentials);
            transport.Start();
            try
            {
                ClientIntentJournal recovered = null;
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (DateTime.UtcNow < deadline)
                {
                    recovered = new ClientIntentJournal(journalPath);
                    if (recovered.StreamId == "server-stream" && recovered.Sequence == 6) break;
                    await Task.Delay(10);
                }

                Assert.Null(transport.Failure);
                Assert.NotNull(recovered);
                Assert.Equal("server-stream", recovered!.StreamId);
                Assert.Equal(6, recovered.Sequence);
                Assert.Null(recovered.Pending);
                Assert.Contains("Hello", kinds);
                Assert.DoesNotContain("Command", kinds);
            }
            finally
            {
                transport.Dispose();
                await ((Task)typeof(ServerClientTransport).GetField("_task", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(transport)!).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
    [Fact] public void FreshSessionAndDifferentLogDirectoryRecoverExactUnacknowledgedIntent()
    {
        using var channel = new FusionRequestChannel((_, _) => throw new Exception("No network in storage test"), () => false, TimeSpan.FromSeconds(1), true);
        var requests = new FusionQaRequests(new FusionQaClient(channel));
        var first = Context(requests, "logs-first");
        first.Assign(Assignment());
        using var c1 = first.Create();
        using var t1 = Transport(c1);
        var journal = Journal(t1);
        journal.BindStream("server-stream", 1);
        var command = new JObject { ["MatchId"] = "match", ["OperationId"] = "lost-ack", ["Sequence"] = 1, ["Payload"] = "exact" };
        journal.Begin(command);
        var restarted = Context(requests, "logs-second");
        Assert.Throws<InvalidOperationException>(() => restarted.Create()); // Disk alone cannot assign a game.
        restarted.Assign(Assignment());
        using var c2 = restarted.Create();
        Assert.NotEqual(c1.RunDirectory, c2.RunDirectory);
        using var t2 = Transport(c2);
        var restored = Journal(t2);
        restored.BindStream("server-stream", 2); // Server already applied it; receipt must be retried.
        Assert.True(JToken.DeepEquals(command, restored.Pending));
        restored.Acknowledge("lost-ack", "Applied");
        var reopened = new ClientIntentJournal(((IMatchIntentLocation)c2).IntentJournalPath);
        Assert.Equal(1, reopened.Sequence); Assert.Null(reopened.Pending);
    }
    [Fact] public void ForeignInstanceAssignmentCannotSelectLocalJournal()
    {
        var context = Context(null, "logs");
        var assignment = Assignment(); assignment["InstanceId"] = "other";
        Assert.Throws<InvalidDataException>(() => context.Assign(assignment));
    }
    [Fact] public async Task RestoredIntentIsAcknowledgedEvenWhenNoFurtherSnapshotArrives()
    {
        var journalPath = Store().JournalPath("match", 0);
        var saved = new ClientIntentJournal(journalPath); saved.BindStream("stream", 1);
        saved.Begin(new JObject { ["MatchId"] = "match", ["OperationId"] = "lost-ack", ["Sequence"] = 1,
            ["RoundId"] = "1", ["ContentVersion"] = Version, ["CommandKind"] = "Choose", ["Payload"] = "{\"Token\":1,\"OfferIndex\":0}" });
        var kinds = new System.Collections.Concurrent.ConcurrentQueue<string>();
        FusionRequestChannel channel = null;
        using (channel = new FusionRequestChannel((id, bytes) =>
        {
            var request = JObject.Parse(System.Text.Encoding.UTF8.GetString(bytes));
            var operation = request.Value<string>("Operation"); var kind = request["Body"]?.Value<string>("Kind");
            if (kind != null) kinds.Enqueue(kind);
            JObject response;
            if (operation == "Session") response = new JObject { ["MatchId"] = "match", ["Side"] = 0, ["SessionId"] = "session", ["StreamId"] = "stream", ["Generation"] = 1, ["RefreshAfterSeconds"] = 30 };
            else if (operation == "SocketOpen") response = new JObject();
            else if (kind == "Hello") response = new JObject { ["Kind"] = "Welcome", ["Protocol"] = "tankdraft-server-v2", ["ContentVersion"] = Version,
                ["MatchId"] = "match", ["Side"] = 0, ["SessionId"] = "session", ["StreamId"] = "stream", ["Generation"] = 1, ["NextSequence"] = 2 };
            else if (kind == "Command") response = new JObject { ["Kind"] = "Ack", ["OperationId"] = "lost-ack", ["Reply"] = new JObject { ["Code"] = "Accepted" } };
            else return; // Terminal state producer has no new snapshot: reconciliation must not wait on it.
            channel.Receive(id, System.Text.Encoding.UTF8.GetBytes(new JObject { ["Status"] = 200, ["Body"] = response }.ToString()));
        }, () => false, TimeSpan.FromSeconds(5), true))
        {
            using var credentials = new FusionMatchCredentials(new FusionQaRequests(new FusionQaClient(channel)), "match", 0, Version, Path.Combine(root, "logs"), journalPath);
            using var transport = Transport(credentials);
            transport.Start();
            try
            {
                var deadline = DateTime.UtcNow.AddSeconds(5);
                while (transport.Pending && transport.Failure == null && DateTime.UtcNow < deadline) await Task.Delay(10);
                Assert.Null(transport.Failure); Assert.False(transport.Pending);
                Assert.Equal(new[] { "Hello", "Command" }, kinds.Take(2));
                var recovered = new ClientIntentJournal(journalPath); Assert.Equal(1, recovered.Sequence); Assert.Null(recovered.Pending);
            }
            finally
            {
                transport.Dispose();
                await ((Task)typeof(ServerClientTransport).GetField("_task", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(transport)!).WaitAsync(TimeSpan.FromSeconds(5));
            }
        }
    }
    FusionSessionContext Context(FusionQaRequests requests, string logs)
    {
        Directory.CreateDirectory(Path.Combine(root, logs));
        var context = new FusionSessionContext();
        context.Initialize(requests, "instance-a", Version, Path.Combine(root, logs), _ => Task.CompletedTask, Store()); return context;
    }
    static JObject Assignment() => new() { ["State"] = "Matched", ["InstanceId"] = "instance-a", ["MatchId"] = "match", ["Side"] = 0, ["OpponentKind"] = "Human" };
    static ServerClientTransport Transport(IMatchCredentials c) => new(c, "tankdraft-server-v2", Version, 100, 100, 1000, 8192, 8);
    static ClientIntentJournal Journal(ServerClientTransport transport) => (ClientIntentJournal)typeof(ServerClientTransport).GetField("_journal", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(transport)!;
    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }
}
