using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    public enum UiPanelTransitionStyle
    {
        Fade = 0,
        FadeScale = 1,
        VerticalOffset = 2,
        BottomSheet = 3,
        DrawerLeft = 4,
        DrawerRight = 5,
        Tooltip = 6,
        RewardPop = 7,
        OffscreenLeft = 8,
        OffscreenRight = 9,
        OffscreenTop = 10,
        OffscreenBottom = 11,
    }

    /// <summary>Reentrant panel visibility transition with strict raycast timing.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiPanelTransition : UiMotionElement
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private UiPanelTransitionStyle style = UiPanelTransitionStyle.FadeScale;
        [SerializeField, Range(0.05f, 1f)] private float hiddenScale = 0.96f;
        [SerializeField] private Vector2 hiddenOffset = new Vector2(0f, -24f);
        [SerializeField, Min(0f)] private float offscreenMargin = 32f;
        [SerializeField] private bool translationSpring;
        [SerializeField, Min(0.05f)] private float translationSpringDuration = 0.72f;
        [SerializeField, Min(0f)] private float translationSpringOvershoot = 8f;
        [SerializeField] private bool blockRaycastsWhileTransitioning;
        [SerializeField] private UnityEvent hidden;

        private Vector3 shownScale;
        private Vector2 shownPosition;
        private bool shownStateCaptured;
        private bool targetShown = true;

        /// <summary>
        /// Raised after the hide state has been fully applied. Host UI frameworks can use this
        /// to defer GameObject deactivation without coupling the package to a specific window type.
        /// </summary>
        public event Action HideCompleted;

        /// <summary>Raised after the shown state and interaction have been fully restored.</summary>
        public event Action ShowCompleted;

        public UiPanelTransitionStyle Style
        {
            get => style;
            set => style = value;
        }

        public CanvasGroup CanvasGroup => canvasGroup;
        public RectTransform VisualRoot => visualRoot;

        public float OffscreenMargin
        {
            get => offscreenMargin;
            set => offscreenMargin = Mathf.Max(0f, value);
        }

        /// <summary>
        /// Keeps a modal panel's full-screen blocker active during both entrance and exit while
        /// its own controls remain non-interactable until the entrance completes.
        /// </summary>
        public bool BlockRaycastsWhileTransitioning
        {
            get => blockRaycastsWhileTransitioning;
            set => blockRaycastsWhileTransitioning = value;
        }

        public void Configure(
            CanvasGroup newCanvasGroup,
            RectTransform newVisualRoot,
            UiPanelTransitionStyle newStyle = UiPanelTransitionStyle.FadeScale,
            float newHiddenScale = 0.96f,
            Vector2? newHiddenOffset = null)
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup = newCanvasGroup != null ? newCanvasGroup : GetComponent<CanvasGroup>();
            visualRoot = newVisualRoot != null ? newVisualRoot : transform as RectTransform;
            style = newStyle;
            hiddenScale = Mathf.Clamp(newHiddenScale, 0.05f, 1f);
            if (newHiddenOffset.HasValue)
            {
                hiddenOffset = newHiddenOffset.Value;
            }

            shownStateCaptured = false;
            EnsureReferences();
        }

        /// <summary>
        /// Adds a bounded arrival overshoot to translation-based show transitions. The spring
        /// follows the travel direction, crosses the shown point once and settles back.
        /// </summary>
        public void ConfigureTranslationSpring(float duration = 0.72f, float overshoot = 8f)
        {
            translationSpring = true;
            translationSpringDuration = Mathf.Max(0.05f, duration);
            translationSpringOvershoot = Mathf.Max(0f, overshoot);
        }

        private void Awake()
        {
            EnsureReferences();
        }

        public void Show()
        {
            EnsureReferences();
            targetShown = true;
            SetTransitionInteraction();
            float duration = ResolveTransitionDuration(showing: true);
            if (duration <= 0f)
            {
                ShowImmediate();
                return;
            }

            Sequence sequence = BuildSequence(true, duration);
            sequence.OnComplete(NotifyShown);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        /// <summary>
        /// Applies the authored hidden state before a host activates and binds popup content.
        /// A following <see cref="Show"/> call then owns the complete entrance without a
        /// one-frame flash of the final state.
        /// </summary>
        public void PrepareShow()
        {
            EnsureReferences();
            targetShown = false;
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 0f;
            ApplyVisualState(false);
            SetInteraction(false);
        }

        public void Hide()
        {
            EnsureReferences();
            targetShown = false;
            SetTransitionInteraction();
            float duration = ResolveTransitionDuration(showing: false);
            if (duration <= 0f)
            {
                HideImmediate();
                return;
            }

            Sequence sequence = BuildSequence(false, duration);
            sequence.OnComplete(NotifyHidden);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void ShowImmediate()
        {
            EnsureReferences();
            targetShown = true;
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 1f;
            ApplyVisualState(true);
            NotifyShown();
        }

        public void HideImmediate()
        {
            EnsureReferences();
            targetShown = false;
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 0f;
            ApplyVisualState(false);
            SetInteraction(false);
            NotifyHidden();
        }

        private Sequence BuildSequence(bool showing, float duration)
        {
            UiMotionEffectKind requestedEffect = ResolveRequestedEffect();
            UiMotionEffectKind resolvedEffect = Settings.ResolveEffect(requestedEffect);
            UiMotionTweenPolicy policy = ResolvePolicy(
                style == UiPanelTransitionStyle.RewardPop ? UiMotionEase.OutBack : UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.SetEase(Ease.Linear);
            float fadeDuration = showing && translationSpring && UsesTranslation() &&
                resolvedEffect == UiMotionEffectKind.Translation
                ? duration * 0.25f
                : duration;
            sequence.Insert(0f, UiMotionTweenFactory.Float(
                this,
                () => canvasGroup.alpha,
                value => canvasGroup.alpha = value,
                showing ? 1f : 0f,
                fadeDuration,
                policy));

            if (visualRoot != null && UsesScale() && resolvedEffect == UiMotionEffectKind.Scale)
            {
                if (showing && style == UiPanelTransitionStyle.RewardPop)
                {
                    AppendRewardPopSpring(sequence, duration);
                }
                else
                {
                    sequence.Insert(0f, UiMotionTweenFactory.Vector3(
                        this,
                        () => visualRoot.localScale,
                        value => visualRoot.localScale = value,
                        showing ? shownScale : ResolveHiddenScale(),
                        duration,
                        policy));
                }
            }
            else if (visualRoot != null && UsesTranslation() && resolvedEffect == UiMotionEffectKind.Translation)
            {
                if (showing && translationSpring && translationSpringOvershoot > 0f)
                {
                    AppendTranslationSpring(sequence, duration);
                }
                else
                {
                    Vector2 target = showing ? shownPosition : ResolveHiddenPosition();
                    sequence.Insert(0f, UiMotionTweenFactory.Vector3(
                        this,
                        () => visualRoot.anchoredPosition,
                        value => visualRoot.anchoredPosition = value,
                        target,
                        duration,
                        policy));
                }
            }
            else if (visualRoot != null)
            {
                // Reduced Motion substitutes structural movement with fade. The visual root must
                // stay in its readable shown state while alpha owns the transition.
                visualRoot.localScale = shownScale;
                visualRoot.anchoredPosition = shownPosition;
            }

            return sequence;
        }

        private void AppendRewardPopSpring(Sequence sequence, float duration)
        {
            float expandDuration = duration * 0.62f;
            float settleBackDuration = duration * 0.18f;
            float settleDuration = duration - expandDuration - settleBackDuration;
            Vector3 overshoot = shownScale * 1.08f;
            Vector3 undershoot = shownScale * 0.975f;

            sequence.Insert(0f, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    overshoot,
                    expandDuration,
                    ResolvePolicy(UiMotionEase.OutCubic))
                .SetEase(UiMotionEase.OutCubic.ToDotweenEase()));
            sequence.Insert(expandDuration, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    undershoot,
                    settleBackDuration,
                    ResolvePolicy(UiMotionEase.InOutSine))
                .SetEase(UiMotionEase.InOutSine.ToDotweenEase()));
            sequence.Insert(expandDuration + settleBackDuration, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    shownScale,
                    settleDuration,
                    ResolvePolicy(UiMotionEase.OutBack))
                .SetEase(UiMotionEase.OutBack.ToDotweenEase()));
        }

        private void AppendTranslationSpring(Sequence sequence, float duration)
        {
            float arrivalDuration = duration * 0.65f;
            float reboundDuration = duration * 0.17f;
            float settleDuration = duration - arrivalDuration - reboundDuration;
            Vector2 hiddenPosition = ResolveHiddenPosition();
            Vector2 travelDirection = shownPosition - hiddenPosition;
            if (travelDirection.sqrMagnitude <= Mathf.Epsilon)
            {
                travelDirection = Vector2.down;
            }
            else
            {
                travelDirection.Normalize();
            }

            Vector2 overshootPosition = shownPosition + travelDirection * translationSpringOvershoot;
            Vector2 reboundPosition = shownPosition - travelDirection * translationSpringOvershoot * 0.35f;
            sequence.Insert(0f, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.anchoredPosition,
                    value => visualRoot.anchoredPosition = value,
                    overshootPosition,
                    arrivalDuration,
                    ResolvePolicy(UiMotionEase.OutQuad))
                .SetEase(UiMotionEase.OutQuad.ToDotweenEase()));
            sequence.Insert(arrivalDuration, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.anchoredPosition,
                    value => visualRoot.anchoredPosition = value,
                    reboundPosition,
                    reboundDuration,
                    ResolvePolicy(UiMotionEase.InOutSine))
                .SetEase(UiMotionEase.InOutSine.ToDotweenEase()));
            sequence.Insert(arrivalDuration + reboundDuration, UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.anchoredPosition,
                    value => visualRoot.anchoredPosition = value,
                    shownPosition,
                    settleDuration,
                    ResolvePolicy(UiMotionEase.OutCubic))
                .SetEase(UiMotionEase.OutCubic.ToDotweenEase()));
        }

        private float ResolveTransitionDuration(bool showing)
        {
            float duration = ResolveDuration();
            if (duration <= 0f)
            {
                return duration;
            }

            if (showing && translationSpring && UsesTranslation() &&
                Settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation)
            {
                duration = Mathf.Max(
                    duration,
                    UiMotionDefaults.ScaleDuration(translationSpringDuration, Settings.Intensity));
            }

            if (style != UiPanelTransitionStyle.RewardPop ||
                Settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale)
            {
                return duration;
            }

            return Mathf.Max(
                duration,
                UiMotionDefaults.ScaleDuration(0.52f, Settings.Intensity));
        }

        private void ApplyVisualState(bool shown)
        {
            if (visualRoot == null) return;
            if (UsesScale())
            {
                visualRoot.localScale = shown ? shownScale : ResolveHiddenScale();
                visualRoot.anchoredPosition = shownPosition;
            }
            else if (UsesTranslation())
            {
                visualRoot.anchoredPosition = shown ? shownPosition : ResolveHiddenPosition();
                visualRoot.localScale = shownScale;
            }
            else
            {
                visualRoot.localScale = shownScale;
                visualRoot.anchoredPosition = shownPosition;
            }
        }

        private UiMotionEffectKind ResolveRequestedEffect()
        {
            if (UsesScale()) return UiMotionEffectKind.Scale;
            if (UsesTranslation()) return UiMotionEffectKind.Translation;
            return UiMotionEffectKind.Fade;
        }

        private bool UsesScale()
        {
            return style == UiPanelTransitionStyle.FadeScale
                || style == UiPanelTransitionStyle.Tooltip
                || style == UiPanelTransitionStyle.RewardPop;
        }

        private bool UsesTranslation()
        {
            return style == UiPanelTransitionStyle.VerticalOffset
                || style == UiPanelTransitionStyle.BottomSheet
                || style == UiPanelTransitionStyle.DrawerLeft
                || style == UiPanelTransitionStyle.DrawerRight
                || style == UiPanelTransitionStyle.OffscreenLeft
                || style == UiPanelTransitionStyle.OffscreenRight
                || style == UiPanelTransitionStyle.OffscreenTop
                || style == UiPanelTransitionStyle.OffscreenBottom;
        }

        private Vector3 ResolveHiddenScale()
        {
            float multiplier;
            switch (style)
            {
                case UiPanelTransitionStyle.Tooltip:
                    multiplier = Mathf.Min(hiddenScale, 0.94f);
                    break;
                case UiPanelTransitionStyle.RewardPop:
                    multiplier = Mathf.Min(hiddenScale, 0.82f);
                    break;
                default:
                    multiplier = hiddenScale;
                    break;
            }

            return shownScale * multiplier;
        }

        private Vector2 ResolveHiddenPosition()
        {
            float width = visualRoot != null ? Mathf.Max(96f, visualRoot.rect.width) : 96f;
            float height = visualRoot != null ? Mathf.Max(96f, visualRoot.rect.height) : 96f;
            switch (style)
            {
                case UiPanelTransitionStyle.BottomSheet:
                    return shownPosition + new Vector2(0f, -height);
                case UiPanelTransitionStyle.DrawerLeft:
                    return shownPosition + new Vector2(-width, 0f);
                case UiPanelTransitionStyle.DrawerRight:
                    return shownPosition + new Vector2(width, 0f);
                case UiPanelTransitionStyle.OffscreenLeft:
                case UiPanelTransitionStyle.OffscreenRight:
                case UiPanelTransitionStyle.OffscreenTop:
                case UiPanelTransitionStyle.OffscreenBottom:
                    return ResolveViewportOffscreenPosition();
                default:
                    return shownPosition + hiddenOffset;
            }
        }

        private Vector2 ResolveViewportOffscreenPosition()
        {
            if (visualRoot == null || !(visualRoot.parent is RectTransform parentRect))
            {
                return shownPosition;
            }

            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(parentRect, visualRoot);
            Rect viewport = parentRect.rect;
            Vector2 result = shownPosition;
            switch (style)
            {
                case UiPanelTransitionStyle.OffscreenLeft:
                    result.x += viewport.xMin - offscreenMargin - bounds.max.x;
                    break;
                case UiPanelTransitionStyle.OffscreenRight:
                    result.x += viewport.xMax + offscreenMargin - bounds.min.x;
                    break;
                case UiPanelTransitionStyle.OffscreenTop:
                    result.y += viewport.yMax + offscreenMargin - bounds.min.y;
                    break;
                case UiPanelTransitionStyle.OffscreenBottom:
                    result.y += viewport.yMin - offscreenMargin - bounds.max.y;
                    break;
            }

            return result;
        }

        private void SetInteraction(bool value)
        {
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;
        }

        private void SetTransitionInteraction()
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = blockRaycastsWhileTransitioning;
        }

        private void NotifyHidden()
        {
            SetInteraction(false);
            hidden?.Invoke();
            HideCompleted?.Invoke();
        }

        private void NotifyShown()
        {
            SetInteraction(true);
            ShowCompleted?.Invoke();
        }

        private void EnsureReferences()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (visualRoot == null) visualRoot = transform as RectTransform;
            if (!shownStateCaptured && visualRoot != null)
            {
                shownScale = visualRoot.localScale;
                shownPosition = visualRoot.anchoredPosition;
                shownStateCaptured = true;
            }
        }

        private void OnEnable()
        {
            if (!shownStateCaptured || canvasGroup == null)
            {
                return;
            }

            // Symmetric to OnDisable below: a deactivated panel keeps its shown visuals but loses
            // interaction. Without this restore, a host that toggles the GameObject directly (no
            // Show/Hide call — e.g. a bound SetActive while the owning window stays visible) gets a
            // panel that is fully drawn yet ignores every click. A transition started after this
            // (PrepareShow/Show) re-disables interaction itself for the duration of the tween.
            SetInteraction(targetShown);
        }

        private void OnDisable()
        {
            if (!shownStateCaptured || canvasGroup == null)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = targetShown ? 1f : 0f;
            ApplyVisualState(targetShown);
            SetInteraction(false);
        }
    }
}
