using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace TankDraft.Match.ServerClient
{
    public sealed class NetworkQueueViewModel
    {
        public readonly string Title, Body, ActionLabel;
        public readonly Action Action;
        public NetworkQueueViewModel(string title, string body, string label, Action action)
        { Title = title; Body = body; ActionLabel = label; Action = action; }
    }
    public sealed class NetworkQueuePresentation
    {
        public readonly Action<NetworkQueueViewModel> Show;
        public readonly Action Hide;
        public NetworkQueuePresentation(Action<NetworkQueueViewModel> show, Action hide) { Show = show; Hide = hide; }
    }

    // Account-bound queue intents are independent from the short-lived per-match access stream.
    public sealed class NetworkMatchLauncher : IMatchLauncher, IStartable, ITickable, IDisposable
    {
        readonly NetworkQueueSettings _settings;
        readonly NetworkQueuePresentation _view;
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        HttpClient _http;
        Task<JObject> _request;
        string _operation, _ticket, _retryOperation;
        JObject _payload, _retryPayload;
        ProfileSnapshot _pendingProfile;
        double _pollAt, _paintAt, _deadline;
        bool _started, _searching, _disposed, _loading, _error, _cancelRequested;
        public NetworkMatchLauncher(NetworkQueueSettings settings, NetworkQueuePresentation view) { _settings = settings; _view = view; }
        public void Start()
        {
            if (_started) return;
            _started = true; _settings.Validate();
            if (!Available) return;
            var pin = Environment.GetEnvironmentVariable("TD_LOCAL_TLS_PIN");
            var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false, UseProxy = false };
            handler.ServerCertificateCustomValidationCallback = (_, cert, chain, errors) => LocalTlsPin.Validate(pin, cert, chain, errors, DateTime.UtcNow);
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(5) };
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Environment.GetEnvironmentVariable("TD_QUEUE_GRANT"));
            var leave = Environment.GetEnvironmentVariable("TD_QUEUE_LEAVE_MATCH");
            Begin(string.IsNullOrEmpty(leave) ? "status" : "leave", string.IsNullOrEmpty(leave) ? new JObject() : new JObject { ["MatchId"] = leave });
        }
        static bool Available => Environment.GetEnvironmentVariable("TD_QUEUE_MODE") == "1" && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TD_QUEUE_GRANT"));
        public bool TryLaunch(ProfileSnapshot profile, out string reason)
        {
            reason = null;
            if (_disposed || _loading) return true;
            Start();
            if (!Available)
            { _view.Show(new NetworkQueueViewModel(_settings.ErrorTitle, _settings.NoServer, _settings.Close, _view.Hide)); return true; }
            if (_searching || _error) return true;
            if (_request != null) { _pendingProfile = profile; return true; }
            Join(profile); return true;
        }
        void Join(ProfileSnapshot profile)
        {
            _pendingProfile = null;
            Begin("join", new JObject { ["RequestId"] = Guid.NewGuid().ToString("N"), ["ContentVersion"] = _settings.ContentVersion,
                ["Deck"] = new JArray(profile.UnitIds), ["OrderId"] = profile.OrderIds.FirstOrDefault() ?? "" });
        }
        void Begin(string operation, JObject payload)
        {
            if (_disposed || _loading || _request != null) return;
            _operation = operation; _payload = payload; _error = false;
            if (operation != "status" || !_searching)
                _view.Show(new NetworkQueueViewModel(_settings.SearchTitle, _settings.Connecting, "", null));
            _request = Send(operation, payload);
        }
        async Task<JObject> Send(string operation, JObject payload)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Post, "https://127.0.0.1:18783/v1/queue/" + operation))
            {
                request.Content = new StringContent(payload.ToString(Formatting.None), Encoding.UTF8, "application/json");
                using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, _stop.Token).ConfigureAwait(false))
                {
                    if (response.StatusCode != HttpStatusCode.OK) throw new HttpRequestException("Queue request rejected: " + (int)response.StatusCode);
                    using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                    using (var bytes = new MemoryStream())
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(_stop.Token))
                    {
                        timeout.CancelAfter(5000);
                        var buffer = new byte[4096]; int read;
                        while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, timeout.Token).ConfigureAwait(false)) != 0)
                        { if (bytes.Length + read > 8192) throw new InvalidDataException("Queue response too large."); bytes.Write(buffer, 0, read); }
                        return ServerWire.Parse(new UTF8Encoding(false, true).GetString(bytes.ToArray()));
                    }
                }
            }
        }
        public void Tick()
        {
            if (_disposed || _loading) return;
            var now = ReceivedServerFrame.Clock;
            if (_request != null && _request.IsCompleted)
            {
                var task = _request; _request = null;
                try
                {
                    var result = task.GetAwaiter().GetResult();
                    if (_operation == "leave") Environment.SetEnvironmentVariable("TD_QUEUE_LEAVE_MATCH", null);
                    Accept(result, now);
                }
                catch (Exception)
                {
                    _error = true; _retryOperation = _operation; _retryPayload = _payload;
                    _view.Show(new NetworkQueueViewModel(_settings.ErrorTitle, _settings.ErrorBody, _settings.Retry, Retry));
                }
            }
            if (_error || _loading) return;
            if (_searching && now >= _paintAt)
            {
                _paintAt = now + .2;
                _view.Show(new NetworkQueueViewModel(_settings.SearchTitle, string.Format(_settings.SearchFormat, Math.Max(0, (int)Math.Ceiling(_deadline - now))), _settings.Cancel, Cancel));
            }
            if (_request == null && _searching)
            {
                if (_cancelRequested) Begin("cancel", new JObject { ["TicketId"] = _ticket });
                else if (now >= _pollAt) Begin("status", new JObject());
            }
        }
        void Accept(JObject data, double now)
        {
            switch (data.Value<string>("State"))
            {
                case "Idle":
                    _searching = false; _cancelRequested = false; _ticket = null; _view.Hide();
                    if (_pendingProfile != null) Join(_pendingProfile);
                    break;
                case "Searching":
                    _ticket = data.Value<string>("TicketId");
                    var remaining = data.Value<double>("RemainingSeconds");
                    if (string.IsNullOrEmpty(_ticket) || _ticket.Length > 128 || double.IsNaN(remaining) || remaining < 0 || remaining > 60) throw new InvalidDataException("Invalid queue ticket.");
                    _pendingProfile = null; _searching = true; _deadline = now + remaining; _pollAt = now + _settings.PollMilliseconds / 1000d; _paintAt = 0;
                    break;
                case "Matched":
                    var id = data.Value<string>("MatchId"); var grant = data.Value<string>("MatchGrant"); var side = data.Value<int>("Side"); var kind = data.Value<string>("OpponentKind");
                    if (id == null || !id.StartsWith("queue-", StringComparison.Ordinal) || id.Length != 38 || !Guid.TryParseExact(id.Substring(6), "N", out _) || side < 0 || side > 1 ||
                        grant == null || grant.Length != 43 || grant.Any(c => !(char.IsLetterOrDigit(c) || c == '-' || c == '_')) || kind != "Human" && kind != "Bot")
                        throw new InvalidDataException("Invalid queue assignment.");
                    var directory = Path.Combine(Environment.GetEnvironmentVariable("TD_QUEUE_RUN") ?? throw new InvalidOperationException("Queue run missing."), "matches", id);
                    Directory.CreateDirectory(directory);
                    File.WriteAllText(Path.Combine(directory, "assignment.json"), new JObject { ["MatchId"] = id, ["Side"] = side, ["OpponentKind"] = kind }.ToString(Formatting.None));
                    Environment.SetEnvironmentVariable("TD_LOCAL_GRANT", grant);
                    Environment.SetEnvironmentVariable("TD_LOCAL_SIDE", side.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    Environment.SetEnvironmentVariable("TD_LOCAL_RUN", directory);
                    Environment.SetEnvironmentVariable("TD_QUEUE_MATCH_ID", id);
                    Environment.SetEnvironmentVariable("TD_QUEUE_OPPONENT", kind);
                    if (!UnityEngine.Application.CanStreamedLevelBeLoaded(_settings.MatchSceneName)) throw new InvalidOperationException("Match scene is not in build.");
                    _searching = false; _ticket = null;
                    SceneManager.LoadScene(_settings.MatchSceneName); _loading = true;
                    break;
                default: throw new InvalidDataException("Unknown queue state.");
            }
        }
        void Retry() => Begin(_retryOperation, _retryPayload);
        void Cancel()
        {
            if (!_searching) return;
            _pendingProfile = null; _cancelRequested = true;
            if (_request == null) Begin("cancel", new JObject { ["TicketId"] = _ticket });
        }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true;
            _stop.Cancel(); _http?.Dispose(); _stop.Dispose();
        }
    }
}
