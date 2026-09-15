using System.Net;
using Microsoft.AspNetCore.Builder;
using PlayFab;
using PlayFab.ServerModels;
using TankDraft.RemoteHost;
using TankDraft.Server.PlayFab.Identity;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class EdgegapIngressTests
{
    const string RequestId = "a1b2c3d4e5f6";
    const string ValidMapping = "{\"ports\":{\"websocket\":{\"name\":\"websocket\",\"internal\":8080,\"external\":31492,\"protocol\":\"WS\"}}}";

    [Theory]
    [InlineData(null, RequestId, ValidMapping)]
    [InlineData("0", RequestId, ValidMapping)]
    [InlineData("1", "UPPERCASE", ValidMapping)]
    [InlineData("1", RequestId, "{")]
    [InlineData("1", RequestId, "{\"ports\":{\"websocket\":{\"internal\":8081,\"external\":31492,\"protocol\":\"WS\"}}}")]
    [InlineData("1", RequestId, "{\"ports\":{\"websocket\":{\"internal\":8080,\"external\":0,\"protocol\":\"WS\"}}}")]
    [InlineData("1", RequestId, "{\"ports\":{\"websocket\":{\"internal\":8080,\"external\":31492,\"protocol\":\"HTTP\"}}}")]
    [InlineData("1", RequestId, "{\"ports\":{\"one\":{\"internal\":8080,\"external\":31492,\"protocol\":\"WS\"},\"two\":{\"internal\":8080,\"external\":31493,\"protocol\":\"WS\"}}}")]
    public void InvalidContractFailsClosed(string? flag, string requestId, string mapping) => Assert.Throws<InvalidOperationException>(() => EdgegapIngress.Validate(flag, requestId, mapping));

    [Fact]
    public void AdditionalProxyPortDoesNotReplaceAuthoredListener()
    {
        var mapping = "{\"ports\":{\"gameport\":{\"internal\":8080,\"external\":31492,\"protocol\":\"WS\"},\"proxy\":{\"internal\":8443,\"external\":31493,\"protocol\":\"TCP\"}}}";
        Assert.Equal(31492, EdgegapIngress.Validate("1", RequestId, mapping).ExternalPort);
    }

    [Fact]
    public async Task ValidMappingStartsExpectedHttpListenerAndForwardedProtoDoesNotEnableBypass()
    {
        var ingress = EdgegapIngress.Validate("1", RequestId, ValidMapping);
        var (content, version) = AuthoredContent.Load();
        using var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only"), _ => Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = false, UserInfo = new UserAccountInfo { PlayFabId = "acctA" } } }));
        using var service = new RemoteMatchService(content, version, identity, new RemoteHostSettings(new HashSet<string>(["acctA"])));
        await using var app = RemoteWebHost.Create(service, false, null, ingress); await app.StartAsync();
        try
        {
            using var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri("http://127.0.0.1:8080") };
            using var request = new HttpRequestMessage(HttpMethod.Get, "/healthz"); request.Headers.Add("X-Forwarded-Proto", "https");
            using var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var ready = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/readyz"));
            Assert.Equal(new[] { "ContentVersion", "EconomyWritesEnabled", "InstanceId", "IsDraining", "MetaEnabled" }, ready.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray());
            Assert.False(ready.RootElement.GetProperty("MetaEnabled").GetBoolean());
            Assert.Throws<InvalidOperationException>(() => RemoteWebHost.Create(service, false));
        }
        finally { await app.StopAsync(); service.Dispose(); }
    }
}
