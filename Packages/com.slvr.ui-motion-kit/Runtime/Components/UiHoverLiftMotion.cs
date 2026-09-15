using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SLVR.UIMotion
{
    public enum UiHoverMotionMode
    {
        LiftAndParallax = 0,
        ScaleWave = 1,
        ScaleOnly = 2,
    }

    /// <summary>
    /// Reusable mouse/pen hover and navigation-focus motion for cards and other focal UI elements.
    /// Pointer parallax is event-driven; scale waves own one looping tween only while active.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiHoverLiftMotion : UiMotionElement,
        IPointerEnterHandler,
        IPointerMoveHandler,
        IPointerExitHandler,
        ISelectHandler,
        IDeselectHandler
    {
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private UiHoverMotionMode mode;
        [SerializeField, Min(1f)] private float hoverScale = 1.03f;
        [SerializeField, Min(0f)] private float lift = 8f;
        [SerializeField, Min(0f)] private float maxParallax = 6f;
        [SerializeField, Min(0f)] private float maxRotation = 1.25f;
        [SerializeField, Min(0f)] private float waveScaleDelta = 0.01f;
        [SerializeField, Min(0.1f)] private float waveCycleDuration = 0.8f;

        private Vector3 baseScale;
        private Vector2 basePosition;
        private Vector3 baseEulerAngles;
        private bool initialized;
        private bool interactionEnabled = true;
        private bool hovered;
        private bool focused;

        public RectTransform VisualRoot => visualRoot;
        public UiHoverMotionMode Mode => mode;
        public bool IsHovered => hovered;
        public bool IsFocused => focused;

        private void Awake()
        {
            Initialize();
        }

        private void OnDisable()
        {
            hovered = false;
            focused = false;
            ResetImmediate();
        }

        public void Configure(
            RectTransform target,
            float scale = 1.03f,
            float liftDistance = 8f,
            float parallaxDistance = 6f,
            float rotationDegrees = 1.25f)
        {
            visualRoot = target;
            mode = UiHoverMotionMode.LiftAndParallax;
            hoverScale = Mathf.Max(1f, scale);
            lift = Mathf.Max(0f, liftDistance);
            maxParallax = Mathf.Max(0f, parallaxDistance);
            maxRotation = Mathf.Max(0f, rotationDegrees);
            initialized = false;
            Initialize();
            ResetImmediate();
        }

        public void ConfigureScaleWave(
            RectTransform target,
            float scale = 1.015f,
            float scaleDelta = 0.01f,
            float cycleDuration = 0.8f)
        {
            visualRoot = target;
            mode = UiHoverMotionMode.ScaleWave;
            hoverScale = Mathf.Max(1f, scale);
            lift = 0f;
            maxParallax = 0f;
            maxRotation = 0f;
            waveScaleDelta = Mathf.Max(0f, scaleDelta);
            waveCycleDuration = Mathf.Max(0.1f, cycleDuration);
            initialized = false;
            Initialize();
            ResetImmediate();
        }

        /// <summary>
        /// Scales a visual without ever writing its anchored position or rotation. Use this for
        /// roots owned by a LayoutGroup, where restoring an authored position would fight layout.
        /// </summary>
        public void ConfigureScaleOnly(RectTransform target, float scale = 1.03f)
        {
            visualRoot = target;
            mode = UiHoverMotionMode.ScaleOnly;
            hoverScale = Mathf.Max(1f, scale);
            lift = 0f;
            maxParallax = 0f;
            maxRotation = 0f;
            waveScaleDelta = 0f;
            initialized = false;
            Initialize();
            ResetImmediate();
        }

        public void SetInteractionEnabled(bool value)
        {
            interactionEnabled = value;
            if (!value)
            {
                hovered = false;
                focused = false;
                ResetImmediate();
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!interactionEnabled || UiPointerDeviceUtility.IsTouch(eventData))
            {
                return;
            }

            hovered = true;
            AnimateRestState();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!interactionEnabled || !hovered || eventData == null || visualRoot == null)
            {
                return;
            }

            if (mode != UiHoverMotionMode.LiftAndParallax)
            {
                return;
            }

            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    visualRoot,
                    eventData.position,
                    eventData.enterEventCamera,
                    out Vector2 localPoint))
            {
                return;
            }

            Rect rect = visualRoot.rect;
            float x = rect.width > 0f ? Mathf.Clamp(localPoint.x / (rect.width * 0.5f), -1f, 1f) : 0f;
            float y = rect.height > 0f ? Mathf.Clamp(localPoint.y / (rect.height * 0.5f), -1f, 1f) : 0f;
            UiMotionSettingsSnapshot settings = Settings;
            Lifecycle.Stop(UiMotionChannel.Custom1);
            visualRoot.localScale = settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale
                ? baseScale * hoverScale
                : baseScale;
            visualRoot.anchoredPosition = settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation
                ? basePosition + new Vector2(x * maxParallax, lift + y * maxParallax)
                : basePosition;
            visualRoot.localEulerAngles = settings.ResolveEffect(UiMotionEffectKind.Rotation) == UiMotionEffectKind.Rotation
                ? baseEulerAngles + Vector3.forward * (-x * maxRotation)
                : baseEulerAngles;
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            hovered = false;
            AnimateRestState();
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (!interactionEnabled)
            {
                return;
            }

            focused = true;
            AnimateRestState();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            focused = false;
            AnimateRestState();
        }

        private void AnimateRestState()
        {
            Initialize();
            bool lifted = interactionEnabled && (hovered || focused);
            UiMotionSettingsSnapshot settings = Settings;
            if (mode == UiHoverMotionMode.ScaleWave)
            {
                AnimateScaleWaveState(lifted, settings);
                return;
            }

            bool transformOwnedByLayout = mode == UiHoverMotionMode.ScaleOnly;
            Vector3 targetScale = lifted && settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale
                ? baseScale * hoverScale
                : baseScale;
            Vector2 targetPosition = lifted && settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation
                ? basePosition + Vector2.up * lift
                : basePosition;
            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                visualRoot.localScale = targetScale;
                if (!transformOwnedByLayout)
                {
                    visualRoot.anchoredPosition = targetPosition;
                    visualRoot.localEulerAngles = baseEulerAngles;
                }

                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Join(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                targetScale,
                duration,
                policy));
            if (!transformOwnedByLayout)
            {
                sequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.anchoredPosition,
                    value => visualRoot.anchoredPosition = value,
                    targetPosition,
                    duration,
                    policy));
                sequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localEulerAngles,
                    value => visualRoot.localEulerAngles = value,
                    baseEulerAngles,
                    duration,
                    policy));
            }

            Lifecycle.Play(UiMotionChannel.Custom1, sequence);
        }

        private void AnimateScaleWaveState(bool active, UiMotionSettingsSnapshot settings)
        {
            bool scaleAllowed = active
                && settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            Vector3 targetScale = scaleAllowed ? baseScale * hoverScale : baseScale;
            float duration = ResolveDuration(UiMotionTiming.Fast);
            Lifecycle.Stop(UiMotionChannel.Custom1);

            if (duration <= 0f)
            {
                visualRoot.localScale = baseScale;
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutSine);
            Sequence transition = UiMotionTweenFactory.Sequence(this, policy);
            transition.Join(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                targetScale,
                duration,
                policy));
            if (scaleAllowed && waveScaleDelta > 0f)
            {
                transition.OnComplete(StartScaleWaveLoop);
            }

            Lifecycle.Play(UiMotionChannel.Custom1, transition);
        }

        private void StartScaleWaveLoop()
        {
            if (!isActiveAndEnabled || !interactionEnabled || (!hovered && !focused) || visualRoot == null)
            {
                return;
            }

            UiMotionSettingsSnapshot settings = Settings;
            if (settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale)
            {
                return;
            }

            float halfCycle = UiMotionDefaults.ScaleDuration(waveCycleDuration * 0.5f, settings.Intensity);
            if (halfCycle <= 0f)
            {
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutSine);
            Sequence wave = UiMotionTweenFactory.Sequence(this, policy);
            wave.Append(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                baseScale * (hoverScale + waveScaleDelta),
                halfCycle,
                policy));
            wave.Append(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                baseScale * hoverScale,
                halfCycle,
                policy));
            wave.SetLoops(-1, LoopType.Restart);
            Lifecycle.Play(UiMotionChannel.Custom1, wave);
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (visualRoot == null)
            {
                visualRoot = transform as RectTransform;
            }

            if (visualRoot != null)
            {
                baseScale = visualRoot.localScale;
                basePosition = visualRoot.anchoredPosition;
                baseEulerAngles = visualRoot.localEulerAngles;
            }

            initialized = true;
        }

        private void ResetImmediate()
        {
            if (!initialized || visualRoot == null)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Custom1);
            visualRoot.localScale = baseScale;
            if (mode == UiHoverMotionMode.LiftAndParallax)
            {
                visualRoot.anchoredPosition = basePosition;
                visualRoot.localEulerAngles = baseEulerAngles;
            }
        }
    }
}
