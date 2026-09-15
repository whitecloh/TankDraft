using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace TankDraft.Match.ServerClient
{
    public sealed class RemoteQueueClient : IDisposable
    {
        readonly RemoteSessionContext _context;
        readonly HttpClient _http;
        readonly SemaphoreSlim _gate = new SemaphoreSlim(1, 1);
        readonly SemaphoreSlim _wireGate = new SemaphoreSlim(1, 1);
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        readonly TimeSpan _deadline;
        volatile bool _disposed;
        internal string DiagnosticStage { get; private set; } = "ready";
        internal string DiagnosticPhase { get; private set; } = "none";

        public RemoteQueueClient(RemoteSessionContext context) : this(context,
            new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false }) { }
        internal RemoteQueueClient(RemoteSessionContext context, HttpMessageHandler handler, TimeSpan? deadline = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _deadline = deadline ?? TimeSpan.FromSeconds(10);
            if (_deadline <= TimeSpan.Zero || _deadline > TimeSpan.FromMinutes(1)) throw new ArgumentOutOfRangeException(nameof(deadline));
            _http = new HttpClient(handler, true) { Timeout = Timeout.InfiniteTimeSpan };
        }

        public Task<JObject> JoinAsync(CancellationToken token) => Start("join", null, token);
        public Task<JObject> StatusAsync(CancellationToken token) => Start("status", null, token);
        public Task<JObject> CancelAsync(string ticketId, CancellationToken token) => Start("cancel", ticketId, token);
        public Task<JObject> LeaveAsync(string matchId, CancellationToken token) => Start("leave", matchId, token);

        Task<JObject> Start(string action, string id, CancellationToken token)
        {
            if (_disposed) return Task.FromException<JObject>(new ObjectDisposedException(nameof(RemoteQueueClient)));
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token);
            timeout.CancelAfter(_deadline);
            // Even a platform handler doing synchronous work before returning its Task must not
            // block Unity. The deadline starts here, before the work is scheduled.
            return ExecuteScheduled(action, id, timeout);
        }
        async Task<JObject> ExecuteScheduled(string action, string id, CancellationTokenSource timeout)
        {
            using (timeout)
                return await Task.Run(() => Execute(action, id, timeout.Token)).ConfigureAwait(false);
        }
        async Task<JObject> Execute(string action, string id, CancellationToken token)
        {
            await _gate.WaitAsync(token).ConfigureAwait(false);
            try
            {
                token.ThrowIfCancellationRequested();
                DiagnosticPhase = "none";
                if (_context.InstanceId == null) await RefreshReadyAsync(token).ConfigureAwait(false);
                if (_context.LobbyToken == null || Stopwatch.GetTimestamp() >= _context.LobbyValidUntil)
                    await LoginAsync(token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                var body = new JObject { ["InstanceId"] = _context.InstanceId };
                if (action == "join")
                {
                    if (_context.PendingLeaveMatchId != null) throw new InvalidOperationException("Leave must finish before joining.");
                    _context.PendingJoinOperation ??= Guid.NewGuid().ToString("N");
                    body["OperationId"] = _context.PendingJoinOperation;
                    body["ContentVersion"] = _context.ContentVersion;
                }
                if (action == "cancel" || action == "leave")
                {
                    if (!Id(id, action == "cancel" ? 32 : 65)) throw new InvalidDataException("Invalid queue operation.");
                    body[action == "cancel" ? "TicketId" : "MatchId"] = id;
                }
                JObject value;
                SetQueueDiagnosticStage(action);
                try { value = await SendAsync(HttpMethod.Post, "/v1/queue/" + action, _context.LobbyToken, body, token).ConfigureAwait(false); }
                catch (RemoteQueueException error) when (error.Status == HttpStatusCode.Forbidden)
                {
                    token.ThrowIfCancellationRequested();
                    _context.LobbyToken = null;
                    await LoginAsync(token).ConfigureAwait(false);
                    SetQueueDiagnosticStage(action);
                    value = await SendAsync(HttpMethod.Post, "/v1/queue/" + action, _context.LobbyToken, body, token).ConfigureAwait(false);
                }
                token.ThrowIfCancellationRequested();
                ValidateQueue(value);
                if (Text(value, "State") == "Idle")
                {
                    _context.PendingJoinOperation = null;
                    if (action == "leave" && _context.PendingLeaveMatchId == id)
                    { _context.PendingLeaveMatchId = null; _context.MatchId = null; }
                }
                return value;
            }
            catch (RemoteQueueException error) when (error.Status == HttpStatusCode.Conflict)
            {
                token.ThrowIfCancellationRequested();
                _context.InstanceId = null; _context.LobbyToken = null;
                _context.PendingOperation = null; _context.PendingJoinOperation = null;
                _context.PendingLeaveMatchId = null; _context.MatchId = null;
                throw;
            }
            finally { _gate.Release(); }
        }
        async Task RefreshReadyAsync(CancellationToken token)
        {
            DiagnosticStage = "ready";
            var value = await SendAsync(HttpMethod.Get, "/readyz", null, null, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Only(value, "InstanceId", "ContentVersion", "IsDraining", "EconomyWritesEnabled");
            var instance = Text(value, "InstanceId");
            if (!Id(instance, 32) || Text(value, "ContentVersion") != _context.ContentVersion ||
                value["IsDraining"].Type != JTokenType.Boolean || value["EconomyWritesEnabled"].Type != JTokenType.Boolean ||
                value.Value<bool>("EconomyWritesEnabled") || value.Value<bool>("IsDraining")) throw new InvalidDataException("Remote readiness rejected.");
            _context.InstanceId = instance;
        }
        async Task LoginAsync(CancellationToken token)
        {
            DiagnosticStage = "identity"; DiagnosticPhase = "ticket";
            var ticket = await AwaitBounded(Task.Run(() => _context.Tickets.AcquireSessionTicketAsync(token)), token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (ticket == null || ticket.Length < 1 || ticket.Length > 4096 || ticket.Any(c => c < 33 || c > 126)) throw new InvalidDataException("Session ticket rejected.");
            _context.PendingOperation ??= Guid.NewGuid().ToString("N");
            DiagnosticStage = "lobby";
            var value = await SendAsync(HttpMethod.Post, "/v1/lobby", ticket, new JObject { ["OperationId"] = _context.PendingOperation }, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            Only(value, "LobbyToken", "ExpiresInSeconds");
            var lobby = Text(value, "LobbyToken"); var seconds = Integer(value, "ExpiresInSeconds", 1, 120);
            if (lobby.Length != 43 || lobby.Any(c => !(c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '-' || c == '_'))) throw new InvalidDataException("Lobby token rejected.");
            _context.LobbyToken = lobby; _context.LobbyExpiresSeconds = seconds;
            _context.LobbyValidUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * Math.Max(0, seconds - 5);
            _context.PendingOperation = null;
        }
        void SetQueueDiagnosticStage(string action)
        {
            DiagnosticStage = action switch
            {
                "join" => "queue-join", "status" => "queue-status",
                "cancel" => "queue-cancel", "leave" => "queue-leave",
                _ => throw new InvalidOperationException("Invalid queue action.")
            };
        }
        async Task<JObject> SendAsync(HttpMethod method, string path, string bearer, JObject body, CancellationToken token)
        {
            DiagnosticPhase = "wire-gate";
            // Timed-out callers must leave this queue. Only the real I/O task owns a permit;
            // an uncooperative native request can never accumulate more in-flight requests.
            await _wireGate.WaitAsync(token).ConfigureAwait(false);
            Task<JObject> request;
            try
            {
                token.ThrowIfCancellationRequested();
                request = Task.Run(() => SendCoreAsync(method, path, bearer, body, token));
            }
            catch { _wireGate.Release(); throw; }
            return await AwaitBounded(request, token).ConfigureAwait(false);
        }
        async Task<JObject> SendCoreAsync(HttpMethod method, string path, string bearer, JObject body, CancellationToken token)
        {
            try
            {
                token.ThrowIfCancellationRequested();
                using (var request = new HttpRequestMessage(method, new Uri(_context.BaseUri, path)))
                {
                    if (bearer != null) request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearer);
                    if (body != null) request.Content = new StringContent(body.ToString(Newtonsoft.Json.Formatting.None), Encoding.UTF8, "application/json");
                    DiagnosticPhase = "send";
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
                    {
                        token.ThrowIfCancellationRequested(); DiagnosticPhase = "headers";
                        if (response.StatusCode != HttpStatusCode.OK) throw new RemoteQueueException(response.StatusCode, "Remote request rejected.");
                        if (response.Content.Headers.ContentLength > 8192 || response.Content.Headers.ContentType?.MediaType != "application/json") throw new InvalidDataException("Remote response rejected.");
                        DiagnosticPhase = "body";
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var bytes = new MemoryStream())
                        {
                            var buffer = new byte[1024]; int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false)) != 0)
                            {
                                token.ThrowIfCancellationRequested();
                                if (bytes.Length + read > 8192) throw new InvalidDataException("Remote response too large.");
                                bytes.Write(buffer, 0, read);
                            }
                            token.ThrowIfCancellationRequested(); DiagnosticPhase = "parse";
                            return ServerWire.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
                        }
                    }
                }
            }
            finally { _wireGate.Release(); }
        }
        static async Task<T> AwaitBounded<T>(Task<T> request, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true)))
            {
                await Task.WhenAny(request, canceled.Task).ConfigureAwait(false);
                if (token.IsCancellationRequested)
                {
                    _ = request.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                    token.ThrowIfCancellationRequested();
                }
                return await request.ConfigureAwait(false);
            }
        }
        void ValidateQueue(JObject value)
        {
            Only(value, "State", "TicketId", "RemainingSeconds", "MatchId", "Side", "OpponentKind", "InstanceId");
            if (Text(value, "InstanceId") != _context.InstanceId) throw new RemoteQueueException(HttpStatusCode.Conflict, "InstanceExpired");
            var state = Text(value, "State"); var ticket = Text(value, "TicketId"); var remaining = Integer(value, "RemainingSeconds", 0, 10);
            if (state == "Searching")
            { if (!Id(ticket, 32) || !Nulls(value, "MatchId", "Side", "OpponentKind")) throw new InvalidDataException(); }
            else if (state == "Matched")
            {
                var match = Text(value, "MatchId"); var opponent = Text(value, "OpponentKind");
                if (!Id(match, 65) || !match.StartsWith(_context.InstanceId + "-", StringComparison.Ordinal) || ticket != "" || remaining != 0 ||
                    (opponent != "Human" && opponent != "Bot")) throw new InvalidDataException();
                Integer(value, "Side", 0, 1);
            }
            else if (state != "Idle" || ticket != "" || remaining != 0 || !Nulls(value, "MatchId", "Side", "OpponentKind")) throw new InvalidDataException();
        }
        static bool Nulls(JObject value, params string[] names) => names.All(n => value[n].Type == JTokenType.Null);
        static bool Id(string id, int length) => id != null && id.Length == length && id.All(c => c >= '0' && c <= '9' || c >= 'a' && c <= 'f' || c == '-');
        static string Text(JObject value, string name) => value[name]?.Type == JTokenType.String && value.Value<string>(name).Length <= 128 ? value.Value<string>(name) : throw new InvalidDataException("Invalid remote text.");
        static int Integer(JObject value, string name, int min, int max)
        { if (value[name]?.Type != JTokenType.Integer || !int.TryParse(value[name].ToString(), out var number) || number < min || number > max) throw new InvalidDataException("Invalid remote number."); return number; }
        static void Only(JObject value, params string[] names)
        { if (value.Count != names.Length || value.Properties().Any(p => !names.Contains(p.Name))) throw new InvalidDataException("Invalid remote schema."); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            // Cancellation callbacks and platform HTTP cleanup may perform synchronous work.
            _ = Task.Run(() => { try { _lifetime.Cancel(); } finally { _http.Dispose(); } });
        }
    }
    public sealed class RemoteQueueException : Exception
    {
        public HttpStatusCode Status { get; }
        public RemoteQueueException(HttpStatusCode status, string message) : base(message) { Status = status; }
    }
}
