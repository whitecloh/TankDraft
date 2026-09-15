using System.Net;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Meta;
using TankDraft.Server.Settlement;
using Xunit;

namespace TankDraft.Server.Meta.Tests;

public sealed class PlayFabLegacyRewardProviderTests
{
    const string Account = "ABCDE";
    const string Currency = "GC";
    const string ResultId = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    [Fact]
    public async Task Grant_sends_server_secret_tagged_write_and_validates_balances()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":12}}"""));
        handler.Enqueue("Server/AddUserVirtualCurrency", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":"GC","BalanceChange":3,"Balance":15}"""));
        using var transport = Transport(handler);

        await Provider(transport).GrantAsync(Grant(), default);

        var write = handler.Requests[1];
        Assert.Equal("X-SecretKey", write.HeaderName);
        using var document = JsonDocument.Parse(write.Body);
        var body = document.RootElement;
        Assert.Equal(new[] { "PlayFabId", "VirtualCurrency", "Amount", "CustomTags" }, body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(Account, body.GetProperty("PlayFabId").GetString());
        Assert.Equal(Currency, body.GetProperty("VirtualCurrency").GetString());
        Assert.False(body.TryGetProperty("Currency", out _));
        Assert.Equal(3, body.GetProperty("Amount").GetInt32());
        Assert.Equal(ResultId, body.GetProperty("CustomTags").GetProperty("tankdraftResultId").GetString());
    }

    [Theory]
    [InlineData("bad!", "GC", 3)]
    [InlineData("ABCDE", "usd", 3)]
    [InlineData("ABCDE", "GC", 0)]
    public async Task Invalid_grant_never_makes_http_call(string account, string currency, int amount)
    {
        using var handler = new StubHandler();
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Provider(transport).GrantAsync(new RewardGrant(ResultId, account, currency, amount), default));

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Mutating_original_allowlist_after_construction_cannot_authorize_foreign_account()
    {
        using var handler = new StubHandler();
        using var transport = Transport(handler);
        var allowedAccounts = new HashSet<string>(StringComparer.Ordinal) { Account };
        var provider = new PlayFabLegacyRewardProvider(transport, allowedAccounts, Currency, 10);
        allowedAccounts.Add("FGHIJ");

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => provider.GrantAsync(new RewardGrant(ResultId, "FGHIJ", Currency, 3), default));

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Preflight_overflow_never_makes_write_call()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":2147483647}}"""));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Provider(transport).GrantAsync(Grant(), default));

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Lost_write_response_is_not_retried()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":12}}"""));
        handler.EnqueueFailure("Server/AddUserVirtualCurrency", new HttpRequestException("lost"));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Provider(transport).GrantAsync(Grant(), default));

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Equal(new[] { "Server/GetUserInventory", "Server/AddUserVirtualCurrency" }, handler.Requests.Select(request => request.Endpoint));
    }

    [Theory]
    [InlineData("""{"PlayFabId":"OTHER","VirtualCurrency":"GC","BalanceChange":3,"Balance":15}""")]
    [InlineData("""{"PlayFabId":"ABCDE","VirtualCurrency":"SC","BalanceChange":3,"Balance":15}""")]
    [InlineData("""{"PlayFabId":"ABCDE","VirtualCurrency":"GC","BalanceChange":2,"Balance":15}""")]
    [InlineData("""{"PlayFabId":"ABCDE","VirtualCurrency":"GC","BalanceChange":3}""")]
    public async Task Invalid_write_result_is_provider_unavailable(string result)
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":12}}"""));
        handler.Enqueue("Server/AddUserVirtualCurrency", Data(result));
        using var transport = Transport(handler);

        var failure = await Assert.ThrowsAsync<MetaFailureException>(() => Provider(transport).GrantAsync(Grant(), default));

        Assert.Equal("provider_unavailable", failure.Code);
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task Preflight_network_failure_retries_read_then_sends_only_one_write()
    {
        using var handler = new StubHandler();
        handler.EnqueueFailure("Server/GetUserInventory", new HttpRequestException("read lost"));
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":12}}"""));
        handler.Enqueue("Server/AddUserVirtualCurrency", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":"GC","BalanceChange":3,"Balance":15}"""));
        using var transport = Transport(handler);
        await Provider(transport).GrantAsync(Grant(), default);
        Assert.Equal(2, handler.Requests.Count(x => x.Endpoint == "Server/GetUserInventory"));
        Assert.Single(handler.Requests, x => x.Endpoint == "Server/AddUserVirtualCurrency");
    }

    [Fact]
    public async Task Persistent_preflight_failure_is_bounded_and_never_writes()
    {
        using var handler = new StubHandler();
        for (var i = 0; i < 3; i++) handler.EnqueueFailure("Server/GetUserInventory", new HttpRequestException("offline"));
        using var transport = Transport(handler);
        await Assert.ThrowsAsync<MetaFailureException>(() => Provider(transport).GrantAsync(Grant(), default));
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Equal("Server/GetUserInventory", request.Endpoint));
    }

    [Fact]
    public async Task Cancellation_during_read_retry_stops_without_write()
    {
        using var handler = new StubHandler();
        handler.EnqueueFailure("Server/GetUserInventory", new HttpRequestException("offline"));
        using var transport = Transport(handler);
        using var cancellation = new CancellationTokenSource();
        var grant = Provider(transport).GrantAsync(Grant(), cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => grant);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Successful_delta_is_accepted_when_another_operation_changed_balance()
    {
        using var handler = new StubHandler();
        handler.Enqueue("Server/GetUserInventory", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":{"GC":12}}"""));
        handler.Enqueue("Server/AddUserVirtualCurrency", Data("""{"PlayFabId":"ABCDE","VirtualCurrency":"GC","BalanceChange":3,"Balance":18}"""));
        using var transport = Transport(handler);
        await Provider(transport).GrantAsync(Grant(), default);
        Assert.Equal(2, handler.Requests.Count);
    }

    static RewardGrant Grant() => new(ResultId, Account, Currency, 3);
    static PlayFabLegacyRewardProvider Provider(PlayFabMetaTransport transport) => new(transport, new HashSet<string>(StringComparer.Ordinal) { Account }, Currency, 10);
    static PlayFabMetaTransport Transport(StubHandler handler) => new("B16D9", "server-secret-123", handler);
    static string Data(string data) => "{\"code\":200,\"data\":" + data + "}";

    sealed record CapturedRequest(string Endpoint, string HeaderName, string Body);
    sealed record Response(string Endpoint, string? Body, Exception? Failure);

    sealed class StubHandler : HttpMessageHandler
    {
        readonly Queue<Response> responses = new();
        public List<CapturedRequest> Requests { get; } = [];
        public void Enqueue(string endpoint, string body) => responses.Enqueue(new Response(endpoint, body, null));
        public void EnqueueFailure(string endpoint, Exception failure) => responses.Enqueue(new Response(endpoint, null, failure));

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var endpoint = request.RequestUri!.PathAndQuery.TrimStart('/');
            Assert.NotEmpty(responses);
            var response = responses.Dequeue();
            Assert.Equal(response.Endpoint, endpoint);
            var header = request.Headers.Single();
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            Requests.Add(new CapturedRequest(endpoint, header.Key, body));
            if (response.Failure is not null) throw response.Failure;
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.Body!, Encoding.UTF8, "application/json") };
        }
    }
}
