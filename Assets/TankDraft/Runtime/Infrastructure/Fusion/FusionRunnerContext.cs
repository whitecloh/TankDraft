using UnityEngine;

namespace TankDraft.Infrastructure.FusionTransport
{
    // Set by the owning bootstrap before StartGame; no static cross-runner lookup.
    public sealed class FusionRunnerContext : MonoBehaviour
    {
        public FusionDedicatedBootstrap Owner { get; set; }
    }
}
