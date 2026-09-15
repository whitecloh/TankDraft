using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using VContainer.Unity;
using TankDraft.Contracts;
using TankDraft.Application;
using TankDraft.Content;
using TankDraft.UI;
using TankDraft.Infrastructure.FusionGameplay;

namespace TankDraft.Bootstrap
{
    // Composition boundary: validate authored data, restore profile, then connect presentation.
    public sealed class MainMenuStartup : IStartable, IDisposable
    {
        private readonly MetaCatalogAsset _catalog;
        private readonly IProfileRepository _repository;
        private readonly IMatchLauncher _matchLauncher;
        private readonly IServerProfileClient _serverProfiles;
        private readonly IServerProgressionClient _serverProgression;
        private readonly UIMainMenuRuntimeRoot _view;
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private bool _disposed;
        private bool _starting;
        public ProfileService Profile { get; private set; }
        public MainMenuPresenter Presenter { get; private set; }
        public bool IsReady { get; private set; }
        public Exception Failure { get; private set; }

        public MainMenuStartup(MetaCatalogAsset catalog, IProfileRepository repository, UIMainMenuRuntimeRoot view, IMatchLauncher matchLauncher, IServerProfileClient serverProfiles = null, IServerProgressionClient serverProgression = null)
        {
            _matchLauncher = matchLauncher;
            _catalog = catalog;
            _repository = repository;
            _view = view;
            _serverProfiles = serverProfiles;
            _serverProgression = serverProgression;
        }

        public void Start()
        {
            if (_serverProfiles != null)
            {
                _ = StartServerAsync();
                return;
            }
            try
            {
                CompleteStartup(null);
            }
            catch (Exception exception)
            {
                Fail(exception);
            }
        }

        private async Task StartServerAsync()
        {
            if (_disposed || _starting || IsReady) return;
            _starting = true;
            Failure = null;
            try
            {
                ProfileSnapshot snapshot = await _serverProfiles.GetAsync(_stop.Token);
                if (_disposed) return;
                // A null snapshot is an explicit gateway legacy-QA response. Network
                // errors never reach this branch and therefore never fall back local.
                CompleteStartup(snapshot);
            }
            catch (OperationCanceledException) when (_disposed)
            {
            }
            catch (Exception exception)
            {
                if (!_disposed) Fail(exception);
            }
            finally { _starting = false; }
        }

        private void CompleteStartup(ProfileSnapshot serverSnapshot)
        {
            _view.Validate();
            var definitions = _catalog.CreateDefinitions();
            Profile = new ProfileService(definitions, _catalog.CreateInitialProfile(), _repository, serverSnapshot == null ? null : _serverProfiles);
            if (serverSnapshot == null) Profile.Initialize();
            else Profile.InitializeServer(serverSnapshot);
            string progressionScope = (_serverProgression as FusionProgressionClient)?.JournalScope;
            Presenter = new MainMenuPresenter(Profile, definitions, _catalog, _view, _matchLauncher,
                new ProgressionPresenter(_serverProgression, Profile, _catalog, _view.Progression, progressionScope), progressionScope);
            Presenter.Initialize();
            IsReady = true;
            if (_matchLauncher is FusionMatchLauncher fusion)
                fusion.SetMenuReadiness(() => !_disposed && IsReady && !Presenter.IsBusy);
            if (Environment.GetEnvironmentVariable("TD_QUEUE_AUTOJOIN") == "1") _matchLauncher.TryLaunch(Profile.Current, out _);
        }

        private void Fail(Exception exception)
        {
            Failure = exception;
            Debug.LogException(exception);
            _view.Screen.Show();
            _view.Hud.Hide();
            _view.Arena.Hide();
            _view.Collection.Hide();
            _view.Details.Hide();
            _view.Message.Bind(_catalog.Text.Get(UiTextId.StartupErrorTitle), _catalog.Text.Get(UiTextId.StartupErrorBody),
                _serverProfiles == null ? string.Empty : _catalog.Text.Get(UiTextId.Retry),
                _serverProfiles == null ? (UnityEngine.Events.UnityAction)null : () => { _ = StartServerAsync(); });
        }

        public void Dispose()
        {
            _disposed = true;
            _stop.Cancel();
            Presenter?.Dispose();
            IsReady = false;
        }
    }
}
