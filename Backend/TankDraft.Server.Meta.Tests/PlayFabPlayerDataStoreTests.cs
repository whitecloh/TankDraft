using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Meta;
using Xunit;

namespace TankDraft.Server.Meta.Tests;

public sealed class PlayFabPlayerDataStoreTests
{
    private const string UnitItem = "11111111-1111-1111-1111-111111111111";
    private const string CoinItem = "22222222-2222-2222-2222-222222222222";
    private static readonly PlayerEntity Actor = new("ABCDE", "ENTITY1");

    [Fact]
    public async Task Resolve_uses_server_verified_account_and_maps_title_player_entity()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserAccountInfo", Data("""{"UserInfo":{"PlayFabId":"ABCDE","TitleInfo":{"TitlePlayerAccount":{"Id":"ENTITY1","Type":"title_player_account"}}}}"""));
        using var transport = Transport(handler);
        var store = Store(transport);

        var entity = await store.ResolveAsync("ABCDE", default);

        Assert.Equal(Actor, entity);
        var request = Assert.Single(handler.Requests);
        Assert.Equal("X-SecretKey", request.HeaderName);
        Assert.Equal("ABCDE", JsonDocument.Parse(request.Body).RootElement.GetProperty("PlayFabId").GetString());
    }

    [Fact]
    public async Task Read_profile_returns_missing_named_object_at_actual_profile_version()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Object/GetObjects", Data("""{"ProfileVersion":7,"Objects":{"another_object":{"DataObject":{"x":1}}}}"""));
        using var transport = Transport(handler);

        var stored = await Store(transport).ReadProfileAsync(Actor, default);

        Assert.Null(stored.Profile);
        Assert.Equal(7, stored.ProfileVersion);
    }

    [Fact]
    public async Task Read_profile_rejects_malformed_duplicate_json()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Object/GetObjects", """{"code":200,"data":{"ProfileVersion":7,"Objects":{"tankdraft_profile_v1":{"DataObject":{"SchemaVersion":1,"SchemaVersion":2}}}}}""");
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport).ReadProfileAsync(Actor, default));

        Assert.Equal("provider_unavailable", failure.Code);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    public async Task Read_profile_rejects_named_null_or_malformed_data_object_instead_of_treating_it_as_missing(string dataObject)
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Object/GetObjects", Data("{\"ProfileVersion\":7,\"Objects\":{\"tankdraft_profile_v1\":{\"DataObject\":" + dataObject + "}}}"));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport).ReadProfileAsync(Actor, default));

        Assert.Equal("invalid_profile", failure.Code);
    }

    [Fact]
    public async Task Resolve_rejects_a_provider_response_for_a_different_verified_account()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserAccountInfo", Data("""{"UserInfo":{"PlayFabId":"OTHER","TitleInfo":{"TitlePlayerAccount":{"Id":"entity-1","Type":"title_player_account"}}}}"""));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport).ResolveAsync("ABCDE", default));

        Assert.Equal("invalid_identity", failure.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Inventory_two_pages_with_same_etag_maps_owned_and_currency_and_ignores_unknown()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Inventory/GetInventoryItems", Data($$"""{"ETag":"stable","Items":[{"Id":"{{UnitItem}}","StackId":"unit-stack","Amount":1},{"Id":"{{CoinItem}}","StackId":"coins-1","Amount":4},{"Id":"33333333-3333-3333-3333-333333333333","StackId":"unknown","Amount":99}],"ContinuationToken":"page-2"}"""));
        handler.Enqueue("Inventory/GetInventoryItems", Data($$"""{"ETag":"stable","Items":[{"Id":"{{CoinItem}}","StackId":"coins-2","Amount":5}]}"""));
        using var transport = Transport(handler);

        var inventory = await Store(transport).ReadInventoryAsync(Actor, default);

        Assert.Equal("stable", inventory.SourceVersion);
        Assert.Equal("stable", inventory.ETag);
        Assert.Contains("unit-a", inventory.OwnedContentIds);
        Assert.Equal(9, inventory.Balances["coin"]);
        Assert.Equal(2, handler.Requests.Count(request => request.Endpoint == "Inventory/GetInventoryItems"));
        Assert.Equal("page-2", JsonDocument.Parse(handler.Requests.Last().Body).RootElement.GetProperty("ContinuationToken").GetString());
    }

    [Fact]
    public async Task Inventory_rejects_changed_etag_and_currency_overflow()
    {
        using var changedHandler = new StubHandler();
        EnqueueToken(changedHandler);
        changedHandler.Enqueue("Inventory/GetInventoryItems", Data("""{"ETag":"one","Items":[],"ContinuationToken":"next"}"""));
        changedHandler.Enqueue("Inventory/GetInventoryItems", Data("""{"ETag":"two","Items":[]}"""));
        using var changedTransport = Transport(changedHandler);
        var changed = await Assert.ThrowsAsync<MetaFailureException>(() => Store(changedTransport).ReadInventoryAsync(Actor, default));

        using var overflowHandler = new StubHandler();
        EnqueueToken(overflowHandler);
        overflowHandler.Enqueue("Inventory/GetInventoryItems", Data($$"""{"ETag":"same","Items":[{"Id":"{{CoinItem}}","StackId":"one","Amount":9223372036854775807}],"ContinuationToken":"next"}"""));
        overflowHandler.Enqueue("Inventory/GetInventoryItems", Data($$"""{"ETag":"same","Items":[{"Id":"{{CoinItem}}","StackId":"two","Amount":1}]}"""));
        using var overflowTransport = Transport(overflowHandler);
        var overflow = await Assert.ThrowsAsync<MetaFailureException>(() => Store(overflowTransport).ReadInventoryAsync(Actor, default));

        Assert.Equal("inventory_changed", changed.Code);
        Assert.Equal("invalid_inventory", overflow.Code);
    }

    [Fact]
    public async Task Repeated_requests_share_one_entity_token_and_inventory_capacity_is_bounded()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Object/GetObjects", Data("""{"ProfileVersion":7,"Objects":{}}"""));
        handler.Enqueue("Object/GetObjects", Data("""{"ProfileVersion":7,"Objects":{}}"""));
        using var transport = Transport(handler);
        var store = Store(transport, maximumPages: 1);

        await store.ReadProfileAsync(Actor, default);
        await store.ReadProfileAsync(Actor, default);

        Assert.Equal(1, handler.Requests.Count(request => request.Endpoint == "Authentication/GetEntityToken"));

        using var capacityHandler = new StubHandler();
        EnqueueToken(capacityHandler);
        capacityHandler.Enqueue("Inventory/GetInventoryItems", Data("""{"ETag":"stable","Items":[],"ContinuationToken":"next"}"""));
        using var capacityTransport = Transport(capacityHandler);
        var capacity = await Assert.ThrowsAsync<MetaFailureException>(() => Store(capacityTransport, maximumPages: 1).ReadInventoryAsync(Actor, default));
        Assert.Equal("inventory_capacity", capacity.Code);
    }

    [Fact]
    public async Task Inventory_rejects_a_repeated_continuation_token()
    {
        using var handler = new StubHandler();
        EnqueueToken(handler);
        handler.Enqueue("Inventory/GetInventoryItems", Data("""{"ETag":"stable","Items":[],"ContinuationToken":"again"}"""));
        handler.Enqueue("Inventory/GetInventoryItems", Data("""{"ETag":"stable","Items":[],"ContinuationToken":"again"}"""));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Store(transport, maximumPages: 3).ReadInventoryAsync(Actor, default));

        Assert.Equal("invalid_inventory", failure.Code);
    }

    [Fact]
    public async Task Write_captures_expected_profile_version_and_maps_provider_conflict()
    {
        using var successHandler = new StubHandler();
        EnqueueToken(successHandler);
        successHandler.Enqueue("Object/SetObjects", Data("""{"SetResults":[{"ObjectName":"tankdraft_profile_v1","SetResult":"Updated"}],"ProfileVersion":8}"""));
        using var successTransport = Transport(successHandler);
        var profile = Profile();
        var stored = await Store(successTransport).WriteProfileAsync(Actor, profile, 7, default);

        Assert.Equal(8, stored.ProfileVersion);
        var request = successHandler.Requests.Last();
        Assert.Equal(7, JsonDocument.Parse(request.Body).RootElement.GetProperty("ExpectedProfileVersion").GetInt32());

        using var conflictHandler = new StubHandler();
        EnqueueToken(conflictHandler);
        conflictHandler.Enqueue("Object/SetObjects", """{"code":409,"error":"EntityProfileVersionMismatch","data":{}}""");
        using var conflictTransport = Transport(conflictHandler);
        var conflict = await Assert.ThrowsAsync<MetaFailureException>(() => Store(conflictTransport).WriteProfileAsync(Actor, profile, 7, default));
        Assert.Equal("version_conflict", conflict.Code);
    }

    [Fact]
    public async Task Write_payload_roundtrips_strict_profile_fields_through_entity_object()
    {
        var profile = new PlayerProfile(1, "Commander", 19, 5, 42, 7,
            ImmutableArray.Create("unit-a", "unit-b", "unit-c", "unit-d"),
            ImmutableArray.Create("order-a", "", ""), "0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f0f", "fingerprint");
        using var writeHandler = new StubHandler();
        EnqueueToken(writeHandler);
        writeHandler.Enqueue("Object/SetObjects", Data("""{"SetResults":[{"ObjectName":"tankdraft_profile_v1","SetResult":"Updated"}],"ProfileVersion":8}"""));
        using var writeTransport = Transport(writeHandler);

        await Store(writeTransport).WriteProfileAsync(Actor, profile, 7, default);
        var payload = JsonDocument.Parse(writeHandler.Requests.Last().Body).RootElement;
        var writtenProfile = payload.GetProperty("Objects")[0].GetProperty("DataObject").GetRawText();

        using var readHandler = new StubHandler();
        EnqueueToken(readHandler);
        readHandler.Enqueue("Object/GetObjects", Data("{" + "\"ProfileVersion\":8,\"Objects\":{\"tankdraft_profile_v1\":{\"DataObject\":" + writtenProfile + "}}}"));
        using var readTransport = Transport(readHandler);
        var stored = await Store(readTransport).ReadProfileAsync(Actor, default);

        Assert.Equal(8, stored.ProfileVersion);
        Assert.NotNull(stored.Profile);
        Assert.Equal(profile.SchemaVersion, stored.Profile!.SchemaVersion);
        Assert.Equal(profile.Name, stored.Profile.Name);
        Assert.Equal(profile.CommanderLevel, stored.Profile.CommanderLevel);
        Assert.Equal(profile.ArenaLevel, stored.Profile.ArenaLevel);
        Assert.Equal(profile.ArenaProgress, stored.Profile.ArenaProgress);
        Assert.Equal(profile.Mastery, stored.Profile.Mastery);
        Assert.Equal(profile.UnitIds.ToArray(), stored.Profile.UnitIds.ToArray());
        Assert.Equal(profile.OrderIds.ToArray(), stored.Profile.OrderIds.ToArray());
        Assert.Equal(profile.LastOperationId, stored.Profile.LastOperationId);
        Assert.Equal(profile.LastOperationFingerprint, stored.Profile.LastOperationFingerprint);
    }

    private static PlayFabMetaTransport Transport(StubHandler handler) => new("B16D9", "server-secret-123", handler);

    private static PlayFabPlayerDataStore Store(PlayFabMetaTransport transport, int maximumPages = 20) => new(
        transport,
        [new InventoryContentBinding(UnitItem, "unit-a", false), new InventoryContentBinding(CoinItem, "coin", true)],
        "default", maximumPages);

    private static PlayerProfile Profile() => new(1, "Tester", 1, 1, 0, 0,
        ImmutableArray.Create("unit-a", "unit-b", "unit-c", "unit-d"), ImmutableArray.Create("order-a"), null, null);

    private static void EnqueueToken(StubHandler handler) => handler.Enqueue("Authentication/GetEntityToken", Data("""{"Entity":{"Type":"title"},"EntityToken":"entity-token","TokenExpiration":"2099-01-01T00:00:00Z"}"""));

    private static string Data(string data) => "{\"code\":200,\"data\":" + data + "}";

    private sealed record CapturedRequest(string Endpoint, string HeaderName, string Body);

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Queue<(string Endpoint, string Response)> _responses = new();
        public List<CapturedRequest> Requests { get; } = [];

        public void Enqueue(string endpoint, string response) => _responses.Enqueue((endpoint, response));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri!.PathAndQuery.TrimStart('/');
            Assert.NotEmpty(_responses);
            var expected = _responses.Dequeue();
            Assert.Equal(expected.Endpoint, endpoint);
            var header = request.Headers.Single();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(endpoint, header.Key, body));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(expected.Response, Encoding.UTF8, "application/json") };
        }
    }
}
