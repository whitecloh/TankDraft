using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    public enum UiAttentionEffect
    {
        Nudge = 0,
        Pulse = 1,
        ErrorShake = 2,
        Bounce = 3,
        HighlightRing = 4,
        SpringPulse = 5,
        BubbleReveal = 6,
    }

    /// <summary>Queued semantic attention feedback with accessibility-safe error fallback.</summary>
    [DisallowMultipleComponent]
    public sealed class UiAttentionPulse : UiMotionElement
    {
        [Header("Scope and Target")]
        [SerializeField] private Transform scopeRoot;
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private CanvasGroup highlightRing;

        [Header("Motion")]
        [SerializeField, Min(0f)] private float nudgeDistance = 8f;
        [SerializeField, Min(1f)] private float pulseScale = 1.06f;
        [SerializeField, Min(0f)] private float bounceDistance = 12f;
        [SerializeField, Min(0f)] private float errorShakeDistance = 7f;

        [Header("Spring Pulse")]
        [SerializeField, Min(1f)] private float springPulseScale = 1.09f;
        [SerializeField, Range(0.5f, 1f)] private float springPulseUndershootScale = 0.965f;
        [SerializeField, Min(0f)] private float springPulseDuration = 0.46f;

        [Header("Bubble Reveal")]
        [SerializeField, Range(0f, 1f)] private float bubbleStartScale = 0.72f;
        [SerializeField, Min(1f)] private float bubbleOvershootScale = 1.1f;
        [SerializeField, Range(0.5f, 1f)] private float bubbleUndershootScale = 0.98f;
        [SerializeField, Min(0f)] private float bubbleDuration = 0.38f;

        [Header("Error Fallback")]
        [SerializeField] private Graphic errorColorTarget;
        [SerializeField] private Color errorColor = new Color(1f, 0.25f, 0.25f, 1f);
        [SerializeField] private Outline errorOutline;
        [SerializeField] private GameObject errorMessage;

        private IUiMotionSettingsProvider settingsProvider;
        private Vector3 baseScale;
        private Vector2 basePosition;
        private float baseRingAlpha;
        private Color baseErrorColor;
        private bool baseOutlineEnabled;
        private bool baseMessageActive;
        private bool initialized;
        private bool errorFallbackInitialized;
        private bool motionPositionOwned;
        private bool motionScaleOwned;
        private bool motionRingAlphaOwned;
        private int scopeId;
        private int activeSemanticKey;
        private UiAttentionEffect activeEffect;
        private int mergeCount;

        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Attention, out Tween tween) && tween.IsPlaying();
        public int PendingCount => UiAttentionRuntime.GetPendingCount(ResolveScopeId());
        public int MergeCount => mergeCount;
        public int ActiveSemanticKey => activeSemanticKey;
        public UiAttentionEffect ActiveEffect => activeEffect;
        public UiAttentionRequestResult LastRequestResult { get; private set; }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
        }

        private void OnDisable()
        {
            CancelAll();
            ClearErrorFallback();
        }

        public void Configure(RectTransform newVisualRoot, Transform newScopeRoot = null, CanvasGroup newHighlightRing = null)
        {
            CancelAll();
            visualRoot = newVisualRoot;
            scopeRoot = newScopeRoot;
            highlightRing = newHighlightRing;
            initialized = false;
            Initialize();
        }

        public void ConfigureErrorFallback(
            Graphic colorTarget,
            Outline outline,
            GameObject message,
            Color? overrideErrorColor = null)
        {
            ClearErrorFallback();
            errorColorTarget = colorTarget;
            errorOutline = outline;
            errorMessage = message;
            if (overrideErrorColor.HasValue) errorColor = overrideErrorColor.Value;
            errorFallbackInitialized = false;
            InitializeErrorFallback();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            settingsProvider = provider;
        }

        public UiAttentionRequestResult Play(UiAttentionEffect effect, string semanticKey = "default")
        {
            Initialize();
            int key = ComputeSemanticKey(semanticKey);
            LastRequestResult = UiAttentionRuntime.Submit(this, ResolveScopeId(), key, effect);
            return LastRequestResult;
        }

        public void PlayNudge() => Play(UiAttentionEffect.Nudge, "nudge");
        public void PlayPulse() => Play(UiAttentionEffect.Pulse, "pulse");
        public void PlayError() => Play(UiAttentionEffect.ErrorShake, "error");
        public void PlayBounce() => Play(UiAttentionEffect.Bounce, "bounce");
        public void PlayHighlight() => Play(UiAttentionEffect.HighlightRing, "highlight");
        public void PlaySpringPulse() => Play(UiAttentionEffect.SpringPulse, "spring-pulse");
        public void PlayBubbleReveal() => Play(UiAttentionEffect.BubbleReveal, "bubble-reveal");

        public void CancelAll()
        {
            int currentScopeId = ResolveScopeId();
            Lifecycle.Stop(UiMotionChannel.Attention);
            UiAttentionRuntime.Cancel(this, currentScopeId);
            activeSemanticKey = 0;
            ApplyMotionBaseline();
        }

        public void ClearErrorFallback()
        {
            if (!errorFallbackInitialized) return;
            if (errorColorTarget != null) errorColorTarget.color = baseErrorColor;
            if (errorOutline != null) errorOutline.enabled = baseOutlineEnabled;
            if (errorMessage != null) errorMessage.SetActive(baseMessageActive);
            errorFallbackInitialized = false;
        }

        internal void BeginCoordinatedEffect(UiAttentionEffect effect, int semanticKey)
        {
            activeEffect = effect;
            activeSemanticKey = semanticKey;
            StartEffect(effect, semanticKey);
        }

        internal void MergeCoordinatedEffect(UiAttentionEffect effect, int semanticKey)
        {
            mergeCount++;
            if (GetPriority(effect) < GetPriority(activeEffect)) return;
            activeEffect = effect;
            StartEffect(effect, semanticKey);
        }

        internal void NotifyMergedWhileQueued()
        {
            mergeCount++;
        }

        private void StartEffect(UiAttentionEffect effect, int semanticKey)
        {
            UiMotionSettingsSnapshot settings = ResolveSettings();
            if (effect == UiAttentionEffect.ErrorShake) ApplyErrorFallback();

            UiAttentionEffect resolved = ResolveAccessibleEffect(effect, settings);
            if (!motionPositionOwned && visualRoot != null)
            {
                basePosition = visualRoot.anchoredPosition;
            }

            Tween tween = BuildTween(resolved, settings);
            if (tween == null)
            {
                CompleteEffect(semanticKey);
                return;
            }

            tween.OnComplete(() => CompleteEffect(semanticKey));
            Lifecycle.Play(UiMotionChannel.Attention, tween);
        }

        private Tween BuildTween(UiAttentionEffect effect, UiMotionSettingsSnapshot settings)
        {
            float duration = ResolveDuration(effect, settings);
            if (duration <= 0f) return null;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutBack);
            UiMotionTweenPolicy settlePolicy = ResolvePolicy(settings, UiMotionEase.InOutSine);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            switch (effect)
            {
                case UiAttentionEffect.Nudge:
                    if (visualRoot == null) return null;
                    AppendPosition(sequence, basePosition + Vector2.right * nudgeDistance, duration * 0.5f, policy);
                    AppendPosition(sequence, basePosition, duration * 0.5f, policy);
                    break;
                case UiAttentionEffect.Pulse:
                    if (visualRoot == null) return null;
                    AppendScale(sequence, baseScale * pulseScale, duration * 0.5f, policy);
                    AppendScale(sequence, baseScale, duration * 0.5f, policy);
                    break;
                case UiAttentionEffect.ErrorShake:
                    if (visualRoot == null) return null;
                    float step = duration * 0.2f;
                    AppendPosition(sequence, basePosition + Vector2.right * errorShakeDistance, step, policy);
                    AppendPosition(sequence, basePosition + Vector2.left * errorShakeDistance, step, policy);
                    AppendPosition(sequence, basePosition + Vector2.right * (errorShakeDistance * 0.5f), step, policy);
                    AppendPosition(sequence, basePosition + Vector2.left * (errorShakeDistance * 0.5f), step, policy);
                    AppendPosition(sequence, basePosition, step, policy);
                    break;
                case UiAttentionEffect.Bounce:
                    if (visualRoot == null) return null;
                    AppendPosition(sequence, basePosition + Vector2.up * bounceDistance, duration * 0.5f, policy);
                    AppendPosition(sequence, basePosition, duration * 0.5f, policy);
                    break;
                case UiAttentionEffect.HighlightRing:
                    if (highlightRing == null || settings.DisableFlashes) return null;
                    motionRingAlphaOwned = true;
                    sequence.Append(UiMotionTweenFactory.Float(
                        this,
                        () => highlightRing.alpha,
                        value => highlightRing.alpha = value,
                        1f,
                        duration * 0.5f,
                        policy));
                    sequence.Append(UiMotionTweenFactory.Float(
                        this,
                        () => highlightRing.alpha,
                        value => highlightRing.alpha = value,
                        baseRingAlpha,
                        duration * 0.5f,
                        policy));
                    break;
                case UiAttentionEffect.SpringPulse:
                    if (visualRoot == null) return null;
                    AppendScale(sequence, baseScale * springPulseScale, duration * 0.32f, policy);
                    AppendScale(sequence, baseScale * springPulseUndershootScale, duration * 0.24f, settlePolicy);
                    AppendScale(sequence, baseScale * 1.025f, duration * 0.2f, settlePolicy);
                    AppendScale(sequence, baseScale, duration * 0.24f, settlePolicy);
                    break;
                case UiAttentionEffect.BubbleReveal:
                    if (visualRoot == null) return null;
                    motionScaleOwned = true;
                    visualRoot.localScale = baseScale * bubbleStartScale;
                    AppendScale(sequence, baseScale * bubbleOvershootScale, duration * 0.48f, policy);
                    AppendScale(sequence, baseScale * bubbleUndershootScale, duration * 0.22f, settlePolicy);
                    AppendScale(sequence, baseScale, duration * 0.3f, settlePolicy);
                    break;
                default:
                    return null;
            }

            return sequence;
        }

        private UiAttentionEffect ResolveAccessibleEffect(
            UiAttentionEffect requested,
            UiMotionSettingsSnapshot settings)
        {
            UiMotionEffectKind effectKind;
            switch (requested)
            {
                case UiAttentionEffect.Pulse:
                case UiAttentionEffect.SpringPulse:
                case UiAttentionEffect.BubbleReveal:
                    effectKind = UiMotionEffectKind.Scale;
                    break;
                case UiAttentionEffect.Nudge:
                case UiAttentionEffect.Bounce: effectKind = UiMotionEffectKind.Translation; break;
                case UiAttentionEffect.ErrorShake: effectKind = UiMotionEffectKind.Shake; break;
                case UiAttentionEffect.HighlightRing: effectKind = UiMotionEffectKind.Fade; break;
                default: effectKind = UiMotionEffectKind.Immediate; break;
            }

            UiMotionEffectKind resolved = settings.ResolveEffect(effectKind);
            if (resolved == effectKind) return requested;
            return resolved == UiMotionEffectKind.Fade
                ? UiAttentionEffect.HighlightRing
                : (UiAttentionEffect)(-1);
        }

        private void AppendPosition(
            Sequence sequence,
            Vector2 target,
            float duration,
            UiMotionTweenPolicy policy)
        {
            motionPositionOwned = true;
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.anchoredPosition,
                value => visualRoot.anchoredPosition = value,
                target,
                duration,
                policy));
        }

        private void AppendScale(
            Sequence sequence,
            Vector3 target,
            float duration,
            UiMotionTweenPolicy policy)
        {
            motionScaleOwned = true;
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => visualRoot.localScale,
                value => visualRoot.localScale = value,
                target,
                duration,
                policy));
        }

        private void CompleteEffect(int semanticKey)
        {
            ApplyMotionBaseline();
            int currentScope = ResolveScopeId();
            activeSemanticKey = 0;
            UiAttentionRuntime.Complete(this, currentScope, semanticKey);
        }

        private void ApplyMotionBaseline()
        {
            if (initialized && visualRoot != null)
            {
                if (motionScaleOwned) visualRoot.localScale = baseScale;
                if (motionPositionOwned) visualRoot.anchoredPosition = basePosition;
            }

            if (initialized && motionRingAlphaOwned && highlightRing != null)
            {
                highlightRing.alpha = baseRingAlpha;
            }

            motionPositionOwned = false;
            motionScaleOwned = false;
            motionRingAlphaOwned = false;
        }

        private void ApplyErrorFallback()
        {
            InitializeErrorFallback();
            if (errorColorTarget != null) errorColorTarget.color = errorColor;
            if (errorOutline != null) errorOutline.enabled = true;
            if (errorMessage != null) errorMessage.SetActive(true);
        }

        private void Initialize()
        {
            if (initialized) return;
            if (visualRoot == null) visualRoot = transform as RectTransform;
            if (visualRoot != null)
            {
                baseScale = visualRoot.localScale;
                basePosition = visualRoot.anchoredPosition;
            }

            baseRingAlpha = highlightRing != null ? highlightRing.alpha : 0f;
            scopeId = ComputeScopeId();
            InitializeErrorFallback();
            initialized = true;
        }

        private void InitializeErrorFallback()
        {
            if (errorFallbackInitialized) return;
            baseErrorColor = errorColorTarget != null ? errorColorTarget.color : Color.white;
            baseOutlineEnabled = errorOutline != null && errorOutline.enabled;
            baseMessageActive = errorMessage != null && errorMessage.activeSelf;
            errorFallbackInitialized = true;
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            return settingsProvider != null ? settingsProvider.Current : Settings;
        }

        private int ResolveScopeId()
        {
            if (scopeId == 0) scopeId = ComputeScopeId();
            return scopeId;
        }

        private int ComputeScopeId()
        {
            Transform resolvedScope = scopeRoot != null
                ? scopeRoot
                : transform.parent != null ? transform.parent : transform;
            return resolvedScope.GetInstanceID();
        }

        private static int ComputeSemanticKey(string value)
        {
            if (string.IsNullOrEmpty(value)) value = "default";
            unchecked
            {
                uint hash = 2166136261u;
                for (int i = 0; i < value.Length; i++) hash = (hash ^ value[i]) * 16777619u;
                return (int)hash;
            }
        }

        private static int GetPriority(UiAttentionEffect effect)
        {
            switch (effect)
            {
                case UiAttentionEffect.ErrorShake: return 5;
                case UiAttentionEffect.HighlightRing: return 4;
                case UiAttentionEffect.BubbleReveal: return 4;
                case UiAttentionEffect.SpringPulse: return 3;
                case UiAttentionEffect.Bounce: return 3;
                case UiAttentionEffect.Pulse: return 2;
                case UiAttentionEffect.Nudge: return 1;
                default: return 0;
            }
        }

        private float ResolveDuration(UiAttentionEffect effect, UiMotionSettingsSnapshot settings)
        {
            switch (effect)
            {
                case UiAttentionEffect.SpringPulse:
                    return UiMotionDefaults.ScaleDuration(springPulseDuration, settings.Intensity);
                case UiAttentionEffect.BubbleReveal:
                    return UiMotionDefaults.ScaleDuration(bubbleDuration, settings.Intensity);
                default:
                    return settings.ResolveDuration(UiMotionTiming.Fast);
            }
        }
    }
}
