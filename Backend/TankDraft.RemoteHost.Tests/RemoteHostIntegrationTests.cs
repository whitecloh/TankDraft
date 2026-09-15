using System.Net;
using System.Net.Http.Headers;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using PlayFab;
using PlayFab.ServerModels;
using TankDraft.RemoteHost;
using TankDraft.Server.Security;
using TankDraft.Server.PlayFab.Identity;
using Xunit;

namespace TankDraft.RemoteHost.Tests;

public sealed class RemoteHostIntegrationTests
{
    [Fact]
    public async Task NativeProducerKeepsReadingWhenGatewayRotatesAccessBeforeClientReauthentication()
    {
        await using var fixture = await Fixture.Start(gateway: true, photonQa: true);
        var frames = new List<string>(); using var client = new QaLoopbackClient(fixture, "acctA", frames);
        using var opponent = new QaLoopbackClient(fixture, "acctB", frames);
        await client.Send("Lobby", new { OperationId = "native-lobby-a" }); await opponent.Send("Lobby", new { OperationId = "native-lobby-b" });
        await client.Send("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "native-join-a" });
        await opponent.Send("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "native-join-b" });
        var first = await client.Send("Session", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "native-access-1" });
        await client.Send("SocketOpen", new { });
        await client.Send("SocketExchange", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
        await client.Send("SocketExchange", new { Kind = "EnableParallelRefresh" });
        var renewed = await client.Send("Session", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "native-access-2" });
        Assert.True(renewed.Value<bool>("NativeAccessBound")); Assert.True(renewed.Value<int>("Generation") > first.Value<int>("Generation"));
        var snapshot = await client.Send("SocketExchange", new { Kind = "Poll", AfterEventSequence = (long?)null });
        Assert.Equal("Snapshot", snapshot.Value<string>("Kind"));
        Assert.Equal(renewed.Value<string>("MatchId"), snapshot["Snapshot"]!.Value<string>("MatchId"));
        var ack = await client.Send("SocketExchange", new { Kind = "Reauthenticate" });
        Assert.Equal(renewed.Value<int>("Generation"), ack.Value<int>("Generation"));
        foreach (var wire in frames) foreach (var forbidden in new[] { "AccessToken", "SessionTicket", "GatewayKey", "Authorization" }) Assert.DoesNotContain(forbidden, wire);
    }
    [Fact]
    public async Task PlaintextQaCompletesMatchWithoutExposingAnyBearerCredentials()
    {
        await using var fixture = await Fixture.Start(gateway: true, photonQa: true);
        var frames = new List<string>(); string matchId;
        using (var a = new QaLoopbackClient(fixture, "acctA", frames))
        using (var b = new QaLoopbackClient(fixture, "acctB", frames))
        {
            await a.Send("Lobby", new { OperationId = "qa-login-a" }); await b.Send("Lobby", new { OperationId = "qa-login-b" });
            await a.Send("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "qa-join-a" });
            var matched = await b.Send("Join", new { InstanceId = fixture.Service.InstanceId, ContentVersion = fixture.Service.ContentVersion, OperationId = "qa-join-b" });
            Assert.Equal("Matched", matched.Value<string>("State")); matchId = matched.Value<string>("MatchId")!;
            await a.Send("Session", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "qa-first-access" });
            await a.Send("SocketOpen", new { });
            var welcome = await a.Send("SocketExchange", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
            var snapshot = (await a.Send("SocketExchange", new { Kind = "Poll", AfterEventSequence = (long?)null }))["Snapshot"]!;
            var command = new { Kind = "Command", Command = new { MatchId = matchId, RoundId = snapshot.Value<int>("Round").ToString(), ContentVersion = fixture.Service.ContentVersion,
                OperationId = "qa-choice", Sequence = welcome.Value<long>("NextSequence"), CommandKind = "Choose", Payload = JsonSerializer.Serialize(new { Token = snapshot.Value<long>("ChoiceToken"), OfferIndex = 0 }) } };
            var ack = await a.Send("SocketExchange", command); Assert.True(ack["Reply"]!.Value<bool>("Accepted"));
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(ack, await a.Send("SocketExchange", command)));
        }
        for (var step = 0; step < 120; step++)
        {
            fixture.Advance(TimeSpan.FromSeconds(step == 0 ? 40 : 20));
            for (var settle = 0; settle < 128; settle++) fixture.Service.Pump();
            using var a = new QaLoopbackClient(fixture, "acctA", frames); using var b = new QaLoopbackClient(fixture, "acctB", frames);
            async Task<Newtonsoft.Json.Linq.JToken> Read(QaLoopbackClient client, string side)
            {
                var session = await client.Send("Session", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "qa-return-" + side + "-" + step });
                Assert.Equal(matchId, session.Value<string>("MatchId"));
                await client.Send("SocketOpen", new { });
                await client.Send("SocketExchange", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
                return (await client.Send("SocketExchange", new { Kind = "Poll", AfterEventSequence = (long?)null }))["Snapshot"]!;
            }
            var af = await Read(a, "a"); var bf = await Read(b, "b");
            if (af.Value<string>("Phase") != "MatchResult") continue;
            Assert.Equal("MatchResult", bf.Value<string>("Phase"));
            foreach (var name in new[] { "Wins0", "Wins1", "Results", "LastWinner" }) Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(af[name], bf[name]));
            Assert.Equal(4, Math.Max(af.Value<int>("Wins0"), af.Value<int>("Wins1")));
            foreach (var wire in frames) foreach (var forbidden in new[] { "SessionTicket", "AccessToken", "LobbyToken", "Authorization", "GatewayKey", "PhotonToken", "photon-verified", new string('A', 64) }) Assert.DoesNotContain(forbidden, wire);
            return;
        }
        Assert.Fail("QA match did not finish.");
    }
    [Fact]
    public async Task PhotonQaIdentityRouteRequiresOptInGatewayKeyAndAllowlistedIdentity()
    {
        await using var disabled = await Fixture.Start(gateway: true);
        using var absent = await disabled.QueueRaw("../fusion-qa/lobby", "photon-verified", new { OperationId = "qa-disabled" });
        Assert.Equal(HttpStatusCode.NotFound, absent.StatusCode);
        Assert.Throws<ArgumentException>(() => RemoteWebHost.Create(disabled.Service, true, allowPhotonQa: true));
        await using var enabled = await Fixture.Start(gateway: true, photonQa: true);
        enabled.Client.DefaultRequestHeaders.Remove("X-TankDraft-Player"); enabled.Client.DefaultRequestHeaders.Add("X-TankDraft-Player", "otherA");
        using var foreign = await enabled.QueueRaw("../fusion-qa/lobby", "photon-verified", new { OperationId = "qa-foreign" });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        enabled.Client.DefaultRequestHeaders.Remove("X-TankDraft-Gateway");
        using var noKey = await enabled.QueueRaw("../fusion-qa/lobby", "photon-verified", new { OperationId = "qa-no-key" });
        Assert.Equal(HttpStatusCode.Forbidden, noKey.StatusCode);
    }
    [Fact]
    public async Task FusionClientProtocolRejoinsOfflineMatchAndReadsSameFinalResult()
    {
        await using var fixture = await Fixture.Start(gateway: true);
        string matchId;
        using (var a = new FusionLoopbackClient(fixture, "acctA"))
        using (var b = new FusionLoopbackClient(fixture, "acctB"))
        {
            var al = await a.Send("Lobby", "ticket-player-a", new { OperationId = "client-login-a" });
            var bl = await b.Send("Lobby", "ticket-player-b", new { OperationId = "client-login-b" });
            await a.Send("Join", al.Value<string>("LobbyToken")!, new { InstanceId = fixture.Service.InstanceId, OperationId = "client-join-a", ContentVersion = fixture.Service.ContentVersion });
            var matched = await b.Send("Join", bl.Value<string>("LobbyToken")!, new { InstanceId = fixture.Service.InstanceId, OperationId = "client-join-b", ContentVersion = fixture.Service.ContentVersion });
            Assert.Equal("Matched", matched.Value<string>("State")); matchId = matched.Value<string>("MatchId")!;
            var access = await a.Send("Session", "ticket-player-a", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "client-access" });
            await a.Send("SocketOpen", access.Value<string>("AccessToken")!, new { });
            var welcome = await a.Send("SocketExchange", "", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
            var frame = (await a.Send("SocketExchange", "", new { Kind = "Poll", AfterEventSequence = (long?)null }))["Snapshot"]!;
            var command = new { Kind = "Command", Command = new { MatchId = matchId, RoundId = frame.Value<int>("Round").ToString(), ContentVersion = fixture.Service.ContentVersion,
                OperationId = "client-choice", Sequence = welcome.Value<long>("NextSequence"), CommandKind = "Choose", Payload = JsonSerializer.Serialize(new { Token = frame.Value<long>("ChoiceToken"), OfferIndex = 0 }) } };
            var accepted = await a.Send("SocketExchange", "", command);
            Assert.True(accepted["Reply"]!.Value<bool>("Accepted"));
            Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(accepted, await a.Send("SocketExchange", "", command)));
        }
        // Both client channels and their sockets are gone. Only the existing authority advances time.
        for (var step = 0; step < 120; step++)
        {
            fixture.Advance(TimeSpan.FromSeconds(step == 0 ? 40 : 20));
            for (var settle = 0; settle < 128; settle++) fixture.Service.Pump();
            using var a = new FusionLoopbackClient(fixture, "acctA");
            using var b = new FusionLoopbackClient(fixture, "acctB");
            async Task<Newtonsoft.Json.Linq.JToken> Rejoin(FusionLoopbackClient client, string suffix)
            {
                var access = await client.Send("Session", "ticket-player-" + suffix, new { ContentVersion = fixture.Service.ContentVersion, OperationId = "client-return-" + suffix + "-" + step });
                Assert.Equal(matchId, access.Value<string>("MatchId"));
                await client.Send("SocketOpen", access.Value<string>("AccessToken")!, new { });
                await client.Send("SocketExchange", "", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
                return (await client.Send("SocketExchange", "", new { Kind = "Poll", AfterEventSequence = (long?)null }))["Snapshot"]!;
            }
            var af = await Rejoin(a, "a"); var bf = await Rejoin(b, "b");
            Assert.Equal(matchId, af.Value<string>("MatchId")); Assert.Equal(matchId, bf.Value<string>("MatchId"));
            if (af.Value<string>("Phase") != "MatchResult") continue;
            Assert.Equal("MatchResult", bf.Value<string>("Phase"));
            foreach (var field in new[] { "Wins0", "Wins1", "LastWinner", "Results" }) Assert.True(Newtonsoft.Json.Linq.JToken.DeepEquals(af[field], bf[field]), field);
            Assert.Equal(4, Math.Max(af.Value<int>("Wins0"), af.Value<int>("Wins1")));
            return;
        }
        Assert.Fail("Client adapter did not observe the complete authoritative match.");
    }
    [Fact]
    public async Task FusionPeerBridgeUsesExistingQueueSocketAndCommandValidation()
    {
        await using var fixture = await Fixture.Start(gateway: true);
        using var peer = new TankDraft.Infrastructure.FusionTransport.FusionAuthorityPeer(fixture.Client.BaseAddress!, new string('A', 64), "acctA");
        async Task<JsonElement> Exchange(string operation, string authorization, object body)
        {
            var bytes = await peer.ExecuteAsync(JsonSerializer.SerializeToUtf8Bytes(new { Operation = operation, Authorization = authorization, Body = body }), CancellationToken.None);
            using var json = JsonDocument.Parse(bytes); Assert.Equal(200, json.RootElement.GetProperty("Status").GetInt32()); return json.RootElement.GetProperty("Body").Clone();
        }
        var lobby = await Exchange("Lobby", "ticket-player-a", new { OperationId = "fusion-login" });
        var a = lobby.GetProperty("LobbyToken").GetString()!;
        var b = await fixture.Service.LoginAsync("ticket-player-b", "login-b", CancellationToken.None);
        await Exchange("Join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "fusion-join", ContentVersion = fixture.Service.ContentVersion });
        fixture.Service.Join(b.LobbyToken, "join-b", fixture.Service.ContentVersion);
        var queue = await Exchange("Status", a, new { InstanceId = fixture.Service.InstanceId });
        Assert.Equal("Matched", queue.GetProperty("State").GetString());
        var access = await Exchange("Session", "ticket-player-a", new { ContentVersion = fixture.Service.ContentVersion, OperationId = "fusion-access" });
        await Exchange("SocketOpen", access.GetProperty("AccessToken").GetString()!, new { });
        var hello = await Exchange("SocketExchange", "", new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
        Assert.Equal("Welcome", hello.GetProperty("Kind").GetString());
        var poll = await Exchange("SocketExchange", "", new { Kind = "Poll", AfterEventSequence = (long?)null });
        var snapshot = poll.GetProperty("Snapshot");
        var command = new { Kind = "Command", Command = new { MatchId = snapshot.GetProperty("MatchId").GetString(), RoundId = snapshot.GetProperty("Round").GetInt32().ToString(), ContentVersion = fixture.Service.ContentVersion, OperationId = "fusion-choice", Sequence = hello.GetProperty("NextSequence").GetInt64(), CommandKind = "Choose", Payload = JsonSerializer.Serialize(new { Token = snapshot.GetProperty("ChoiceToken").GetInt64(), OfferIndex = 0 }) } };
        var ack = await Exchange("SocketExchange", "", command);
        Assert.Equal("Ack", ack.GetProperty("Kind").GetString());
        Assert.True(ack.GetProperty("Reply").GetProperty("Accepted").GetBoolean());
        var repeat = await Exchange("SocketExchange", "", command); Assert.Equal(ack.GetRawText(), repeat.GetRawText());
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.ExecuteAsync(JsonSerializer.SerializeToUtf8Bytes(new { Operation = "http://attacker.invalid/", Authorization = "x", Body = new { } }), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidDataException>(() => peer.ExecuteAsync(new byte[8193], CancellationToken.None));
        await Assert.ThrowsAsync<Newtonsoft.Json.JsonReaderException>(() => peer.ExecuteAsync(Encoding.UTF8.GetBytes("{\"Operation\":\"Lobby\",\"Operation\":\"Session\",\"Authorization\":\"x\",\"Body\":{}}"), CancellationToken.None));
    }
    [Fact]
    public async Task FusionGatewayBindsVerifiedPhotonPlayerToPlayFabLobbyAndMatch()
    {
        await using var fixture = await Fixture.Start(gateway: true);
        var a = await fixture.Lobby("ticket-player-a", "login-a");
        using (var foreignLogin = new HttpRequestMessage(HttpMethod.Post, "/v1/lobby") { Content = new StringContent("{\"OperationId\":\"foreign-login\"}", Encoding.UTF8, "application/json") })
        {
            foreignLogin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ticket-player-b");
            using var response = await fixture.Client.SendAsync(foreignLogin);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        fixture.Client.DefaultRequestHeaders.Remove("X-TankDraft-Player"); fixture.Client.DefaultRequestHeaders.Add("X-TankDraft-Player", "acctB");
        var b = await fixture.Lobby("ticket-player-b", "login-b");
        fixture.Client.DefaultRequestHeaders.Remove("X-TankDraft-Player"); fixture.Client.DefaultRequestHeaders.Add("X-TankDraft-Player", "acctA");
        using (var stolen = await fixture.QueueRaw("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "steal", ContentVersion = fixture.Service.ContentVersion }))
            Assert.Equal(HttpStatusCode.Forbidden, stolen.StatusCode);
        Assert.Equal("Idle", fixture.Service.Status(b).State);
        fixture.Service.Join(a, "join-a", fixture.Service.ContentVersion); fixture.Service.Join(b, "join-b", fixture.Service.ContentVersion);
        var wrongSession = new HttpRequestMessage(HttpMethod.Post, "/v1/session") { Content = new StringContent(JsonSerializer.Serialize(new { ContentVersion = fixture.Service.ContentVersion, OperationId = "wrong-account" }), Encoding.UTF8, "application/json") };
        wrongSession.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ticket-player-b");
        using (var rejected = await fixture.Client.SendAsync(wrongSession)) Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
        var accessB = await fixture.Service.ExchangeMatchAsync("ticket-player-b", fixture.Service.ContentVersion, "b-access", CancellationToken.None);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("X-TankDraft-Gateway", new string('A', 64)); socket.Options.SetRequestHeader("X-TankDraft-Player", "acctA");
        socket.Options.SetRequestHeader("Authorization", "Bearer " + accessB.AccessToken);
        var endpoint = new UriBuilder(new Uri(fixture.Client.BaseAddress!, "/v1/socket")) { Scheme = "ws" }.Uri;
        await Assert.ThrowsAsync<WebSocketException>(() => socket.ConnectAsync(endpoint, CancellationToken.None));
        fixture.Client.DefaultRequestHeaders.Remove("X-TankDraft-Player");
        using var missing = await fixture.QueueRaw("status", a, new { InstanceId = fixture.Service.InstanceId });
        Assert.Equal(HttpStatusCode.Forbidden, missing.StatusCode);
    }
    [Fact]
    public async Task LoopbackQueueSessionAndSocketEnforceRotationAndCommandReplay()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        Assert.Equal("Searching", (await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion })).GetProperty("State").GetString());
        var matched = await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        Assert.Equal("Matched", matched.GetProperty("State").GetString());
        var access = await fixture.Session("ticket-player-a", "session-a");
        await using var socket = await fixture.Socket(access.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
        var welcome = await socket.Receive(); Assert.Equal("Welcome", welcome.GetProperty("Kind").GetString());
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var snapshot = (await socket.Receive()).GetProperty("Snapshot");
        var command = new { MatchId = welcome.GetProperty("MatchId").GetString(), RoundId = snapshot.GetProperty("Round").GetInt32().ToString(), ContentVersion = fixture.Service.ContentVersion, OperationId = "choose-a", Sequence = welcome.GetProperty("NextSequence").GetInt64(), CommandKind = "Choose", Payload = JsonSerializer.Serialize(new { Token = snapshot.GetProperty("ChoiceToken").GetInt64(), OfferIndex = 0 }) };
        await socket.Send(new { Kind = "Command", Command = command }); var ack = await socket.Receive(); Assert.True(ack.GetProperty("Reply").GetProperty("Accepted").GetBoolean());
        await socket.Send(new { Kind = "Command", Command = command }); var duplicate = await socket.Receive(); Assert.Equal(ack.GetProperty("Reply").GetRawText(), duplicate.GetProperty("Reply").GetRawText());
        var renewed = await fixture.Session("ticket-player-a", "renew-a"); var renewedToken = renewed.GetProperty("AccessToken").GetString()!;
        await socket.Send(new { Kind = "Reauthenticate", AccessToken = renewedToken }); var reauth = await socket.Receive(); Assert.Equal("Reauthenticated", reauth.GetProperty("Kind").GetString()); Assert.Equal(welcome.GetProperty("StreamId").GetString(), reauth.GetProperty("StreamId").GetString());
        var old = await fixture.SocketFailure(access.GetProperty("AccessToken").GetString()!); Assert.Null(old);
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); Assert.Equal("Snapshot", (await socket.Receive()).GetProperty("Kind").GetString());
    }
    [Fact]
    public async Task StrictHttpRejectsDuplicatesAndOldInstanceAndCancelDoesNotResurrect()
    {
        await using var fixture = await Fixture.Start(); var lobby = await fixture.Lobby("ticket-player-a", "login-a");
        var duplicate = new HttpRequestMessage(HttpMethod.Post, "/v1/lobby") { Content = new StringContent("{\"OperationId\":\"x\",\"OperationId\":\"y\"}", Encoding.UTF8, "application/json") }; duplicate.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ticket-player-a");
        using var rejected = await fixture.Client.SendAsync(duplicate); Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var joined = await fixture.Queue("join", lobby, new { InstanceId = fixture.Service.InstanceId, OperationId = "join", ContentVersion = fixture.Service.ContentVersion });
        Assert.Equal("Idle", (await fixture.Queue("cancel", lobby, new { InstanceId = fixture.Service.InstanceId, TicketId = joined.GetProperty("TicketId").GetString() })).GetProperty("State").GetString());
        Assert.Equal("Idle", (await fixture.Queue("join", lobby, new { InstanceId = fixture.Service.InstanceId, OperationId = "join", ContentVersion = fixture.Service.ContentVersion })).GetProperty("State").GetString());
        var stale = await fixture.QueueRaw("status", lobby, new { InstanceId = "old-instance" }); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }
    [Fact]
    public async Task ForeignReauthenticationClosesSocketAndHttpRejectsOriginAndOversize()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var aAccess = await fixture.Session("ticket-player-a", "session-a"); var bAccess = await fixture.Session("ticket-player-b", "session-b");
        await using var socket = await fixture.Socket(aAccess.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await socket.Receive();
        await socket.Send(new { Kind = "Reauthenticate", AccessToken = bAccess.GetProperty("AccessToken").GetString()! });
        var closeBuffer = new byte[256]; using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var close = await socket.Socket.ReceiveAsync(closeBuffer, closeTimeout.Token); Assert.Equal(WebSocketMessageType.Close, close.MessageType); Assert.Equal(4401, (int)close.CloseStatus!);
        using var origin = new HttpRequestMessage(HttpMethod.Post, "/v1/lobby") { Content = new StringContent("{\"OperationId\":\"origin\"}", Encoding.UTF8, "application/json") };
        origin.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ticket-player-a"); origin.Headers.Add("Origin", "https://untrusted.invalid"); using var originResponse = await fixture.Client.SendAsync(origin); Assert.Equal(HttpStatusCode.BadRequest, originResponse.StatusCode);
        using var large = new HttpRequestMessage(HttpMethod.Post, "/v1/lobby") { Content = new StringContent("{" + new string('x', 5000) + "}", Encoding.UTF8, "application/json") };
        large.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "ticket-player-a"); using var largeResponse = await fixture.Client.SendAsync(large); Assert.Contains(largeResponse.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
    }
    [Fact]
    public async Task NegotiatedParallelRefreshHintsThenReauthenticatesWithoutReplacingTheSocket()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var first = await fixture.Session("ticket-player-a", "session-a");
        await using var socket = await fixture.Socket(first.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
        var welcome = await socket.Receive(); Assert.True(welcome.GetProperty("ParallelAccessRefresh").GetBoolean());
        await socket.Send(new { Kind = "EnableParallelRefresh" }); Assert.Equal("ParallelRefreshEnabled", (await socket.Receive()).GetProperty("Kind").GetString());

        var renewed = await fixture.Session("ticket-player-a", "renew-a");
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null });
        var hint = await socket.Receive(); Assert.Equal("AccessRefreshRequired", hint.GetProperty("Kind").GetString()); Assert.Single(hint.EnumerateObject());
        await socket.Send(new { Kind = "Reauthenticate", AccessToken = renewed.GetProperty("AccessToken").GetString()! });
        Assert.Equal("Reauthenticated", (await socket.Receive()).GetProperty("Kind").GetString());
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); Assert.Equal("Snapshot", (await socket.Receive()).GetProperty("Kind").GetString());

        _ = await fixture.Session("ticket-player-a", "renew-b");
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); Assert.Equal("AccessRefreshRequired", (await socket.Receive()).GetProperty("Kind").GetString());
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null });
        await AssertUnauthorizedClose(socket);
    }
    [Fact]
    public async Task NegotiatedParallelRefreshRejectsInvalidReauthenticationToken()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var first = await fixture.Session("ticket-player-a", "session-a");
        await using var socket = await fixture.Socket(first.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await socket.Receive();
        await socket.Send(new { Kind = "EnableParallelRefresh" }); _ = await socket.Receive();
        _ = await fixture.Session("ticket-player-a", "renew-a");
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); Assert.Equal("AccessRefreshRequired", (await socket.Receive()).GetProperty("Kind").GetString());
        await socket.Send(new { Kind = "Reauthenticate", AccessToken = "invalid-access-token" });
        await AssertUnauthorizedClose(socket);
    }
    [Fact]
    public async Task NegotiatedParallelRefreshRejectsExpiredReauthenticationToken()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var first = await fixture.Session("ticket-player-a", "session-a");
        await using var socket = await fixture.Socket(first.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await socket.Receive();
        await socket.Send(new { Kind = "EnableParallelRefresh" }); _ = await socket.Receive();
        fixture.Advance(TimeSpan.FromSeconds(61));
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); Assert.Equal("AccessRefreshRequired", (await socket.Receive()).GetProperty("Kind").GetString());
        var expired = await fixture.Session("ticket-player-a", "renew-a");
        fixture.Advance(TimeSpan.FromSeconds(61));
        await socket.Send(new { Kind = "Reauthenticate", AccessToken = expired.GetProperty("AccessToken").GetString()! });
        await AssertUnauthorizedClose(socket);
    }
    [Fact]
    public async Task LegacySocketWithoutParallelOptInClosesOnRevokedPollToken()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var first = await fixture.Session("ticket-player-a", "session-a");
        await using var socket = await fixture.Socket(first.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await socket.Receive();
        _ = await fixture.Session("ticket-player-a", "renew-a");
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null });
        await AssertUnauthorizedClose(socket);
    }
    [Fact]
    public async Task SocketCommandReplayAfterRenewalReturnsTheOriginalReceiptExactlyOnce()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var first = await fixture.Session("ticket-player-a", "session-a"); var firstToken = first.GetProperty("AccessToken").GetString()!;
        await using var socket = await fixture.Socket(firstToken);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion });
        var welcome = await socket.Receive();
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null });
        var snapshot = (await socket.Receive()).GetProperty("Snapshot");
        var payload = JsonSerializer.Serialize(new { Token = snapshot.GetProperty("ChoiceToken").GetInt64(), OfferIndex = 0 });
        var command = new { MatchId = welcome.GetProperty("MatchId").GetString(), RoundId = snapshot.GetProperty("Round").GetInt32().ToString(), ContentVersion = fixture.Service.ContentVersion, OperationId = "replay-after-abort", Sequence = welcome.GetProperty("NextSequence").GetInt64(), CommandKind = "Choose", Payload = payload };
        await socket.Send(new { Kind = "Command", Command = command });
        await WaitUntil(() => fixture.Service.UseAccess(firstToken, (match, _) => match.Runtime.InspectDecisions().Count == 1));
        var expected = fixture.Service.UseAccess(firstToken, (match, access) => match.Runtime.Execute(access.Caller,
            new CommandEnvelope(command.MatchId!, command.RoundId, command.ContentVersion, command.OperationId, command.Sequence, command.CommandKind, command.Payload)));
        await socket.DisposeAsync();

        var renewed = await fixture.Session("ticket-player-a", "renew-a");
        await using var returned = await fixture.Socket(renewed.GetProperty("AccessToken").GetString()!);
        await returned.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await returned.Receive();
        await returned.Send(new { Kind = "Command", Command = command });
        var replay = await returned.Receive();
        var reply = replay.GetProperty("Reply");
        Assert.Equal("Ack", replay.GetProperty("Kind").GetString());
        Assert.Equal(expected.Accepted, reply.GetProperty("Accepted").GetBoolean());
        Assert.Equal(expected.Code, reply.GetProperty("Code").GetString());
        Assert.Equal(expected.Payload, reply.GetProperty("Payload").GetString());
        Assert.True(fixture.Service.UseAccess(renewed.GetProperty("AccessToken").GetString()!, (match, _) => match.Runtime.InspectDecisions().Count == 1));
    }
    [Fact]
    public async Task DraftAutoSelectsAtTheFiveSecondBoundaryWithoutClientCommand()
    {
        await using var fixture = await Fixture.Start();
        var a = await fixture.Lobby("ticket-player-a", "login-a"); var b = await fixture.Lobby("ticket-player-b", "login-b");
        await fixture.Queue("join", a, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-a", ContentVersion = fixture.Service.ContentVersion });
        await fixture.Queue("join", b, new { InstanceId = fixture.Service.InstanceId, OperationId = "join-b", ContentVersion = fixture.Service.ContentVersion });
        var access = await fixture.Session("ticket-player-a", "session-a");
        var token = access.GetProperty("AccessToken").GetString()!;
        await using var socket = await fixture.Socket(token);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await socket.Receive();
        fixture.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1)); fixture.Service.Pump();
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var before = (await socket.Receive()).GetProperty("Snapshot");
        Assert.Equal("Draft", before.GetProperty("Phase").GetString());
        var beforeChoice = before.GetProperty("ChoiceToken").GetInt64();
        fixture.Advance(TimeSpan.FromTicks(1)); fixture.Service.Pump();
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var after = (await socket.Receive()).GetProperty("Snapshot");
        Assert.NotEqual(beforeChoice, after.GetProperty("ChoiceToken").GetInt64());
        Assert.Equal(2, fixture.Service.UseAccess(token, (match, _) => match.Runtime.InspectDecisions().Count));
        Assert.True(fixture.Service.UseAccess(token, (match, _) => match.Runtime.InspectDecisions().All(decision => decision.Automatic)));
    }
    [Fact]
    public async Task TwoHumanMatchesCompleteLeaveAndDoNotAcceptThePreviousMatchAccess()
    {
        await using var fixture = await Fixture.Start();
        var first = await CreateHumanMatch(fixture, "first");
        var final = await AdvanceToResult(fixture, 120, "first-result");
        await using var aSocket = await fixture.Socket(final.A.GetProperty("AccessToken").GetString()!);
        await using var bSocket = await fixture.Socket(final.B.GetProperty("AccessToken").GetString()!);
        await aSocket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await aSocket.Receive();
        await bSocket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await bSocket.Receive();
        await aSocket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var aFinal = (await aSocket.Receive()).GetProperty("Snapshot");
        await bSocket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var bFinal = (await bSocket.Receive()).GetProperty("Snapshot");
        Assert.Equal("MatchResult", aFinal.GetProperty("Phase").GetString());
        Assert.Equal(aFinal.GetProperty("Results").GetRawText(), bFinal.GetProperty("Results").GetRawText());

        var aLeave = await fixture.Lobby("ticket-player-a", "leave-login-a"); var bLeave = await fixture.Lobby("ticket-player-b", "leave-login-b");
        Assert.Equal("Idle", (await fixture.Queue("leave", aLeave, new { InstanceId = fixture.Service.InstanceId, MatchId = first.MatchId })).GetProperty("State").GetString());
        Assert.Equal("Idle", (await fixture.Queue("leave", bLeave, new { InstanceId = fixture.Service.InstanceId, MatchId = first.MatchId })).GetProperty("State").GetString());
        var second = await CreateHumanMatch(fixture, "second", aLeave, bLeave);
        Assert.NotEqual(first.MatchId, second.MatchId);
        Assert.Null(await fixture.SocketFailure(final.A.GetProperty("AccessToken").GetString()!));
        var current = await fixture.Session("ticket-player-a", "second-session-a");
        Assert.Equal(second.MatchId, current.GetProperty("MatchId").GetString());
    }
    [Fact]
    public async Task BothOfflineSocketsProgressAutomaticallyAndReturnTheSameFinalResult()
    {
        await using var fixture = await Fixture.Start();
        _ = await CreateHumanMatch(fixture, "offline");
        var a = await fixture.Session("ticket-player-a", "offline-session-a"); var b = await fixture.Session("ticket-player-b", "offline-session-b");
        await using (var aSocket = await fixture.Socket(a.GetProperty("AccessToken").GetString()!))
        await using (var bSocket = await fixture.Socket(b.GetProperty("AccessToken").GetString()!))
        {
            await aSocket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await aSocket.Receive();
            await bSocket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await bSocket.Receive();
        }
        var final = await AdvanceToResult(fixture, 120, "offline-result");
        await using var returnedA = await fixture.Socket(final.A.GetProperty("AccessToken").GetString()!);
        await using var returnedB = await fixture.Socket(final.B.GetProperty("AccessToken").GetString()!);
        await returnedA.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await returnedA.Receive();
        await returnedB.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); _ = await returnedB.Receive();
        await returnedA.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var aFinal = (await returnedA.Receive()).GetProperty("Snapshot");
        await returnedB.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var bFinal = (await returnedB.Receive()).GetProperty("Snapshot");
        Assert.Equal("MatchResult", aFinal.GetProperty("Phase").GetString()); Assert.Equal("MatchResult", bFinal.GetProperty("Phase").GetString());
        foreach (var name in new[] { "Wins0", "Wins1", "LastWinner", "Results" }) Assert.Equal(aFinal.GetProperty(name).GetRawText(), bFinal.GetProperty(name).GetRawText());
        Assert.True(fixture.Service.UseAccess(final.A.GetProperty("AccessToken").GetString()!, (match, _) => match.Runtime.InspectDecisions().Count >= 2));
        Assert.True(fixture.Service.UseAccess(final.A.GetProperty("AccessToken").GetString()!, (match, _) => match.Runtime.InspectDecisions().All(decision => decision.Automatic)));
    }
    [Fact]
    public async Task ChangedReplayPayloadAndStaleDraftChoiceAreRejected()
    {
        await using var fixture = await Fixture.Start();
        _ = await CreateHumanMatch(fixture, "reject");
        var access = await fixture.Session("ticket-player-a", "reject-session-a");
        await using var socket = await fixture.Socket(access.GetProperty("AccessToken").GetString()!);
        await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = fixture.Service.ContentVersion }); var welcome = await socket.Receive();
        await socket.Send(new { Kind = "Poll", AfterEventSequence = (long?)null }); var snapshot = (await socket.Receive()).GetProperty("Snapshot");
        var token = snapshot.GetProperty("ChoiceToken").GetInt64();
        var command = new { MatchId = welcome.GetProperty("MatchId").GetString(), RoundId = snapshot.GetProperty("Round").GetInt32().ToString(), ContentVersion = fixture.Service.ContentVersion, OperationId = "payload-conflict", Sequence = welcome.GetProperty("NextSequence").GetInt64(), CommandKind = "Choose", Payload = JsonSerializer.Serialize(new { Token = token, OfferIndex = 0 }) };
        await socket.Send(new { Kind = "Command", Command = command }); Assert.True((await socket.Receive()).GetProperty("Reply").GetProperty("Accepted").GetBoolean());
        var conflict = new { command.MatchId, command.RoundId, command.ContentVersion, command.OperationId, command.Sequence, command.CommandKind, Payload = JsonSerializer.Serialize(new { Token = token, OfferIndex = 1 }) };
        await socket.Send(new { Kind = "Command", Command = conflict }); Assert.Equal("OperationConflict", (await socket.Receive()).GetProperty("Reply").GetProperty("Code").GetString());
        fixture.Advance(TimeSpan.FromSeconds(5)); fixture.Service.Pump();
        var stale = new { command.MatchId, command.RoundId, command.ContentVersion, OperationId = "stale-choice", Sequence = command.Sequence + 1, command.CommandKind, command.Payload };
        await socket.Send(new { Kind = "Command", Command = stale }); Assert.False((await socket.Receive()).GetProperty("Reply").GetProperty("Accepted").GetBoolean());
    }
    [Fact]
    public async Task PublicHostWithoutNativeTlsFailsClosed()
    {
        await using var fixture = await Fixture.Start();
        Assert.Throws<InvalidOperationException>(() => RemoteWebHost.Create(fixture.Service, false));
    }

    static async Task AssertUnauthorizedClose(WireSocket socket)
    {
        var closeBuffer = new byte[256]; using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var close = await socket.Socket.ReceiveAsync(closeBuffer, closeTimeout.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType); Assert.Equal(4401, (int)close.CloseStatus!);
    }
    static async Task WaitUntil(Func<bool> complete)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (complete()) return;
            await Task.Delay(10);
        }
        Assert.True(complete(), "Server did not process the sent command.");
    }
    static async Task<(string ALobby, string BLobby, string MatchId)> CreateHumanMatch(Fixture fixture, string prefix, string? aLobby = null, string? bLobby = null)
    {
        aLobby ??= await fixture.Lobby("ticket-player-a", prefix + "-login-a");
        bLobby ??= await fixture.Lobby("ticket-player-b", prefix + "-login-b");
        Assert.Equal("Searching", (await fixture.Queue("join", aLobby, new { InstanceId = fixture.Service.InstanceId, OperationId = prefix + "-join-a", ContentVersion = fixture.Service.ContentVersion })).GetProperty("State").GetString());
        var matched = await fixture.Queue("join", bLobby, new { InstanceId = fixture.Service.InstanceId, OperationId = prefix + "-join-b", ContentVersion = fixture.Service.ContentVersion });
        Assert.Equal("Matched", matched.GetProperty("State").GetString());
        return (aLobby, bLobby, matched.GetProperty("MatchId").GetString()!);
    }
    static async Task<(JsonElement A, JsonElement B)> AdvanceToResult(Fixture fixture, int maximumSteps, string prefix)
    {
        for (var step = 0; step < maximumSteps; step++)
        {
            fixture.Advance(TimeSpan.FromSeconds(20));
            for (var settle = 0; settle < 128; settle++) fixture.Service.Pump();
            var a = await fixture.Session("ticket-player-a", prefix + "-a-" + step);
            var b = await fixture.Session("ticket-player-b", prefix + "-b-" + step);
            var complete = fixture.Service.UseAccess(a.GetProperty("AccessToken").GetString()!, (match, _) => match.Domain.Phase.ToString() == "MatchResult");
            if (complete) return (a, b);
        }
        throw new Xunit.Sdk.XunitException("Match did not reach its result under the virtual watchdog.");
    }

    // In-process callback stand-in only: this test does not connect to Photon or prove encryption.
    sealed class FusionLoopbackClient : IDisposable
    {
        readonly TankDraft.Infrastructure.FusionTransport.FusionAuthorityPeer peer;
        readonly TankDraft.Infrastructure.FusionTransport.FusionRequestChannel channel;
        readonly TankDraft.Infrastructure.FusionTransport.FusionAuthorityClient client;
        Task delivery = Task.CompletedTask;
        public FusionLoopbackClient(Fixture fixture, string account)
        {
            peer = new(fixture.Client.BaseAddress!, new string('A', 64), account);
            channel = new((id, bytes) => delivery = Deliver(id, bytes), () => true, TimeSpan.FromSeconds(3));
            client = new(channel);
        }
        async Task Deliver(int id, byte[] bytes)
        {
            try { channel.Receive(id, await peer.ExecuteAsync(bytes, CancellationToken.None)); }
            catch { channel.Receive(id, Encoding.UTF8.GetBytes("{\"Status\":503,\"Body\":{}}")); throw; }
        }
        public async Task<Newtonsoft.Json.Linq.JObject> Send(string operation, string authorization, object body)
        {
            try { return await client.ExchangeAsync(operation, authorization, Newtonsoft.Json.Linq.JObject.FromObject(body), CancellationToken.None); }
            finally { await delivery; }
        }
        public void Dispose() { channel.Dispose(); peer.Dispose(); }
    }
    sealed class QaLoopbackClient : IDisposable
    {
        readonly TankDraft.Infrastructure.FusionTransport.FusionQaAuthorityPeer peer;
        readonly TankDraft.Infrastructure.FusionTransport.FusionRequestChannel channel;
        readonly TankDraft.Infrastructure.FusionTransport.FusionQaClient client;
        readonly List<string> frames;
        Task delivery = Task.CompletedTask;
        public QaLoopbackClient(Fixture fixture, string account, List<string> wireFrames)
        {
            frames = wireFrames; peer = new(fixture.Client.BaseAddress!, new string('A', 64), account);
            channel = new((id, bytes) => delivery = Deliver(id, bytes), () => false, TimeSpan.FromSeconds(3), allowPlaintextQa: true);
            client = new(channel);
        }
        async Task Deliver(int id, byte[] bytes)
        {
            try
            {
                frames.Add(Encoding.UTF8.GetString(bytes));
                var response = await peer.ExecuteAsync(bytes, CancellationToken.None);
                frames.Add(Encoding.UTF8.GetString(response)); channel.Receive(id, response);
            }
            catch { channel.Receive(id, Encoding.UTF8.GetBytes("{\"Status\":503,\"Body\":{}}")); throw; }
        }
        public async Task<Newtonsoft.Json.Linq.JObject> Send(string operation, object body)
        {
            try { return await client.ExchangeAsync(operation, Newtonsoft.Json.Linq.JObject.FromObject(body), CancellationToken.None); }
            finally { await delivery; }
        }
        public void Dispose() { channel.Dispose(); peer.Dispose(); }
    }
    sealed class Fixture : IAsyncDisposable
    {
        readonly WebApplication app; readonly Clock clock; public HttpClient Client { get; } public RemoteMatchService Service { get; }
        Fixture(WebApplication a, RemoteMatchService s, HttpClient c, Clock time) => (app, Service, Client, clock) = (a,s,c,time);
        public static async Task<Fixture> Start(bool gateway = false, bool photonQa = false)
        {
            var clock = new Clock(); var (content, version) = AuthoredContent.Load();
            var identity = new PlayFabIdentityAdapter(new PlayFabIdentityOptions("B16D9", "injected-test-only", 4), request => Task.FromResult(new PlayFabResult<AuthenticateSessionTicketResult> { Result = new AuthenticateSessionTicketResult { IsSessionTicketExpired = false, UserInfo = new UserAccountInfo { PlayFabId = request.SessionTicket switch { "ticket-player-a" => "acctA", "ticket-player-b" => "acctB", _ => "otherA" } } } }), () => clock.GetUtcNow());
            var service = new RemoteMatchService(content, version, identity, new RemoteHostSettings(new HashSet<string>(["acctA", "acctB"])), clock); var app = RemoteWebHost.Create(service, true, gatewayKey: gateway ? new string('A', 64) : null, allowPhotonQa: photonQa); await app.StartAsync();
            var client = new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = new Uri(app.Urls.Single()), Timeout = TimeSpan.FromSeconds(5) };
            if (gateway) { client.DefaultRequestHeaders.Add("X-TankDraft-Gateway", new string('A', 64)); client.DefaultRequestHeaders.Add("X-TankDraft-Player", "acctA"); }
            return new Fixture(app, service, client, clock);
        }
        public async Task<string> Lobby(string ticket, string op) { using var r = await Post("/v1/lobby", ticket, new { OperationId = op }); r.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return json.RootElement.GetProperty("LobbyToken").GetString()!; }
        public async Task<JsonElement> Queue(string operation, string lobby, object body) { using var r = await QueueRaw(operation, lobby, body); r.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return json.RootElement.Clone(); }
        public Task<HttpResponseMessage> QueueRaw(string operation, string lobby, object body) => Post("/v1/queue/" + operation, lobby, body);
        public async Task<JsonElement> Session(string ticket, string operation) { using var r = await Post("/v1/session", ticket, new { ContentVersion = Service.ContentVersion, OperationId = operation }); r.EnsureSuccessStatusCode(); using var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()); return json.RootElement.Clone(); }
        public void Advance(TimeSpan value) => clock.Advance(value);
        async Task<HttpResponseMessage> Post(string path, string token, object value) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json") }; request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token); return await Client.SendAsync(request); }
        public async Task<WireSocket> Socket(string token) { var socket = new ClientWebSocket(); socket.Options.SetRequestHeader("Authorization", "Bearer " + token); var endpoint = new UriBuilder(new Uri(Client.BaseAddress!, "/v1/socket")) { Scheme = "ws" }.Uri; await socket.ConnectAsync(endpoint, CancellationToken.None); return new WireSocket(socket); }
        public async Task<ClientWebSocket?> SocketFailure(string token) { try { var socket = await Socket(token); await socket.Send(new { Kind = "Hello", Protocol = "tankdraft-server-v2", ContentVersion = Service.ContentVersion }); await socket.Receive(); return socket.Socket; } catch (WebSocketException) { return null; } catch (OperationCanceledException) { return null; } catch (JsonException) { return null; } }
        public async ValueTask DisposeAsync() { Client.Dispose(); await app.StopAsync(); await app.DisposeAsync(); Service.Dispose(); }
    }
    sealed class WireSocket(ClientWebSocket socket) : IAsyncDisposable
    {
        const int MaximumMessageBytes = 1024 * 1024;
        public ClientWebSocket Socket { get; } = socket;
        public Task Send(object value) => Socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(value), WebSocketMessageType.Text, true, CancellationToken.None);
        public async Task<JsonElement> Receive()
        {
            var buffer = new byte[16 * 1024];
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var bytes = new MemoryStream();
            WebSocketReceiveResult result;
            do
            {
                result = await Socket.ReceiveAsync(buffer, timeout.Token);
                if (result.MessageType != WebSocketMessageType.Text || bytes.Length + result.Count > MaximumMessageBytes)
                    throw new WebSocketException("Unexpected server message.");
                bytes.Write(buffer, 0, result.Count);
            } while (!result.EndOfMessage);
            using var json = JsonDocument.Parse(bytes.ToArray());
            return json.RootElement.Clone();
        }
        public ValueTask DisposeAsync() { Socket.Dispose(); return ValueTask.CompletedTask; }
    }
    sealed class Clock : TimeProvider
    {
        readonly DateTimeOffset started = DateTimeOffset.UtcNow;
        long ticks = 1;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref ticks);
        public override DateTimeOffset GetUtcNow() => started.AddTicks(Interlocked.Read(ref ticks));
        public void Advance(TimeSpan value) => Interlocked.Add(ref ticks, value.Ticks);
    }
}
