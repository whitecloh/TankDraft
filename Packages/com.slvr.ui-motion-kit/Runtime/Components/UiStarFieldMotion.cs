using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Animates an authored field of star CanvasGroups with one budget lease and one tween.
    /// The stars remain separate prefab objects, while their phases are evenly distributed.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiStarFieldMotion : UiMotionElement
    {
        [SerializeField] private CanvasGroup[] stars;
        [SerializeField] private UiMotionBudgetClass budgetClass = UiMotionBudgetClass.Accent;
        [SerializeField, Min(0.1f)] private float cycleDuration = 1.55f;
        [SerializeField, Range(0f, 1f)] private float minimumAlphaFactor = 0.12f;
        [SerializeField, Range(0f, 1f)] private float peakAlpha = 1f;
        [SerializeField, Range(0.25f, 6f)] private float pulseSharpness = 2.2f;
        [SerializeField, Min(0f)] private float orbitRadius;
        [SerializeField, Range(0f, 1f)] private float orbitRadiusPulseFactor = 0.25f;
        [SerializeField] private int deterministicSeed = 731;

        private IUiMotionSettingsProvider settingsProvider;
        private UiMotionBudget.Lease budgetLease;
        private float[] baseAlphas;
        private RectTransform[] starRects;
        private Vector2[] basePositions;
        private bool hasBudgetLease;
        private bool initialized;

        public int StarCount => stars?.Length ?? 0;
        public float CycleDuration => cycleDuration;
        public bool IsPlaying => Lifecycle.TryGet(UiMotionChannel.Idle, out Tween tween) && tween.IsPlaying();

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            SubscribeSettings();
            TryStart();
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
            StopAndRelease(true);
        }

        public void Configure(
            CanvasGroup[] authoredStars,
            float secondsPerCycle = 1.55f,
            float dimAlphaFactor = 0.12f,
            float maximumAlpha = 1f,
            float sharpness = 2.2f)
        {
            StopAndRelease(true);
            stars = authoredStars;
            cycleDuration = Mathf.Max(0.1f, secondsPerCycle);
            minimumAlphaFactor = Mathf.Clamp01(dimAlphaFactor);
            peakAlpha = Mathf.Clamp01(maximumAlpha);
            pulseSharpness = Mathf.Clamp(sharpness, 0.25f, 6f);
            initialized = false;
            Initialize();
            TryStart();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider))
            {
                return;
            }

            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
            StopAndRelease(true);
            TryStart();
        }

        public static float EvaluatePulse(float phase01, float sharpness)
        {
            float wrapped = Mathf.Repeat(phase01, 1f);
            float wave = 0.5f - 0.5f * Mathf.Cos(wrapped * Mathf.PI * 2f);
            return Mathf.Pow(wave, Mathf.Clamp(sharpness, 0.25f, 6f));
        }

        public static Vector2 EvaluateOrbitOffset(float phase01, float radius, float pulseFactor)
        {
            float angle = Mathf.Repeat(phase01, 1f) * Mathf.PI * 2f;
            float orbit = Mathf.Max(0f, radius) * (1f + Mathf.Clamp01(pulseFactor) * Mathf.Sin(angle * 2f));
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * orbit;
        }

        private void TryStart()
        {
            if (!isActiveAndEnabled || hasBudgetLease || stars == null || stars.Length == 0)
            {
                return;
            }

            UiMotionSettingsSnapshot settings = ResolveSettings();
            if (!settings.IdleEffects || settings.ReducedMotion || settings.Intensity <= 0f ||
                settings.ResolveEffect(UiMotionEffectKind.Fade) != UiMotionEffectKind.Fade ||
                !UiIdleMotionRuntime.TryAcquire(settings, budgetClass, out budgetLease))
            {
                RestoreBaseline();
                return;
            }

            hasBudgetLease = true;
            float phase = 0f;
            float seedPhase = UiMotionDefaults.GetDeterministicPhase01(deterministicSeed);
            UiMotionTweenPolicy policy = ResolvePolicy(
                settings,
                UiMotionEase.Linear,
                UiMotionDisableBehaviour.Kill);
            Tween tween = UiMotionTweenFactory.Float(
                    this,
                    () => phase,
                    value =>
                    {
                        phase = value;
                        ApplyPhase(value + seedPhase, settings.Intensity);
                    },
                    1f,
                    cycleDuration,
                    policy)
                .SetEase(Ease.Linear)
                .SetLoops(-1, LoopType.Restart);
            Lifecycle.Play(UiMotionChannel.Idle, tween);
        }

        private void ApplyPhase(float phase, float intensity)
        {
            if (stars == null || baseAlphas == null || starRects == null || basePositions == null)
            {
                return;
            }

            int count = Mathf.Min(stars.Length, baseAlphas.Length);
            float spacing = count > 0 ? 1f / count : 0f;
            for (int i = 0; i < count; i++)
            {
                CanvasGroup star = stars[i];
                if (star == null)
                {
                    continue;
                }

                float pulse = EvaluatePulse(phase + spacing * i, pulseSharpness);
                float dimAlpha = baseAlphas[i] * minimumAlphaFactor;
                float brightAlpha = Mathf.Lerp(baseAlphas[i], peakAlpha, intensity);
                star.alpha = Mathf.Lerp(dimAlpha, brightAlpha, pulse);
                RectTransform rect = starRects[i];
                if (orbitRadius > 0f && rect != null)
                {
                    rect.anchoredPosition = basePositions[i] + EvaluateOrbitOffset(
                        phase + spacing * i,
                        orbitRadius,
                        orbitRadiusPulseFactor) * Mathf.Clamp01(intensity);
                }
            }
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            int count = stars?.Length ?? 0;
            baseAlphas = new float[count];
            starRects = new RectTransform[count];
            basePositions = new Vector2[count];
            for (int i = 0; i < count; i++)
            {
                baseAlphas[i] = stars[i] != null ? stars[i].alpha : 0f;
                starRects[i] = stars[i] != null ? stars[i].transform as RectTransform : null;
                basePositions[i] = starRects[i] != null ? starRects[i].anchoredPosition : Vector2.zero;
            }

            initialized = true;
        }

        private void StopAndRelease(bool restoreBaseline)
        {
            Lifecycle.Stop(UiMotionChannel.Idle);
            if (hasBudgetLease)
            {
                budgetLease.Dispose();
                budgetLease = default;
                hasBudgetLease = false;
            }

            if (restoreBaseline)
            {
                RestoreBaseline();
            }
        }

        private void RestoreBaseline()
        {
            if (!initialized || stars == null || baseAlphas == null || starRects == null || basePositions == null)
            {
                return;
            }

            int count = Mathf.Min(stars.Length, baseAlphas.Length);
            for (int i = 0; i < count; i++)
            {
                if (stars[i] != null)
                {
                    stars[i].alpha = baseAlphas[i];
                }

                if (orbitRadius > 0f && starRects[i] != null)
                {
                    starRects[i].anchoredPosition = basePositions[i];
                }
            }
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
            if (settingsProvider != null)
            {
                settingsProvider.Changed -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged()
        {
            StopAndRelease(true);
            TryStart();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            cycleDuration = Mathf.Max(0.1f, cycleDuration);
            minimumAlphaFactor = Mathf.Clamp01(minimumAlphaFactor);
            peakAlpha = Mathf.Clamp01(peakAlpha);
            pulseSharpness = Mathf.Clamp(pulseSharpness, 0.25f, 6f);
            orbitRadius = Mathf.Max(0f, orbitRadius);
            orbitRadiusPulseFactor = Mathf.Clamp01(orbitRadiusPulseFactor);
        }
#endif
    }
}
