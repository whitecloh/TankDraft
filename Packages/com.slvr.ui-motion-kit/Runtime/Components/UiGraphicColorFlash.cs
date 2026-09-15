using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Plays a lifecycle-safe transient color flash on an authored UGUI Graphic and always restores
    /// the color that was active when the flash started. Reduced-motion/flash-disabled settings keep
    /// a static readable color state for the same short duration instead of oscillating the color.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiGraphicColorFlash : UiMotionElement
    {
        [SerializeField] private Graphic target;
        [SerializeField] private Color flashColor = new Color(1f, 0.25f, 0.25f, 1f);
        [SerializeField, Min(0f)] private float duration = 0.42f;
        [SerializeField, Range(0f, 0.5f)] private float holdFraction = 0.12f;

        private IUiMotionSettingsProvider settingsProvider;
        private Color baseColor;
        private float progress;
        private bool baselineCaptured;

        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Attention, out Tween tween) && tween.IsPlaying();
        public Graphic Target => target;

        private void Awake()
        {
            if (target == null)
            {
                target = GetComponent<Graphic>();
            }
        }

        private void OnDisable()
        {
            StopAndRestore();
        }

        public void Configure(
            Graphic colorTarget,
            Color? overrideFlashColor = null,
            float? overrideDuration = null)
        {
            StopAndRestore();
            target = colorTarget;
            if (overrideFlashColor.HasValue)
            {
                flashColor = overrideFlashColor.Value;
            }

            if (overrideDuration.HasValue)
            {
                duration = Mathf.Max(0f, overrideDuration.Value);
            }
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            settingsProvider = provider;
        }

        public void Play()
        {
            StopAndRestore();
            if (target == null)
            {
                return;
            }

            baseColor = target.color;
            baselineCaptured = true;
            progress = 0f;

            UiMotionSettingsSnapshot settings = settingsProvider != null
                ? settingsProvider.Current
                : Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(duration, settings.Intensity);
            float feedbackDuration = resolvedDuration > 0f ? resolvedDuration : 0.18f;
            bool animateColor = resolvedDuration > 0f &&
                                !settings.ReducedMotion &&
                                !settings.DisableFlashes &&
                                settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutSine);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);

            if (animateColor)
            {
                float safeHoldFraction = Mathf.Clamp(holdFraction, 0f, 0.5f);
                float holdDuration = feedbackDuration * safeHoldFraction;
                float transitionDuration = Mathf.Max(0f, (feedbackDuration - holdDuration) * 0.5f);
                sequence.Append(CreateColorTween(1f, transitionDuration, policy));
                if (holdDuration > 0f)
                {
                    sequence.AppendInterval(holdDuration);
                }

                sequence.Append(CreateColorTween(0f, transitionDuration, policy));
            }
            else
            {
                ApplyColor(1f);
                sequence.AppendInterval(feedbackDuration);
            }

            sequence.OnComplete(RestoreBaseline);
            Lifecycle.Play(UiMotionChannel.Attention, sequence);
        }

        public void StopAndRestore()
        {
            Lifecycle.Stop(UiMotionChannel.Attention);
            RestoreBaseline();
        }

        private Tween CreateColorTween(float endValue, float tweenDuration, UiMotionTweenPolicy policy)
        {
            return UiMotionTweenFactory.Float(
                this,
                () => progress,
                value =>
                {
                    progress = value;
                    ApplyColor(value);
                },
                endValue,
                tweenDuration,
                policy);
        }

        private void ApplyColor(float amount)
        {
            if (target == null || !baselineCaptured)
            {
                return;
            }

            Color blended = Color.Lerp(baseColor, flashColor, Mathf.Clamp01(amount));
            blended.a = baseColor.a;
            target.color = blended;
        }

        private void RestoreBaseline()
        {
            if (target != null && baselineCaptured)
            {
                target.color = baseColor;
            }

            progress = 0f;
            baselineCaptured = false;
        }
    }
}
