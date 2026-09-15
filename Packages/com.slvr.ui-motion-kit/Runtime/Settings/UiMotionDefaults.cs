using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Safe hardcoded fallback values used when assets are absent.</summary>
    public static class UiMotionDefaults
    {
        public static UiMotionSettingsSnapshot Settings => new UiMotionSettingsSnapshot(
            null,
            null,
            1f,
            false,
            false,
            true,
            true,
            false);

        public static float ScaleDuration(float duration, float intensity)
        {
            float clampedIntensity = Mathf.Clamp01(intensity);
            if (duration <= 0f || clampedIntensity <= 0f)
            {
                return 0f;
            }

            return duration * Mathf.Lerp(0.35f, 1f, clampedIntensity);
        }

        /// <summary>Returns a stable 0..1 phase without runtime-random state.</summary>
        public static float GetDeterministicPhase01(int stableId)
        {
            unchecked
            {
                uint value = (uint)stableId;
                value ^= value >> 16;
                value *= 0x7feb352d;
                value ^= value >> 15;
                value *= 0x846ca68b;
                value ^= value >> 16;
                return (value & 0x00FFFFFFu) / 16777215f;
            }
        }
    }
}
