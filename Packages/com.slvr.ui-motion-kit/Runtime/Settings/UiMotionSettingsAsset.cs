using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Serializable project settings. Persistence is delegated to the host project.</summary>
    [CreateAssetMenu(fileName = "SLVR_UIMotion_Settings", menuName = "SLVR/UI Motion/Settings")]
    public sealed class UiMotionSettingsAsset : ScriptableObject
    {
        [SerializeField] private UiMotionTheme activeTheme;
        [SerializeField] private UiMotionQualityProfile qualityProfile;
        [SerializeField, Range(0f, 1f)] private float intensity = 1f;
        [SerializeField] private bool reducedMotion;
        [SerializeField] private bool disableFlashes;
        [SerializeField] private bool idleEffects = true;
        [SerializeField] private bool useUnscaledTime = true;
        [SerializeField] private bool debugOverlay;

        public UiMotionSettingsSnapshot CreateSnapshot() => new UiMotionSettingsSnapshot(
            activeTheme,
            qualityProfile,
            intensity,
            reducedMotion,
            disableFlashes,
            idleEffects,
            useUnscaledTime,
            debugOverlay);
    }
}
