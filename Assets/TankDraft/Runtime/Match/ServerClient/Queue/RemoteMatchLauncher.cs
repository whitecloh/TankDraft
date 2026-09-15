using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer.Unity;

namespace TankDraft.Match.ServerClient
{
    // UI requests a decision; the authenticated server owns queue, assignment and bot fallback.
    public sealed class RemoteMatchLauncher : IMatchLauncher, IStartable, ITickable, IDisposable
    {
        enum Operation { Join, Status, Cancel, Leave }
        readonly NetworkQueueSettings _settings;
        readonly NetworkQueuePresentation _view;
        readonly CancellationTokenSource _stop = new CancellationTokenSource();
        RemoteQueueClient _queue;
        Task<JObject> _pending;
        Operation _operation;
        string _ticket;
        bool _loading, _disposed, _searching, _cancelRequested, _failed, _autoStarted;
        double _poll, _autoAt;

        public RemoteMatchLauncher(NetworkQueueSettings settings, NetworkQueuePresentation view)
        { _settings = settings; _view = view; }
        public void Start()
        {
            _settings.Validate();
            if (RemoteSessionContext.Current == null) { ShowConfigurationError(); return; }
            _queue = new RemoteQueueClient(RemoteSessionContext.Current);
            _autoAt = ReceivedServerFrame.Clock + 2;
            if (RemoteSessionContext.Current.PendingLeaveMatchId != null) Begin(Operation.Leave);
        }
        public bool TryLaunch(ProfileSnapshot profile, out string reason)
        {
            reason = null;
            if (_disposed || _loading) return true;
            if (_queue == null) { ShowConfigurationError(); return true; }
            if (_pending != null || _searching) return true;
            _failed = false; _cancelRequested = false;
            Begin(RemoteSessionContext.Current.PendingLeaveMatchId != null ? Operation.Leave : Operation.Join);
            return true;
        }
        public void Tick()
        {
            if (_disposed || _loading || _queue == null) return;
            var now = ReceivedServerFrame.Clock;
            if (_pending != null && _pending.IsCompleted)
            {
                var completed = _pending; _pending = null;
                try { Accept(completed.GetAwaiter().GetResult()); }
                catch (Exception error)
                {
                    _searching = false; _failed = true;
                    var conflict = error is RemoteQueueException queueError && queueError.Status == System.Net.HttpStatusCode.Conflict;
                    _view.Show(new NetworkQueueViewModel(_settings.ErrorTitle,
                        conflict ? "Серверный экземпляр завершён; незавершённый матч аннулирован без наград." : _settings.ErrorBody,
                        _settings.Retry, Retry));
                    Write("queue-error.txt", conflict ? "InstanceExpired" : "RequestFailed");
                    WriteDiagnostic(error);
                }
            }
            if (_loading || _failed || _pending != null) return;
            if (_searching)
            {
                if (_cancelRequested && _ticket != null) Begin(Operation.Cancel);
                else if (now >= _poll) Begin(Operation.Status);
            }
            else if (!_autoStarted && Environment.GetEnvironmentVariable("TD_REMOTE_AUTO_QUEUE") == "1" && now >= _autoAt)
            { _autoStarted = true; Environment.SetEnvironmentVariable("TD_REMOTE_AUTO_QUEUE", null); TryLaunch(null, out _); }
        }
        void Retry()
        {
            if (_pending != null || _disposed) return;
            _failed = false;
            if (RemoteSessionContext.Current.InstanceId == null) Begin(Operation.Join);
            else if (RemoteSessionContext.Current.PendingLeaveMatchId != null) Begin(Operation.Leave);
            else Begin(_operation);
        }
        void Begin(Operation operation)
        {
            _operation = operation;
            _view.Show(new NetworkQueueViewModel(_settings.SearchTitle, _settings.Connecting,
                operation == Operation.Leave ? null : _settings.Cancel, operation == Operation.Leave ? (Action)null : Cancel));
            switch (operation)
            {
                case Operation.Join: _pending = _queue.JoinAsync(_stop.Token); break;
                case Operation.Status: _pending = _queue.StatusAsync(_stop.Token); break;
                case Operation.Cancel: _pending = _queue.CancelAsync(_ticket, _stop.Token); break;
                case Operation.Leave: _pending = _queue.LeaveAsync(RemoteSessionContext.Current.PendingLeaveMatchId, _stop.Token); break;
            }
            _poll = ReceivedServerFrame.Clock + _settings.PollMilliseconds / 1000d;
        }
        void Accept(JObject value)
        {
            var state = value.Value<string>("State");
            if (state == "Idle")
            {
                _searching = false; _ticket = null; _cancelRequested = false; _view.Hide();
                Write("queue-idle.txt", _operation.ToString());
                return;
            }
            if (state == "Searching")
            {
                _searching = true; _ticket = value.Value<string>("TicketId");
                _view.Show(new NetworkQueueViewModel(_settings.SearchTitle,
                    string.Format(_settings.SearchFormat, value.Value<int>("RemainingSeconds")), _settings.Cancel, Cancel));
                return;
            }
            var context = RemoteSessionContext.Current;
            context.MatchId = value.Value<string>("MatchId"); context.Side = value.Value<int>("Side");
            if (!Application.CanStreamedLevelBeLoaded(_settings.MatchSceneName)) throw new InvalidOperationException("Match scene is not in build.");
            Write("remote-assignment.json", value.ToString(Newtonsoft.Json.Formatting.None));
            try { File.AppendAllText(Path.Combine(context.RunDirectory, "remote-assignments.jsonl"), value.ToString(Newtonsoft.Json.Formatting.None) + Environment.NewLine); }
            catch { Debug.LogWarning("Remote assignment history could not be saved."); }
            _loading = true; _view.Hide(); SceneManager.LoadScene(_settings.MatchSceneName);
        }
        void Cancel() { if (!_disposed && !_loading) _cancelRequested = true; }
        void ShowConfigurationError() => _view.Show(new NetworkQueueViewModel(_settings.ErrorTitle,
            RemoteSessionContext.BootstrapError ?? "Настройки удалённого теста недоступны.", _settings.Close, _view.Hide));
        void Write(string file, string value)
        {
            try { var dir = RemoteSessionContext.Current.RunDirectory; Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, file), value); }
            catch { Debug.LogWarning("Remote test evidence could not be saved."); }
        }
        void WriteDiagnostic(Exception error)
        {
            var types = new JArray();
            int? status = null;
            for (var current = error; current != null && types.Count < 4; current = current.InnerException)
            {
                types.Add(current.GetType().Name);
                if (current is RemoteQueueException queueError) status = (int)queueError.Status;
            }
            var context = RemoteSessionContext.Current;
            var diagnostic = new JObject
            {
                ["Operation"] = _operation.ToString(),
                ["Stage"] = _queue?.DiagnosticStage ?? "ready",
                ["Phase"] = _queue?.DiagnosticPhase ?? "none",
                ["ExceptionTypes"] = types,
                ["HttpStatus"] = status.HasValue ? new JValue(status.Value) : JValue.CreateNull(),
                ["HasInstance"] = context?.InstanceId != null,
                ["HasLobby"] = context?.LobbyToken != null
            };
            Write("queue-diagnostic.json", diagnostic.ToString(Newtonsoft.Json.Formatting.None));
        }
        public void Dispose()
        { if (_disposed) return; _disposed = true; _stop.Cancel(); _queue?.Dispose(); _stop.Dispose(); }
    }
}
