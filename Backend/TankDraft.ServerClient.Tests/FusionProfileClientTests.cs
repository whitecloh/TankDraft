using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Application;
using TankDraft.Contracts;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class FusionProfileClientTests : IDisposable
{
    const string Version = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    readonly string root = Path.Combine(Path.GetTempPath(), "TankDraftProfileClient", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExplicitDisabledIsTheOnlyLocalFallbackSignal()
    {
        using var fixture = CreateFixture(_ => Reply(200, new JObject { ["Enabled"] = false }));
        Assert.Null(await fixture.Client.GetAsync(default));

        using var malformed = CreateFixture(_ => Reply(200, new JObject()));
        await Assert.ThrowsAsync<InvalidDataException>(() => malformed.Client.GetAsync(default));

        using var denied = CreateFixture(_ => Reply(503, new JObject()));
        await Assert.ThrowsAsync<FusionAuthorityException>(() => denied.Client.GetAsync(default));
    }

    [Fact]
    public async Task EnabledProfileRequiresBoundedTypedInventoryAndSchema()
    {
        using var fixture = CreateFixture(request =>
        {
            string operation = request.Value<string>("Operation");
            if (operation == "Lobby") return Reply(200, new JObject());
            JObject value = Profile();
            ((JArray)value["Inventory"]!["OwnedContentIds"]!)[0] = 7;
            return Reply(200, value);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetAsync(default));
    }

    [Fact]
    public async Task RecentResultsAreProjectedWithoutProviderPayload()
    {
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            JObject profile = Profile();
            profile["RecentResults"] = new JArray(Result());
            return Reply(200, profile);
        });

        ProfileSnapshot snapshot = await fixture.Client.GetAsync(default);
        RecentMatchResult result = Assert.Single(snapshot.RecentResults);
        Assert.Equal(ResultId, result.ResultId);
        Assert.Equal("match-1", result.MatchId);
        Assert.Equal(3, result.OwnWins);
        Assert.Equal(1, result.OpponentWins);
        Assert.True(result.Won);
        Assert.True(result.IsBot);
        Assert.Equal("coins", result.Currency);
        Assert.Equal(5, result.Amount);
        Assert.Equal(RewardDeliveryState.Applied, result.State);
    }

    [Fact]
    public async Task RecentResultsAcceptAnOlderContentVersion()
    {
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            JObject profile = Profile();
            JObject result = Result();
            result["ContentVersion"] = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
            profile["RecentResults"] = new JArray(result);
            return Reply(200, profile);
        });

        Assert.Single((await fixture.Client.GetAsync(default)).RecentResults);
    }

    [Fact]
    public async Task RecentResultsRejectMalformedDuplicateOversizedAndUnknownState()
    {
        foreach (Action<JObject> mutate in new Action<JObject>[]
        {
            profile => profile["RecentResults"] = new JArray(new JObject { ["ResultId"] = "bad" }),
            profile => profile["RecentResults"] = new JArray(Result(), Result()),
            profile => profile["RecentResults"] = new JArray(Enumerable.Range(0, ProfileSnapshot.MaximumRecentResults + 1).Select(_ => Result())),
            profile => { JObject result = Result(); result["Reward"]!["State"] = "Unknown"; profile["RecentResults"] = new JArray(result); }
        })
        {
            using var fixture = CreateFixture(request =>
            {
                if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
                JObject profile = Profile();
                mutate(profile);
                return Reply(200, profile);
            });
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetAsync(default));
        }
    }

    [Fact]
    public void ProfileSnapshotCopiesAndPreservesRecentResults()
    {
        RecentMatchResult[] recent = { Projection() };
        ProfileSnapshot snapshot = new ProfileSnapshot("Commander", 1, 1, 0, 1, 2, 3, 0,
            new[] { "unit.1", "unit.2", "unit.3", "unit.4", "unit.5", "order.1" }, new[] { "unit.1", "unit.2", "unit.3", "unit.4" }, new[] { "order.1", "", "" }, recent);
        recent[0] = new RecentMatchResult(OtherResultId, "match-2", 1, 3, false, false, "coins", 7, RewardDeliveryState.Pending);

        ProfileSnapshot changed = snapshot.WithLoadout(new[] { "unit.5", "unit.2", "unit.3", "unit.4" }, new[] { "order.1", "", "" });
        Assert.Equal(ResultId, snapshot.RecentResults[0].ResultId);
        Assert.Equal(ResultId, changed.RecentResults[0].ResultId);
        Assert.Throws<NotSupportedException>(() => ((IList<RecentMatchResult>)snapshot.RecentResults).Add(Projection()));
    }

    [Fact]
    public async Task UncertainSaveReusesOperationAndCannotBeReplacedByDifferentIntent()
    {
        var operations = new List<string>();
        bool firstSave = true;
        using var fixture = CreateFixture(request =>
        {
            string operation = request.Value<string>("Operation");
            if (operation == "Lobby") return Reply(200, new JObject());
            if (operation == "ProfileGet") return Reply(200, Profile());
            JObject body = (JObject)request["Body"]!;
            operations.Add(body.Value<string>("OperationId")!);
            if (firstSave) { firstSave = false; return Reply(503, new JObject()); }
            return Reply(200, Profile(version: 2, units: new[] { "unit.5", "unit.2", "unit.3", "unit.4" }));
        });
        await fixture.Client.GetAsync(default);
        string[] first = { "unit.5", "unit.2", "unit.3", "unit.4" };
        string[] orders = { "order.1", "", "" };
        await Assert.ThrowsAsync<FusionAuthorityException>(() => fixture.Client.SaveLoadoutAsync(first, orders, default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Client.SaveLoadoutAsync(new[] { "unit.1", "unit.5", "unit.3", "unit.4" }, orders, default));
        ProfileSnapshot saved = await fixture.Client.SaveLoadoutAsync(first, orders, default);
        Assert.Equal("unit.5", saved.UnitIds[0]);
        Assert.Equal(2, operations.Count);
        Assert.Equal(operations[0], operations[1]);
    }

    [Fact]
    public async Task DefinitiveSaveRejectionClearsPendingIntentForRefreshedDifferentLoadout()
    {
        var operations = new List<string>();
        var expectedVersions = new List<int>();
        var profileReads = 0;
        using var fixture = CreateFixture(request =>
        {
            string operation = request.Value<string>("Operation");
            if (operation == "Lobby") return Reply(200, new JObject());
            if (operation == "ProfileGet") return Reply(200, Profile(version: ++profileReads == 1 ? 1 : 2));
            JObject body = (JObject)request["Body"]!;
            operations.Add(body.Value<string>("OperationId")!);
            expectedVersions.Add(body.Value<int>("ExpectedProfileVersion"));
            if (operations.Count == 1) return Reply(409, new JObject());
            return Reply(200, Profile(version: 3, units: body["UnitIds"]!.Values<string>().ToArray()));
        });
        await fixture.Client.GetAsync(default);
        await Assert.ThrowsAsync<FusionAuthorityException>(() => fixture.Client.SaveLoadoutAsync(new[] { "unit.5", "unit.2", "unit.3", "unit.4" }, new[] { "order.1", "", "" }, default));
        ServerProfileRefreshRequiredException refresh = await Assert.ThrowsAsync<ServerProfileRefreshRequiredException>(() => fixture.Client.SaveLoadoutAsync(new[] { "unit.1", "unit.5", "unit.3", "unit.4" }, new[] { "order.1", "", "" }, default));
        Assert.Equal("unit.1", refresh.Snapshot.UnitIds[0]);
        ProfileSnapshot saved = await fixture.Client.SaveLoadoutAsync(new[] { "unit.1", "unit.5", "unit.3", "unit.4" }, new[] { "order.1", "", "" }, default);
        Assert.Equal("unit.5", saved.UnitIds[1]);
        Assert.Equal(new[] { 1, 2 }, expectedVersions);
        Assert.NotEqual(operations[0], operations[1]);
    }

    [Fact]
    public async Task GetDuringUncertainSaveRetriesExactOriginalVersionBeforeRefresh()
    {
        var operations = new List<string>();
        var expectedVersions = new List<int>();
        var profileReads = 0;
        string[] intent = { "unit.5", "unit.2", "unit.3", "unit.4" };
        using var fixture = CreateFixture(request =>
        {
            string operation = request.Value<string>("Operation");
            if (operation == "Lobby") return Reply(200, new JObject());
            if (operation == "ProfileGet") return Reply(200, Profile(version: ++profileReads == 1 ? 1 : 2, units: intent));
            JObject body = (JObject)request["Body"]!;
            operations.Add(body.Value<string>("OperationId")!);
            expectedVersions.Add(body.Value<int>("ExpectedProfileVersion"));
            if (operations.Count == 1) return Reply(503, new JObject());
            return Reply(200, Profile(version: 2, units: intent));
        });
        await fixture.Client.GetAsync(default);
        await Assert.ThrowsAsync<FusionAuthorityException>(() => fixture.Client.SaveLoadoutAsync(intent, new[] { "order.1", "", "" }, default));
        ProfileSnapshot refreshed = await fixture.Client.GetAsync(default);
        Assert.Equal("unit.5", refreshed.UnitIds[0]);
        Assert.Equal(new[] { 1, 1 }, expectedVersions);
        Assert.Equal(operations[0], operations[1]);
    }

    [Fact]
    public async Task ServerEquipDoesNotWriteOrMutateBeforeAcknowledgement()
    {
        ProfileSnapshot seed = Seed();
        var repository = new RecordingRepository(seed);
        var server = new DeferredProfileClient();
        var service = new ProfileService(Definitions(), seed, repository, server);
        service.InitializeServer(seed);
        Task<EquipResult> pending = service.EquipAsync("unit.5", 0, default);
        Assert.Equal(0, repository.Writes);
        Assert.Equal("unit.1", service.Current.UnitIds[0]);
        server.Complete(seed.WithLoadout(new[] { "unit.5", "unit.2", "unit.3", "unit.4" }, new[] { "order.1", "", "" }));
        Assert.Equal(EquipResult.Success, await pending);
        Assert.Equal(0, repository.Writes);
        Assert.Equal("unit.5", service.Current.UnitIds[0]);
    }

    [Fact]
    public async Task FailedServerEquipRetainsCurrentSnapshot()
    {
        ProfileSnapshot seed = Seed();
        var repository = new RecordingRepository(seed);
        var service = new ProfileService(Definitions(), seed, repository, new FailingProfileClient());
        service.InitializeServer(seed);
        Assert.Equal(EquipResult.SaveFailed, await service.EquipAsync("unit.5", 0, default));
        Assert.Equal("unit.1", service.Current.UnitIds[0]);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task ServerRefreshReplacesVisibleProfileWithoutWritingLocalRepository()
    {
        ProfileSnapshot seed = Seed();
        ProfileSnapshot refreshed = seed.WithLoadout(new[] { "unit.2", "unit.1", "unit.3", "unit.4" }, new[] { "order.1", "", "" });
        var repository = new RecordingRepository(seed);
        var service = new ProfileService(Definitions(), seed, repository, new RefreshingProfileClient(refreshed));
        service.InitializeServer(seed);
        Assert.Equal(EquipResult.SaveFailed, await service.EquipAsync("unit.5", 0, default));
        Assert.Equal("unit.2", service.Current.UnitIds[0]);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task RefreshReplacesCurrentWithoutRepositoryWritesAndFailurePreservesCurrent()
    {
        ProfileSnapshot seed = Seed();
        ProfileSnapshot refreshed = seed.WithLoadout(new[] { "unit.2", "unit.1", "unit.3", "unit.4" }, new[] { "order.1", "", "" });
        var repository = new RecordingRepository(seed);
        var server = new SequenceProfileClient(refreshed, new IOException("offline"));
        var service = new ProfileService(Definitions(), seed, repository, server);
        service.InitializeServer(seed);
        var changed = new List<ProfileSnapshot>();
        service.Changed += changed.Add;

        Assert.True(service.IsServerBacked);
        await service.RefreshAsync(default);
        Assert.Equal("unit.2", service.Current.UnitIds[0]);
        Assert.Single(changed);
        Assert.Equal(0, repository.Writes);
        await Assert.ThrowsAsync<IOException>(() => service.RefreshAsync(default));
        Assert.Equal("unit.2", service.Current.UnitIds[0]);
        Assert.Equal(3, service.Current.Coins);
        Assert.Equal(0, repository.Writes);
    }

    [Fact]
    public async Task RefreshAndEquipShareTheServerGate()
    {
        ProfileSnapshot seed = Seed();
        ProfileSnapshot refreshed = seed.WithLoadout(new[] { "unit.2", "unit.1", "unit.3", "unit.4" }, new[] { "order.1", "", "" });
        ProfileSnapshot saved = refreshed.WithLoadout(new[] { "unit.5", "unit.1", "unit.3", "unit.4" }, new[] { "order.1", "", "" });
        var server = new BlockingProfileClient();
        var service = new ProfileService(Definitions(), seed, new RecordingRepository(seed), server);
        service.InitializeServer(seed);

        Task refresh = service.RefreshAsync(default);
        Task<EquipResult> equip = service.EquipAsync("unit.5", 0, default);
        Assert.Equal(1, server.GetCalls);
        Assert.Equal(0, server.SaveCalls);
        server.CompleteGet(refreshed);
        await refresh;
        await server.SaveStarted;
        Assert.Equal(1, server.SaveCalls);
        server.CompleteSave(saved);
        Assert.Equal(EquipResult.Success, await equip);
        Assert.Equal("unit.5", service.Current.UnitIds[0]);
    }

    [Fact]
    public async Task DisposalCancelsFutureProfileOperations()
    {
        var fixture = CreateFixture(_ => Reply(200, new JObject { ["Enabled"] = false }));
        fixture.Client.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => fixture.Client.GetAsync(default));
        fixture.Dispose();
    }

    Fixture CreateFixture(Func<JObject, byte[]> reply)
    {
        FusionRequestChannel channel = null!;
        channel = new FusionRequestChannel((id, bytes) =>
        {
            JObject request = FusionQaProtocol.ReadRequest(bytes);
            channel.Receive(id, reply(request));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true);
        var context = new FusionSessionContext();
        context.Initialize(new FusionQaRequests(new FusionQaClient(channel)), "instance", Version, root, _ => Task.CompletedTask,
            new FusionResumeStorage(root, "account", "instance", Version));
        return new Fixture(channel, new FusionProfileClient(context));
    }

    static byte[] Reply(int status, JObject body) => Encoding.UTF8.GetBytes(new JObject { ["Status"] = status, ["Body"] = body }.ToString(Formatting.None));

    const string ResultId = "1111111111111111111111111111111111111111111111111111111111111111";
    const string OtherResultId = "2222222222222222222222222222222222222222222222222222222222222222";

    static JObject Profile(int version = 1, string[] units = null) => new()
    {
        ["Enabled"] = true,
        ["ProfileVersion"] = version,
        ["Profile"] = new JObject
        {
            ["SchemaVersion"] = 1, ["Name"] = "Commander", ["CommanderLevel"] = 1, ["ArenaLevel"] = 1, ["ArenaProgress"] = 0,
            ["Mastery"] = 0, ["UnitIds"] = new JArray(units ?? new[] { "unit.1", "unit.2", "unit.3", "unit.4" }), ["OrderIds"] = new JArray("order.1", "", "")
        },
        ["Inventory"] = new JObject
        {
            ["OwnedContentIds"] = new JArray("unit.1", "unit.2", "unit.3", "unit.4", "unit.5", "order.1"),
            ["Balances"] = new JObject { ["energy"] = 1, ["gems"] = 2, ["coins"] = 3 }
        }
    };

    static JObject Result() => new()
    {
        ["ResultId"] = ResultId,
        ["MatchId"] = "match-1",
        ["ContentVersion"] = Version,
        ["Wins0"] = 3,
        ["Wins1"] = 1,
        ["Winner"] = 0,
        ["Side"] = 0,
        ["OpponentKind"] = "Bot",
        ["Reward"] = new JObject { ["Currency"] = "coins", ["Amount"] = 5, ["State"] = "Applied", ["Reason"] = "ignored" }
    };

    static RecentMatchResult Projection() => new(ResultId, "match-1", 3, 1, true, true, "coins", 5, RewardDeliveryState.Applied);

    static MetaDefinitions Definitions() => new(new[]
    {
        new ContentDefinition("unit.1", ContentKind.Unit, FormationRow.Tank, 1), new ContentDefinition("unit.2", ContentKind.Unit, FormationRow.Tank, 1),
        new ContentDefinition("unit.3", ContentKind.Unit, FormationRow.Tank, 1), new ContentDefinition("unit.4", ContentKind.Unit, FormationRow.Tank, 1),
        new ContentDefinition("unit.5", ContentKind.Unit, FormationRow.Tank, 1), new ContentDefinition("order.1", ContentKind.Order, FormationRow.Artillery, 1)
    }, new ArmyRules(4, new[] { 1, 20, 35 }));

    static ProfileSnapshot Seed() => new("Commander", 1, 1, 0, 1, 2, 3, 0,
        new[] { "unit.1", "unit.2", "unit.3", "unit.4", "unit.5", "order.1" }, new[] { "unit.1", "unit.2", "unit.3", "unit.4" }, new[] { "order.1", "", "" });

    public void Dispose() { if (Directory.Exists(root)) Directory.Delete(root, true); }

    sealed class Fixture : IDisposable
    {
        readonly FusionRequestChannel channel;
        public FusionProfileClient Client { get; }
        public Fixture(FusionRequestChannel channel, FusionProfileClient client) { this.channel = channel; Client = client; }
        public void Dispose() { Client.Dispose(); channel.Dispose(); }
    }

    sealed class RecordingRepository : IProfileRepository
    {
        readonly ProfileSnapshot loaded;
        public int Writes { get; private set; }
        public RecordingRepository(ProfileSnapshot loaded) { this.loaded = loaded; }
        public ProfileSnapshot Load() => loaded;
        public void Save(ProfileSnapshot profile) { Writes++; }
    }

    sealed class DeferredProfileClient : IServerProfileClient
    {
        readonly TaskCompletionSource<ProfileSnapshot> reply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<ProfileSnapshot> GetAsync(CancellationToken token) => reply.Task;
        public Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token) => reply.Task;
        public void Complete(ProfileSnapshot snapshot) => reply.TrySetResult(snapshot);
    }

    sealed class FailingProfileClient : IServerProfileClient
    {
        public Task<ProfileSnapshot> GetAsync(CancellationToken token) => Task.FromException<ProfileSnapshot>(new IOException("offline"));
        public Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token) => Task.FromException<ProfileSnapshot>(new IOException("offline"));
    }

    sealed class RefreshingProfileClient : IServerProfileClient
    {
        readonly ProfileSnapshot refreshed;
        public RefreshingProfileClient(ProfileSnapshot refreshed) { this.refreshed = refreshed; }
        public Task<ProfileSnapshot> GetAsync(CancellationToken token) => Task.FromResult(refreshed);
        public Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token) => Task.FromException<ProfileSnapshot>(new ServerProfileRefreshRequiredException(refreshed));
    }

    sealed class SequenceProfileClient : IServerProfileClient
    {
        readonly Queue<object> getReplies;
        public SequenceProfileClient(params object[] getReplies) { this.getReplies = new Queue<object>(getReplies); }
        public Task<ProfileSnapshot> GetAsync(CancellationToken token)
        {
            object reply = getReplies.Dequeue();
            if (reply is Exception error) return Task.FromException<ProfileSnapshot>(error);
            return Task.FromResult((ProfileSnapshot)reply);
        }
        public Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token) => Task.FromException<ProfileSnapshot>(new NotSupportedException());
    }

    sealed class BlockingProfileClient : IServerProfileClient
    {
        readonly TaskCompletionSource<ProfileSnapshot> getReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<ProfileSnapshot> saveReply = new(TaskCreationOptions.RunContinuationsAsynchronously);
        readonly TaskCompletionSource<bool> saveStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int GetCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public Task SaveStarted { get { return saveStarted.Task; } }
        public Task<ProfileSnapshot> GetAsync(CancellationToken token) { GetCalls++; return getReply.Task; }
        public Task<ProfileSnapshot> SaveLoadoutAsync(string[] units, string[] orders, CancellationToken token)
        {
            SaveCalls++;
            saveStarted.TrySetResult(true);
            return saveReply.Task;
        }
        public void CompleteGet(ProfileSnapshot profile) { getReply.TrySetResult(profile); }
        public void CompleteSave(ProfileSnapshot profile) { saveReply.TrySetResult(profile); }
    }
}
