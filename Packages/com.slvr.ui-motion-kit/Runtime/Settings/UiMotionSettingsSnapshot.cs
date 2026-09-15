using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Kinds of motion used by Reduced Motion substitution.</summary>
    public enum UiMotionEffectKind
    {
        Immediate = 0,
        Fade = 1,
        Translation = 2,
        Scale = 3,
        Rotation = 4,
        Shake = 5,
        Shimmer = 6,
        ParticleBurst = 7,
    }

    /// <summary>Immutable settings value read by runtime motion components.</summary>
    public readonly struct UiMotionSettingsSnapshot
    {
        public UiMotionSettingsSnapshot(
            UiMotionTheme theme,
            UiMotionQualityProfile quality,
            float intensity,
            bool reducedMotion,
            bool disableFlashes,
            bool idleEffects,
            bool useUnscaledTime,
            bool debugOverlay)
        {
            Theme = theme;
            Quality = quality;
            Intensity = Mathf.Clamp01(intensity);
            ReducedMotion = reducedMotion;
            DisableFlashes = disableFlashes;
            IdleEffects = idleEffects;
            UseUnscaledTime = useUnscaledTime;
            DebugOverlay = debugOverlay;
        }

        public UiMotionTheme Theme { get; }
        public UiMotionQualityProfile Quality { get; }
        public float Intensity { get; }
        public bool ReducedMotion { get; }
        public bool DisableFlashes { get; }
        public bool IdleEffects { get; }
        public bool UseUnscaledTime { get; }
        public bool DebugOverlay { get; }

        public float ResolveDuration(UiMotionTiming timing)
        {
            float duration = Theme != null
                ? Theme.Timings.Get(timing)
                : UiMotionTimingTokens.Balanced.Get(timing);
            return UiMotionDefaults.ScaleDuration(duration, Intensity);
        }

        public UiMotionEffectKind ResolveEffect(UiMotionEffectKind requested)
        {
            if (Intensity <= 0f)
            {
                return UiMotionEffectKind.Immediate;
            }

            if (!ReducedMotion)
            {
                return requested;
            }

            switch (requested)
            {
                case UiMotionEffectKind.ParticleBurst:
                case UiMotionEffectKind.Shimmer:
                    return UiMotionEffectKind.Immediate;
                case UiMotionEffectKind.Immediate:
                    return UiMotionEffectKind.Immediate;
                default:
                    return UiMotionEffectKind.Fade;
            }
        }
    }
}
