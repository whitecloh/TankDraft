using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Reflection;
using Newtonsoft.Json.Linq;
using TankDraft.Match.ServerClient;
using Xunit;

namespace TankDraft.ServerClient.Tests;

[CollectionDefinition("LocalTlsSocketIntegration", DisableParallelization = true)]
public sealed class LocalTlsSocketIntegrationCollection { }

[Collection("LocalTlsSocketIntegration")]
public sealed class LocalTlsSocketIntegrationTests
{
    [Fact]
    public async Task ConnectAsync_completes_scoped_self_signed_tls_upgrade_without_os_trust()
    {
        using var certificate = CreateLoopbackCertificate();
        using var server = new UpgradeServer(certificate, expectPeerTlsAbort: false);
        var credentials = new TlsCredentials(Pin(certificate));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        using var socket = await LocalTlsSocket.ConnectAsync(credentials, Access(), timeout.Token);
        await server.Completion;

        Assert.True(credentials.CallbackInvoked);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task ConnectAsync_rejects_wrong_pin_after_scoped_callback()
    {
        using var certificate = CreateLoopbackCertificate();
        using var server = new UpgradeServer(certificate, expectPeerTlsAbort: true);
        var credentials = new TlsCredentials(new string('0', 64));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await Assert.ThrowsAsync<AuthenticationException>(() => LocalTlsSocket.ConnectAsync(credentials, Access(), timeout.Token));
        await server.Completion;

        Assert.True(credentials.CallbackInvoked);
    }

    [Fact]
    public async Task Reauthenticate_keeps_one_transport_connection_and_rejects_cross_match_access()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftRenewalTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var credentials = new RotatingCredentials(directory,
            new MatchAccess("token-2", "session-2", "stream-1", "match-1", 2, 30),
            new MatchAccess("token-3", "session-3", "stream-1", "match-1", 3, 30),
            new MatchAccess("token-4", "session-4", "stream-1", "match-1", 4, 30));
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 20, 20, 100, 1024, 8);
        var socket = new ReauthenticationSocket();
        var current = new MatchAccess("token-1", "session-1", "stream-1", "match-1", 1, 30);
        typeof(ServerClientTransport).GetField("_matchId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transport, "match-1");
        typeof(ServerClientTransport).GetField("<Connections>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(transport, 1);

        try
        {
            for (var expectedGeneration = 2; expectedGeneration <= 4; expectedGeneration++)
            {
                current = await InvokeReauthenticate(transport, socket, current);
                Assert.Equal(expectedGeneration, current.Generation);
                Assert.Equal(1, transport.Connections);
            }

            Assert.Equal(new[] { "token-2", "token-3", "token-4" }, socket.RequestedTokens);
            Assert.Equal(3, credentials.AcquireCount);

            var mismatched = new RotatingCredentials(directory, new MatchAccess("wrong", "session-5", "stream-2", "match-1", 5, 30));
            using var rejected = new ServerClientTransport(mismatched, "tankdraft-server-v2", "content-v1", 20, 20, 100, 1024, 8);
            typeof(ServerClientTransport).GetField("_matchId", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(rejected, "match-1");
            await Assert.ThrowsAsync<InvalidDataException>(() => InvokeReauthenticate(rejected, new ReauthenticationSocket(), current));
        }
        finally
        {
            credentials.Dispose();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Transport_run_preserves_connection_and_frames_through_renewal_and_server_catchup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftRenewalRunTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var certificate = CreateLoopbackCertificate();
        using var server = new RenewalServer(certificate);
        using var environment = new LocalProcessEnvironment(directory, Pin(certificate));
        using var credentials = new LocalProcessCredentials();
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 5, 20, 1000, 8192, 128);
        try
        {
            transport.Start();
            await server.ThreeRenewals.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var frames = new List<ReceivedServerFrame>();
            while (transport.TryTake(out var frame)) frames.Add(frame);
            Assert.True(transport.Connected);
            Assert.Equal(1, transport.Connections);
            Assert.True(transport.AuthGeneration >= 4);
            Assert.True(frames.Count >= 4);
            Assert.All(frames, frame => Assert.True(frame.Value.CatchingUp));
            Assert.True(frames[0].Reset);
            Assert.All(frames.Skip(1), frame => Assert.False(frame.Reset));
            Assert.Equal(1, server.ConnectionCount);
            Assert.True(server.LastCursor > 0);
            // Buffered frames must retain the timing/session that produced them,
            // even though the transport has already renewed and performed later polls.
            Assert.Equal(1, frames[0].AuthGeneration);
            Assert.Contains(frames, frame => frame.AuthGeneration >= 2);
            Assert.All(frames, frame =>
            {
                var timing = frame.ExchangeTiming;
                Assert.True(timing.StartedAt > 0);
                Assert.True(timing.SentAt >= timing.StartedAt);
                Assert.True(timing.WireCompletedAt >= timing.SentAt);
                Assert.True(timing.JsonCompletedAt >= timing.WireCompletedAt);
                Assert.True(frame.ReceivedAt >= timing.JsonCompletedAt);
                Assert.True(timing.PayloadBytes > 0);
                Assert.Equal(1, frame.Connection);
                Assert.Equal("renew-stream", frame.StreamId);
            });
        }
        finally
        {
            transport.Dispose();
            server.Dispose();
            try { await server.Completion.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static Task<MatchAccess> InvokeReauthenticate(ServerClientTransport transport, WebSocket socket, MatchAccess current)
    {
        var method = typeof(ServerClientTransport).GetMethod("Reauthenticate", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (Task<MatchAccess>)method!.Invoke(transport, [socket, current, CancellationToken.None])!;
    }

    [Fact]
    public async Task Slow_snapshot_exchange_does_not_add_an_extra_poll_period_or_burst()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftCadence", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var certificate = CreateLoopbackCertificate();
        using var server = new RenewalServer(certificate, snapshotDelayMilliseconds: 300);
        using var environment = new LocalProcessEnvironment(directory, Pin(certificate));
        using var credentials = new LocalProcessCredentials();
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 200, 20, 3000, 8192, 128);
        try
        {
            transport.Start();
            await server.SixPolls.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var times = server.PollStarts.ToArray();
            var gaps = times.Zip(times.Skip(1), (a, b) => (b - a) * 1000).Order().ToArray();
            Assert.Equal(1, transport.Connections);
            Assert.True(gaps.Min() >= 280, "Sequential exchange must not burst after a delayed response.");
            Assert.True(gaps[gaps.Length / 2] < 450, "Do not add another 200 ms after the 300 ms response.");
            var measured = new List<ReceivedServerFrame>();
            while (transport.TryTake(out var frame)) measured.Add(frame);
            Assert.NotEmpty(measured);
            Assert.All(measured, frame => Assert.True((frame.ExchangeTiming.WireCompletedAt - frame.ExchangeTiming.SentAt) * 1000 >= 280));
        }
        finally
        {
            transport.Dispose(); server.Dispose();
            try { await server.Completion.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(200, false)]
    [InlineData(200, true)]
    public async Task Parallel_refresh_keeps_polling_during_verification_and_handles_revocation_race(int responseDelay, bool initialHint)
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftParallelRefresh", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var certificate = CreateLoopbackCertificate();
        using var server = new RenewalServer(certificate, parallelRefresh: true, renewalResponseDelay: responseDelay, initialHint: initialHint);
        using var environment = new LocalProcessEnvironment(directory, Pin(certificate));
        using var credentials = new LocalProcessCredentials();
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 50, 20, 3000, 8192, 128);
        try
        {
            transport.Start();
            await server.ThreeRenewals.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Null(transport.Failure);
            Assert.Equal(1, transport.Connections);
            Assert.Equal(1, server.ConnectionCount);
            Assert.True(server.PollsDuringVerification >= (initialHint ? 6 : 9), "Snapshots must continue during HTTP verification while access is valid.");
            if (responseDelay > 0) Assert.True(server.RefreshHints >= 3, "Rotation before the HTTP response must use the explicit handoff.");
            Assert.True(transport.AuthGeneration >= 3);
            var frames = new List<ReceivedServerFrame>();
            while (transport.TryTake(out var frame)) frames.Add(frame);
            Assert.True(frames.Count >= 10);
            Assert.All(frames.Skip(1), frame => Assert.False(frame.Reset));
        }
        finally
        {
            transport.Dispose(); server.Dispose();
            try { await server.Completion.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Suspend_during_parallel_refresh_cancels_socket_work_and_disposes_without_waiting_for_http()
    {
        var directory = Path.Combine(Path.GetTempPath(), "TankDraftRefreshSuspend", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        using var certificate = CreateLoopbackCertificate();
        using var server = new RenewalServer(certificate, parallelRefresh: true, renewalResponseDelay: 200);
        using var environment = new LocalProcessEnvironment(directory, Pin(certificate));
        using var credentials = new LocalProcessCredentials();
        using var transport = new ServerClientTransport(credentials, "tankdraft-server-v2", "content-v1", 50, 20, 3000, 8192, 128);
        try
        {
            transport.Start();
            await server.VerificationStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            transport.SetSuspended(true);
            await Task.Delay(150);
            var count = server.PollStarts.Count;
            await Task.Delay(200);
            Assert.Equal(count, server.PollStarts.Count);
            Assert.False(transport.Connected);
            Assert.Null(transport.Failure);
            transport.Dispose();
            var run = (Task)typeof(ServerClientTransport).GetField("_task", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(transport)!;
            await run.WaitAsync(TimeSpan.FromSeconds(2));
        }
        finally
        {
            transport.Dispose(); server.Dispose();
            try { await server.Completion.WaitAsync(TimeSpan.FromSeconds(2)); } catch { }
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }

    private static MatchAccess Access() => new("access-token", "session", "stream", "match", 1, 30);

    private static X509Certificate2 CreateLoopbackCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=127.0.0.1", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var names = new SubjectAlternativeNameBuilder();
        names.AddIpAddress(IPAddress.Loopback);
        request.CertificateExtensions.Add(names.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(5));
#pragma warning disable SYSLIB0057
        return new X509Certificate2(generated.Export(X509ContentType.Pfx), string.Empty, X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
#pragma warning restore SYSLIB0057
    }

    private static string Pin(X509Certificate certificate)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(certificate.GetRawCertData()));
    }

    private sealed class TlsCredentials(string pin) : IMatchCredentials
    {
        public bool CallbackInvoked { get; private set; }
        public Uri Endpoint { get; } = new("wss://127.0.0.1:18783/v1/socket");
        public int Side => 0;
        public string RunDirectory => Path.GetTempPath();
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors)
        {
            CallbackInvoked = true;
            return LocalTlsPin.Validate(pin, certificate, chain, errors, DateTime.UtcNow);
        }
        public Task<MatchAccess> AcquireAsync(CancellationToken token) => Task.FromResult(Access());
        public void Dispose() { }
    }

    private sealed class RotatingCredentials(string runDirectory, params MatchAccess[] access) : IMatchCredentials
    {
        private readonly Queue<MatchAccess> _access = new(access);
        public int AcquireCount { get; private set; }
        public Uri Endpoint { get; } = new("wss://127.0.0.1:18783/v1/socket");
        public int Side => 0;
        public string RunDirectory { get; } = runDirectory;
        public bool ValidateServerCertificate(X509Certificate certificate, X509Chain chain, SslPolicyErrors errors) => true;
        public Task<MatchAccess> AcquireAsync(CancellationToken token)
        {
            AcquireCount++;
            return Task.FromResult(_access.Dequeue());
        }
        public void Dispose() { }
    }

    private sealed class ReauthenticationSocket : WebSocket
    {
        private byte[] _reply = Array.Empty<byte>();
        public List<string> RequestedTokens { get; } = [];
        public override WebSocketCloseStatus? CloseStatus => null;
        public override string CloseStatusDescription => null;
        public override string SubProtocol => null;
        public override WebSocketState State => WebSocketState.Open;
        public override void Abort() { }
        public override Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken) => Task.CompletedTask;
        public override void Dispose() { }
        public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            Array.Copy(_reply, 0, buffer.Array!, buffer.Offset, _reply.Length);
            return Task.FromResult(new WebSocketReceiveResult(_reply.Length, WebSocketMessageType.Text, true));
        }
        public override Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            var request = ServerWire.Parse(Encoding.UTF8.GetString(buffer.Array!, buffer.Offset, buffer.Count));
            var token = request.Value<string>("AccessToken")!;
            RequestedTokens.Add(token);
            var generation = int.Parse(token.AsSpan(token.Length - 1));
            _reply = Encoding.UTF8.GetBytes(new JObject
            {
                ["Kind"] = "Reauthenticated", ["SessionId"] = "session-" + generation, ["StreamId"] = "stream-1",
                ["Generation"] = generation, ["MatchId"] = "match-1", ["Side"] = 0
            }.ToString());
            return Task.CompletedTask;
        }
    }

    private sealed class LocalProcessEnvironment : IDisposable
    {
        readonly Dictionary<string, string> _previous = new();
        public LocalProcessEnvironment(string runDirectory, string pin)
        {
            Set("TD_LOCAL_ENDPOINT", "wss://127.0.0.1:18783/v1/socket");
            Set("TD_LOCAL_GRANT", "renewal-grant");
            Set("TD_LOCAL_TLS_PIN", pin);
            Set("TD_LOCAL_SIDE", "0");
            Set("TD_LOCAL_RUN", runDirectory);
        }
        void Set(string name, string value)
        {
            _previous[name] = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose()
        {
            foreach (var value in _previous) Environment.SetEnvironmentVariable(value.Key, value.Value);
        }
    }

    private sealed class RenewalServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 18783);
        private readonly X509Certificate2 _certificate;
        private readonly CancellationTokenSource _stop = new();
        private readonly object _sync = new();
        private readonly List<TcpClient> _clients = [];
        private readonly List<Task> _handlers = [];
        private int _generation, _connectionCount;
        private long _lastCursor;
        private readonly int _snapshotDelayMilliseconds;
        private readonly bool _parallelRefresh;
        private readonly int _renewalResponseDelay;
        private readonly bool _initialHint;
        private int _verifying, _pollsDuringVerification, _refreshHints;
        public int PollsDuringVerification => Volatile.Read(ref _pollsDuringVerification);
        public int RefreshHints => Volatile.Read(ref _refreshHints);
        public System.Collections.Concurrent.ConcurrentQueue<double> PollStarts { get; } = new();
        public TaskCompletionSource<bool> SixPolls { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> ThreeRenewals { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<bool> VerificationStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task Completion { get; }
        public int ConnectionCount => Volatile.Read(ref _connectionCount);
        public long LastCursor => Interlocked.Read(ref _lastCursor);

        public RenewalServer(X509Certificate2 certificate, int snapshotDelayMilliseconds = 0, bool parallelRefresh = false, int renewalResponseDelay = 0, bool initialHint = false)
        {
            _certificate = certificate;
            _snapshotDelayMilliseconds = snapshotDelayMilliseconds;
            _parallelRefresh = parallelRefresh;
            _renewalResponseDelay = renewalResponseDelay;
            _initialHint = initialHint;
            _listener.Start();
            Completion = ServeAsync();
        }

        private async Task ServeAsync()
        {
            try
            {
                while (!_stop.IsCancellationRequested)
                {
                    TcpClient client;
                    try { client = await _listener.AcceptTcpClientAsync(_stop.Token); }
                    catch (OperationCanceledException) { break; }
                    catch (ObjectDisposedException) { break; }
                    lock (_sync)
                    {
                        if (_stop.IsCancellationRequested) { client.Dispose(); break; }
                        _clients.Add(client);
                        _handlers.Add(ServeClientAsync(client, _stop.Token));
                    }
                }
            }
            finally
            {
                Task[] handlers;
                lock (_sync) handlers = _handlers.ToArray();
                try { await Task.WhenAll(handlers); } catch (OperationCanceledException) { } catch (IOException) { } catch (WebSocketException) { }
            }
        }

        private async Task ServeClientAsync(TcpClient client, CancellationToken token)
        {
            try
            {
                using (client)
                using (var ssl = new SslStream(client.GetStream(), false))
                {
                    await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                    {
                        ServerCertificate = _certificate,
                        EnabledSslProtocols = SslProtocols.Tls12,
                        CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                    }, token);
                    var request = await ReadRequestAsync(ssl, token);
                    if (request.Line == "POST /v1/local-session HTTP/1.1")
                    {
                        if (!request.Headers.TryGetValue("Authorization", out var authorization) || authorization != "Bearer renewal-grant")
                        {
                            await WriteHttpAsync(ssl, 401, "Unauthorized", string.Empty, token);
                            return;
                        }
                        if (_parallelRefresh && Volatile.Read(ref _generation) > 0)
                        {
                            Interlocked.Exchange(ref _verifying, 1);
                            VerificationStarted.TrySetResult(true);
                            await Task.Delay(500, token);
                            Interlocked.Exchange(ref _verifying, 0);
                        }
                        var generation = Interlocked.Increment(ref _generation);
                        if (_parallelRefresh && generation > 1 && _renewalResponseDelay > 0)
                            await Task.Delay(_renewalResponseDelay, token);
                        var access = new JObject
                        {
                            ["AccessToken"] = "renew-token-" + generation, ["SessionId"] = "renew-session-" + generation,
                            ["StreamId"] = "renew-stream", ["MatchId"] = "renew-match", ["Generation"] = generation,
                            ["Side"] = 0, ["RefreshAfterSeconds"] = _snapshotDelayMilliseconds == 0 ? .11 : 30
                        }.ToString(Newtonsoft.Json.Formatting.None);
                        await WriteHttpAsync(ssl, 200, "OK", access, token);
                        return;
                    }
                    if (request.Line != "GET /v1/socket HTTP/1.1" || !request.Headers.TryGetValue("Sec-WebSocket-Key", out var key) ||
                        !request.Headers.TryGetValue("Authorization", out var socketAuthorization)) throw new InvalidDataException("Unexpected local fixture request.");
                    var generationFromToken = ParseGeneration(socketAuthorization, "Bearer renew-token-");
                    var response = "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: " + UpgradeServer.Accept(key) + "\r\n\r\n";
                    var responseBytes = Encoding.ASCII.GetBytes(response);
                    await ssl.WriteAsync(responseBytes, token);
                    await ssl.FlushAsync(token);
                    Interlocked.Increment(ref _connectionCount);
                    await ServeWebSocketAsync(ssl, generationFromToken, token);
                }
            }
            catch (OperationCanceledException) { }
            catch (IOException) { }
            catch (WebSocketException) { }
            finally
            {
                lock (_sync) _clients.Remove(client);
            }
        }

        private async Task ServeWebSocketAsync(Stream ssl, int initialGeneration, CancellationToken token)
        {
            var revision = 0L;
            var activeGeneration = initialGeneration;
            var optedIn = false;
            var awaitingRefresh = false;
            while (!token.IsCancellationRequested)
            {
                var requestJson = await ReadFrame(ssl, token);
                if (requestJson is null) return;
                var requestObject = ServerWire.Parse(requestJson);
                if (awaitingRefresh && requestObject.Value<string>("Kind") != "Reauthenticate")
                    throw new InvalidDataException("Only reauthentication is allowed after the refresh hint.");
                switch (requestObject.Value<string>("Kind"))
                {
                    case "Hello":
                        await WriteFrame(ssl, new JObject
                        {
                            ["Kind"] = "Welcome", ["Protocol"] = "tankdraft-server-v2", ["ContentVersion"] = "content-v1", ["MatchId"] = "renew-match",
                            ["Side"] = 0, ["SessionId"] = "renew-session-" + initialGeneration, ["StreamId"] = "renew-stream", ["Generation"] = initialGeneration, ["NextSequence"] = 1,
                            ["ParallelAccessRefresh"] = _parallelRefresh
                        }.ToString(), token);
                        break;
                    case "EnableParallelRefresh":
                        if (!_parallelRefresh) throw new InvalidDataException("Feature not supported.");
                        optedIn = true;
                        await WriteFrame(ssl, new JObject { ["Kind"] = "ParallelRefreshEnabled" }.ToString(), token);
                        break;
                    case "Poll":
                        PollStarts.Enqueue(ReceivedServerFrame.Clock);
                        if (PollStarts.Count >= 6) SixPolls.TrySetResult(true);
                        if (optedIn && (activeGeneration != Volatile.Read(ref _generation) || (_initialHint && activeGeneration == 1)))
                        {
                            awaitingRefresh = true;
                            Interlocked.Increment(ref _refreshHints);
                            await WriteFrame(ssl, new JObject { ["Kind"] = "AccessRefreshRequired" }.ToString(), token);
                            break;
                        }
                        if (Volatile.Read(ref _verifying) == 1) Interlocked.Increment(ref _pollsDuringVerification);
                        if (_snapshotDelayMilliseconds > 0) await Task.Delay(_snapshotDelayMilliseconds, token);
                        var cursor = requestObject["AfterEventSequence"];
                        if (cursor is { Type: JTokenType.Integer }) Interlocked.Exchange(ref _lastCursor, cursor.Value<long>());
                        revision++;
                        await WriteFrame(ssl, Snapshot(revision).ToString(), token);
                        break;
                    case "Reauthenticate":
                        var generation = ParseGeneration(requestObject.Value<string>("AccessToken"), "renew-token-");
                        if (optedIn && generation != Volatile.Read(ref _generation)) throw new InvalidDataException("Replacement access is revoked.");
                        activeGeneration = generation;
                        awaitingRefresh = false;
                        await WriteFrame(ssl, new JObject
                        {
                            ["Kind"] = "Reauthenticated", ["SessionId"] = "renew-session-" + generation, ["StreamId"] = "renew-stream",
                            ["Generation"] = generation, ["MatchId"] = "renew-match", ["Side"] = 0
                        }.ToString(), token);
                        if (generation >= 4) ThreeRenewals.TrySetResult(true);
                        break;
                    default: throw new InvalidDataException("Unexpected transport request.");
                }
            }
        }

        private static int ParseGeneration(string value, string prefix)
        {
            if (string.IsNullOrEmpty(value) || !value.StartsWith(prefix, StringComparison.Ordinal) || !int.TryParse(value[prefix.Length..], out var generation) || generation < 1) throw new InvalidDataException("Invalid fixture access.");
            return generation;
        }

        private static async Task<HttpRequest> ReadRequestAsync(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (bytes.Count < 8192)
            {
                if (await stream.ReadAsync(one, 0, 1, token) != 1) throw new EndOfStreamException();
                bytes.Add(one[0]);
                var count = bytes.Count;
                if (count < 4 || bytes[count - 4] != '\r' || bytes[count - 3] != '\n' || bytes[count - 2] != '\r' || bytes[count - 1] != '\n') continue;
                var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var index = 1; index < lines.Length - 2; index++)
                {
                    var separator = lines[index].IndexOf(':');
                    if (separator <= 0 || !headers.TryAdd(lines[index][..separator], lines[index][(separator + 1)..].Trim())) throw new InvalidDataException("Invalid fixture headers.");
                }
                return new HttpRequest(lines[0], headers);
            }
            throw new InvalidDataException("Fixture headers exceed limit.");
        }

        private static async Task WriteHttpAsync(Stream stream, int status, string reason, string body, CancellationToken token)
        {
            var payload = Encoding.UTF8.GetBytes(body);
            var headers = "HTTP/1.1 " + status + " " + reason + "\r\nContent-Type: application/json\r\nContent-Length: " + payload.Length + "\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(headers), token);
            if (payload.Length != 0) await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        private sealed record HttpRequest(string Line, Dictionary<string, string> Headers);

        private static JObject Snapshot(long revision) => new()
        {
            ["Kind"] = "Snapshot",
            ["Snapshot"] = new JObject
            {
                ["BattleStateVersion"] = TankDraft.Contracts.Battle.BattleEntityState.WireVersion,
                ["MatchId"] = "renew-match", ["ContentVersion"] = "content-v1", ["Phase"] = "Battle", ["Fault"] = null,
                ["Revision"] = revision, ["ChoiceToken"] = 0, ["SimulationTick"] = revision, ["EventSequence"] = revision,
                ["Round"] = 1, ["ChoiceNumber"] = 0, ["BonusSide"] = -1, ["Wins0"] = 0, ["Wins1"] = 0, ["LastWinner"] = -1, ["OrderCharges"] = 0,
                ["IsComeback"] = false, ["Committed"] = false, ["CanUseOrder"] = false, ["CatchingUp"] = true, ["ResyncRequired"] = false,
                ["ServerNow"] = DateTimeOffset.UtcNow.ToString("O"), ["DeadlineAt"] = null,
                ["Entities"] = new JArray(), ["Events"] = new JArray(), ["Army"] = new JArray(), ["Offers"] = new JArray(), ["Results"] = new JArray()
            }
        };

        private static async Task<string> ReadFrame(Stream stream, CancellationToken token)
        {
            var first = await ReadByte(stream, token);
            if (first < 0) return null;
            var second = await ReadByte(stream, token);
            if (second < 0 || (first & 0x0f) == 8) return null;
            var length = second & 0x7f;
            if (length == 126)
            {
                var high = await ReadByte(stream, token);
                var low = await ReadByte(stream, token);
                if (high < 0 || low < 0) throw new EndOfStreamException();
                length = (high << 8) | low;
            }
            if ((second & 0x80) == 0 || length < 0 || length > 8192) throw new InvalidDataException("Invalid client WebSocket frame.");
            var mask = new byte[4]; await ReadExactly(stream, mask, token);
            var payload = new byte[length]; await ReadExactly(stream, payload, token);
            for (var index = 0; index < payload.Length; index++) payload[index] ^= mask[index % mask.Length];
            return Encoding.UTF8.GetString(payload);
        }

        private static async Task WriteFrame(Stream stream, string value, CancellationToken token)
        {
            var payload = Encoding.UTF8.GetBytes(value);
            if (payload.Length > 8192) throw new InvalidDataException("Server test message too large.");
            if (payload.Length < 126) await stream.WriteAsync(new byte[] { 0x81, (byte)payload.Length }, token);
            else await stream.WriteAsync(new byte[] { 0x81, 126, (byte)(payload.Length >> 8), (byte)payload.Length }, token);
            await stream.WriteAsync(payload, token);
            await stream.FlushAsync(token);
        }

        private static async Task<int> ReadByte(Stream stream, CancellationToken token)
        {
            var value = new byte[1];
            return await stream.ReadAsync(value, token) == 1 ? value[0] : -1;
        }

        private static async Task ReadExactly(Stream stream, byte[] destination, CancellationToken token)
        {
            var offset = 0;
            while (offset < destination.Length)
            {
                var read = await stream.ReadAsync(destination.AsMemory(offset), token);
                if (read == 0) throw new EndOfStreamException();
                offset += read;
            }
        }

        public void Dispose()
        {
            if (_stop.IsCancellationRequested) return;
            _stop.Cancel();
            _listener.Stop();
            lock (_sync) foreach (var client in _clients) client.Dispose();
        }
    }

    private sealed class UpgradeServer : IDisposable
    {
        private readonly TcpListener _listener = new(IPAddress.Loopback, 18783);
        private readonly X509Certificate2 _certificate;
        private readonly bool _expectPeerTlsAbort;
        public Task Completion { get; }
        public UpgradeServer(X509Certificate2 certificate, bool expectPeerTlsAbort)
        {
            _certificate = certificate;
            _expectPeerTlsAbort = expectPeerTlsAbort;
            _listener.Start(1);
            Completion = ServeAsync();
        }
        private async Task ServeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var listenerAbort = timeout.Token.Register(() => _listener.Stop());
            using var client = await _listener.AcceptTcpClientAsync(timeout.Token);
            using var ssl = new SslStream(client.GetStream(), false);
            try
            {
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                {
                    ServerCertificate = _certificate,
                    EnabledSslProtocols = SslProtocols.Tls12,
                    CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                }, timeout.Token);
                var request = await ReadHeadersAsync(ssl, timeout.Token);
                var key = request["Sec-WebSocket-Key"];
                var response = "HTTP/1.1 101 Switching Protocols\r\nConnection: Upgrade\r\nUpgrade: websocket\r\nSec-WebSocket-Accept: " + Accept(key) + "\r\n\r\n";
                var bytes = Encoding.ASCII.GetBytes(response);
                await ssl.WriteAsync(bytes, 0, bytes.Length);
                await ssl.FlushAsync();
            }
            catch (AuthenticationException) { }
            catch (IOException) when (_expectPeerTlsAbort) { }
        }
        internal static async Task<Dictionary<string, string>> ReadHeadersAsync(Stream stream, CancellationToken token)
        {
            var bytes = new List<byte>();
            var one = new byte[1];
            while (bytes.Count < 8192)
            {
                if (await stream.ReadAsync(one, 0, 1, token) != 1) throw new EndOfStreamException();
                bytes.Add(one[0]);
                var count = bytes.Count;
                if (count >= 4 && bytes[count - 4] == '\r' && bytes[count - 3] == '\n' && bytes[count - 2] == '\r' && bytes[count - 1] == '\n')
                {
                    var lines = Encoding.ASCII.GetString(bytes.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
                    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    for (var i = 1; i < lines.Length - 2; i++)
                    {
                        var separator = lines[i].IndexOf(':');
                        if (separator <= 0 || !result.TryAdd(lines[i].Substring(0, separator), lines[i].Substring(separator + 1).Trim())) throw new InvalidDataException();
                    }
                    return result;
                }
            }
            throw new InvalidDataException("Client WebSocket headers exceed limit.");
        }
        internal static string Accept(string key)
        {
            using var sha = SHA1.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.ASCII.GetBytes(key + "258EAFA5-E914-47DA-95CA-C5AB0DC85B11")));
        }
        public void Dispose() => _listener.Stop();
    }
}
