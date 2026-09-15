using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Base class that resolves settings, presets and lifecycle for UI motion components.</summary>
    [RequireComponent(typeof(UiMotionLifecycle))]
    public abstract class UiMotionElement : MonoBehaviour
    {
        [SerializeField] private UiMotionSettingsAsset settingsAsset;
        [SerializeField] private UiMotionPreset preset;

        private UiMotionLifecycle lifecycle;

        protected UiMotionLifecycle Lifecycle
        {
            get
            {
                if (lifecycle == null)
                {
                    lifecycle = GetComponent<UiMotionLifecycle>();
                }

                return lifecycle;
            }
        }

        protected UiMotionPreset Preset => preset;
        protected UiMotionSettingsSnapshot Settings => settingsAsset != null
            ? settingsAsset.CreateSnapshot()
            : UiMotionDefaults.Settings;

        protected float ResolveDuration(UiMotionTiming fallback = UiMotionTiming.Normal)
        {
            UiMotionTiming timing = preset != null ? preset.Timing : fallback;
            return Settings.ResolveDuration(timing);
        }

        protected UiMotionTweenPolicy ResolvePolicy(
            UiMotionEase fallbackEase = UiMotionEase.OutCubic,
            UiMotionDisableBehaviour disableBehaviour = UiMotionDisableBehaviour.Kill)
        {
            return ResolvePolicy(Settings, fallbackEase, disableBehaviour);
        }

        protected UiMotionTweenPolicy ResolvePolicy(
            UiMotionSettingsSnapshot settings,
            UiMotionEase fallbackEase = UiMotionEase.OutCubic,
            UiMotionDisableBehaviour disableBehaviour = UiMotionDisableBehaviour.Kill)
        {
            UiMotionEase ease = preset != null
                ? preset.ResolveEase(settings.Theme)
                : settings.Theme != null ? settings.Theme.StandardEase : fallbackEase;
            return new UiMotionTweenPolicy(ease, settings.UseUnscaledTime, true, true, disableBehaviour);
        }
    }
}
