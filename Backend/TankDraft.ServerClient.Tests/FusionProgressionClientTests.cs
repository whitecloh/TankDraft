using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class FusionProgressionClientTests : IDisposable
{
    const string Version = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    readonly string root = Path.Combine(Path.GetTempPath(), "TankDraftProgressionClient", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task GetProjectsImmutableStateAndReceiptStatus()
    {
        JObject sent = null!;
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            sent = (JObject)request["Body"]!;
            return Reply(200, State("Completed"));
        });

        ProgressionSnapshot snapshot = await fixture.Client.GetAsync(default);
        UnitProgressSnapshot unit = Assert.Single(snapshot.Units);
        Assert.Equal(7, snapshot.Sequence);
        Assert.Equal("unit.1", unit.Id);
        Assert.Equal(2, unit.Level);
        Assert.Equal(4, unit.Bits);
        Assert.Equal(9, unit.MasteryXp);
        Assert.Equal(3, snapshot.MasteryChips);
        Assert.Equal("Completed", snapshot.OperationStatus);
        Assert.Equal(new[] { "milestone.1" }, snapshot.ClaimedMilestones);
        Assert.Equal("instance", sent.Value<string>("InstanceId"));
        Assert.Equal(Version, sent.Value<string>("ContentVersion"));
    }

    [Fact]
    public async Task GetWithoutReceiptUsesServerContractAndPreservesRulesVersion()
    {
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            var body = State("Completed"); body.Remove("Receipt"); return Reply(200, body);
        });
        var snapshot = await fixture.Client.GetAsync(default);
        Assert.Null(snapshot.OperationStatus);
        Assert.Equal("reference-progression-qa-v1", snapshot.RulesVersion);
        Assert.Equal(7, snapshot.Sequence);
    }

    [Fact]
    public async Task ExecuteSendsOnlyWhitelistedIntentAndReturnsReviewStatus()
    {
        JObject sent = null!;
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            Assert.Equal("ProgressionExecute", request.Value<string>("Operation"));
            sent = (JObject)request["Body"]!;
            return Reply(200, State("NeedsReview"));
        });

        Guid operation = Guid.Parse("11111111-1111-1111-1111-111111111111");
        ProgressionSnapshot snapshot = await fixture.Client.ExecuteAsync("UpgradeUnit", "unit.1", operation, 7, default);
        Assert.Equal("NeedsReview", snapshot.OperationStatus);
        Assert.Equal("UpgradeUnit", sent.Value<string>("Kind"));
        Assert.Equal("unit.1", sent.Value<string>("TargetId"));
        Assert.Equal(operation.ToString("N"), sent.Value<string>("OperationId"));
        Assert.Equal(7L, sent.Value<long>("ExpectedSequence"));
        Assert.Null(sent["Amount"]);
        Assert.Null(sent["Price"]);
        Assert.Null(sent["ServerGrant"]);
    }

    [Fact]
    public async Task DefinitiveRefusalIsDistinctFromUncertainProviderFailure()
    {
        using var rejected = CreateFixture(request => request.Value<string>("Operation") == "Lobby" ? Reply(200, new JObject()) : Reply(403, new JObject { ["Code"] = "insufficient_funds" }));
        var error = await Assert.ThrowsAsync<ProgressionRejectedException>(() => rejected.Client.ExecuteAsync("BuyOffer", "pack.silver", Guid.NewGuid(), 0, default));
        Assert.Equal("insufficient_funds", error.Code);
        using var uncertain = CreateFixture(request => request.Value<string>("Operation") == "Lobby" ? Reply(200, new JObject()) : Reply(503, new JObject { ["Code"] = "provider_unavailable" }));
        await Assert.ThrowsAsync<FusionAuthorityException>(() => uncertain.Client.ExecuteAsync("BuyOffer", "pack.silver", Guid.NewGuid(), 0, default));
    }

    [Fact]
    public async Task CompletedReceiptForDifferentIntentCannotClearPendingPurchase()
    {
        using var fixture = CreateFixture(request => Reply(200, request.Value<string>("Operation") == "Lobby" ? new JObject() : State("Completed")));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.ExecuteAsync("BuyOffer", "pack.silver", Guid.NewGuid(), 0, default));
    }

    [Fact]
    public async Task MissingStateDisabledAndMalformedValuesNeverBecomeEmptySnapshot()
    {
        foreach (Func<JObject> response in new Func<JObject>[]
        {
            () => new JObject { ["Enabled"] = false },
            () => new JObject { ["Enabled"] = true },
            () => { JObject value = State("Pending"); value["State"]!["MasteryChips"] = -1; return value; },
            () => { JObject value = State("Pending"); value["State"]!["Units"]!["unit.1"]!["Level"] = -1; return value; },
            () => { JObject value = State("Pending"); value["Receipt"]!["Status"] = ""; return value; }
        })
        {
            using var fixture = CreateFixture(request => Reply(200, request.Value<string>("Operation") == "Lobby" ? new JObject() : response()));
            await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetAsync(default));
        }
    }

    [Fact]
    public async Task OversizedUnitsAndInvalidIntentAreRejected()
    {
        using var fixture = CreateFixture(request =>
        {
            if (request.Value<string>("Operation") == "Lobby") return Reply(200, new JObject());
            JObject value = State("Prepared");
            JObject units = (JObject)value["State"]!["Units"]!;
            for (int index = 0; index < ProgressionSnapshot.MaximumUnits; index++) units["unit." + (index + 2)] = new JObject { ["Level"] = 0, ["Bits"] = 0, ["MasteryXp"] = 0 };
            return Reply(200, value);
        });
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetAsync(default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.ExecuteAsync("GrantCoins", "unit.1", Guid.NewGuid(), 0, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.ExecuteAsync("UpgradeUnit", " ", Guid.NewGuid(), 0, default));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Client.ExecuteAsync("UpgradeUnit", "unit.1", Guid.Empty, 0, default));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => fixture.Client.ExecuteAsync("UpgradeUnit", "unit.1", Guid.NewGuid(), -1, default));
    }

    [Fact]
    public async Task ForbiddenResponseDuplicateKeysAreRejectedByExistingTransportParser()
    {
        FusionRequestChannel channel = null!;
        channel = new FusionRequestChannel((id, bytes) =>
        {
            FusionQaProtocol.ReadRequest(bytes);
            string raw = "{\"Status\":200,\"Body\":{\"Enabled\":true,\"Enabled\":true}}";
            channel.Receive(id, Encoding.UTF8.GetBytes(raw));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true);
        using var fixture = CreateFixture(channel);
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Client.GetAsync(default));
    }

    private Fixture CreateFixture(Func<JObject, byte[]> reply)
    {
        FusionRequestChannel channel = null!;
        channel = new FusionRequestChannel((id, bytes) =>
        {
            JObject request = FusionQaProtocol.ReadRequest(bytes);
            channel.Receive(id, reply(request));
        }, () => false, TimeSpan.FromSeconds(1), allowPlaintextQa: true);
        return CreateFixture(channel);
    }

    private Fixture CreateFixture(FusionRequestChannel channel)
    {
        var context = new FusionSessionContext();
        context.Initialize(new FusionQaRequests(new FusionQaClient(channel)), "instance", Version, root, _ => Task.CompletedTask,
            new FusionResumeStorage(root, "account", "instance", Version));
        return new Fixture(channel, new FusionProgressionClient(context));
    }

    private static byte[] Reply(int status, JObject body) => Encoding.UTF8.GetBytes(new JObject { ["Status"] = status, ["Body"] = body }.ToString(Formatting.None));

    private static JObject State(string status) => new()
    {
        ["Enabled"] = true,
        ["RulesVersion"] = "reference-progression-qa-v1",
        ["State"] = new JObject
        {
            ["SchemaVersion"] = 1,
            ["AppliedSequence"] = 7,
            ["Units"] = new JObject { ["unit.1"] = new JObject { ["Level"] = 2, ["Bits"] = 4, ["MasteryXp"] = 9 } },
            ["MasteryChips"] = 3,
            ["ClaimedMilestones"] = new JArray("milestone.1")
        },
        ["Rules"] = new JObject(),
        ["Receipt"] = new JObject { ["Status"] = status, ["OperationId"] = "11111111111111111111111111111111", ["Sequence"] = 7, ["ResolvedUnitIds"] = new JArray("unit.1") }
    };

    public void Dispose()
    {
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly FusionRequestChannel channel;
        public FusionProgressionClient Client { get; }
        public Fixture(FusionRequestChannel channel, FusionProgressionClient client) { this.channel = channel; Client = client; }
        public void Dispose() { Client.Dispose(); channel.Dispose(); }
    }
}
