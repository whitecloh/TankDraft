using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using PlayFab;
using PlayFab.ClientModels;
using TankDraft.Match.ServerClient;

namespace TankDraft.Identity.PlayFab
{
    // Closed-test login for an account already provisioned by the operator. CustomId is a credential,
    // supplied by the platform's private identity store, never an authored asset or a device/model name.
    public sealed class UnityPlayFabSessionSource : IPlayFabSessionSource, IDisposable
    {
        readonly object _sync = new object();
        readonly SynchronizationContext _unity;
        readonly PlayFabClientInstanceAPI _api;
        readonly string _customId, _titleId;
        readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        Task<string> _pending;
        string _ticket, _account;
        long _validUntil;
        bool _disposed;

        public UnityPlayFabSessionSource(string titleId, string provisionedCustomId)
        {
            if (!Valid(titleId, 5, 32) || !Valid(provisionedCustomId, 32, 128))
                throw new ArgumentException("Invalid provisioned PlayFab identity configuration.");
            _unity = SynchronizationContext.Current ?? throw new InvalidOperationException("Create PlayFab session source on the Unity main thread.");
            _titleId = titleId; _customId = provisionedCustomId;
            _api = new PlayFabClientInstanceAPI(new PlayFabApiSettings
            {
                TitleId = titleId, DisableDeviceInfo = true, DisableFocusTimeCollection = true
            });
        }

        public Task<string> AcquireSessionTicketAsync(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_disposed) throw new ObjectDisposedException(nameof(UnityPlayFabSessionSource));
                if (_ticket != null && Stopwatch.GetTimestamp() < _validUntil) return Task.FromResult(_ticket);
                if (_pending != null) return AwaitCaller(_pending, token);
                var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending = completion.Task;
                // A timed-out caller does not spawn another SDK request while the first callback is outstanding.
                _pending.ContinueWith(failed => { var ignored = failed.Exception; }, TaskContinuationOptions.OnlyOnFaulted);
                _unity.Post(_ => StartLogin(completion), null);
                return AwaitCaller(completion.Task, token);
            }
        }

        void StartLogin(TaskCompletionSource<string> completion)
        {
            lock (_sync)
            {
                if (_disposed) { completion.TrySetCanceled(); return; }
                try
                {
                    _api.LoginWithCustomID(new LoginWithCustomIDRequest
                    {
                        TitleId = _titleId, CustomId = _customId, CreateAccount = false
                    }, result => Complete(completion, result), error => Complete(completion, null));
                }
                catch { Complete(completion, null); }
            }
        }

        void Complete(TaskCompletionSource<string> completion, LoginResult result)
        {
            lock (_sync)
            {
                _pending = null;
                if (_disposed) { _api.ForgetAllCredentials(); completion.TrySetCanceled(); return; }
                if (result == null || !Valid(result.PlayFabId, 5, 32) || !ValidTicket(result.SessionTicket) ||
                    result.NewlyCreated || (_account != null && _account != result.PlayFabId))
                {
                    _ticket = null; _api.ForgetAllCredentials();
                    completion.TrySetException(new MatchAuthenticationException("PlayFab login failed; existing provisioned account required."));
                    return;
                }
                _account = result.PlayFabId; _ticket = result.SessionTicket;
                _validUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 60;
                completion.TrySetResult(_ticket);
            }
        }

        async Task<string> AwaitCaller(Task<string> request, CancellationToken token)
        {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(token, _lifetime.Token))
            using (linked.Token.Register(() => canceled.TrySetResult(true)))
            {
                await Task.WhenAny(request, canceled.Task).ConfigureAwait(false);
                linked.Token.ThrowIfCancellationRequested();
                return await request.ConfigureAwait(false);
            }
        }
        static bool Valid(string value, int min, int max)
        {
            if (value == null || value.Length < min || value.Length > max) return false;
            foreach (var c in value) if (!(c >= 'a' && c <= 'z' || c >= 'A' && c <= 'Z' || c >= '0' && c <= '9' || c == '-' || c == '_')) return false;
            return true;
        }
        static bool ValidTicket(string value)
        {
            if (value == null || value.Length < 1 || value.Length > 4096) return false;
            foreach (var c in value) if (c < 0x21 || c > 0x7e) return false;
            return true;
        }
        public void Dispose()
        {
            lock (_sync) { if (_disposed) return; _disposed = true; _ticket = null; }
            // Keep the managed CTS alive until outstanding caller registrations have unwound.
            _lifetime.Cancel();
            _unity.Post(_ => _api.ForgetAllCredentials(), null);
        }
    }
}
