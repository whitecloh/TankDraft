using System;
using TankDraft.BattlePresentation;
using TankDraft.Match.Content;
using TankDraft.Match.Presentation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TankDraft.Match.Bootstrap
{
    public sealed class MatchLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private MatchSettingsAsset _settings;
        [SerializeField]
        private BattleWorldView _world;
        [SerializeField]
        private UIMatchScreen _screen;
        protected override void Configure(IContainerBuilder builder)
        {
            if (!_settings || !_world || !_screen)
                throw new InvalidOperationException("Match scope is missing authored references.");
            builder.RegisterInstance(_settings);
            builder.RegisterInstance(_world);
            builder.RegisterInstance(_screen);
            builder.RegisterEntryPoint<MatchSession>(Lifetime.Singleton).AsSelf();
        }
    }
}
