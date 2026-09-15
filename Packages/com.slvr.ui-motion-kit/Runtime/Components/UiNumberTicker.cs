using System;
using System.Globalization;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    public enum UiNumberFormatMode
    {
        Integer = 0,
        Decimal = 1,
        Compact = 2,
    }

    public interface IUiNumberFormatter
    {
        string Format(double value);
    }

    /// <summary>Allocation-aware default formatter; strings are produced only when the visible value is updated.</summary>
    public sealed class UiDefaultNumberFormatter : IUiNumberFormatter
    {
        private readonly UiNumberFormatMode mode;
        private readonly int decimalPlaces;

        public UiDefaultNumberFormatter(UiNumberFormatMode mode, int decimalPlaces = 1)
        {
            this.mode = mode;
            this.decimalPlaces = Mathf.Clamp(decimalPlaces, 0, 6);
        }

        public string Format(double value)
        {
            switch (mode)
            {
                case UiNumberFormatMode.Decimal:
                    return value.ToString("F" + decimalPlaces, CultureInfo.InvariantCulture);
                case UiNumberFormatMode.Compact:
                    return FormatCompact(value, decimalPlaces);
                default:
                    return Math.Round(value, MidpointRounding.AwayFromZero)
                        .ToString(CultureInfo.InvariantCulture);
            }
        }

        private static string FormatCompact(double value, int places)
        {
            double absolute = Math.Abs(value);
            if (absolute >= 1_000_000_000d)
            {
                return (value / 1_000_000_000d).ToString("F" + places, CultureInfo.InvariantCulture) + "B";
            }

            if (absolute >= 1_000_000d)
            {
                return (value / 1_000_000d).ToString("F" + places, CultureInfo.InvariantCulture) + "M";
            }

            if (absolute >= 1_000d)
            {
                return (value / 1_000d).ToString("F" + places, CultureInfo.InvariantCulture) + "K";
            }

            return Math.Round(value, MidpointRounding.AwayFromZero)
                .ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>Rapid-update-safe numeric display with injectable formatting and accessible completion feedback.</summary>
    [DisallowMultipleComponent]
    public sealed class UiNumberTicker : UiMotionElement
    {
        [Header("Display")]
        [SerializeField] private Text targetText;
        [SerializeField] private TMP_Text targetTmpText;
        [SerializeField] private UiNumberFormatMode formatMode = UiNumberFormatMode.Integer;
        [SerializeField, Range(0, 6)] private int decimalPlaces = 1;

        [Header("Counting Motion")]
        [SerializeField] private bool overrideDuration;
        [SerializeField, Min(0f)] private float animationDuration = 2f;
        [SerializeField] private AnimationCurve countingCurve = CreateDefaultCountingCurve();

        [Header("Completion Feedback")]
        [SerializeField] private RectTransform punchRoot;
        [SerializeField] private Graphic flashTarget;
        [SerializeField] private bool completionPunch;
        [SerializeField] private bool completionColorFlash;
        [SerializeField, Min(1f)] private float punchScale = 1.06f;
        [SerializeField] private Color flashColor = Color.white;

        [Header("Events")]
        [SerializeField] private UnityEvent<string> displayTextChanged = new UnityEvent<string>();
        [SerializeField] private UnityEvent completed = new UnityEvent();

        private IUiMotionSettingsProvider settingsProvider;
        private IUiNumberFormatter formatter;
        private UiDefaultNumberFormatter defaultFormatter;
        private Vector3 baseScale;
        private Color baseColor;
        private bool initialized;
        private double displayedValue;
        private double targetValue;
        private int mergeCount;
        private string lastDisplayText;
        private float flashProgress;

        public double DisplayedValue => displayedValue;
        public double TargetValue => targetValue;
        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Value, out Tween tween) && tween.IsPlaying();
        public int MergeCount => mergeCount;
        public bool UsesCustomDuration => overrideDuration;
        public float AnimationDuration => animationDuration;
        public AnimationCurve CountingCurve => countingCurve;
        public UnityEvent<string> DisplayTextChanged => displayTextChanged;
        public UnityEvent Completed => completed;

        private void Awake()
        {
            Initialize();
        }

        private void OnValidate()
        {
            animationDuration = Mathf.Max(0f, animationDuration);
            EnsureCountingCurve();
        }

        private void OnEnable()
        {
            Initialize();
            SubscribeSettings();
            ApplyDisplay();
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
            Lifecycle.Stop(UiMotionChannel.Value);
            Lifecycle.Stop(UiMotionChannel.Attention);
            RestoreFeedbackBaseline();
        }

        public void Configure(Text text, RectTransform newPunchRoot = null, Graphic newFlashTarget = null)
        {
            targetText = text;
            targetTmpText = null;
            punchRoot = newPunchRoot;
            flashTarget = newFlashTarget;
            initialized = false;
            Initialize();
            ApplyDisplay();
        }

        public void Configure(TMP_Text text, RectTransform newPunchRoot = null, Graphic newFlashTarget = null)
        {
            targetText = null;
            targetTmpText = text;
            punchRoot = newPunchRoot;
            flashTarget = newFlashTarget;
            initialized = false;
            Initialize();
            ApplyDisplay();
        }

        public void SetFormatter(IUiNumberFormatter value)
        {
            formatter = value;
            ApplyDisplay();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider)) return;
            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
        }

        /// <summary>
        /// Uses an exact counting duration and a normalized 0..1 progress curve. Passing no curve
        /// selects the default ease-out cubic profile: fast at the start and slow near completion.
        /// </summary>
        public void ConfigureAnimation(float duration, AnimationCurve curve = null)
        {
            overrideDuration = true;
            animationDuration = Mathf.Max(0f, duration);
            countingCurve = curve ?? CreateDefaultCountingCurve();
        }

        /// <summary>Returns duration control to the theme timing token while preserving the curve.</summary>
        public void UseThemeDuration()
        {
            overrideDuration = false;
        }

        public void SetValue(double value, bool immediate = false)
        {
            SetValueInternal(value, immediate, 0f);
        }

        /// <summary>Keeps the current display stable, then starts the normal count animation.</summary>
        public void SetValueDelayed(double value, float initialDelay)
        {
            SetValueInternal(value, immediate: false, Mathf.Max(0f, initialDelay));
        }

        private void SetValueInternal(double value, bool immediate, float initialDelay)
        {
            Initialize();
            targetValue = value;
            if (immediate || !isActiveAndEnabled)
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                displayedValue = targetValue;
                ApplyDisplay();
                return;
            }

            UiMotionSettingsSnapshot settings = ResolveSettings();
            float duration = overrideDuration
                ? animationDuration
                : settings.ResolveDuration(UiMotionTiming.Normal);
            if (duration <= 0f || settings.Intensity <= 0f || settings.ReducedMotion)
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                displayedValue = targetValue;
                ApplyDisplay();
                PlayCompletionFeedback(settings);
                return;
            }

            if (IsPlaying) mergeCount++;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Tween valueTween = UiMotionTweenFactory.Double(
                this,
                () => displayedValue,
                current =>
                {
                    displayedValue = current;
                    ApplyDisplay();
                },
                targetValue,
                duration,
                policy);
            EnsureCountingCurve();
            valueTween.SetEase(countingCurve);
            Tween tween = valueTween;
            if (initialDelay > 0f)
            {
                Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
                sequence.AppendInterval(initialDelay);
                sequence.Append(valueTween);
                tween = sequence;
            }

            tween.OnComplete(() =>
            {
                displayedValue = targetValue;
                ApplyDisplay();
                PlayCompletionFeedback(settings);
                completed?.Invoke();
            });
            Lifecycle.Play(UiMotionChannel.Value, tween);
        }

        private void Initialize()
        {
            if (initialized) return;
            EnsureCountingCurve();
            if (punchRoot == null) punchRoot = transform as RectTransform;
            if (punchRoot != null) baseScale = punchRoot.localScale;
            if (flashTarget != null) baseColor = flashTarget.color;
            RebuildDefaultFormatter();
            initialized = true;
        }

        private void EnsureCountingCurve()
        {
            if (countingCurve == null || countingCurve.length < 2)
            {
                countingCurve = CreateDefaultCountingCurve();
            }
        }

        private static AnimationCurve CreateDefaultCountingCurve()
        {
            // Hermite tangents (3 -> 0) produce y = 1 - (1 - x)^3: a monotonic ease-out cubic.
            var curve = new AnimationCurve(
                new Keyframe(0f, 0f, 3f, 3f),
                new Keyframe(1f, 1f, 0f, 0f));
            curve.preWrapMode = WrapMode.ClampForever;
            curve.postWrapMode = WrapMode.ClampForever;
            return curve;
        }

        private void RebuildDefaultFormatter()
        {
            defaultFormatter = new UiDefaultNumberFormatter(formatMode, decimalPlaces);
        }

        private void ApplyDisplay()
        {
            if (defaultFormatter == null) RebuildDefaultFormatter();
            string text = (formatter ?? defaultFormatter).Format(displayedValue);
            if (string.Equals(text, lastDisplayText, StringComparison.Ordinal)) return;
            lastDisplayText = text;
            if (targetText != null) targetText.text = text;
            if (targetTmpText != null) targetTmpText.text = text;
            displayTextChanged?.Invoke(text);
        }

        private void PlayCompletionFeedback(UiMotionSettingsSnapshot settings)
        {
            Lifecycle.Stop(UiMotionChannel.Attention);
            RestoreFeedbackBaseline();
            if (!completionPunch && !completionColorFlash) return;

            float duration = settings.ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f) return;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutBack);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            bool hasStep = false;
            UiMotionEffectKind scaleEffect = settings.ResolveEffect(UiMotionEffectKind.Scale);
            if (completionPunch && punchRoot != null && scaleEffect == UiMotionEffectKind.Scale)
            {
                sequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => punchRoot.localScale,
                    current => punchRoot.localScale = current,
                    baseScale * punchScale,
                    duration * 0.5f,
                    policy));
                sequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => punchRoot.localScale,
                    current => punchRoot.localScale = current,
                    baseScale,
                    duration * 0.5f,
                    policy));
                hasStep = true;
            }

            if (completionColorFlash && flashTarget != null && !settings.DisableFlashes)
            {
                flashProgress = 0f;
                sequence.Join(UiMotionTweenFactory.Float(
                    this,
                    () => flashProgress,
                    progress =>
                    {
                        flashProgress = progress;
                        Color color = Color.Lerp(baseColor, flashColor, progress);
                        color.a = baseColor.a;
                        flashTarget.color = color;
                    },
                    1f,
                    duration * 0.5f,
                    policy).SetLoops(2, LoopType.Yoyo));
                hasStep = true;
            }

            if (!hasStep)
            {
                sequence.Kill(false);
                return;
            }

            sequence.OnComplete(RestoreFeedbackBaseline);
            Lifecycle.Play(UiMotionChannel.Attention, sequence);
        }

        private void RestoreFeedbackBaseline()
        {
            if (!initialized) return;
            flashProgress = 0f;
            if (punchRoot != null) punchRoot.localScale = baseScale;
            if (flashTarget != null) flashTarget.color = baseColor;
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
            double latestTarget = targetValue;
            Lifecycle.Stop(UiMotionChannel.Value);
            SetValue(latestTarget);
        }
    }
}
