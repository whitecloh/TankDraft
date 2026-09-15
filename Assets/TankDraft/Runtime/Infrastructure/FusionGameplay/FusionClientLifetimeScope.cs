using TankDraft.Match.ServerClient;
using VContainer;
using VContainer.Unity;

namespace TankDraft.Infrastructure.FusionGameplay
{
    // Authored persistent parent of menu/match scopes; no global session service locator.
    public sealed class FusionClientLifetimeScope : LifetimeScope
    {
        protected override void Configure(IContainerBuilder builder)
        {
            var context = new FusionSessionContext();
            builder.RegisterInstance(context);
            builder.RegisterInstance<IMatchCredentialsFactory>(context);
            builder.RegisterComponent(GetComponent<FusionGameClientFlow>());
        }
    }
}
