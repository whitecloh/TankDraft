using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using TankDraft.Server.Match;
using TankDraft.Server.Security;

namespace TankDraft.LocalHost;

internal static partial class LocalTlsHost
{
    // Separate host: the baseline verifier advances its clock past access expiry.
    private static async Task VerifyExtendedAsync(LocalTlsSettings settings, AuthoredContent content, string version)
    {
        var clock = new LocalVerificationClock();
        var families = new LocalAccessFamilies(4, clock);
        var grantA = families.Create("qa-player-0", LocalMatchEndpoint.MatchId, "0", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        var grantB = families.Create("qa-player-1", LocalMatchEndpoint.MatchId, "1", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        var crossMatchGrant = families.Create("qa-player-0", "other-match", "0", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        var crossSideGrant = families.Create("qa-player-0", LocalMatchEndpoint.MatchId, "1", TimeSpan.FromSeconds(settings.GrantTtlSeconds));
        using var endpoint = new LocalMatchEndpoint(content, version, HostSettings.Load(), clock);
        using var certificate = CreateCertificate(out var pin);
        await using var app = await StartAsync(settings, endpoint, families, version, certificate, CancellationToken.None);
        try
        {
            var first = Issue(families, grantA, settings);
            using var original = await ConnectAndHelloAsync(first.AccessToken, pin, version, settings);
            await ExpectConnectStatusAsync(first.AccessToken, pin, 409, "duplicate active stream rejected before upgrade");
            var before = await PollSnapshotAsync(original, settings);
            var choose = new CommandEnvelope(before.MatchId, before.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), before.ContentVersion,
                "lost-ack", 1, "Choose", $"{{\"Token\":{before.ChoiceToken},\"OfferIndex\":0}}");
            await SendCommandAsync(original, choose);
            var committedBeforeAbort = await WaitForCommittedAsync(endpoint, families, first.AccessToken, settings);
            original.Abort();
            Ensure(families.InvalidateAccess(grantA.Value), "access invalidated for restart");
            var second = Issue(families, grantA, settings);
            Ensure(second.Generation == first.Generation + 1 && second.StreamId == first.StreamId, "generation advances while stream remains stable");
            using var retried = await ConnectAndHelloAsync(second.AccessToken, pin, version, settings);
            await SendCommandAsync(retried, choose);
            var retry = ReadAck(await Receive(retried, settings));
            Ensure(retry.Accepted, "lost ACK retry parsed as accepted");
            var capturedAfterRetry = families.UseAccess(second.AccessToken, x => endpoint.Capture(x.Caller, null));
            Ensure(capturedAfterRetry.Committed && capturedAfterRetry.Revision == committedBeforeAbort.Revision && capturedAfterRetry.Army.SequenceEqual(committedBeforeAbort.Army), "retry did not mutate committed state");
            endpoint.RestartForVerification();
            var next = families.UseAccess(second.AccessToken, x => endpoint.NextCommandSequence(x.Caller));
            Ensure(next == 2, "next sequence survives durable restart");
            var opponentAccess = Issue(families, grantB, settings);
            using (var opponent = await ConnectAndHelloAsync(opponentAccess.AccessToken, pin, version, settings))
            {
                var opponentSnapshot = await PollSnapshotAsync(opponent, settings);
                var opponentChoose = new CommandEnvelope(opponentSnapshot.MatchId, opponentSnapshot.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), opponentSnapshot.ContentVersion,
                    "opponent-round-one", 1, "Choose", $"{{\"Token\":{opponentSnapshot.ChoiceToken},\"OfferIndex\":0}}");
                await SendCommandAsync(opponent, opponentChoose);
                Ensure(ReadAck(await Receive(opponent, settings)).Accepted, "opponent initial choice accepted");
            }
            ServerMatchSnapshot nextRound = default!;
            for (var attempt = 0; attempt < 100; attempt++)
            {
                clock.Advance(TimeSpan.FromSeconds(5));
                await Task.Delay(25);
                var timelineAccess = Issue(families, grantA, settings);
                var observed = families.UseAccess(timelineAccess.AccessToken, x => endpoint.Capture(x.Caller, null));
                if (observed.Round > before.Round && observed.Phase == "Draft") { nextRound = observed; break; }
            }
            if (nextRound is null) throw new InvalidOperationException("TLS QA failed: next draft round available for pending sequence");
            var pendingGrant = nextRound.BonusSide == 1 ? grantB : grantA;
            var pendingChoose = new CommandEnvelope(nextRound.MatchId, nextRound.Round.ToString(System.Globalization.CultureInfo.InvariantCulture), nextRound.ContentVersion,
                "pending-round-two", 2, "Choose", $"{{\"Token\":{nextRound.ChoiceToken},\"OfferIndex\":0}}");
            retried.Abort();
            Ensure(families.InvalidateAccess(pendingGrant.Value), "pending access invalidated");
            var third = Issue(families, pendingGrant, settings);
            using var pending = await ConnectAndHelloAsync(third.AccessToken, pin, version, settings);
            await SendCommandAsync(pending, pendingChoose);
            var pendingAck = ReadAck(await Receive(pending, settings));
            Ensure(pendingAck.Accepted, "uncommitted sequence two accepted once after refresh");

            var malformedGrant = pendingGrant == grantB ? grantA : grantB;
            var malformed = Issue(families, malformedGrant, settings);
            using (var malformedSocket = await ConnectAndHelloAsync(malformed.AccessToken, pin, version, settings))
            {
                await malformedSocket.SendAsync(Encoding.UTF8.GetBytes("{\"Kind\":\"Command\",\"Command\":{\"MatchId\":\"x\",\"MatchId\":\"x\"}}"), WebSocketMessageType.Text, true, CancellationToken.None);
                await ExpectCloseStatusAsync(malformedSocket, WebSocketCloseStatus.InvalidPayloadData, settings, "duplicate command fields rejected");
            }
            await Task.Delay(50);
            var cursor = Issue(families, malformedGrant, settings);
            using (var cursorSocket = await ConnectAndHelloAsync(cursor.AccessToken, pin, version, settings))
            {
                await Send(cursorSocket, new { Kind = "Poll", AfterEventSequence = "not-a-number" });
                await ExpectCloseStatusAsync(cursorSocket, WebSocketCloseStatus.InvalidPayloadData, settings, "string cursor rejected");
            }
            await Task.Delay(50);

            clock.Advance(TimeSpan.FromSeconds(settings.AccessTtlSeconds + 1));
            await Send(pending, new { Kind = "Poll", AfterEventSequence = (long?)null });
            await ExpectCloseStatusAsync(pending, (WebSocketCloseStatus)4401, settings, "expired access closes 4401");
            await ExpectRenewalBindingRejectedAsync(grantA, grantB, "cross-account renewal rejected");
            await ExpectRenewalBindingRejectedAsync(grantA, crossMatchGrant, "cross-match renewal rejected");
            await ExpectRenewalBindingRejectedAsync(grantA, crossSideGrant, "cross-side renewal rejected");
            var revokedAccess = Issue(families, malformedGrant, settings);
            Ensure(families.Revoke(malformedGrant), "family revoked");
            await ExpectConnectStatusAsync(revokedAccess.AccessToken, pin, 401, "revoked access rejected before upgrade");
            Console.WriteLine("PASS local TLS extended: active stream, durable lost ACK retry, generation, sequence restart, malformed messages, expiry and revoke");
        }
        finally { endpoint.Flush(); families.Revoke(grantA); families.Revoke(grantB); families.Revoke(crossMatchGrant); families.Revoke(crossSideGrant); await app.StopAsync(); }

        async Task ExpectRenewalBindingRejectedAsync(LocalGrant sourceGrant, LocalGrant renewedGrant, string name)
        {
            var source = Issue(families, sourceGrant, settings);
            using var socket = await ConnectAndHelloAsync(source.AccessToken, pin, version, settings);
            var renewed = Issue(families, renewedGrant, settings);
            await Send(socket, new { Kind = "Reauthenticate", AccessToken = renewed.AccessToken });
            await ExpectCloseStatusAsync(socket, (WebSocketCloseStatus)4401, settings, name);
            await Task.Delay(25);
        }
    }

    private static LocalAccessIssue Issue(LocalAccessFamilies families, LocalGrant grant, LocalTlsSettings settings) =>
        families.Issue(grant.Value, TimeSpan.FromSeconds(settings.AccessTtlSeconds), TimeSpan.FromSeconds(settings.RefreshMarginSeconds))
        ?? throw new InvalidOperationException("TLS QA access issue failed.");

    private static async Task<ClientWebSocket> ConnectAndHelloAsync(string token, string pin, string version, LocalTlsSettings settings)
    {
        var socket = await ConnectAsync(token, pin);
        await Send(socket, new { Kind = "Hello", Protocol, ContentVersion = version });
        using var doc = JsonDocument.Parse(await Receive(socket, settings));
        Ensure(doc.RootElement.GetProperty("Kind").GetString() == "Welcome", "TLS welcome");
        return socket;
    }

    private static async Task<ServerMatchSnapshot> PollSnapshotAsync(ClientWebSocket socket, LocalTlsSettings settings)
    {
        await Send(socket, new { Kind = "Poll", AfterEventSequence = (long?)null });
        using var doc = JsonDocument.Parse(await Receive(socket, settings));
        return doc.RootElement.GetProperty("Snapshot").Deserialize<ServerMatchSnapshot>(AuthoredContent.Json) ?? throw new InvalidOperationException("TLS snapshot missing.");
    }

    private static Task SendCommandAsync(ClientWebSocket socket, CommandEnvelope command) => Send(socket, new { Kind = "Command", Command = command });

    private static CommandReply ReadAck(string text)
    {
        using var doc = JsonDocument.Parse(text);
        Ensure(doc.RootElement.GetProperty("Kind").GetString() == "Ack", "TLS ACK kind");
        return doc.RootElement.GetProperty("Reply").Deserialize<CommandReply>(AuthoredContent.Json) ?? throw new InvalidOperationException("TLS ACK reply missing.");
    }

    private static async Task<ServerMatchSnapshot> WaitForCommittedAsync(LocalMatchEndpoint endpoint, LocalAccessFamilies families, string token, LocalTlsSettings settings)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var snapshot = families.UseAccess(token, x => endpoint.Capture(x.Caller, null));
            if (snapshot.Committed) return snapshot;
            await Task.Delay(settings.TimeoutSeconds * 10);
        }
        throw new InvalidOperationException("TLS QA lost-ACK command did not commit.");
    }

    private static async Task ExpectConnectStatusAsync(string token, string pin, int expected, string name)
    {
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Authorization", "Bearer " + token);
        socket.Options.RemoteCertificateValidationCallback = (_, certificate, _, errors) => certificate is not null && errors == System.Net.Security.SslPolicyErrors.RemoteCertificateChainErrors && Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(certificate.Export(System.Security.Cryptography.X509Certificates.X509ContentType.Cert))) == pin;
        try { using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5)); await socket.ConnectAsync(new Uri("wss://127.0.0.1:18783" + SocketPath), timeout.Token); }
        catch (WebSocketException error) when (error.Message.Contains(expected.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)) { return; }
        catch (WebSocketException error) when (error.InnerException is System.Net.Http.HttpRequestException request && (int?)request.StatusCode == expected) { return; }
        throw new InvalidOperationException("TLS QA expected HTTP " + expected + ": " + name);
    }

    private static async Task ExpectCloseStatusAsync(ClientWebSocket socket, WebSocketCloseStatus expected, LocalTlsSettings settings, string name)
    {
        var buffer = new byte[256];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(settings.TimeoutSeconds));
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        Ensure(result.MessageType == WebSocketMessageType.Close && result.CloseStatus == expected, name);
    }
}
