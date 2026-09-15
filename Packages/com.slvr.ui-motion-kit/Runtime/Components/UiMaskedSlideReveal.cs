using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Reveals one visual from an authored offset while a separate label/group fades in.</summary>
    [DisallowMultipleComponent]
    public sealed class UiMaskedSlideReveal : UiMotionElement
    {
        [SerializeField] private RectTransform slideRoot;
        [SerializeField] private CanvasGroup fadeGroup;
        [SerializeField] private Vector2 hiddenOffset = new Vector2(64f, 0f);
        [SerializeField, Min(0f)] private float slideDuration = 0.3f;
        [SerializeField, Min(0f)] private float fadeDuration = 0.2f;
        [SerializeField] private UiMotionEase slideEase = UiMotionEase.OutBack;

        private Vector2 shownPosition;
        private float shownAlpha = 1f;
        private bool initialized;
        private bool prepared;

        public RectTransform SlideRoot => slideRoot;
        public CanvasGroup FadeGroup => fadeGroup;
        public UiMotionEase SlideEase => slideEase;
        public bool IsPrepared => prepared;

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            ResetImmediate();
        }

        public void Configure(
            RectTransform visual,
            CanvasGroup group,
            Vector2 offset,
            float duration = 0.3f,
            float groupFadeDuration = 0.2f,
            UiMotionEase easing = UiMotionEase.OutBack)
        {
            slideRoot = visual;
            fadeGroup = group;
            hiddenOffset = offset;
            slideDuration = Mathf.Max(0f, duration);
            fadeDuration = Mathf.Max(0f, groupFadeDuration);
            slideEase = easing;
            initialized = false;
            Initialize();
            ResetImmediate();
        }

        public void Prepare()
        {
            Initialize();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            UiMotionSettingsSnapshot settings = Settings;
            slideRoot.anchoredPosition = settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation
                ? shownPosition + hiddenOffset
                : shownPosition;
            if (fadeGroup != null)
            {
                fadeGroup.alpha = settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade
                    ? 0f
                    : shownAlpha;
            }

            prepared = true;
        }

        public void Play()
        {
            Initialize();
            if (!prepared)
            {
                Prepare();
            }

            UiMotionSettingsSnapshot settings = Settings;
            float resolvedSlideDuration = UiMotionDefaults.ScaleDuration(slideDuration, settings.Intensity);
            float resolvedFadeDuration = UiMotionDefaults.ScaleDuration(fadeDuration, settings.Intensity);
            if (resolvedSlideDuration <= 0f && resolvedFadeDuration <= 0f)
            {
                ResetImmediate();
                return;
            }

            UiMotionTweenPolicy slidePolicy = ResolvePolicy(settings, slideEase);
            UiMotionTweenPolicy fadePolicy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, fadePolicy).SetEase(Ease.Linear);
            bool hasStep = false;
            if (settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation &&
                resolvedSlideDuration > 0f)
            {
                sequence.Insert(0f, UiMotionTweenFactory.Vector3(
                    this,
                    () => slideRoot.anchoredPosition,
                    value => slideRoot.anchoredPosition = value,
                    shownPosition,
                    resolvedSlideDuration,
                    slidePolicy));
                hasStep = true;
            }
            else
            {
                slideRoot.anchoredPosition = shownPosition;
            }

            if (fadeGroup != null &&
                settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade &&
                resolvedFadeDuration > 0f)
            {
                sequence.Insert(0f, UiMotionTweenFactory.Float(
                    this,
                    () => fadeGroup.alpha,
                    value => fadeGroup.alpha = value,
                    shownAlpha,
                    resolvedFadeDuration,
                    fadePolicy));
                hasStep = true;
            }
            else if (fadeGroup != null)
            {
                fadeGroup.alpha = shownAlpha;
            }

            if (!hasStep)
            {
                sequence.Kill(false);
                ResetImmediate();
                return;
            }

            sequence.OnComplete(ResetImmediate);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
            prepared = false;
        }

        public void ResetImmediate()
        {
            if (!initialized)
            {
                Initialize();
            }

            Lifecycle.Stop(UiMotionChannel.Visibility);
            if (slideRoot != null)
            {
                slideRoot.anchoredPosition = shownPosition;
            }

            if (fadeGroup != null)
            {
                fadeGroup.alpha = shownAlpha;
            }

            prepared = false;
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (slideRoot == null)
            {
                slideRoot = transform as RectTransform;
            }

            if (slideRoot == null)
            {
                throw new System.InvalidOperationException($"{name}: slideRoot is not assigned.");
            }

            shownPosition = slideRoot.anchoredPosition;
            shownAlpha = fadeGroup != null ? fadeGroup.alpha : 1f;
            initialized = true;
        }
    }
}
