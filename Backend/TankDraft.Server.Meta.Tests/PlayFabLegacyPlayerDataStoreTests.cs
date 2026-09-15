using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Meta;
using Xunit;

namespace TankDraft.Server.Meta.Tests;

public sealed class PlayFabLegacyPlayerDataStoreTests
{
    const string UnitItem = "legacy-unit";
    const string CoinCode = "GC";
    static readonly PlayerEntity Actor = new("ABCDE", "ENTITY1");

    [Fact]
    public async Task Inventory_maps_only_exact_catalog_items_and_all_configured_currencies()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","Inventory":[{"ItemInstanceId":"owned","ItemId":"legacy-unit","CatalogVersion":"v1"},{"ItemInstanceId":"wrong-version","ItemId":"legacy-unit","CatalogVersion":"v2"},{"ItemInstanceId":"unknown","ItemId":"other","CatalogVersion":"v1"}],"VirtualCurrency":{"GC":12}}"""));
        using var transport = Transport(handler);

        var inventory = await Store(transport).ReadInventoryAsync(Actor, default);

        Assert.Equal(new[] { "unit-a" }, inventory.OwnedContentIds.Order(StringComparer.Ordinal));
        Assert.Equal(12, inventory.Balances["coin"]);
        Assert.Equal(0, inventory.Balances["scrap"]);
        Assert.Null(inventory.ETag);
        Assert.StartsWith("legacy:", inventory.SourceVersion, StringComparison.Ordinal);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("X-SecretKey", request.HeaderName);
        Assert.Equal("ABCDE", JsonDocument.Parse(request.Body).RootElement.GetProperty("PlayFabId").GetString());
    }

    [Fact]
    public async Task Inventory_fingerprint_is_stable_and_expired_or_depleted_mapped_items_are_not_owned()
    {
        const string response = """{"PlayFabId":"ABCDE","Inventory":[{"ItemInstanceId":"expired","ItemId":"legacy-unit","CatalogVersion":"v1","Expiration":"2000-01-01T00:00:00Z"},{"ItemInstanceId":"depleted","ItemId":"legacy-unit","CatalogVersion":"v1","RemainingUses":0}],"VirtualCurrency":{"GC":12}}""";
        using var firstHandler = new StubHandler();
        firstHandler.Enqueue("Server/GetUserInventory", Data(response));
        using var secondHandler = new StubHandler();
        secondHandler.Enqueue("Server/GetUserInventory", Data(response));
        using var first = Transport(firstHandler);
        using var second = Transport(secondHandler);

        var one = await Store(first).ReadInventoryAsync(Actor, default);
        var two = await Store(second).ReadInventoryAsync(Actor, default);

        Assert.Empty(one.OwnedContentIds);
        Assert.Equal(one.SourceVersion, two.SourceVersion);
    }

    [Fact]
    public async Task Inventory_treats_null_optional_durable_fields_as_absent()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","Inventory":[{"ItemInstanceId":"durable","ItemId":"legacy-unit","CatalogVersion":"v1","RemainingUses":null,"Expiration":null}],"VirtualCurrency":{}}"""));
        using var transport = Transport(handler);

        var inventory = await Store(transport).ReadInventoryAsync(Actor, default);

        Assert.Contains("unit-a", inventory.OwnedContentIds);
    }

    [Theory]
    [InlineData("GC")]
    [InlineData("ZZ")]
    public async Task Inventory_rejects_currency_over_provider_int32_range_even_when_unknown(string code)
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("{\"PlayFabId\":\"ABCDE\",\"Inventory\":[],\"VirtualCurrency\":{\"" + code + "\":2147483648}}"));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport).ReadInventoryAsync(Actor, default));

        Assert.Equal("invalid_inventory", failure.Code);
    }

    [Theory]
    [InlineData("""{"PlayFabId":"ABCDE","Inventory":[{"ItemInstanceId":"same","ItemId":"legacy-unit","CatalogVersion":"v1"},{"ItemInstanceId":"same","ItemId":"legacy-unit","CatalogVersion":"v1"}],"VirtualCurrency":{}}""")]
    [InlineData("""{"PlayFabId":"ABCDE","Inventory":[],"VirtualCurrency":{"GC":-1}}""")]
    [InlineData("""{"PlayFabId":"ABCDE","Inventory":[{"ItemInstanceId":"bad-uses","ItemId":"legacy-unit","CatalogVersion":"v1","RemainingUses":-1}],"VirtualCurrency":{}}""")]
    public async Task Inventory_rejects_invalid_instances_or_balances(string response)
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data(response));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport).ReadInventoryAsync(Actor, default));

        Assert.Equal("invalid_inventory", failure.Code);
    }

    [Fact]
    public async Task Shared_profile_store_preserves_entity_object_cas_contract()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Object/SetObjects", Data("""{"SetResults":[{"ObjectName":"tankdraft_profile_v1","SetResult":"Updated"}],"ProfileVersion":8}"""));
        using var transport = Transport(handler);
        var profile = new PlayerProfile(1, "Tester", 1, 1, 0, 0, ImmutableArray.Create("unit-a"), ImmutableArray.Create("order-a"), null, null);

        var stored = await Store(transport).WriteProfileAsync(Actor, profile, 7, default);

        Assert.Equal(8, stored.ProfileVersion);
        var request = handler.Requests.Last();
        Assert.Equal("X-EntityToken", request.HeaderName);
        Assert.Equal(7, JsonDocument.Parse(request.Body).RootElement.GetProperty("ExpectedProfileVersion").GetInt32());
    }

    [Fact]
    public void Constructor_requires_explicit_unique_bindings_and_two_letter_currency_codes()
    {
        using var transport = Transport(new StubHandler());
        Assert.Throws<ArgumentException>(() => new PlayFabLegacyPlayerDataStore(transport, "v1", [new LegacyInventoryContentBinding(UnitItem, "unit-a"), new LegacyInventoryContentBinding(UnitItem, "unit-b")], [new LegacyCurrencyBinding(CoinCode, "coin")]));
        Assert.Throws<ArgumentException>(() => new PlayFabLegacyPlayerDataStore(transport, "v1", [new LegacyInventoryContentBinding(UnitItem, "unit-a")], [new LegacyCurrencyBinding("usd", "coin")]));
    }

    static PlayFabLegacyPlayerDataStore Store(PlayFabMetaTransport transport) => new(
        transport, "v1", [new LegacyInventoryContentBinding(UnitItem, "unit-a")], [new LegacyCurrencyBinding(CoinCode, "coin"), new LegacyCurrencyBinding("SC", "scrap")]);

    static PlayFabMetaTransport Transport(StubHandler handler) => new("B16D9", "server-secret-123", handler);
    static void EnqueueToken(StubHandler handler) => handler.Enqueue("Authentication/GetEntityToken", Data("""{"Entity":{"Type":"title"},"EntityToken":"entity-token","TokenExpiration":"2099-01-01T00:00:00Z"}"""));
    static string Data(string data) => "{\"code\":200,\"data\":" + data + "}";

    sealed record CapturedRequest(string Endpoint, string HeaderName, string Body);

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Queue<(string Endpoint, string Response)> responses = new();
        public List<CapturedRequest> Requests { get; } = [];
        public void Enqueue(string endpoint, string response) => responses.Enqueue((endpoint, response));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri!.PathAndQuery.TrimStart('/');
            Assert.NotEmpty(responses);
            var expected = responses.Dequeue();
            Assert.Equal(expected.Endpoint, endpoint);
            var header = request.Headers.Single();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(endpoint, header.Key, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(expected.Response, Encoding.UTF8, "application/json") };
        }
    }
}
