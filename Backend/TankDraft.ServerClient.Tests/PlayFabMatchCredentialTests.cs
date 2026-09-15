using System.Net;
using System.Net.Http;
using System.Net.Security;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class PlayFabMatchCredentialTests
{
    [Theory]
    [InlineData("ws://remote.example/v1/socket")]
    [InlineData("wss://127.0.0.1/v1/socket")]
    [InlineData("wss://localhost/v1/socket")]
    [InlineData("wss://remote.example/v1/socket?seat=0")]
    [InlineData("wss://user@remote.example/v1/socket")]
    public void Remote_endpoint_must_be_a_clean_dns_wss_assignment(string endpoint)
    {
        using var handler = new FakeHandler(_ => Ok());
        Assert.Throws<InvalidOperationException>(() => Create(new Uri(endpoint), handler));
    }

    [Fact]
    public async Task Sends_bearer_and_only_content_version_and_operation_id()
    {
        using var handler = new FakeHandler(_ => Ok());
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);

        await credentials.AcquireAsync(CancellationToken.None);

        Assert.Equal("https://match.example/v1/session", handler.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer", handler.Request.Headers.Authorization!.Scheme);
        Assert.Equal("ticket-secret", handler.Request.Headers.Authorization.Parameter);
        var payload = ServerWire.Parse(handler.Payloads.Single());
        Assert.Equal(new[] { "ContentVersion", "OperationId" }, payload.Properties().Select(property => property.Name).OrderBy(name => name));
        Assert.Equal("content-1", payload.Value<string>("ContentVersion"));
        Assert.True(Guid.TryParseExact(payload.Value<string>("OperationId"), "N", out _));
        Assert.Null(payload["Side"]);
        Assert.Null(payload["MatchId"]);
    }

    [Fact]
    public async Task Accepts_an_opaque_printable_playfab_ticket_up_to_4096_bytes()
    {
        var ticket = new string('a', 4092) + "+/=_";
        using var handler = new FakeHandler(_ => Ok());
        using var credentials = new PlayFabMatchCredentials(new Uri("wss://match.example/v1/socket"), "match-1", 0, "content-1", Path.GetTempPath(), new TicketSource(ticket), handler);
        await credentials.AcquireAsync(CancellationToken.None);
        Assert.Equal(ticket, handler.Request.Headers.Authorization!.Parameter);
    }

    [Fact]
    public async Task Lost_response_reuses_operation_id_and_success_rotates_it()
    {
        var calls = 0;
        using var handler = new FakeHandler(_ => ++calls == 1 ? throw new HttpRequestException("ticket-secret") : Ok());
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);

        await Assert.ThrowsAsync<HttpRequestException>(() => credentials.AcquireAsync(CancellationToken.None));
        await credentials.AcquireAsync(CancellationToken.None);
        var second = ServerWire.Parse(handler.Payloads[1]).Value<string>("OperationId");
        await credentials.AcquireAsync(CancellationToken.None);
        var third = ServerWire.Parse(handler.Payloads[2]).Value<string>("OperationId");
        Assert.Equal(ServerWire.Parse(handler.Payloads[0]).Value<string>("OperationId"), second);
        Assert.NotEqual(second, third);
    }

    [Theory]
    [InlineData("wrong-match", 0)]
    [InlineData("match-1", 1)]
    public async Task Rejects_wrong_match_or_side(string matchId, int side)
    {
        using var handler = new FakeHandler(_ => Ok(matchId, side));
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);
        await Assert.ThrowsAsync<MatchAuthenticationException>(() => credentials.AcquireAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("{\"AccessToken\":\"token-1\",\"AccessToken\":\"token-2\"}")]
    [InlineData("{\"AccessToken\":\"token-1\"} trailing")]
    [InlineData("{\"AccessToken\":\"token-1\",\"SessionId\":\"session-1\",\"StreamId\":\"stream-1\",\"MatchId\":\"match-1\",\"Side\":0,\"Generation\":1,\"ExpiresInSeconds\":0,\"RefreshAfterSeconds\":0}")]
    public async Task Rejects_strict_or_malformed_session_json(string json)
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => credentials.AcquireAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Rejects_oversized_session_response()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(new string('x', 16 * 1024 + 1)) });
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => credentials.AcquireAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancellation_keeps_operation_and_exceptions_do_not_expose_secrets()
    {
        var calls = 0;
        var handler = new FakeHandler(_ => ++calls == 1 ? throw new OperationCanceledException() : throw new HttpRequestException("ticket-secret"));
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => credentials.AcquireAsync(CancellationToken.None));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => credentials.AcquireAsync(CancellationToken.None));
        Assert.Equal(ServerWire.Parse(handler.Payloads[0]).Value<string>("OperationId"), ServerWire.Parse(handler.Payloads[1]).Value<string>("OperationId"));
        Assert.DoesNotContain("ticket-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Provider_exceptions_are_sanitized()
    {
        using var handler = new FakeHandler(_ => Ok());
        using var credentials = new PlayFabMatchCredentials(new Uri("wss://match.example/v1/socket"), "match-1", 0, "content-1", Path.GetTempPath(), new ThrowingSource(), handler);
        var error = await Assert.ThrowsAsync<MatchAuthenticationException>(() => credentials.AcquireAsync(CancellationToken.None));
        Assert.DoesNotContain("provider-secret", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispose_cancels_pending_ticket_acquisition_without_releasing_a_disposed_gate()
    {
        var source = new BlockingSource();
        using var handler = new FakeHandler(_ => Ok());
        var credentials = new PlayFabMatchCredentials(new Uri("wss://match.example/v1/socket"), "match-1", 0, "content-1", Path.GetTempPath(), source, handler);
        var pending = credentials.AcquireAsync(CancellationToken.None);
        await source.Started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        credentials.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public void Remote_tls_requires_normal_platform_validation()
    {
        using var handler = new FakeHandler(_ => Ok());
        using var credentials = Create(new Uri("wss://match.example/v1/socket"), handler);
        Assert.False(credentials.ValidateServerCertificate(null!, null!, SslPolicyErrors.None));
        Assert.False(credentials.ValidateServerCertificate(null!, null!, SslPolicyErrors.RemoteCertificateChainErrors));
    }

    static PlayFabMatchCredentials Create(Uri endpoint, HttpMessageHandler handler) => new(endpoint, "match-1", 0, "content-1", Path.GetTempPath(), new TicketSource(), handler);
    static HttpResponseMessage Ok(string match = "match-1", int side = 0) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(new JObject
        {
            ["AccessToken"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopq", ["SessionId"] = "session-1", ["StreamId"] = "stream-1", ["MatchId"] = match,
            ["Side"] = side, ["Generation"] = 1, ["ExpiresInSeconds"] = 60, ["RefreshAfterSeconds"] = 30
        }.ToString())
    };

    sealed class TicketSource(string ticket = "ticket-secret") : IPlayFabSessionSource
    {
        public Task<string> AcquireSessionTicketAsync(CancellationToken token) => Task.FromResult(ticket);
    }
    sealed class ThrowingSource : IPlayFabSessionSource
    {
        public Task<string> AcquireSessionTicketAsync(CancellationToken token) => throw new InvalidOperationException("provider-secret");
    }
    sealed class BlockingSource : IPlayFabSessionSource
    {
        public TaskCompletionSource<bool> Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<string> AcquireSessionTicketAsync(CancellationToken token)
        {
            Started.TrySetResult(true);
            return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously).Task;
        }
    }
    sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    {
        public HttpRequestMessage Request { get; private set; }
        public List<string> Payloads { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            Request = request;
            Payloads.Add(request.Content == null ? string.Empty : request.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            return Task.FromResult(reply(request));
        }
    }
}
