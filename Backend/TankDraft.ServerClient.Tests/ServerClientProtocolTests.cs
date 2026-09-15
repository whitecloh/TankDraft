using System.Reflection;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using TankDraft.Match.ServerClient;
using TankDraft.Server.Match;
using Xunit;

namespace TankDraft.ServerClient.Tests;

public sealed class ServerClientProtocolTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "TankDraftServerClientTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public void Journal_persists_the_exact_pending_envelope_after_reopen()
    {
        var original = Command("op-1", 1);
        var journal = new ClientIntentJournal(JournalPath);

        journal.Begin(original);

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal(0, reopened.Sequence);
        Assert.NotNull(reopened.Pending);
        Assert.True(JToken.DeepEquals(original, reopened.Pending));
    }

    [Fact]
    public void Lost_ack_keeps_the_same_pending_intent_after_reopen()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.Begin(Command("op-lost", 1));

        // The transport can lose the response after the server has committed it; no local ACK is written.
        var afterLostAck = new ClientIntentJournal(JournalPath);

        Assert.Equal("op-lost", afterLostAck.Pending!.Value<string>("OperationId"));
        Assert.Equal(1, afterLostAck.Pending.Value<long>("Sequence"));
    }

    [Fact]
    public void Accepted_ack_clears_pending_and_next_sequence_survives_restart()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.Begin(Command("op-1", 1));
        journal.Acknowledge("op-1", "Accepted");

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal(1, reopened.Sequence);
        Assert.Null(reopened.Pending);
        reopened.Begin(Command("op-2", 2));

        var withSecondIntent = new ClientIntentJournal(JournalPath);
        Assert.Equal(1, withSecondIntent.Sequence);
        Assert.Equal(2, withSecondIntent.Pending!.Value<long>("Sequence"));
    }

    [Fact]
    public void Catching_up_keeps_pending_sequence_for_retry()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.Begin(Command("op-catchup", 1));

        journal.Acknowledge("op-catchup", "CatchingUp");

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal(0, reopened.Sequence);
        Assert.Equal(1, reopened.Pending!.Value<long>("Sequence"));
    }

    [Fact]
    public void Protocol_hard_rejection_keeps_intent_and_fails_closed()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.Begin(Command("op-reject", 1));

        var error = Assert.Throws<InvalidDataException>(() => journal.Acknowledge("op-reject", "MalformedCommand"));

        Assert.Contains("rejected", error.Message, StringComparison.OrdinalIgnoreCase);
        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal("op-reject", reopened.Pending!.Value<string>("OperationId"));
    }

    [Fact]
    public void Mismatched_operation_ack_does_not_clear_pending_intent()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.Begin(Command("op-expected", 1));

        Assert.Throws<InvalidDataException>(() => journal.Acknowledge("op-other", "Accepted"));

        Assert.Equal("op-expected", new ClientIntentJournal(JournalPath).Pending!.Value<string>("OperationId"));
    }

    [Fact]
    public void Corrupted_journal_is_rejected()
    {
        Directory.CreateDirectory(_directory);
        File.WriteAllText(JournalPath, "{\"Sequence\":0,\"Pending\":");

        Assert.ThrowsAny<Exception>(() => _ = new ClientIntentJournal(JournalPath));
    }

    [Fact]
    public void Actual_server_snapshot_json_preserves_battle_values_and_global_event_sequence()
    {
        var snapshot = FixtureSnapshot();
        var options = new JsonSerializerOptions();
        ServerMatchJson.Register(options);
        var frame = new ServerFrame(ServerWire.Parse(JsonSerializer.Serialize(snapshot, options)));

        Assert.Equal(77, frame.EventSequence);
        Assert.Equal(3, frame.Entities.Count);
        Assert.Equal(new[] { BattleEntityKind.Unit, BattleEntityKind.Projectile, BattleEntityKind.Zone }, frame.Entities.Select(entity => entity.Kind));
        Assert.Equal(new BattleVec(1.25f, -2.5f).X, frame.Entities[0].Position.X);
        Assert.Equal(71, frame.Entities[0].Hp);
        Assert.Equal("shell", frame.Entities[1].DefinitionId);
        Assert.Equal("fire-zone", frame.Entities[2].DefinitionId);
        Assert.Single(frame.Events);
        Assert.Equal(701, frame.Events[0].Sequence);
        Assert.Equal(42, frame.Events[0].Tick);
        Assert.Equal(BattleEventKind.ZoneTick, frame.Events[0].Kind);
    }

    [Theory]
    [InlineData("{\"MatchId\":\"a\",\"MatchId\":\"b\"}")]
    [InlineData("{\"MatchId\":\"a\"} trailing")]
    public void Wire_rejects_duplicate_and_trailing_json(string json) =>
        Assert.ThrowsAny<Newtonsoft.Json.JsonException>(() => ServerWire.Parse(json));

    [Fact]
    public void Outgoing_command_contains_only_client_intent_not_authority_state()
    {
        var credentials = new TestCredentials(_directory);
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v1", "content-v1", 100, 1000, 5000, 1024, 8);
        typeof(ServerClientTransport).GetField("_connected", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transport, true);

        transport.Submit(new ServerFrame(ServerWire.Parse(SnapshotJson())), "Choose", 2);

        var pending = new ClientIntentJournal(Path.Combine(_directory, "intent-0.json")).Pending!;
        Assert.Equal(new[] { "CommandKind", "ContentVersion", "MatchId", "OperationId", "Payload", "RoundId", "Sequence" }, pending.Properties().Select(property => property.Name).OrderBy(name => name));
        var payload = ServerWire.Parse(pending.Value<string>("Payload")!);
        Assert.Equal(new[] { "OfferIndex", "Token" }, payload.Properties().Select(property => property.Name).OrderBy(name => name));
        Assert.Null(pending["Winner"]);
        Assert.Null(pending["Army"]);
        Assert.Null(pending["Events"]);
    }

    [Fact]
    public void Pinned_self_signed_leaf_allows_only_the_exact_leaf_with_untrusted_root()
    {
        using var certificate = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var wrongCertificate = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var chain = BuildChain(certificate);

        Assert.True(HasOnlyUntrustedRoot(chain));
        Assert.True(LocalTlsPin.Validate(Pin(certificate), certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
        Assert.False(LocalTlsPin.Validate(Pin(wrongCertificate), certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
    }

    [Fact]
    public void Tls_pin_rejects_missing_certificate_and_hostname_mismatch()
    {
        using var certificate = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var chain = BuildChain(certificate);

        Assert.False(LocalTlsPin.Validate(Pin(certificate), null!, chain, SslPolicyErrors.RemoteCertificateNotAvailable, DateTime.UtcNow));
        Assert.False(LocalTlsPin.Validate(Pin(certificate), certificate, chain, SslPolicyErrors.RemoteCertificateNameMismatch, DateTime.UtcNow));
    }

    [Fact]
    public void Tls_pin_rejects_expired_and_not_yet_valid_leafs()
    {
        using var expired = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1));
        using var future = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(1), DateTimeOffset.UtcNow.AddMinutes(2));
        using var expiredChain = BuildChain(expired);
        using var futureChain = BuildChain(future);

        Assert.False(LocalTlsPin.Validate(Pin(expired), expired, expiredChain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
        Assert.False(LocalTlsPin.Validate(Pin(future), future, futureChain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
    }

    [Fact]
    public void Tls_pin_rejects_non_untrusted_root_chain_flags()
    {
        using var expired = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-2), DateTimeOffset.UtcNow.AddMinutes(-1));
        using var chain = BuildChain(expired);

        Assert.Contains(chain.ChainStatus, status => (status.Status & X509ChainStatusFlags.NotTimeValid) != 0);
        Assert.False(LocalTlsPin.Validate(Pin(expired), expired, chain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
    }

    [Fact]
    public void Tls_pin_rejects_revoked_chain_flag()
    {
        using var certificate = CreateCertificate(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        using var chain = BuildChain(certificate);
        typeof(X509Chain).GetField("_lazyChainStatus", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(chain,
            new[] { new X509ChainStatus { Status = X509ChainStatusFlags.Revoked } });

        Assert.False(LocalTlsPin.Validate(Pin(certificate), certificate, chain, SslPolicyErrors.RemoteCertificateChainErrors, DateTime.UtcNow));
    }

    [Fact]
    public void Local_tls_socket_accepts_only_a_complete_valid_websocket_upgrade()
    {
        const string key = "dGhlIHNhbXBsZSBub25jZQ==";
        const string response = "HTTP/1.1 101 Switching Protocols\r\nConnection: keep-alive, Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n\r\n";

        LocalTlsSocket.ValidateUpgradeResponse(response, key);
    }

    [Theory]
    [InlineData("HTTP/1.1 200 OK\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n\r\n")]
    [InlineData("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: wrong\r\n\r\n")]
    [InlineData("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\n\r\n")]
    [InlineData("HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=\r\nSec-WebSocket-Extensions: permessage-deflate\r\n\r\n")]
    public void Local_tls_socket_rejects_malformed_upgrade_responses(string response)
    {
        Assert.Throws<InvalidDataException>(() => LocalTlsSocket.ValidateUpgradeResponse(response, "dGhlIHNhbXBsZSBub25jZQ=="));
    }

    [Theory]
    [InlineData("HTTP/1.1 401 Unauthorized\r\n\r\n")]
    [InlineData("HTTP/1.1 409 Conflict\r\n\r\n")]
    [InlineData("HTTP/1.1 429 Too Many Requests\r\n\r\n")]
    [InlineData("HTTP/1.1 503 Service Unavailable\r\n\r\n")]
    public void Local_tls_socket_treats_retryable_upgrade_statuses_as_transient(string response)
    {
        Assert.Throws<WebSocketException>(() => LocalTlsSocket.ValidateUpgradeResponse(response, "dGhlIHNhbXBsZSBub25jZQ=="));
    }

    [Fact]
    public void Match_access_rejects_invalid_fields_and_refresh_times()
    {
        Assert.Throws<InvalidDataException>(() => new MatchAccess("", "session", "stream", "match", 1, 30));
        Assert.Throws<InvalidDataException>(() => new MatchAccess("token", "session", "stream", "match", 0, 30));
        Assert.Throws<InvalidDataException>(() => new MatchAccess("token", "session", "stream", "match", 1, 0));
        Assert.Throws<InvalidDataException>(() => new MatchAccess("token", "session", "stream", "match", 1, double.NaN));
    }

    [Fact]
    public void Journal_binds_new_stream_before_first_intent()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-1", 1));

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal("stream-1", reopened.StreamId);
        Assert.True(JToken.DeepEquals(Command("op-1", 1), reopened.Pending));
    }

    [Fact]
    public void Fresh_journal_adopts_existing_server_stream_sequence_before_its_first_intent()
    {
        var journal = new ClientIntentJournal(JournalPath);

        journal.BindStream("stream-1", 9);

        Assert.Equal(8, journal.Sequence);
        journal.Begin(Command("op-9", 9));
        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal("stream-1", reopened.StreamId);
        Assert.Equal(8, reopened.Sequence);
        Assert.Equal(9, reopened.Pending!.Value<long>("Sequence"));
    }

    [Fact]
    public void Acknowledged_journal_adopts_a_monotonically_advanced_server_sequence_and_survives_reopen()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-1", 1));
        journal.Acknowledge("op-1", "Accepted");

        journal.BindStream("stream-1", 12);

        Assert.Equal(11, journal.Sequence);
        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal(11, reopened.Sequence);
        reopened.Begin(Command("op-12", 12));
        Assert.Equal(12, reopened.Pending!.Value<long>("Sequence"));
    }

    [Fact]
    public void Acknowledged_journal_rejects_rewind_and_stream_mismatch_without_rewriting_disk()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 5);
        var before = File.ReadAllBytes(JournalPath);

        Assert.Throws<InvalidDataException>(() => journal.BindStream("stream-1", 4));
        Assert.Throws<InvalidDataException>(() => journal.BindStream("stream-2", 6));

        Assert.Equal(4, journal.Sequence);
        Assert.Equal("stream-1", journal.StreamId);
        Assert.Equal(before, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void Reopened_pending_intent_binds_same_stream_at_server_next_sequence_and_ack_preserves_envelope()
    {
        var original = Command("op-retry", 1);
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(original);

        var reopened = new ClientIntentJournal(JournalPath);
        reopened.BindStream("stream-1", 2);
        Assert.True(JToken.DeepEquals(original, reopened.Pending));
        reopened.Acknowledge("op-retry", "Accepted");

        var afterAck = new ClientIntentJournal(JournalPath);
        Assert.Equal("stream-1", afterAck.StreamId);
        Assert.Equal(1, afterAck.Sequence);
        Assert.Null(afterAck.Pending);
    }

    [Fact]
    public void Pending_intent_allows_server_next_sequence_before_uncommitted_retry()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-pending", 1));

        new ClientIntentJournal(JournalPath).BindStream("stream-1", 1);

        Assert.Equal("op-pending", new ClientIntentJournal(JournalPath).Pending!.Value<string>("OperationId"));
    }

    [Fact]
    public void Pending_intent_rejects_a_large_server_gap_without_mutating_the_exact_envelope()
    {
        var original = Command("op-pending", 1);
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(original);
        var before = File.ReadAllBytes(JournalPath);

        Assert.Throws<InvalidDataException>(() => journal.BindStream("stream-1", 1000));

        Assert.Equal(0, journal.Sequence);
        Assert.Equal("stream-1", journal.StreamId);
        Assert.True(JToken.DeepEquals(original, journal.Pending));
        Assert.Equal(before, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void Failed_stream_binding_write_keeps_memory_and_journal_bytes_unchanged()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        var before = File.ReadAllBytes(JournalPath);
        Directory.CreateDirectory(JournalPath + ".tmp");

        Assert.ThrowsAny<Exception>(() => journal.BindStream("stream-1", 2));

        Assert.Equal(0, journal.Sequence);
        Assert.Equal("stream-1", journal.StreamId);
        Assert.Null(journal.Pending);
        Assert.Equal(before, File.ReadAllBytes(JournalPath));
    }

    [Fact]
    public void Stream_change_fails_closed_and_retains_pending_intent_on_disk()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-stream", 1));

        Assert.Throws<InvalidDataException>(() => new ClientIntentJournal(JournalPath).BindStream("stream-2", 2));

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal("stream-1", reopened.StreamId);
        Assert.Equal("op-stream", reopened.Pending!.Value<string>("OperationId"));
    }

    [Theory]
    [InlineData("stream-1", 3)]
    [InlineData("", 2)]
    [InlineData("stream-1", 0)]
    public void Divergent_or_malformed_stream_binding_does_not_clear_pending(string streamId, long nextSequence)
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-retain", 1));

        Assert.Throws<InvalidDataException>(() => new ClientIntentJournal(JournalPath).BindStream(streamId, nextSequence));

        var reopened = new ClientIntentJournal(JournalPath);
        Assert.Equal("stream-1", reopened.StreamId);
        Assert.Equal("op-retain", reopened.Pending!.Value<string>("OperationId"));
    }

    [Fact]
    public void Acknowledged_journal_keeps_stream_and_sequence_when_access_token_rotates()
    {
        var journal = new ClientIntentJournal(JournalPath);
        journal.BindStream("stream-1", 1);
        journal.Begin(Command("op-rotate", 1));
        journal.Acknowledge("op-rotate", "Accepted");

        var freshAccess = new MatchAccess("new-token", "new-session", "stream-1", "match-1", 2, 30);
        var reopened = new ClientIntentJournal(JournalPath);
        reopened.BindStream(freshAccess.StreamId, 2);

        Assert.Equal("stream-1", reopened.StreamId);
        Assert.Equal(1, reopened.Sequence);
        Assert.Null(reopened.Pending);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory)) Directory.Delete(_directory, true);
    }

    private string JournalPath => Path.Combine(_directory, "intent.json");

    private static JObject Command(string operationId, long sequence) => new()
    {
        ["MatchId"] = "match-1", ["RoundId"] = "1", ["ContentVersion"] = "content-1", ["OperationId"] = operationId,
        ["Sequence"] = sequence, ["CommandKind"] = "Choose", ["Payload"] = "{\"Token\":9,\"OfferIndex\":0}"
    };

    private static X509Certificate2 CreateCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=TankDraft Test", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Chain BuildChain(X509Certificate2 certificate)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllFlags;
        chain.Build(certificate);
        return chain;
    }

    private static bool HasOnlyUntrustedRoot(X509Chain chain) => chain.ChainStatus.All(status =>
        (status.Status & ~X509ChainStatusFlags.UntrustedRoot) == X509ChainStatusFlags.NoError);

    private static string Pin(X509Certificate certificate)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(certificate.GetRawCertData()));
    }

    private static ServerMatchSnapshot FixtureSnapshot() => new(
        "match-1", "content-v1", 12, 3, "Battle", 9, 1, false, -1, false, null, true, 1,
        new[] { new ArmyEntry("unit", 2, 0) }, new[] { new MatchOffer("offer", "unit", DraftActionKind.Add, 1) }, 1, 0, 0,
        DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null, false, 66,
        new[]
        {
            new BattleEntityState(1, 0, BattleEntityKind.Unit, "tank", new BattleVec(1.25f, -2.5f), new BattleVec(1, -2), new BattleVec(1, 0), 71, 100, 1, 0, 0),
            new BattleEntityState(2, 1, BattleEntityKind.Projectile, "shell", new BattleVec(3, 4), new BattleVec(2, 4), new BattleVec(1, 0), 1, 1, .1f, .5f, 1),
            new BattleEntityState(3, 1, BattleEntityKind.Zone, "fire-zone", new BattleVec(5, 6), new BattleVec(5, 6), new BattleVec(0, 1), 1, 1, 2, .25f, -1)
        },
        77, false, new[] { new ServerBattleEvent(701, 3, new BattleEvent(4, 42, BattleEventKind.ZoneTick, 3, 1, new BattleVec(5, 6), 11, 2, .5f)) },
        Array.Empty<ServerRoundResult>(), null);

    private static string SnapshotJson()
    {
        var options = new JsonSerializerOptions();
        ServerMatchJson.Register(options);
        return JsonSerializer.Serialize(FixtureSnapshot() with { Phase = "Draft", Committed = false, CatchingUp = false }, options);
    }

    private sealed class TestCredentials(string runDirectory) : IMatchCredentials
    {
        public Uri Endpoint { get; } = new("ws://127.0.0.1:18782/v1/socket");
        public int Side => 0;
        public string RunDirectory { get; } = runDirectory;
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => true;
        public Task<MatchAccess> AcquireAsync(CancellationToken token) => Task.FromResult(new MatchAccess("test-token", "session", "stream", "match-1", 1, 30));
        public void Dispose() { }
    }
}
