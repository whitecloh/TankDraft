using System;
using System.Threading.Tasks;
using Fusion;

namespace TankDraft.Infrastructure.FusionTransport
{
    // Diagnostic gateway has no network-loaded scenes. Production scene routing remains separate.
    public sealed class FusionDiagnosticSceneManager : NetworkSceneManagerDefault
    {
        protected override GetAddressableScenesResult GetAddressableScenes() => Task.FromResult(Array.Empty<string>());
    }
}
