using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Authored countdown pop: digits snap in, then fade while shrinking; the final phrase stays
    /// visible until the host explicitly hides it. Text and styling remain owned by the host UI.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiCountdownMotion : UiMotionElement
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform visualRoot;
        [SerializeField, Min(0f)] private float digitEnterDuration = 0.13f;
        [SerializeField, Min(0f)] private float digitVisibleDuration = 0.15f;
        [SerializeField, Min(0f)] private float digitExitDuration = 0.55f;
        [SerializeField, Min(0f)] private float finalEnterDuration = 0.16f;

        private Vector3 shownScale = Vector3.one;
        private bool initialized;

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.0001f;

        private void Awake()
        {
            Initialize();
            HideImmediate();
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        public void Configure(CanvasGroup targetCanvasGroup, RectTransform targetVisualRoot)
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup = targetCanvasGroup;
            visualRoot = targetVisualRoot;
            initialized = false;
            Initialize();
            HideImmediate();
        }

        public void PlayDigit()
        {
            Initialize();
            if (canvasGroup == null)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Visibility);
            UiMotionSettingsSnapshot settings = Settings;
            bool animateScale = visualRoot != null &&
                                settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            float enterDuration = UiMotionDefaults.ScaleDuration(digitEnterDuration, settings.Intensity);
            float exitDuration = UiMotionDefaults.ScaleDuration(digitExitDuration, settings.Intensity);

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            if (visualRoot != null)
            {
                visualRoot.localScale = animateScale ? Vector3.zero : shownScale;
            }

            UiMotionTweenPolicy enterPolicy = ResolvePolicy(settings, UiMotionEase.OutBack);
            UiMotionTweenPolicy exitPolicy = ResolvePolicy(settings, UiMotionEase.InQuad);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, enterPolicy);
            if (animateScale && enterDuration > 0f)
            {
                sequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    shownScale,
                    enterDuration,
                    enterPolicy));
            }

            sequence.AppendInterval(digitVisibleDuration);
            if (exitDuration > 0f)
            {
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => canvasGroup.alpha,
                    value => canvasGroup.alpha = value,
                    0f,
                    exitDuration,
                    exitPolicy));
                if (animateScale)
                {
                    sequence.Join(UiMotionTweenFactory.Vector3(
                        this,
                        () => visualRoot.localScale,
                        value => visualRoot.localScale = value,
                        Vector3.zero,
                        exitDuration,
                        exitPolicy));
                }
            }

            sequence.OnComplete(ApplyHiddenState);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void PlayFinal()
        {
            Initialize();
            if (canvasGroup == null)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Visibility);
            UiMotionSettingsSnapshot settings = Settings;
            bool animateScale = visualRoot != null &&
                                settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            float enterDuration = UiMotionDefaults.ScaleDuration(finalEnterDuration, settings.Intensity);

            canvasGroup.alpha = 1f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            if (visualRoot != null)
            {
                visualRoot.localScale = animateScale ? Vector3.zero : shownScale;
            }

            if (!animateScale || enterDuration <= 0f)
            {
                if (visualRoot != null)
                {
                    visualRoot.localScale = shownScale;
                }

                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutBack);
            Tween tween = UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                shownScale,
                enterDuration,
                policy);
            Lifecycle.Play(UiMotionChannel.Visibility, tween);
        }

        public void HideImmediate()
        {
            if (initialized)
            {
                Lifecycle.Stop(UiMotionChannel.Visibility);
            }

            ApplyHiddenState();
        }

        private void ApplyHiddenState()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (visualRoot != null)
            {
                visualRoot.localScale = Vector3.zero;
            }
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (visualRoot == null && canvasGroup != null)
            {
                visualRoot = canvasGroup.transform as RectTransform;
            }

            if (canvasGroup == null && visualRoot != null)
            {
                canvasGroup = visualRoot.GetComponentInParent<CanvasGroup>();
            }

            shownScale = visualRoot != null ? visualRoot.localScale : Vector3.one;
            initialized = true;
        }
    }
}
