using System;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using UnityEngine;
using VContainer;
using VContainer.Unity;

namespace TankDraft.BattleBootstrap
{
    public sealed class BattlePrototypeLifetimeScope : LifetimeScope
    {
        [SerializeField]
        private BattleScenarioCatalogAsset _scenarioCatalog;
        [SerializeField]
        private BattleViewCatalogAsset _viewCatalog;
        [SerializeField]
        private BattlePresentationSettings _presentation;
        [SerializeField]
        private BattleWorldView _worldView;
        [SerializeField]
        private UIBattlePrototypeScreen _screen;
        protected override void Configure(IContainerBuilder builder)
        {
            if (!_scenarioCatalog || !_viewCatalog || !_presentation || !_worldView || !_screen)
                throw new InvalidOperationException("Battle prototype scope has missing authored references.");
            builder.RegisterInstance(_scenarioCatalog);
            builder.RegisterInstance(_viewCatalog);
            builder.RegisterInstance(_presentation);
            builder.RegisterInstance(_worldView);
            builder.RegisterInstance(_screen);
            builder.RegisterEntryPoint<BattlePrototypeSession>(Lifetime.Singleton).AsSelf();
        }
    }
}