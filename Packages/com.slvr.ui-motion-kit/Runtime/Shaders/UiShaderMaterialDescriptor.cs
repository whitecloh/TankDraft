using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    public enum UiShaderEffect
    {
        Shimmer = 0,
        GradientFlow = 1,
        SoftGlowOverlay = 2,
    }

    /// <summary>Serializable, immutable-at-runtime description of a shared UI material variant.</summary>
    [Serializable]
    public struct UiShaderMaterialDescriptor : IEquatable<UiShaderMaterialDescriptor>
    {
        [SerializeField] private UiShaderEffect effect;
        [SerializeField] private Color primaryColor;
        [SerializeField] private Color secondaryColor;
        [SerializeField] private float width;
        [SerializeField] private float softness;
        [SerializeField] private float angle;
        [SerializeField] private float intensity;
        [SerializeField] private float phase;
        [SerializeField] private float speed;
        [SerializeField] private float scale;
        [SerializeField] private float pulseAmount;
        [SerializeField] private bool animate;

        public UiShaderMaterialDescriptor(
            UiShaderEffect effect,
            Color primaryColor,
            Color secondaryColor,
            float width,
            float softness,
            float angle,
            float intensity,
            float phase,
            float speed,
            float scale,
            float pulseAmount,
            bool animate)
        {
            this.effect = effect;
            this.primaryColor = primaryColor;
            this.secondaryColor = secondaryColor;
            this.width = width;
            this.softness = softness;
            this.angle = angle;
            this.intensity = intensity;
            this.phase = phase;
            this.speed = speed;
            this.scale = scale;
            this.pulseAmount = pulseAmount;
            this.animate = animate;
        }

        public UiShaderEffect Effect => effect;
        public Color PrimaryColor => primaryColor;
        public Color SecondaryColor => secondaryColor;
        public float Width => width;
        public float Softness => softness;
        public float Angle => angle;
        public float Intensity => intensity;
        public float Phase => phase;
        public float Speed => speed;
        public float Scale => scale;
        public float PulseAmount => pulseAmount;
        public bool Animate => animate;

        public static UiShaderMaterialDescriptor ShimmerDefault => new UiShaderMaterialDescriptor(
            UiShaderEffect.Shimmer,
            new Color(1f, 1f, 1f, 0.8f),
            Color.white,
            0.18f,
            0.12f,
            35f,
            0.65f,
            0f,
            0.25f,
            1.5f,
            0.2f,
            true);

        /// <summary>Warm, bounded shimmer used by primary and reroll buttons.</summary>
        public static UiShaderMaterialDescriptor ButtonShimmer => new UiShaderMaterialDescriptor(
            UiShaderEffect.Shimmer,
            new Color(1f, 0.9f, 0.55f, 0.75f),
            Color.white,
            0.17f,
            0.11f,
            35f,
            0.65f,
            0f,
            0.4f,
            1f,
            0f,
            true);

        public static UiShaderMaterialDescriptor GradientFlowDefault => new UiShaderMaterialDescriptor(
            UiShaderEffect.GradientFlow,
            new Color(0.8f, 0.9f, 1f, 1f),
            new Color(1f, 0.8f, 0.95f, 1f),
            0.18f,
            0.12f,
            0f,
            0.25f,
            0f,
            0.12f,
            1.5f,
            0.2f,
            true);

        public static UiShaderMaterialDescriptor SoftGlowDefault => new UiShaderMaterialDescriptor(
            UiShaderEffect.SoftGlowOverlay,
            new Color(0.4f, 0.8f, 1f, 1f),
            Color.white,
            0.18f,
            0.12f,
            0f,
            1f,
            0f,
            0.5f,
            1.5f,
            0.2f,
            true);

        internal UiShaderMaterialDescriptor Resolve(UiMotionSettingsSnapshot settings)
        {
            bool qualityAllowsAnimation = settings.Quality == null
                || (settings.Quality.Tier != UiMotionQualityTier.Low
                    && settings.Quality.AmbientShadersEnabled);
            bool resolvedAnimate = animate
                && qualityAllowsAnimation
                && !settings.ReducedMotion
                && !settings.DisableFlashes;

            var resolved = new UiShaderMaterialDescriptor(
                effect,
                primaryColor,
                secondaryColor,
                Mathf.Clamp(width, 0.01f, 1f),
                Mathf.Clamp(softness, 0.001f, 1f),
                Mathf.Clamp(angle, -180f, 180f),
                Mathf.Max(0f, intensity),
                Mathf.Repeat(phase, 1f),
                Mathf.Clamp(speed, -4f, 4f),
                Mathf.Clamp(scale, 0.25f, 8f),
                Mathf.Clamp01(pulseAmount),
                resolvedAnimate);

            return resolved.NormalizeUnusedFields();
        }

        private UiShaderMaterialDescriptor NormalizeUnusedFields()
        {
            switch (effect)
            {
                case UiShaderEffect.Shimmer:
                    return new UiShaderMaterialDescriptor(
                        effect, primaryColor, Color.white, width, softness, angle, intensity,
                        phase, speed, 1f, 0f, animate);
                case UiShaderEffect.GradientFlow:
                    return new UiShaderMaterialDescriptor(
                        effect, primaryColor, secondaryColor, 0f, 0f, angle, intensity,
                        phase, speed, scale, 0f, animate);
                default:
                    return new UiShaderMaterialDescriptor(
                        effect, primaryColor, Color.white, 0f, 0f, 0f, intensity,
                        phase, speed, 1f, pulseAmount, animate);
            }
        }

        public bool Equals(UiShaderMaterialDescriptor other)
        {
            return effect == other.effect
                && primaryColor.Equals(other.primaryColor)
                && secondaryColor.Equals(other.secondaryColor)
                && width.Equals(other.width)
                && softness.Equals(other.softness)
                && angle.Equals(other.angle)
                && intensity.Equals(other.intensity)
                && phase.Equals(other.phase)
                && speed.Equals(other.speed)
                && scale.Equals(other.scale)
                && pulseAmount.Equals(other.pulseAmount)
                && animate == other.animate;
        }

        public override bool Equals(object obj)
        {
            return obj is UiShaderMaterialDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)effect;
                hash = (hash * 397) ^ primaryColor.GetHashCode();
                hash = (hash * 397) ^ secondaryColor.GetHashCode();
                hash = (hash * 397) ^ width.GetHashCode();
                hash = (hash * 397) ^ softness.GetHashCode();
                hash = (hash * 397) ^ angle.GetHashCode();
                hash = (hash * 397) ^ intensity.GetHashCode();
                hash = (hash * 397) ^ phase.GetHashCode();
                hash = (hash * 397) ^ speed.GetHashCode();
                hash = (hash * 397) ^ scale.GetHashCode();
                hash = (hash * 397) ^ pulseAmount.GetHashCode();
                hash = (hash * 397) ^ animate.GetHashCode();
                return hash;
            }
        }
    }
}
