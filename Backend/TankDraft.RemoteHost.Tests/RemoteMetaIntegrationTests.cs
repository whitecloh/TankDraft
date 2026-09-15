using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PlayFab;
using PlayFab.ServerModels;
using TankDraft.Infrastructure.FusionTransport;
using TankDraft.RemoteHost;
using TankDraft.Server.Meta;
using TankDraft.Server.PlayFab.Identity;
using TankDraft.Server.Progression;
using TankDraft.Server.Settlement;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed partial class RemoteMetaIntegrationTests
{
    const string GatewayKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

    [Fact]
    public async Task FusionQaProfileRoundTripIsVersionedIdempotentAndBoundToQueuedMatchState()
    {
        await using var fixture = await Fixture.StartAsync(metaEnabled: true);
        Assert.True(fixture.Service.MetaEnabled);
        Assert.Throws<InvalidOperationException>(() => fixture.Service.Join("ignored", "direct-join", fixture.Service.ContentVersion));

        using var a = new QaPeer(fixture.Endpoint, "acctA");
        using var b = new QaPeer(fixture.Endpoint, "acctB");
        await a.OkAsync("Lobby", new { OperationId = "meta-lobby-a" });
        JObject initial = await a.OkAsync("ProfileGet", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion });
        Assert.True(initial.Value<bool>("Enabled"));
        Assert.Equal(1, initial.Value<int>("ProfileVersion"));
        Assert.Equal(1, fixture.Store.Writes("acctA"));

        string[] swapped = { "unit.heavy_tank", "unit.mines", "unit.tank_destroyer", "unit.field_artillery" };
        string operation = Guid.NewGuid().ToString("N");
        object save = new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, ExpectedProfileVersion = 1, OperationId = operation, UnitIds = swapped, OrderIds = new[] { "order.reinforce_armor", "", "" } };
        JObject stored = await a.OkAsync("ProfileSave", save);
        Assert.Equal(2, stored.Value<int>("ProfileVersion"));
        Assert.Equal(swapped, stored["Profile"]!["UnitIds"]!.Values<string>());
        Assert.Equal(2, fixture.Store.Writes("acctA"));
        JObject repeated = await a.OkAsync("ProfileSave", save);
        Assert.True(JToken.DeepEquals(stored, repeated));
        Assert.Equal(2, fixture.Store.Writes("acctA"));

        using (var reloaded = new QaPeer(fixture.Endpoint, "acctA"))
        {
            await reloaded.OkAsync("Lobby", new { OperationId = "meta-lobby-a-reload" });
            JObject restored = await reloaded.OkAsync("ProfileGet", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion });
            Assert.Equal(2, restored.Value<int>("ProfileVersion"));
            Assert.Equal(swapped, restored["Profile"]!["UnitIds"]!.Values<string>());
        }

        // The old peer's lobby was intentionally superseded by the reload peer.
        // Use a fresh one for the assignment that follows.
        using var queuedA = new QaPeer(fixture.Endpoint, "acctA");
        await queuedA.OkAsync("Lobby", new { OperationId = "meta-lobby-a-queue" });
        await b.OkAsync("Lobby", new { OperationId = "meta-lobby-b" });
        await b.OkAsync("ProfileGet", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion });
        JObject notOwned = await b.RawAsync("ProfileSave", new
        {
            InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, ExpectedProfileVersion = 1,
            OperationId = Guid.NewGuid().ToString("N"), UnitIds = new[] { "unit.medium_tank", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" },
            OrderIds = new[] { "order.reinforce_armor", "", "" }
        });
        Assert.Equal(403, notOwned.Value<int>("Status"));
        Assert.Equal(1, fixture.Store.Writes("acctB"));
        JObject bIdle = await b.OkAsync("Status", new { InstanceId = fixture.Service.InstanceId });
        Assert.Equal("Idle", bIdle.Value<string>("State"));

        await queuedA.OkAsync("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "meta-join-a" });
        JObject matched = await b.OkAsync("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "meta-join-b" });
        Assert.Equal("Matched", matched.Value<string>("State"));
        JObject aStatus = await queuedA.OkAsync("Status", new { InstanceId = fixture.Service.InstanceId });
        Assert.Equal(matched.Value<string>("MatchId"), aStatus.Value<string>("MatchId"));

        JObject activeEdit = await queuedA.RawAsync("ProfileSave", new
        {
            InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, ExpectedProfileVersion = 2,
            OperationId = Guid.NewGuid().ToString("N"), UnitIds = swapped, OrderIds = new[] { "order.reinforce_armor", "", "" }
        });
        Assert.Equal(409, activeEdit.Value<int>("Status"));
        Assert.Equal(2, fixture.Store.Writes("acctA"));

        var hostile = Encoding.UTF8.GetBytes(new JObject
        {
            ["Operation"] = "ProfileGet",
            ["Body"] = new JObject { ["InstanceId"] = fixture.Service.InstanceId, ["ContentVersion"] = fixture.Service.ContentVersion, ["AccountId"] = "acctB" }
        }.ToString(Formatting.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => queuedA.Peer.ExecuteAsync(hostile, CancellationToken.None));
        Assert.Equal(2, fixture.Store.Writes("acctA"));
    }

    [Fact]
    public async Task DisabledMetaEndpointReturnsExplicitLegacySignal()
    {
        await using var fixture = await Fixture.StartAsync(metaEnabled: false);
        using var peer = new QaPeer(fixture.Endpoint, "acctA");
        await peer.OkAsync("Lobby", new { OperationId = "legacy-lobby" });
        JObject result = await peer.OkAsync("ProfileGet", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion });
        Assert.False(result.Value<bool>("Enabled"));
        Assert.Null(result["Profile"]);
        Assert.Equal(0, fixture.Store.Writes("acctA"));
    }

    sealed class QaPeer : IDisposable
    {
        public FusionQaAuthorityPeer Peer { get; }
        public QaPeer(Uri endpoint, string account) => Peer = new FusionQaAuthorityPeer(endpoint, GatewayKey, account);
        public async Task<JObject> OkAsync(string operation, object body)
        {
            JObject response = await RawAsync(operation, body);
            Assert.Equal(200, response.Value<int>("Status"));
            return (JObject)response["Body"]!;
        }
        public async Task<JObject> RawAsync(string operation, object body)
        {
            byte[] request = Encoding.UTF8.GetBytes(new JObject { ["Operation"] = operation, ["Body"] = JObject.FromObject(body) }.ToString(Formatting.None));
            return JObject.Parse(Encoding.UTF8.GetString(await Peer.ExecuteAsync(request, CancellationToken.None)));
        }
        public void Dispose() => Peer.Dispose();
    }

    sealed class Fixture : IAsyncDisposable
    {
        readonly WebApplication app;
        public RemoteMatchService Service { get; }
        public TestStore Store { get; }
        public Uri Endpoint { get; }
        public TestClock? Clock { get; }
        public SqliteSettlementStore? Settlements { get; }
        public ProgressionService? Progression { get; }
        Fixture(WebApplication app, RemoteMatchService service, TestStore store, TestClock? clock, SqliteSettlementStore? settlements, ProgressionService? progression)
        {
            this.app = app; Service = service; Store = store; Clock = clock; Settlements = settlements; Progression = progression; Endpoint = new Uri(app.Urls.Single());
        }
        public static async Task<Fixture> StartAsync(bool metaEnabled, bool progressionEnabled = false, bool settlementEnabled = false)
        {
            var (content, version) = AuthoredContent.Load();
            var clock = settlementEnabled ? new TestClock() : null;
            var time = (TimeProvider?)clock ?? TimeProvider.System;
            var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "test-only", 4), _ => Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult>()), time.GetUtcNow);
            var service = new RemoteMatchService(content, version, identity, new RemoteHostSettings(new HashSet<string>(["acctA", "acctB"])), clock);
            MetaRules rules = System.Text.Json.JsonSerializer.Deserialize<MetaRules>(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Content", "meta-rules.json"))) ?? throw new InvalidDataException();
            if (rules.ContentVersion != version) throw new InvalidDataException("Test authored meta version mismatch.");
            var store = new TestStore(rules);
            if (metaEnabled)
                service.ConfigureMeta(new PlayerMetaService(store, rules), (account, _) => Task.FromResult(new PlayerEntity(account, account + "Entity")));
            ProgressionService? progression = null;
            if (progressionEnabled)
            {
                var progressionRules = ProgressionContent.Load().Rules();
                var journal = new TankDraft.Server.Progression.SqliteProgressionStore(Path.Combine(Path.GetTempPath(), "TankDraftProgressionHttp", Guid.NewGuid().ToString("N"), "operations.sqlite"));
                progression = new ProgressionService(journal, new ProgressionWallet(), progressionRules);
                service.ConfigureProgression(progression, progressionRules, journal);
            }
            SqliteSettlementStore? settlements = null;
            if (settlementEnabled)
            {
                settlements = new SqliteSettlementStore(Path.Combine(Path.GetTempPath(), "TankDraftProgressionSettlement", Guid.NewGuid().ToString("N"), "results.sqlite"), new RewardPolicy("progression-test-v1", "CO", 1, 1, 10));
                service.ConfigureSettlement(settlements, new SettlementProvider());
            }
            var app = RemoteWebHost.Create(service, true, gatewayKey: GatewayKey, allowPhotonQa: true);
            await app.StartAsync();
            return new Fixture(app, service, store, clock, settlements, progression);
        }
        public async ValueTask DisposeAsync()
        {
            await app.StopAsync();
            await app.DisposeAsync();
            Service.Dispose();
            Settlements?.Dispose();
        }
    }

    sealed class SettlementProvider : IRewardProvider
    {
        public Task GrantAsync(RewardGrant grant, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    sealed class TestClock : TimeProvider
    {
        readonly DateTimeOffset start = DateTimeOffset.UtcNow;
        long ticks = 1;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override DateTimeOffset GetUtcNow() => start.AddTicks(Interlocked.Read(ref ticks));
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }

    sealed class TestStore : IPlayerDataStore
    {
        readonly object gate = new();
        readonly Dictionary<string, StoredProfile> profiles = new(StringComparer.Ordinal);
        readonly ImmutableDictionary<string, long> balances = ImmutableDictionary<string, long>.Empty.Add("energy", 10).Add("gems", 20).Add("coins", 30);
        readonly ImmutableHashSet<string> all;
        readonly ImmutableHashSet<string> withoutMedium;
        readonly Dictionary<string, int> writes = new(StringComparer.Ordinal);
        public TestStore(MetaRules rules)
        {
            all = rules.Definitions.Select(x => x.Id).ToImmutableHashSet(StringComparer.Ordinal);
            withoutMedium = all.Remove("unit.medium_tank");
        }
        public int Writes(string account) { lock (gate) return writes.TryGetValue(account, out int value) ? value : 0; }
        public Task<StoredProfile> ReadProfileAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            lock (gate) return Task.FromResult(profiles.TryGetValue(player.AccountId, out StoredProfile? value) ? value! : new StoredProfile(0, null));
        }
        public Task<InventorySnapshot> ReadInventoryAsync(PlayerEntity player, CancellationToken cancellationToken)
        {
            ImmutableHashSet<string> owned = player.AccountId == "acctB" ? withoutMedium : all;
            return Task.FromResult(new InventorySnapshot("test-inventory-v1", "test-etag", owned, balances));
        }
        public Task<StoredProfile> WriteProfileAsync(PlayerEntity player, PlayerProfile profile, int expectedVersion, CancellationToken cancellationToken)
        {
            lock (gate)
            {
                StoredProfile current = profiles.TryGetValue(player.AccountId, out StoredProfile? value) ? value! : new StoredProfile(0, null);
                if (current.ProfileVersion != expectedVersion) throw new MetaFailureException("version_conflict");
                StoredProfile next = new(expectedVersion + 1, profile);
                profiles[player.AccountId] = next;
                writes[player.AccountId] = WritesUnsafe(player.AccountId) + 1;
                return Task.FromResult(next);
            }
        }
        int WritesUnsafe(string account) => writes.TryGetValue(account, out int value) ? value : 0;
    }
}
