using System;
using TankDraft.BattlePresentation;
using TankDraft.Match.Presentation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TankDraft.Match.ServerClient
{
    public sealed class ServerClientScope : LifetimeScope
    {
        [SerializeField] ServerClientSettings _settings;
        [SerializeField] BattleWorldView _world;
        [SerializeField] UIMatchScreen _screen;
        void OnApplicationPause(bool paused)
        {
            if (Application.isMobilePlatform && Container != null)
                Container.Resolve<ServerClientSession>().SetApplicationPaused(paused);
        }
        protected override void Configure(IContainerBuilder builder)
        {
            if (!_settings || !_world || !_screen) throw new InvalidOperationException("Server client requires authored settings, world and screen.");
            builder.RegisterInstance(_settings); builder.RegisterInstance(_world); builder.RegisterInstance(_screen);
            if (RemoteSessionContext.Current != null)
                builder.RegisterInstance<IMatchCredentialsFactory>(RemoteSessionContext.Current);
            else if (!builder.Exists(typeof(IMatchCredentialsFactory), true, true))
                builder.Register<LocalMatchCredentialsFactory>(Lifetime.Singleton).As<IMatchCredentialsFactory>();
            builder.RegisterEntryPoint<ServerClientSession>(Lifetime.Singleton).AsSelf();
        }
    }
}
