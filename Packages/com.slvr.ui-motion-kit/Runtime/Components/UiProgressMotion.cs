using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Layout-safe fill animation with a trailing ghost, sweep and milestone hooks.</summary>
    [DisallowMultipleComponent]
    public sealed class UiProgressMotion : UiMotionElement
    {
        [Header("Fill")]
        [SerializeField] private Image mainFill;
        [SerializeField] private Image ghostFill;
        [SerializeField] private CanvasGroup sweep;
        [SerializeField, Min(0.01f)] private float minimumDuration = 0.08f;
        [SerializeField, Min(0.01f)] private float maximumDuration = 0.5f;

        [Header("Milestones")]
        [SerializeField] private float[] milestones = { 0.25f, 0.5f, 0.75f, 1f };
        [SerializeField] private UnityEvent<float> milestoneReached = new UnityEvent<float>();
        [SerializeField] private UnityEvent sweepTriggered = new UnityEvent();

        private IUiMotionSettingsProvider settingsProvider;
        private float displayedValue;
        private float targetValue;
        private int mergeCount;

        public float DisplayedValue => displayedValue;
        public float TargetValue => targetValue;
        public int MergeCount => mergeCount;
        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Value, out Tween tween) && tween.IsPlaying();
        public UnityEvent<float> MilestoneReached => milestoneReached;
        public UnityEvent SweepTriggered => sweepTriggered;

        private void Awake()
        {
            ReadInitialValue();
        }

        private void OnEnable()
        {
            SubscribeSettings();
            ApplyValues(displayedValue, displayedValue);
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
            Lifecycle.Stop(UiMotionChannel.Value);
            Lifecycle.Stop(UiMotionChannel.Attention);
            if (sweep != null) sweep.alpha = 0f;
        }

        public void Configure(Image newMainFill, Image newGhostFill = null, CanvasGroup newSweep = null)
        {
            mainFill = newMainFill;
            ghostFill = newGhostFill;
            sweep = newSweep;
            ReadInitialValue();
            ApplyValues(displayedValue, displayedValue);
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider)) return;
            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
        }

        public void SetValue(float value, bool immediate = false)
        {
            float next = Mathf.Clamp01(value);
            float previousTarget = targetValue;
            targetValue = next;
            if (mainFill == null)
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                displayedValue = targetValue;
                NotifyMilestones(previousTarget, targetValue);
                return;
            }

            if (immediate || !isActiveAndEnabled)
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                displayedValue = targetValue;
                ApplyValues(targetValue, targetValue);
                NotifyMilestones(previousTarget, targetValue);
                return;
            }

            UiMotionSettingsSnapshot settings = ResolveSettings();
            if (settings.Intensity <= 0f || settings.ReducedMotion || Mathf.Approximately(displayedValue, targetValue))
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                displayedValue = targetValue;
                ApplyValues(targetValue, targetValue);
                NotifyMilestones(previousTarget, targetValue);
                return;
            }

            if (IsPlaying) mergeCount++;
            float from = displayedValue;
            bool increasing = targetValue > from;
            float delta = Mathf.Abs(targetValue - from);
            float duration = Mathf.Lerp(minimumDuration, maximumDuration, delta)
                * UiMotionDefaults.ScaleDuration(1f, settings.Intensity);
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);

            if (ghostFill != null)
            {
                if (increasing) ghostFill.fillAmount = targetValue;
                else mainFill.fillAmount = targetValue;
            }

            Tween tween = UiMotionTweenFactory.Float(
                this,
                () => increasing || ghostFill == null ? mainFill.fillAmount : ghostFill.fillAmount,
                current =>
                {
                    displayedValue = current;
                    if (increasing || ghostFill == null) mainFill.fillAmount = current;
                    else ghostFill.fillAmount = current;
                },
                targetValue,
                duration,
                policy);
            tween.OnComplete(() =>
            {
                displayedValue = targetValue;
                ApplyValues(targetValue, targetValue);
                NotifyMilestones(previousTarget, targetValue);
                if (targetValue > previousTarget) PlaySweep(settings);
            });
            Lifecycle.Play(UiMotionChannel.Value, tween);
        }

        private void ReadInitialValue()
        {
            displayedValue = mainFill != null ? Mathf.Clamp01(mainFill.fillAmount) : 0f;
            targetValue = displayedValue;
            if (sweep != null) sweep.alpha = 0f;
        }

        private void ApplyValues(float main, float ghost)
        {
            if (mainFill != null) mainFill.fillAmount = Mathf.Clamp01(main);
            if (ghostFill != null) ghostFill.fillAmount = Mathf.Clamp01(ghost);
        }

        private void NotifyMilestones(float previous, float current)
        {
            if (current <= previous || milestones == null) return;
            for (int i = 0; i < milestones.Length; i++)
            {
                float milestone = Mathf.Clamp01(milestones[i]);
                if (previous < milestone && current >= milestone)
                {
                    milestoneReached?.Invoke(milestone);
                }
            }
        }

        private void PlaySweep(UiMotionSettingsSnapshot settings)
        {
            sweepTriggered?.Invoke();
            if (sweep == null || settings.DisableFlashes) return;
            float duration = settings.ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f) return;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutCubic);
            sweep.alpha = 0f;
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Append(UiMotionTweenFactory.Float(
                this, () => sweep.alpha, alpha => sweep.alpha = alpha, 1f, duration * 0.5f, policy));
            sequence.Append(UiMotionTweenFactory.Float(
                this, () => sweep.alpha, alpha => sweep.alpha = alpha, 0f, duration * 0.5f, policy));
            Lifecycle.Play(UiMotionChannel.Attention, sequence);
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            return settingsProvider != null ? settingsProvider.Current : Settings;
        }

        private void SubscribeSettings()
        {
            if (settingsProvider != null)
            {
                settingsProvider.Changed -= OnSettingsChanged;
                settingsProvider.Changed += OnSettingsChanged;
            }
        }

        private void UnsubscribeSettings()
        {
            if (settingsProvider != null) settingsProvider.Changed -= OnSettingsChanged;
        }

        private void OnSettingsChanged()
        {
            if (!IsPlaying) return;
            float latest = targetValue;
            Lifecycle.Stop(UiMotionChannel.Value);
            SetValue(latest);
        }
    }
}
