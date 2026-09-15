using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    public enum UiIdleMotionMode
    {
        Breathing = 0,
        Bob = 1,
        GentleRotation = 2,
        GlowPulse = 3,
        ContinuousRotation = 4,
        ShimmerTrigger = 5,
        Wiggle = 6,
    }

    /// <summary>Budgeted, visibility-owned ambient motion without a component Update loop.</summary>
    [DisallowMultipleComponent]
    public sealed class UiIdleMotion : UiMotionElement
    {
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private CanvasGroup glowGroup;
        [SerializeField] private UiIdleMotionMode mode = UiIdleMotionMode.Breathing;
        [SerializeField] private UiMotionBudgetClass budgetClass = UiMotionBudgetClass.Secondary;
        [SerializeField] private bool suppressWhenModalOpen = true;
        [SerializeField] private int deterministicSeed;
        [SerializeField, Min(0.01f)] private float cycleMultiplier = 6f;
        [SerializeField, Min(0f)] private float scaleAmplitude = 0.025f;
        [SerializeField, Min(0f)] private float positionAmplitude = 6f;
        [SerializeField, Min(0f)] private float rotationAmplitude = 2f;
        [SerializeField, Min(0f)] private float cyclePause;
        [SerializeField, Range(0f, 1f)] private float glowMaxAlpha = 1f;
        [SerializeField] private UnityEvent shimmerTriggered;

        private IUiMotionSettingsProvider settingsProvider;
        private UiMotionBudget.Lease budgetLease;
        private bool hasBudgetLease;
        private bool bypassBudget;
        private bool logicalVisible = true;
        private bool initialized;
        private Vector3 baseScale;
        private Vector2 basePosition;
        private Vector3 baseEulerAngles;
        private float baseGlowAlpha;
        private float phaseOffset01;

        public UiIdleMotionMode Mode
        {
            get => mode;
            set
            {
                if (mode == value) return;
                mode = value;
                RefreshAdmission();
            }
        }

        public UiMotionBudgetClass BudgetClass
        {
            get => budgetClass;
            set
            {
                if (budgetClass == value) return;
                budgetClass = value;
                RefreshAdmission();
            }
        }

        public bool IsAdmitted => bypassBudget ? IsPlaying : hasBudgetLease;
        public bool UsesBudget => !bypassBudget;
        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Idle, out Tween tween) && tween.IsPlaying();
        public float CycleMultiplier => cycleMultiplier;
        public float CyclePause => cyclePause;
        public float PhaseOffset01
        {
            get
            {
                Initialize();
                return phaseOffset01;
            }
        }
        public UnityEvent ShimmerTriggered => shimmerTriggered;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            UiIdleMotionRuntime.ModalSuppressionChanged += HandleModalSuppression;
            SubscribeSettings();
            TryStart();
        }

        private void OnDisable()
        {
            UiIdleMotionRuntime.ModalSuppressionChanged -= HandleModalSuppression;
            UnsubscribeSettings();
            StopAndRelease(true);
        }

        public void Configure(
            UiIdleMotionMode newMode,
            RectTransform newVisualRoot = null,
            CanvasGroup newGlowGroup = null)
        {
            StopAndRelease(true);
            bypassBudget = false;
            mode = newMode;
            visualRoot = newVisualRoot;
            glowGroup = newGlowGroup;
            initialized = false;
            Initialize();
            TryStart();
        }

        /// <summary>Configures a short repeating rotation wiggle for valid drop targets.</summary>
        public void ConfigureWiggle(
            RectTransform target,
            float rotationDegrees = 1.25f,
            float durationMultiplier = 1.2f,
            float pauseBetweenCycles = 0f,
            bool useBudget = true)
        {
            StopAndRelease(true);
            bypassBudget = !useBudget;
            mode = UiIdleMotionMode.Wiggle;
            visualRoot = target;
            glowGroup = null;
            rotationAmplitude = Mathf.Max(0f, rotationDegrees);
            cycleMultiplier = Mathf.Max(0.1f, durationMultiplier);
            cyclePause = Mathf.Max(0f, pauseBetweenCycles);
            initialized = false;
            Initialize();
            TryStart();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider)) return;
            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
            RefreshAdmission();
        }

        /// <summary>Host visibility bridge for CanvasGroup-hidden objects that remain active.</summary>
        public void SetVisible(bool value)
        {
            if (logicalVisible == value) return;
            logicalVisible = value;
            if (value) TryStart();
            else StopAndRelease(true);
        }

        /// <summary>Re-reads visibility, quality, intensity and accessibility policy.</summary>
        public void RefreshAdmission()
        {
            StopAndRelease(true);
            TryStart();
        }

        private void Initialize()
        {
            if (initialized) return;
            if (visualRoot == null) visualRoot = transform as RectTransform;
            if (visualRoot != null)
            {
                baseScale = visualRoot.localScale;
                basePosition = visualRoot.anchoredPosition;
                baseEulerAngles = visualRoot.localEulerAngles;
            }

            baseGlowAlpha = glowGroup != null ? glowGroup.alpha : 0f;
            phaseOffset01 = UiMotionDefaults.GetDeterministicPhase01(ComputeStableId());
            initialized = true;
        }

        private void TryStart()
        {
            if (!CanAdmit(out UiMotionSettingsSnapshot settings)) return;
            if (!bypassBudget)
            {
                if (!UiIdleMotionRuntime.TryAcquire(settings, budgetClass, out budgetLease)) return;
                hasBudgetLease = true;
            }

            Tween tween = BuildTween(settings);
            if (tween == null)
            {
                ReleaseBudget();
                return;
            }

            Lifecycle.Play(UiMotionChannel.Idle, tween);
        }

        private bool CanAdmit(out UiMotionSettingsSnapshot settings)
        {
            settings = ResolveSettings();
            if (!isActiveAndEnabled || !logicalVisible || !settings.IdleEffects || settings.Intensity <= 0f)
            {
                return false;
            }

            if (suppressWhenModalOpen && UiIdleMotionRuntime.IsModalSuppressed) return false;
            if (settings.Quality != null
                && settings.Quality.Tier == UiMotionQualityTier.Low
                && (mode == UiIdleMotionMode.GlowPulse || mode == UiIdleMotionMode.ShimmerTrigger))
            {
                return false;
            }

            UiMotionEffectKind requested = ResolveEffectKind();
            return settings.ResolveEffect(requested) == requested;
        }

        private Tween BuildTween(UiMotionSettingsSnapshot settings)
        {
            float qualityScale = settings.Quality == null
                ? 1f
                : settings.Quality.Tier == UiMotionQualityTier.Low ? 0.5f
                : settings.Quality.Tier == UiMotionQualityTier.High ? 1.1f
                : 1f;
            float amplitude = settings.Intensity * qualityScale;
            UiMotionTiming timing = mode == UiIdleMotionMode.Wiggle
                ? UiMotionTiming.Fast
                : UiMotionTiming.Slow;
            float duration = settings.ResolveDuration(timing) * cycleMultiplier;
            if (duration <= 0f) return null;

            UiMotionTweenPolicy policy = ResolvePolicy(
                settings,
                mode == UiIdleMotionMode.ContinuousRotation ? UiMotionEase.Linear : UiMotionEase.InOutCubic,
                UiMotionDisableBehaviour.Kill);
            Tween tween;
            switch (mode)
            {
                case UiIdleMotionMode.Breathing:
                    if (visualRoot == null) return null;
                    tween = UiMotionTweenFactory.Vector3(
                            this,
                            () => visualRoot.localScale,
                            value => visualRoot.localScale = value,
                            baseScale * (1f + scaleAmplitude * amplitude),
                            duration,
                            policy)
                        .SetLoops(-1, LoopType.Yoyo);
                    break;
                case UiIdleMotionMode.Bob:
                    if (visualRoot == null) return null;
                    tween = UiMotionTweenFactory.Vector3(
                            this,
                            () => visualRoot.anchoredPosition,
                            value => visualRoot.anchoredPosition = value,
                            basePosition + Vector2.up * (positionAmplitude * amplitude),
                            duration,
                            policy)
                        .SetLoops(-1, LoopType.Yoyo);
                    break;
                case UiIdleMotionMode.GentleRotation:
                    if (visualRoot == null) return null;
                    tween = UiMotionTweenFactory.Vector3(
                            this,
                            () => visualRoot.localEulerAngles,
                            value => visualRoot.localEulerAngles = value,
                            baseEulerAngles + Vector3.forward * (rotationAmplitude * amplitude),
                            duration,
                            policy)
                        .SetLoops(-1, LoopType.Yoyo);
                    break;
                case UiIdleMotionMode.GlowPulse:
                    if (glowGroup == null) return null;
                    float targetAlpha = Mathf.Lerp(baseGlowAlpha, glowMaxAlpha, amplitude);
                    tween = UiMotionTweenFactory.Float(
                            this,
                            () => glowGroup.alpha,
                            value => glowGroup.alpha = value,
                            targetAlpha,
                            duration,
                            policy)
                        .SetLoops(-1, LoopType.Yoyo);
                    break;
                case UiIdleMotionMode.ContinuousRotation:
                    if (visualRoot == null) return null;
                    tween = UiMotionTweenFactory.Vector3(
                            this,
                            () => visualRoot.localEulerAngles,
                            value => visualRoot.localEulerAngles = value,
                            baseEulerAngles + Vector3.back * 360f,
                            duration * 2f,
                            policy)
                        .SetLoops(-1, LoopType.Restart)
                        .SetEase(Ease.Linear);
                    break;
                case UiIdleMotionMode.ShimmerTrigger:
                    Sequence shimmer = UiMotionTweenFactory.Sequence(this, policy);
                    shimmer.AppendInterval(duration * 2f);
                    shimmer.AppendCallback(() => shimmerTriggered?.Invoke());
                    tween = shimmer.SetLoops(-1, LoopType.Restart);
                    break;
                case UiIdleMotionMode.Wiggle:
                    if (visualRoot == null) return null;
                    float rotation = rotationAmplitude * amplitude;
                    float phase = 0f;
                    Quaternion authoredRotation = visualRoot.localRotation;
                    Tween wiggleCycle = UiMotionTweenFactory.Float(
                            this,
                            () => phase,
                            value =>
                            {
                                phase = value;
                                float signedAngle = Mathf.Sin(value * Mathf.PI * 2f) * rotation;
                                visualRoot.localRotation = authoredRotation * Quaternion.AngleAxis(signedAngle, Vector3.forward);
                            },
                            1f,
                            duration,
                            policy)
                        .SetEase(Ease.Linear);
                    Sequence wiggle = UiMotionTweenFactory.Sequence(this, policy);
                    wiggle.Append(wiggleCycle);
                    if (cyclePause > 0f)
                    {
                        wiggle.AppendInterval(cyclePause);
                    }

                    tween = wiggle.SetLoops(-1, LoopType.Restart);
                    break;
                default:
                    return null;
            }

            tween.SetDelay(duration * phaseOffset01, false);
            return tween;
        }

        private void StopAndRelease(bool restoreBaseline)
        {
            Lifecycle.Stop(UiMotionChannel.Idle);
            ReleaseBudget();
            if (restoreBaseline) ApplyBaseline();
        }

        private void ReleaseBudget()
        {
            if (!hasBudgetLease) return;
            hasBudgetLease = false;
            budgetLease.Dispose();
            budgetLease = default;
        }

        private void ApplyBaseline()
        {
            if (!initialized) return;
            if (visualRoot != null)
            {
                visualRoot.localScale = baseScale;
                visualRoot.anchoredPosition = basePosition;
                visualRoot.localEulerAngles = baseEulerAngles;
            }

            if (glowGroup != null) glowGroup.alpha = baseGlowAlpha;
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            return settingsProvider != null ? settingsProvider.Current : Settings;
        }

        private UiMotionEffectKind ResolveEffectKind()
        {
            switch (mode)
            {
                case UiIdleMotionMode.Breathing: return UiMotionEffectKind.Scale;
                case UiIdleMotionMode.Bob: return UiMotionEffectKind.Translation;
                case UiIdleMotionMode.GentleRotation:
                case UiIdleMotionMode.ContinuousRotation:
                case UiIdleMotionMode.Wiggle:
                    return UiMotionEffectKind.Rotation;
                case UiIdleMotionMode.GlowPulse: return UiMotionEffectKind.Fade;
                case UiIdleMotionMode.ShimmerTrigger: return UiMotionEffectKind.Shimmer;
                default: return UiMotionEffectKind.Immediate;
            }
        }

        private int ComputeStableId()
        {
            unchecked
            {
                uint hash = 2166136261u;
                Transform current = transform;
                while (current != null)
                {
                    string objectName = current.name;
                    for (int i = 0; i < objectName.Length; i++)
                    {
                        hash = (hash ^ objectName[i]) * 16777619u;
                    }

                    hash = (hash ^ (uint)current.GetSiblingIndex()) * 16777619u;
                    current = current.parent;
                }

                string scenePath = gameObject.scene.path;
                for (int i = 0; i < scenePath.Length; i++)
                {
                    hash = (hash ^ scenePath[i]) * 16777619u;
                }

                hash = (hash ^ (uint)deterministicSeed) * 16777619u;
                return (int)hash;
            }
        }

        private void HandleModalSuppression(bool suppressed)
        {
            if (!suppressWhenModalOpen) return;
            if (suppressed) StopAndRelease(true);
            else TryStart();
        }

        private void HandleSettingsChanged()
        {
            RefreshAdmission();
        }

        private void SubscribeSettings()
        {
            if (settingsProvider != null) settingsProvider.Changed += HandleSettingsChanged;
        }

        private void UnsubscribeSettings()
        {
            if (settingsProvider != null) settingsProvider.Changed -= HandleSettingsChanged;
        }
    }
}
