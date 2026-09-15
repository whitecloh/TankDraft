using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace TankDraft.Match.ServerClient
{
    // Immutable timing for this exchange, never the transport's next request.
    public readonly struct ServerExchangeTiming
    {
        public readonly double StartedAt, SentAt, WireCompletedAt, JsonCompletedAt;
        public readonly int PayloadBytes;
        public ServerExchangeTiming(double startedAt, double sentAt, double wireCompletedAt, double jsonCompletedAt, int payloadBytes)
        { StartedAt = startedAt; SentAt = sentAt; WireCompletedAt = wireCompletedAt; JsonCompletedAt = jsonCompletedAt; PayloadBytes = payloadBytes; }
    }
    public sealed class ReceivedServerFrame
    {
        public readonly ServerFrame Value;
        public readonly double ReceivedAt;
        public readonly bool Reset;
        public readonly ServerExchangeTiming ExchangeTiming;
        public readonly int Connection, AuthGeneration;
        public readonly long RenewalMilliseconds;
        public readonly string StreamId;
        public ReceivedServerFrame(ServerFrame value, bool reset) : this(value, reset, default, 0, 0, 0, null) { }
        public ReceivedServerFrame(ServerFrame value, bool reset, ServerExchangeTiming timing, int connection, int authGeneration, long renewalMilliseconds, string streamId)
        {
            Value = value; Reset = reset; ReceivedAt = Clock; ExchangeTiming = timing;
            Connection = connection; AuthGeneration = authGeneration; RenewalMilliseconds = renewalMilliseconds; StreamId = streamId;
        }
        public static double Clock => (double)System.Diagnostics.Stopwatch.GetTimestamp() / System.Diagnostics.Stopwatch.Frequency;
    }

    public sealed class ServerClientTransport : IDisposable
    {
        readonly IMatchCredentials _credentials;
        readonly string _protocol, _version;
        readonly int _poll, _retry, _timeout, _maxBytes, _capacity;
        readonly object _sync = new object();
        readonly Queue<ReceivedServerFrame> _frames = new Queue<ReceivedServerFrame>();
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly ClientIntentJournal _journal;
        readonly System.Diagnostics.Stopwatch _diagnosticStopwatch = System.Diagnostics.Stopwatch.StartNew();
        WebSocket _socket;
        CancellationTokenSource _connectionLifetime;
        Task _task;
        volatile bool _connected, _disposed, _suspended;
        volatile string _failure;
        long _latestRevision = -1;
        string _matchId;
        double _pauseUntil;
        bool _restartRequested;
        int _diagnosticCount;
        string _diagnosticStage = StageAcquire, _diagnosticPhase;
        MatchAccess _access;
        ServerExchangeTiming _lastExchangeTiming;
        const string StageAcquire = "Acquire", StageConnect = "Connect", StageHello = "Hello", StagePoll = "Poll", StageCommand = "Command", StageReauthenticate = "Reauthenticate";
        const string PhaseSend = "Send", PhaseReceive = "Receive";
        sealed class RenewSessionException : Exception
        {
            public readonly int? CloseStatus;
            public RenewSessionException(int? closeStatus) { CloseStatus = closeStatus; }
        }
        public bool Connected => _connected;
        public string Failure => _failure;
        public int Side => _credentials.Side;
        public int Connections { get; private set; }
        public int AuthGeneration => _access == null ? 0 : _access.Generation;
        public string StreamId => _access == null ? null : _access.StreamId;
        public long LastPollMilliseconds { get; private set; }
        public long LastRenewalMilliseconds { get; private set; }
        public bool Pending { get { lock (_sync) return _journal.Pending != null; } }
        public ServerClientTransport(IMatchCredentials credentials, string protocol, string version, int poll, int retry, int timeout, int maxBytes, int capacity)
        {
            _credentials = credentials; _protocol = protocol; _version = version;
            _poll = poll; _retry = retry; _timeout = timeout; _maxBytes = maxBytes; _capacity = capacity;
            var journalName = credentials is IMatchSocketFactory custom ? custom.JournalFileName : credentials is PlayFabMatchCredentials remote ? remote.JournalFileName : "intent-" + credentials.Side + ".json";
            _journal = new ClientIntentJournal(credentials is IMatchIntentLocation persistent ? persistent.IntentJournalPath : Path.Combine(credentials.RunDirectory, journalName));
        }
        public void Start() { if (_task != null) throw new InvalidOperationException(); _task = Task.Run(Run); }
        public bool TryTake(out ReceivedServerFrame frame)
        {
            lock (_sync) { frame = _frames.Count == 0 ? null : _frames.Dequeue(); return frame != null; }
        }
        public void Submit(ServerFrame frame, string kind, int index)
        {
            lock (_sync)
            {
                if (_suspended || !_connected || _journal.Pending != null || frame.CatchingUp || frame.Committed || frame.Phase != "Draft") return;
                var payload = new JObject { ["Token"] = frame.ChoiceToken };
                if (kind == "Choose") payload["OfferIndex"] = index;
                _journal.Begin(new JObject { ["MatchId"] = frame.MatchId, ["RoundId"] = frame.Round.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["ContentVersion"] = frame.ContentVersion, ["OperationId"] = Guid.NewGuid().ToString("N"), ["Sequence"] = _journal.Sequence + 1,
                    ["CommandKind"] = kind, ["Payload"] = payload.ToString(Formatting.None) });
            }
        }
        public void DisconnectForValidation(double seconds)
        {
            lock (_sync) { _pauseUntil = ReceivedServerFrame.Clock + seconds; _connected = false; _connectionLifetime?.Cancel(); _socket?.Abort(); }
        }
        public void SetSuspended(bool suspended)
        {
            lock (_sync)
            {
                if (_disposed) return;
                _suspended = suspended;
                if (suspended) { _connected = false; _frames.Clear(); _connectionLifetime?.Cancel(); _socket?.Abort(); }
            }
        }
        async Task Run()
        {
            var token = _lifetime.Token;
            while (!token.IsCancellationRequested)
            {
                try
                {
                    while (_suspended || ReceivedServerFrame.Clock < _pauseUntil) await Task.Delay(_poll, token).ConfigureAwait(false);
                    SetDiagnosticStage(StageAcquire);
                    var access = await _credentials.AcquireAsync(token).ConfigureAwait(false);
                    SetDiagnosticStage(StageConnect);
                    using (var socket = await Connect(access, token).ConfigureAwait(false))
                    using (var connection = CancellationTokenSource.CreateLinkedTokenSource(token))
                    {
                        lock (_sync) { _socket = socket; _connectionLifetime = connection; }
                        Task<MatchAccess> renewal = null;
                        double renewalStarted = 0;
                        try
                        {
                            SetDiagnosticStage(StageHello);
                            var hello = await Exchange(socket, new JObject { ["Kind"] = "Hello", ["Protocol"] = _protocol, ["ContentVersion"] = _version }, connection.Token).ConfigureAwait(false);
                            if (hello.Value<string>("Kind") != "Welcome" || hello.Value<string>("Protocol") != _protocol || hello.Value<string>("ContentVersion") != _version || hello.Value<int>("Side") != Side)
                                throw new InvalidDataException("Server/client content or protocol mismatch.");
                            if (hello.Value<string>("SessionId") != access.SessionId || hello.Value<string>("StreamId") != access.StreamId || hello.Value<int>("Generation") != access.Generation)
                                throw new InvalidDataException("Access session binding mismatch.");
                            var matchId = hello.Value<string>("MatchId");
                            if (matchId != access.MatchId) throw new InvalidDataException("Access match mismatch.");
                            if (_matchId != null && _matchId != matchId) throw new InvalidDataException("Match identity changed during reconnect.");
                            _matchId = matchId;
                            lock (_sync)
                            {
                                if (_journal.Pending != null && _journal.Pending.Value<string>("MatchId") != matchId) throw new InvalidDataException("Pending intent belongs to another match.");
                                _journal.BindStream(access.StreamId, hello.Value<long>("NextSequence"));
                            }
                            _access = access;
                            var parallelRefresh = hello["ParallelAccessRefresh"]?.Type == JTokenType.Boolean && hello.Value<bool>("ParallelAccessRefresh");
                            if (parallelRefresh)
                            {
                                var enabled = await Exchange(socket, new JObject { ["Kind"] = "EnableParallelRefresh" }, connection.Token).ConfigureAwait(false);
                                if (!IsControl(enabled, "ParallelRefreshEnabled")) throw new InvalidDataException("Refresh negotiation failed.");
                            }
                            Connections++;
                            // A terminal match may publish only one unchanged native state. Reconcile
                            // the durable intent before presentation/auto-exit can consume that result.
                            await ReconcilePending(socket, connection.Token).ConfigureAwait(false);
                            long? cursor = null;
                            bool reset = true;
                            while (!token.IsCancellationRequested)
                            {
                                if (_suspended) break;
                                if (parallelRefresh && renewal == null && ReceivedServerFrame.Clock >= access.RefreshAt)
                                {
                                    renewalStarted = ReceivedServerFrame.Clock;
                                    renewal = AcquireForRefresh(connection.Token);
                                }
                                if (renewal != null && renewal.IsCompleted)
                                {
                                    SetDiagnosticStage(StageReauthenticate);
                                    access = await ApplyRenewedAccess(socket, access, await renewal.ConfigureAwait(false), connection.Token).ConfigureAwait(false);
                                    LastRenewalMilliseconds = (long)((ReceivedServerFrame.Clock - renewalStarted) * 1000);
                                    _access = access;
                                    renewal = null;
                                }
                                if (!parallelRefresh && ReceivedServerFrame.Clock >= access.RefreshAt)
                                {
                                    SetDiagnosticStage(StageReauthenticate);
                                    access = await Reauthenticate(socket, access, connection.Token).ConfigureAwait(false);
                                    _access = access;
                                    continue;
                                }
                                var pollStarted = ReceivedServerFrame.Clock;
                                SetDiagnosticStage(StagePoll);
                                var result = await Exchange(socket, new JObject { ["Kind"] = "Poll", ["AfterEventSequence"] = cursor.HasValue ? new JValue(cursor.Value) : JValue.CreateNull() }, connection.Token).ConfigureAwait(false);
                                var pollTiming = _lastExchangeTiming;
                                LastPollMilliseconds = (long)((ReceivedServerFrame.Clock - pollStarted) * 1000);
                                if (parallelRefresh && IsControl(result, "AccessRefreshRequired"))
                                {
                                    // The server has already revoked the old access. It sends no state
                                    // until this socket proves the replacement; never issue another Poll.
                                    if (renewal == null)
                                    {
                                        // Access may have expired or been rotated outside this loop.
                                        // Re-prove the same binding; never answer the hint with a Poll.
                                        renewalStarted = ReceivedServerFrame.Clock;
                                        renewal = AcquireForRefresh(connection.Token);
                                    }
                                    SetDiagnosticStage(StageReauthenticate);
                                    access = await ApplyRenewedAccess(socket, access, await renewal.ConfigureAwait(false), connection.Token).ConfigureAwait(false);
                                    LastRenewalMilliseconds = (long)((ReceivedServerFrame.Clock - renewalStarted) * 1000);
                                    _access = access;
                                    renewal = null;
                                    continue;
                                }
                                if (result.Value<string>("Kind") != "Snapshot") throw new InvalidDataException("Expected snapshot.");
                                var frame = new ServerFrame((JObject)result["Snapshot"]);
                                if (frame.MatchId != _matchId || frame.ContentVersion != _version || frame.Revision < _latestRevision || frame.Fault != null)
                                    throw new InvalidDataException("Invalid or faulted server state: match=" + (frame.MatchId == _matchId) + ", content=" + (frame.ContentVersion == _version) + ", revision=" + frame.Revision + ", minimum=" + _latestRevision + ", fault=" + (frame.Fault != null));
                                _latestRevision = frame.Revision;
                                lock (_sync)
                                {
                                    if (_suspended) break;
                                    if (_frames.Count >= _capacity) { _frames.Clear(); reset = true; }
                                    _frames.Enqueue(new ReceivedServerFrame(frame, reset || frame.ResyncRequired, pollTiming,
                                        Connections, access.Generation, LastRenewalMilliseconds, access.StreamId));
                                }
                                reset = false; cursor = frame.EventSequence;
                                // A due server tick is not a transport failure. Command admission still checks CatchingUp.
                                _connected = !_suspended;
                                JObject command;
                                lock (_sync) command = _journal.Pending == null ? null : (JObject)_journal.Pending.DeepClone();
                                // Pending intent stays journalled while HTTP may revoke the old token.
                                // Poll has a negotiated renewal handoff; Command deliberately does not.
                                if (!_suspended && renewal == null && command != null && !frame.CatchingUp)
                                {
                                    await ReconcilePending(socket, connection.Token).ConfigureAwait(false);
                                }
                                // The configured period includes the exchange, rather than adding another
                                // full sleep after its round trip. Slow exchanges never cause catch-up bursts.
                                var delay = RemainingPollDelay(_poll, pollStarted, ReceivedServerFrame.Clock);
                                if (delay > 0) await Task.Delay(delay, connection.Token).ConfigureAwait(false);
                            }
                        }
                        finally
                        {
                            lock (_sync) { _connectionLifetime = null; }
                            connection.Cancel();
                            if (renewal != null) ObserveFault(renewal);
                        }
                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
                catch (System.Security.Authentication.AuthenticationException) { _failure = "Server TLS authentication failed."; break; }
                catch (MatchAuthenticationException error) { _failure = error.Message; break; }
                catch (RenewSessionException error) { WriteTransportDiagnostic(error); /* Expired access: reacquire with the login provider and keep the exact pending intent. */ }
                catch (InvalidDataException error) { _failure = error.Message; break; }
                catch (JsonException) { _failure = "Invalid server message."; break; }
                catch (IOException) { _failure = "Local intent storage failed."; break; }
                catch (Exception error) when (error is WebSocketException || error is HttpRequestException || error is OperationCanceledException || error is ObjectDisposedException)
                {
                    WriteTransportDiagnostic(error);
                    /* Transient connection failure: keep the exact pending intent, reacquire access and request full state. */
                }
                catch (Exception) { _failure = "Client transport failed."; break; }
                finally
                {
                    _connected = false;
                    lock (_sync) { _socket = null; _frames.Clear(); }
                }
                try { await Task.Delay(_retry, token).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
            }
            _credentials.Dispose();
            _lifetime.Dispose();
        }
        async Task ReconcilePending(WebSocket socket, CancellationToken token)
        {
            JObject command;
            lock (_sync) command = _journal.Pending == null ? null : (JObject)_journal.Pending.DeepClone();
            if (command == null || _suspended) return;
            SetDiagnosticStage(StageCommand);
            var ack = await Exchange(socket, new JObject { ["Kind"] = "Command", ["Command"] = command }, token).ConfigureAwait(false);
            if (ack.Value<string>("Kind") != "Ack") throw new InvalidDataException("Expected acknowledgement.");
            lock (_sync) _journal.Acknowledge(ack.Value<string>("OperationId"), ack["Reply"].Value<string>("Code"));
            if (DiagnosticsEnabled)
            {
                var receipt = new JObject { ["OperationId"] = ack["OperationId"], ["Sequence"] = command["Sequence"], ["Code"] = ack["Reply"]["Code"] };
                try { File.AppendAllText(Path.Combine(_credentials.RunDirectory, "command-receipts.jsonl"), receipt.ToString(Formatting.None) + Environment.NewLine); }
                catch (IOException) { /* Optional QA evidence cannot fail a committed command. */ }
                catch (UnauthorizedAccessException) { }
            }
        }
        async Task<WebSocket> Connect(MatchAccess access, CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(_timeout);
                try
                {
                    if (_credentials is IMatchSocketFactory custom) return await custom.ConnectAsync(access, limit.Token).ConfigureAwait(false);
                    if (_credentials is LocalProcessCredentials)
                        return await LocalTlsSocket.ConnectAsync(_credentials, access, limit.Token).ConfigureAwait(false);
                    // Remote credentials require a public DNS WSS endpoint and platform TLS
                    // validation. The pinned loopback QA socket has its own strict endpoint.
                    if (_credentials.Endpoint.Scheme != "wss" || _credentials.Endpoint.HostNameType != UriHostNameType.Dns || _credentials.Endpoint.IsLoopback)
                        throw new InvalidDataException("Invalid remote TLS endpoint.");
                    var remote = new ClientWebSocket();
                    try
                    {
                        remote.Options.Proxy = null;
                        remote.Options.SetRequestHeader("Authorization", "Bearer " + access.Token);
                        await remote.ConnectAsync(_credentials.Endpoint, limit.Token).ConfigureAwait(false);
                        return remote;
                    }
                    catch { remote.Dispose(); throw; }
                }
                catch (System.Net.Sockets.SocketException error) { throw new WebSocketException("Connection failed.", error); }
                catch (InvalidDataException) { throw; }
                catch (IOException error) { throw new WebSocketException("Connection interrupted.", error); }
            }
        }
        async Task<MatchAccess> Reauthenticate(WebSocket socket, MatchAccess current, CancellationToken token)
        {
            var started = ReceivedServerFrame.Clock;
            var renewed = await _credentials.AcquireAsync(token).ConfigureAwait(false);
            var result = await ApplyRenewedAccess(socket, current, renewed, token).ConfigureAwait(false);
            LastRenewalMilliseconds = (long)((ReceivedServerFrame.Clock - started) * 1000);
            return result;
        }
        async Task<MatchAccess> AcquireForRefresh(CancellationToken token)
        {
            using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                deadline.CancelAfter(_timeout);
                // Some providers execute synchronous work before their first await. Keep it
                // off the socket loop, and bound providers that fail to honour cancellation.
                var task = Task.Run(() => _credentials.AcquireAsync(deadline.Token), deadline.Token);
                try
                {
                    var timeout = Task.Delay(_timeout, token);
                    if (await Task.WhenAny(task, timeout).ConfigureAwait(false) != task)
                        throw new OperationCanceledException("Access refresh deadline elapsed.", token);
                    token.ThrowIfCancellationRequested();
                    return await task.ConfigureAwait(false);
                }
                finally { deadline.Cancel(); ObserveFault(task); }
            }
        }
        static void ObserveFault(Task task)
        {
            _ = task.ContinueWith(completed => { var ignored = completed.Exception; }, CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        static bool IsControl(JObject value, string kind) => value.Count == 1 && value["Kind"]?.Type == JTokenType.String && value.Value<string>("Kind") == kind;
        async Task<MatchAccess> ApplyRenewedAccess(WebSocket socket, MatchAccess current, MatchAccess renewed, CancellationToken token)
        {
            if (renewed.MatchId != _matchId || renewed.MatchId != current.MatchId || renewed.StreamId != current.StreamId || renewed.Generation < current.Generation)
                throw new InvalidDataException("Renewed access changed the active match identity.");
            var reply = await Exchange(socket, new JObject { ["Kind"] = "Reauthenticate", ["AccessToken"] = renewed.Token }, token).ConfigureAwait(false);
            if (reply.Value<string>("Kind") != "Reauthenticated" || reply.Value<string>("SessionId") != renewed.SessionId || reply.Value<string>("StreamId") != renewed.StreamId ||
                reply.Value<int>("Generation") != renewed.Generation || reply.Value<string>("MatchId") != renewed.MatchId || reply.Value<int>("Side") != Side)
                throw new InvalidDataException("Renewed access session binding mismatch.");
            return renewed;
        }
        internal static int RemainingPollDelay(int periodMilliseconds, double started, double now)
            => (int)Math.Ceiling(Math.Max(0, periodMilliseconds - Math.Max(0, now - started) * 1000));
        async Task<JObject> Exchange(WebSocket socket, JObject message, CancellationToken token)
        {
            using (var limit = CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                limit.CancelAfter(_timeout);
                _diagnosticPhase = PhaseSend;
                var startedAt = ReceivedServerFrame.Clock;
                var bytes = Encoding.UTF8.GetBytes(message.ToString(Formatting.None));
                await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, limit.Token).ConfigureAwait(false);
                var sentAt = ReceivedServerFrame.Clock;
                if (_credentials is LocalProcessCredentials && !_restartRequested && Side == 0 && message.Value<string>("Kind") == "Command" && Environment.GetEnvironmentVariable("TD_LOCAL_AUTO") == "1" && Environment.GetEnvironmentVariable("TD_LOCAL_RESTARTED") != "1")
                {
                    _restartRequested = true;
                    // QA only: ask the owning launcher to kill this process with an unacknowledged intent on disk.
                    File.WriteAllText(Path.Combine(_credentials.RunDirectory, "restart-request-0"), "sent-before-ack");
                    await Task.Delay(_timeout, limit.Token).ConfigureAwait(false);
                }
                var buffer = new byte[Math.Min(16384, _maxBytes)];
                using (var stream = new MemoryStream())
                {
                    _diagnosticPhase = PhaseReceive;
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), limit.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            if ((int?)result.CloseStatus == 4401) throw new RenewSessionException((int?)result.CloseStatus);
                            if ((int?)result.CloseStatus == 4400 || result.CloseStatus == WebSocketCloseStatus.PolicyViolation || result.CloseStatus == WebSocketCloseStatus.InvalidPayloadData)
                                throw new InvalidDataException("Server rejected the protocol.");
                            throw new WebSocketException("Connection ended.");
                        }
                        if (result.MessageType != WebSocketMessageType.Text) throw new InvalidDataException("Binary server message rejected.");
                        if (stream.Length + result.Count > _maxBytes) throw new InvalidDataException("Server message exceeds limit.");
                        stream.Write(buffer, 0, result.Count);
                    } while (!result.EndOfMessage);
                    var wireCompletedAt = ReceivedServerFrame.Clock;
                    var parsed = ServerWire.Parse(new UTF8Encoding(false, true).GetString(stream.ToArray()));
                    _lastExchangeTiming = new ServerExchangeTiming(startedAt, sentAt, wireCompletedAt, ReceivedServerFrame.Clock, (int)stream.Length);
                    return parsed;
                }
            }
        }
        void SetDiagnosticStage(string stage)
        {
            _diagnosticStage = stage;
            _diagnosticPhase = null;
            _diagnosticStopwatch.Restart();
        }
        bool DiagnosticsEnabled
        {
            get
            {
#if UNITY_ANDROID && TANKDRAFT_REMOTE_ANDROID_QA && !UNITY_EDITOR
                return true;
#else
                return Environment.GetEnvironmentVariable("TD_LOCAL_AUTO") == "1";
#endif
            }
        }
        void WriteTransportDiagnostic(Exception error)
        {
            if (!DiagnosticsEnabled || System.Threading.Interlocked.Increment(ref _diagnosticCount) > 32) return;
            var diagnostic = new JObject
            {
                ["Stage"] = _diagnosticStage,
                ["Phase"] = _diagnosticPhase == null ? JValue.CreateNull() : new JValue(_diagnosticPhase),
                ["DurationMs"] = _diagnosticStopwatch.ElapsedMilliseconds,
                ["ExceptionType"] = error.GetType().Name,
                ["Connections"] = Connections,
                ["AuthGeneration"] = AuthGeneration,
                ["Suspended"] = _suspended,
                ["LifetimeCancelled"] = _lifetime.IsCancellationRequested
            };
            for (var current = error; current != null; current = current.InnerException)
            {
                if (current is WebSocketException webSocket && webSocket.WebSocketErrorCode != WebSocketError.Success)
                    diagnostic["WebSocketErrorCode"] = (int)webSocket.WebSocketErrorCode;
                if (current is System.Net.Sockets.SocketException socket)
                    diagnostic["SocketErrorCode"] = (int)socket.SocketErrorCode;
                if (current is RenewSessionException renew && renew.CloseStatus.HasValue)
                    diagnostic["WebSocketCloseStatus"] = renew.CloseStatus.Value;
            }
            try { File.AppendAllText(Path.Combine(_credentials.RunDirectory, "transport-diagnostic-" + Side + ".jsonl"), diagnostic.ToString(Formatting.None) + Environment.NewLine); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
            lock (_sync) _socket?.Abort();
            // Do not block Unity's main thread; the task owns and disposes its socket.
        }
    }
}
