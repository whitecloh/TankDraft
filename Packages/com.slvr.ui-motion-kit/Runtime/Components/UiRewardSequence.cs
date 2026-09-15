using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    public enum UiRewardStepKind
    {
        Dim = 0,
        Title = 1,
        RewardReveal = 2,
        Rays = 3,
        Burst = 4,
        Rarity = 5,
        Ticker = 6,
        CurrencyFly = 7,
        Cta = 8,
    }

    [Serializable]
    public sealed class UiRewardSequenceStep
    {
        [SerializeField] private UiRewardStepKind kind;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform root;
        [SerializeField, Min(0f)] private float duration = 0.2f;
        [SerializeField, Range(0f, 1f)] private float targetAlpha = 1f;
        [SerializeField, Min(1f)] private float overshootScale = 1.1f;

        public UiRewardSequenceStep(
            UiRewardStepKind kind,
            CanvasGroup group = null,
            RectTransform root = null,
            float duration = 0.2f,
            float targetAlpha = 1f,
            float overshootScale = 1.1f)
        {
            this.kind = kind;
            this.group = group;
            this.root = root;
            this.duration = duration;
            this.targetAlpha = targetAlpha;
            this.overshootScale = overshootScale;
        }

        public UiRewardStepKind Kind => kind;
        public CanvasGroup Group => group;
        public RectTransform Root => root;
        public float Duration => Mathf.Max(0f, duration);
        public float TargetAlpha => Mathf.Clamp01(targetAlpha);
        public float OvershootScale => Mathf.Max(1f, overshootScale);
    }

    /// <summary>Composable, skippable reward presentation that always converges to one valid final state.</summary>
    [DisallowMultipleComponent]
    public sealed class UiRewardSequence : UiMotionElement
    {
        [SerializeField] private UiRewardSequenceStep[] steps = Array.Empty<UiRewardSequenceStep>();
        [SerializeField] private UiNumberTicker ticker;
        [SerializeField] private UiCurrencyFlyEffect currencyFly;
        [SerializeField] private double tickerTarget;
        [SerializeField, Min(0)] private int currencyIconCount = 8;
        [SerializeField, Min(0f)] private float minimumReadableTime = 0.35f;
        [SerializeField, Min(1f)] private float acceleratedTimeScale = 3f;
        [SerializeField] private UnityEvent<UiRewardStepKind> stepStarted = new UnityEvent<UiRewardStepKind>();
        [SerializeField] private UnityEvent completed = new UnityEvent();
        [SerializeField] private UnityEvent skipped = new UnityEvent();

        private IUiMotionSettingsProvider settingsProvider;
        private Sequence sequence;
        private Vector3[] baseScales = Array.Empty<Vector3>();
        private float startedAt;
        private bool finalStateApplied;

        public bool IsPlaying => sequence != null && sequence.IsActive() && sequence.IsPlaying();
        public float Elapsed => Mathf.Max(0f, Time.unscaledTime - startedAt);
        public UnityEvent<UiRewardStepKind> StepStarted => stepStarted;
        public UnityEvent Completed => completed;
        public UnityEvent Skipped => skipped;

        private void OnDisable()
        {
            if (sequence != null && sequence.IsActive()) sequence.Kill(false);
            sequence = null;
            currencyFly?.Skip();
            if (!finalStateApplied) ApplyFinalState();
        }

        private void OnDestroy()
        {
            if (sequence != null && sequence.IsActive()) sequence.Kill(false);
            sequence = null;
            currencyFly?.Skip();
        }

        public void Configure(
            UiRewardSequenceStep[] newSteps,
            UiNumberTicker newTicker = null,
            UiCurrencyFlyEffect newCurrencyFly = null)
        {
            if (IsPlaying) ForceSkip();
            steps = newSteps ?? Array.Empty<UiRewardSequenceStep>();
            ticker = newTicker;
            currencyFly = newCurrencyFly;
            CaptureBaselines();
        }

        public void ConfigureValues(double newTickerTarget, int newCurrencyIconCount)
        {
            tickerTarget = newTickerTarget;
            currencyIconCount = Mathf.Max(0, newCurrencyIconCount);
        }

        public void ConfigureTiming(float newMinimumReadableTime, float newAcceleratedTimeScale)
        {
            minimumReadableTime = Mathf.Max(0f, newMinimumReadableTime);
            acceleratedTimeScale = Mathf.Max(1f, newAcceleratedTimeScale);
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            settingsProvider = provider;
        }

        public void Play()
        {
            if (!isActiveAndEnabled) return;
            if (sequence != null && sequence.IsActive()) sequence.Kill(false);
            currencyFly?.Skip();
            CaptureBaselines();
            UiMotionSettingsSnapshot settings = ResolveSettings();
            PrepareInitialState(settings);
            finalStateApplied = false;
            startedAt = Time.unscaledTime;

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutBack);
            sequence = UiMotionTweenFactory.Sequence(this, policy);
            for (int i = 0; i < steps.Length; i++) AppendStep(sequence, steps[i], i, settings, policy);
            sequence.OnComplete(() =>
            {
                sequence = null;
                ApplyFinalState();
                completed?.Invoke();
            });
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public bool TryAccelerate()
        {
            if (!IsPlaying || Elapsed < minimumReadableTime) return false;
            sequence.timeScale = acceleratedTimeScale;
            return true;
        }

        public bool TrySkip()
        {
            if (!IsPlaying || Elapsed < minimumReadableTime) return false;
            ForceSkip();
            return true;
        }

        public void ForceSkip()
        {
            if (sequence != null && sequence.IsActive()) sequence.Kill(false);
            sequence = null;
            currencyFly?.Skip();
            ApplyFinalState();
            skipped?.Invoke();
        }

        private void AppendStep(
            Sequence ownerSequence,
            UiRewardSequenceStep step,
            int index,
            UiMotionSettingsSnapshot settings,
            UiMotionTweenPolicy policy)
        {
            if (step == null) return;
            ownerSequence.AppendCallback(() =>
            {
                stepStarted?.Invoke(step.Kind);
                TriggerSemanticStep(step.Kind);
            });

            float duration = UiMotionDefaults.ScaleDuration(step.Duration, settings.Intensity);
            if (duration <= 0f)
            {
                ownerSequence.AppendCallback(() => ApplyStepFinal(step, index));
                return;
            }

            if (step.Group != null)
            {
                ownerSequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => step.Group.alpha,
                    alpha => step.Group.alpha = alpha,
                    step.TargetAlpha,
                    duration,
                    policy));
            }
            else
            {
                ownerSequence.AppendInterval(duration);
            }

            bool allowScale = settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            if (allowScale && step.Root != null && UsesRevealScale(step.Kind))
            {
                Vector3 baseline = GetBaseScale(index, step.Root);
                ownerSequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => step.Root.localScale,
                    value => step.Root.localScale = value,
                    baseline * step.OvershootScale,
                    duration * 0.55f,
                    policy));
                ownerSequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => step.Root.localScale,
                    value => step.Root.localScale = value,
                    baseline,
                    duration * 0.45f,
                    policy));
            }
        }

        private void TriggerSemanticStep(UiRewardStepKind kind)
        {
            switch (kind)
            {
                case UiRewardStepKind.Ticker:
                    ticker?.SetValue(tickerTarget);
                    break;
                case UiRewardStepKind.CurrencyFly:
                    currencyFly?.Play(currencyIconCount);
                    break;
            }
        }

        private void PrepareInitialState(UiMotionSettingsSnapshot settings)
        {
            bool allowScale = settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            for (int i = 0; i < steps.Length; i++)
            {
                UiRewardSequenceStep step = steps[i];
                if (step == null) continue;
                if (step.Group != null) step.Group.alpha = 0f;
                if (allowScale && step.Root != null && UsesRevealScale(step.Kind))
                {
                    step.Root.localScale = GetBaseScale(i, step.Root) * 0.85f;
                }
            }
        }

        private void ApplyFinalState()
        {
            for (int i = 0; i < steps.Length; i++) ApplyStepFinal(steps[i], i);
            ticker?.SetValue(tickerTarget, true);
            finalStateApplied = true;
        }

        private void ApplyStepFinal(UiRewardSequenceStep step, int index)
        {
            if (step == null) return;
            if (step.Group != null) step.Group.alpha = step.TargetAlpha;
            if (step.Root != null) step.Root.localScale = GetBaseScale(index, step.Root);
        }

        private void CaptureBaselines()
        {
            if (baseScales.Length != steps.Length) baseScales = new Vector3[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                UiRewardSequenceStep step = steps[i];
                baseScales[i] = step != null && step.Root != null ? step.Root.localScale : Vector3.one;
            }
        }

        private Vector3 GetBaseScale(int index, RectTransform root)
        {
            return index >= 0 && index < baseScales.Length ? baseScales[index] : root.localScale;
        }

        private static bool UsesRevealScale(UiRewardStepKind kind)
        {
            return kind == UiRewardStepKind.Title
                || kind == UiRewardStepKind.RewardReveal
                || kind == UiRewardStepKind.Rarity
                || kind == UiRewardStepKind.Cta;
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            return settingsProvider != null ? settingsProvider.Current : Settings;
        }
    }
}
