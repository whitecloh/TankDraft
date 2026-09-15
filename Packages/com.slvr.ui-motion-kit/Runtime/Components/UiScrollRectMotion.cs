using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Reusable lifecycle-safe reset and navigation motion for a ScrollRect.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ScrollRect))]
    public sealed class UiScrollRectMotion : UiMotionElement
    {
        [SerializeField] private ScrollRect scrollRect;
        [SerializeField] private bool resetHorizontal;
        [SerializeField, Range(0f, 1f)] private float horizontalStart;
        [SerializeField] private bool resetVertical = true;
        [SerializeField, Range(0f, 1f)] private float verticalStart = 1f;
        [SerializeField, Min(0f)] private float duration = 0.22f;
        [Header("Mask Reveal")]
        [SerializeField] private RectMask2D revealMask;
        [SerializeField, Min(0f)] private float revealDuration = 0.62f;
        [SerializeField, Min(0)] private int revealSoftness = 160;
        [SerializeField, Range(0f, 1f)] private float revealInitialVisibleFraction = 0.28f;

        private Vector4 revealedPadding;
        private Vector2Int revealedSoftness;
        private bool revealStateCaptured;
        private bool revealPrepared;

        public ScrollRect ScrollRect => scrollRect;
        public Vector2 StartNormalizedPosition => new Vector2(horizontalStart, verticalStart);
        public RectMask2D RevealMask => revealMask;
        public bool IsRevealPrepared => revealPrepared;
        public float RevealInitialVisibleFraction => revealInitialVisibleFraction;

        private void Awake()
        {
            ResolveScrollRect();
            ResolveRevealMask();
            CaptureRevealState();
        }

        private void OnDisable()
        {
            Lifecycle.Stop(UiMotionChannel.Custom0);
            Lifecycle.Stop(UiMotionChannel.Custom1);
            ApplyRevealedState();
            revealPrepared = false;
            scrollRect?.StopMovement();
        }

        public void Configure(
            ScrollRect target,
            bool horizontal = false,
            float horizontalPosition = 0f,
            bool vertical = true,
            float verticalPosition = 1f)
        {
            scrollRect = target;
            resetHorizontal = horizontal;
            horizontalStart = Mathf.Clamp01(horizontalPosition);
            resetVertical = vertical;
            verticalStart = Mathf.Clamp01(verticalPosition);
        }

        /// <summary>
        /// Configures an optional top-to-bottom soft reveal without resizing the viewport or content.
        /// Existing mask padding and softness are restored after every reveal.
        /// </summary>
        public void ConfigureReveal(
            RectMask2D target,
            float targetDuration = 0.62f,
            int minimumVerticalSoftness = 160,
            float initialVisibleFraction = 0.28f)
        {
            Lifecycle.Stop(UiMotionChannel.Custom1);
            if (revealPrepared)
            {
                ApplyRevealedState();
            }

            revealMask = target;
            revealDuration = Mathf.Max(0f, targetDuration);
            revealSoftness = Mathf.Max(0, minimumVerticalSoftness);
            revealInitialVisibleFraction = Mathf.Clamp01(initialVisibleFraction);
            revealStateCaptured = false;
            revealPrepared = false;
            CaptureRevealState();
        }

        public void ResetToStart(bool immediate = true)
        {
            ResolveScrollRect();
            if (scrollRect == null)
            {
                return;
            }

            scrollRect.StopMovement();
            Vector2 target = ResolveTargetPosition();
            AnimateTo(target, immediate);
        }

        /// <summary>
        /// Collapses only the RectMask2D clip region. ScrollRect geometry, layout and content stay unchanged.
        /// </summary>
        public void PrepareReveal()
        {
            ResolveRevealMask();
            CaptureRevealState();
            Lifecycle.Stop(UiMotionChannel.Custom1);
            ApplyRevealedState();
            revealPrepared = false;
            if (revealMask == null)
            {
                return;
            }

            UiMotionSettingsSnapshot settings = Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(revealDuration, settings.Intensity);
            if (resolvedDuration <= 0f ||
                settings.ResolveEffect(UiMotionEffectKind.Translation) != UiMotionEffectKind.Translation)
            {
                return;
            }

            Vector2Int softness = revealedSoftness;
            softness.y = Mathf.Max(softness.y, revealSoftness);
            revealMask.softness = softness;

            Vector4 padding = revealedPadding;
            float viewportHeight = Mathf.Abs(revealMask.rectTransform.rect.height);
            float initialVisibleHeight = viewportHeight * revealInitialVisibleFraction;
            padding.y = Mathf.Max(
                revealedPadding.y,
                viewportHeight - revealedPadding.w - initialVisibleHeight);
            revealMask.padding = padding;
            revealPrepared = true;
        }

        /// <summary>Reveals the prepared mask from its top edge down to the authored clip region.</summary>
        public void PlayReveal()
        {
            ResolveRevealMask();
            CaptureRevealState();
            if (!revealPrepared)
            {
                PrepareReveal();
            }

            if (!revealPrepared || revealMask == null)
            {
                return;
            }

            UiMotionSettingsSnapshot settings = Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(revealDuration, settings.Intensity);
            if (resolvedDuration <= 0f ||
                settings.ResolveEffect(UiMotionEffectKind.Translation) != UiMotionEffectKind.Translation)
            {
                ResetRevealImmediate();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Tween tween = UiMotionTweenFactory.Float(
                this,
                () => revealMask.padding.y,
                value =>
                {
                    Vector4 padding = revealMask.padding;
                    padding.y = value;
                    revealMask.padding = padding;
                },
                revealedPadding.y,
                resolvedDuration,
                policy);
            tween.OnComplete(() =>
            {
                ApplyRevealedState();
                revealPrepared = false;
            });
            Lifecycle.Play(UiMotionChannel.Custom1, tween);
        }

        public void ResetRevealImmediate()
        {
            Lifecycle.Stop(UiMotionChannel.Custom1);
            ApplyRevealedState();
            revealPrepared = false;
        }

        /// <summary>Moves vertical content so a descendant target is centered in the viewport.</summary>
        public void CenterOn(RectTransform target, bool immediate = false)
        {
            ResolveScrollRect();
            if (scrollRect == null || target == null || scrollRect.content == null || !scrollRect.vertical
                || !target.IsChildOf(scrollRect.content))
            {
                return;
            }

            RectTransform viewport = scrollRect.viewport != null
                ? scrollRect.viewport
                : scrollRect.transform as RectTransform;
            if (viewport == null)
            {
                return;
            }

            Canvas.ForceUpdateCanvases();
            Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                viewport,
                scrollRect.content);
            float scrollableHeight = contentBounds.size.y - viewport.rect.height;
            if (scrollableHeight <= 0.01f)
            {
                return;
            }

            Bounds targetBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, target);
            float offsetToCenter = viewport.rect.center.y - targetBounds.center.y;
            Vector2 normalizedTarget = scrollRect.normalizedPosition;
            normalizedTarget.y = Mathf.Clamp01(
                scrollRect.verticalNormalizedPosition - offsetToCenter / scrollableHeight);
            scrollRect.StopMovement();
            AnimateTo(normalizedTarget, immediate);
        }

        private void AnimateTo(Vector2 target, bool immediate)
        {
            UiMotionSettingsSnapshot settings = Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(duration, settings.Intensity);
            if (immediate || resolvedDuration <= 0f ||
                settings.ResolveEffect(UiMotionEffectKind.Translation) != UiMotionEffectKind.Translation)
            {
                Lifecycle.Stop(UiMotionChannel.Custom0);
                scrollRect.normalizedPosition = target;
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Tween tween = UiMotionTweenFactory.Vector3(
                this,
                () => scrollRect.normalizedPosition,
                value => scrollRect.normalizedPosition = value,
                target,
                resolvedDuration,
                policy);
            Lifecycle.Play(UiMotionChannel.Custom0, tween);
        }

        public void ResetImmediate()
        {
            ResetToStart(true);
        }

        private Vector2 ResolveTargetPosition()
        {
            Vector2 current = scrollRect.normalizedPosition;
            return new Vector2(
                resetHorizontal ? horizontalStart : current.x,
                resetVertical ? verticalStart : current.y);
        }

        private void ResolveScrollRect()
        {
            if (scrollRect == null)
            {
                scrollRect = GetComponent<ScrollRect>();
            }
        }

        private void ResolveRevealMask()
        {
            if (revealMask == null)
            {
                revealMask = GetComponent<RectMask2D>();
            }
        }

        private void CaptureRevealState()
        {
            if (revealStateCaptured || revealMask == null)
            {
                return;
            }

            revealedPadding = revealMask.padding;
            revealedSoftness = revealMask.softness;
            revealStateCaptured = true;
        }

        private void ApplyRevealedState()
        {
            if (!revealStateCaptured || revealMask == null)
            {
                return;
            }

            revealMask.padding = revealedPadding;
            revealMask.softness = revealedSoftness;
        }
    }
}
