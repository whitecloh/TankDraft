using System.IO;
using UnityEngine;
using VContainer;
using VContainer.Unity;
using TankDraft.Contracts;
using TankDraft.Content;
using TankDraft.Infrastructure;
using TankDraft.UI;
using TankDraft.Match.Content;
using TankDraft.Match.Bootstrap;
using TankDraft.Match.ServerClient;
using TankDraft.Infrastructure.FusionGameplay;

namespace TankDraft.Bootstrap
{
    public sealed class MainMenuLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private MetaCatalogAsset _catalog;
        [SerializeField]
        private MatchSettingsAsset _matchSettings;
        [SerializeField] private NetworkQueueSettings _queueSettings;
        [SerializeField]
        private UIMainMenuRuntimeRoot _uiRoot;
        [SerializeField]
        private LocalProfileSettings _profileSettings;
        protected override void Configure(IContainerBuilder builder)
        {
            if (!_catalog || !_uiRoot || !_profileSettings || !_matchSettings || !_queueSettings)
                throw new System.InvalidOperationException("Main menu scope is missing authored references.");
            builder.RegisterInstance(_catalog);
            builder.RegisterInstance(_queueSettings);
            builder.RegisterInstance(new NetworkQueuePresentation(vm => _uiRoot.Message.Bind(vm.Title, vm.Body, vm.ActionLabel, vm.Action == null ? null : new UnityEngine.Events.UnityAction(vm.Action)), () => _uiRoot.Message.Hide()));
            if (builder.Exists(typeof(FusionSessionContext), true, true))
            {
                builder.RegisterEntryPoint<FusionMatchLauncher>(Lifetime.Singleton).AsSelf().As<IMatchLauncher>();
                builder.Register<FusionProfileClient>(Lifetime.Singleton).As<IServerProfileClient>();
                builder.Register<FusionProgressionClient>(Lifetime.Singleton).As<IServerProgressionClient>();
            }
            else if (RemoteSessionContext.IsRequested)
                builder.RegisterEntryPoint<RemoteMatchLauncher>(Lifetime.Singleton).AsSelf().As<IMatchLauncher>();
            else
                builder.RegisterEntryPoint<NetworkMatchLauncher>(Lifetime.Singleton).AsSelf().As<IMatchLauncher>();
            builder.RegisterInstance(_uiRoot);
            builder.RegisterInstance<IProfileRepository>(new JsonProfileRepository(_profileSettings.GetPath()));
            builder.RegisterEntryPoint<MainMenuStartup>(Lifetime.Singleton).AsSelf();
        }
    }
}
