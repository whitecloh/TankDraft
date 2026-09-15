using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Shows an authored message briefly with a lifecycle-safe fade/scale pop, then hides it.
    /// Text ownership stays with the host so this component remains localization-agnostic.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiTransientMessage : UiMotionElement
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform visualRoot;
        [SerializeField, Min(0f)] private float visibleDuration = 1.6f;
        [SerializeField, Range(0.5f, 1f)] private float hiddenScale = 0.9f;

        private Vector3 shownScale = Vector3.one;
        private bool initialized;
        private bool visible;

        public bool IsVisible => visible;
        public float VisibleDuration => visibleDuration;

        private void Awake()
        {
            Initialize();
            HideImmediate();
        }

        private void OnDisable()
        {
            HideImmediate();
        }

        public void Configure(
            CanvasGroup messageCanvasGroup,
            RectTransform messageRoot,
            float messageVisibleDuration = 1.6f,
            float messageHiddenScale = 0.9f)
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup = messageCanvasGroup;
            visualRoot = messageRoot;
            visibleDuration = Mathf.Max(0f, messageVisibleDuration);
            hiddenScale = Mathf.Clamp(messageHiddenScale, 0.5f, 1f);
            initialized = false;
            Initialize();
            HideImmediate();
        }

        public void Show()
        {
            Initialize();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            if (canvasGroup == null)
            {
                visible = false;
                return;
            }

            visible = true;
            canvasGroup.alpha = 0f;
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
            if (visualRoot != null)
            {
                visualRoot.localScale = shownScale * hiddenScale;
            }

            UiMotionSettingsSnapshot settings = Settings;
            float enterDuration = settings.ResolveDuration(UiMotionTiming.Fast);
            float exitDuration = settings.ResolveDuration(UiMotionTiming.Fast);
            bool animateFade = enterDuration > 0f &&
                               settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade;
            bool animateExitFade = exitDuration > 0f &&
                                   settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade;
            bool animateScale = enterDuration > 0f &&
                                visualRoot != null &&
                                settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            if (!animateFade)
            {
                canvasGroup.alpha = 1f;
            }

            if (!animateScale && visualRoot != null)
            {
                visualRoot.localScale = shownScale;
            }

            UiMotionTweenPolicy enterPolicy = ResolvePolicy(settings, UiMotionEase.OutBack);
            UiMotionTweenPolicy exitPolicy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, enterPolicy);
            if (animateFade)
            {
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => canvasGroup.alpha,
                    value => canvasGroup.alpha = value,
                    1f,
                    enterDuration,
                    enterPolicy));
            }

            if (animateScale)
            {
                Tween scaleTween = UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    shownScale,
                    enterDuration,
                    enterPolicy);
                if (animateFade)
                {
                    sequence.Join(scaleTween);
                }
                else
                {
                    sequence.Append(scaleTween);
                }
            }

            sequence.AppendInterval(visibleDuration);
            if (animateExitFade)
            {
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => canvasGroup.alpha,
                    value => canvasGroup.alpha = value,
                    0f,
                    exitDuration,
                    exitPolicy));
            }

            sequence.OnComplete(ApplyHiddenState);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
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
            visible = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }

            if (visualRoot != null)
            {
                visualRoot.localScale = shownScale * hiddenScale;
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
                canvasGroup = visualRoot.GetComponent<CanvasGroup>();
            }

            if (visualRoot != null)
            {
                shownScale = visualRoot.localScale;
            }

            initialized = true;
        }
    }
}
