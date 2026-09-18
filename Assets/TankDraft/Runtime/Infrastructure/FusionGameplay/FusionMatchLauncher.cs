using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TankDraft.Contracts;
using TankDraft.Match.ServerClient;
using UnityEngine;
using UnityEngine.SceneManagement;
using VContainer;
using VContainer.Unity;

namespace TankDraft.Infrastructure.FusionGameplay
{
    public sealed class FusionMatchLauncher : IMatchLauncher, IMenuMatchState, IStartable, ITickable, IDisposable
    {
        readonly NetworkQueueSettings settings;
        readonly NetworkQueuePresentation view;
        readonly FusionSessionContext context;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        FusionQueueClient queue;
        Task<JObject> pending;
        string operation, target, ticket;
        bool searching, cancelRequested, loading, failed, disposed, launchRequested, autoStarted, qaCancelled, menuCaptured;
        double pollAt, autoAt, cancelAt;
        Func<bool> menuReady;
        public bool IsMenuIdle => queue != null && !disposed && !loading && !searching && !failed && pending == null && !launchRequested;
        public void SetMenuReadiness(Func<bool> ready) => menuReady = ready;
        public FusionMatchLauncher(NetworkQueueSettings settings, NetworkQueuePresentation view, FusionSessionContext context)
        { this.settings = settings; this.view = view; this.context = context; }
        public void Start()
        {
            settings.Validate();
            if (settings.ContentVersion != context.ContentVersion) throw new InvalidOperationException("Queue content mismatch.");
            queue = new FusionQueueClient(context.Requests, context.InstanceId, context.ContentVersion);
            autoAt = ReceivedServerFrame.Clock + 2;
            Begin(context.PendingLeave == null ? "Status" : "Leave", context.PendingLeave);
        }
        public bool TryLaunch(ProfileSnapshot profile, out string reason)
        {
            reason = null;
            if (queue == null) { reason = "Подключение ещё не готово. Дождитесь загрузки игры."; return false; }
            if (disposed || loading || searching || failed) return true;
            launchRequested = true;
            if (pending == null) Launch();
            return true;
        }
        void Launch() { launchRequested = false; cancelRequested = false; Begin("Join", null); }
        void Begin(string next, string value)
        {
            if (pending != null || disposed || loading) return;
            operation = next; target = value; failed = false;
            if (!searching || next != "Status") view.Show(new NetworkQueueViewModel(settings.SearchTitle, settings.Connecting,
                next == "Leave" ? "" : settings.Cancel, next == "Leave" ? (Action)null : Cancel));
            pending = queue.Send(next, value, stop.Token);
            pollAt = ReceivedServerFrame.Clock + settings.PollMilliseconds / 1000d;
        }
        public void Tick()
        {
            if (disposed || loading) return;
            var now = ReceivedServerFrame.Clock;
            if (!menuCaptured && menuReady != null && menuReady() && now >= autoAt - .5 && Environment.GetEnvironmentVariable("TD_FUSION_CAPTURE_SCREENSHOTS") == "1")
            { menuCaptured = true; ScreenCapture.CaptureScreenshot(Path.Combine(context.RunDirectory, "menu-" + Guid.NewGuid().ToString("N") + ".png")); }
            if (pending != null && pending.IsCompleted)
            {
                var task = pending; pending = null;
                try { Accept(task.GetAwaiter().GetResult()); }
                catch (FusionTransport.FusionAuthorityException error) when (operation == "Join" && error.Code == "unsupported_battle_loadout")
                {
                    failed = true;
                    view.Show(new NetworkQueueViewModel(settings.UnsupportedLoadoutTitle, settings.UnsupportedLoadoutBody, settings.Close, CloseRejectedLoadout));
                    Evidence("UnsupportedLoadout", null);
                }
                catch
                {
                    failed = true;
                    view.Show(new NetworkQueueViewModel(settings.ErrorTitle, settings.ErrorBody, settings.Retry, Retry));
                    Evidence("RequestFailed", null);
                }
            }
            if (loading || failed || pending != null) return;
            if (searching)
            {
                if (Environment.GetEnvironmentVariable("TD_FUSION_QA_CANCEL") == "1" && !qaCancelled && now >= cancelAt)
                { qaCancelled = true; Cancel(); }
                if (cancelRequested) Begin("Cancel", ticket);
                else if (now >= pollAt) Begin("Status", null);
            }
            else if (launchRequested) Launch();
            else if (!autoStarted && now >= autoAt && menuReady != null && menuReady() && Environment.GetEnvironmentVariable("TD_FUSION_AUTO_QUEUE") == "1")
            { autoStarted = true; TryLaunch(null, out _); }
        }
        void Accept(JObject value)
        {
            var state = value.Value<string>("State");
            Evidence(state, value);
            if (state == "Idle")
            {
                if (operation == "Leave") context.ConfirmLeave();
                if (operation == "Cancel")
                {
                    launchRequested = false;
                    if (qaCancelled) { autoStarted = false; autoAt = ReceivedServerFrame.Clock + 2; }
                }
                searching = false; cancelRequested = false; ticket = null; view.Hide(); return;
            }
            if (state == "Searching")
            {
                if (!searching) cancelAt = ReceivedServerFrame.Clock + 1;
                searching = true; ticket = value.Value<string>("TicketId");
                view.Show(new NetworkQueueViewModel(settings.SearchTitle, string.Format(settings.SearchFormat, value.Value<int>("RemainingSeconds")), settings.Cancel, Cancel));
                return;
            }
            // A match can win the cancel race; never hide or abandon an assigned game locally.
            if (!Application.CanStreamedLevelBeLoaded(settings.MatchSceneName)) throw new InvalidOperationException();
            context.Assign(value); loading = true; view.Hide();
            _ = LoadMatch();
        }
        async Task LoadMatch()
        {
            try
            {
                await context.LoadMatchAsync(settings.MatchSceneName);
            }
            catch { loading = false; failed = true; view.Show(new NetworkQueueViewModel(settings.ErrorTitle, settings.ErrorBody, settings.Retry, Retry)); }
        }
        void Cancel() { if (!loading && !disposed) { cancelRequested = true; launchRequested = false; } }
        void Retry() { if (!disposed && pending == null) Begin(operation, target); }
        void CloseRejectedLoadout()
        {
            if (disposed || pending != null || loading) return;
            failed = false; launchRequested = false; autoStarted = true;
            view.Hide();
        }
        void Evidence(string state, JObject value)
        {
            try { File.AppendAllText(Path.Combine(context.RunDirectory, "queue.jsonl"), new JObject { ["Operation"] = operation, ["State"] = state, ["Value"] = value, ["Utc"] = DateTime.UtcNow.ToString("O") }.ToString(Formatting.None) + Environment.NewLine); }
            catch { Debug.LogWarning("Fusion QA queue evidence unavailable."); }
        }
        public void Dispose() { if (disposed) return; disposed = true; stop.Cancel(); stop.Dispose(); }
    }
}
