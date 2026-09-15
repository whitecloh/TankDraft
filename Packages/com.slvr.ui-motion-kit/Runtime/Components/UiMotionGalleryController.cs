using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Sample-only controls and benchmark readout. It does not persist host settings.</summary>
    [DisallowMultipleComponent]
    public sealed class UiMotionGalleryController : MonoBehaviour
    {
        [SerializeField] private UiMotionTheme[] themes = System.Array.Empty<UiMotionTheme>();
        [SerializeField] private UiMotionQualityProfile[] qualities = System.Array.Empty<UiMotionQualityProfile>();
        [SerializeField] private Text benchmarkText;

        private readonly StringBuilder builder = new StringBuilder(256);
        private readonly UiMotionSettingsProvider provider = new UiMotionSettingsProvider();
        private int themeIndex;
        private int qualityIndex;
        private float intensity = 1f;
        private bool reducedMotion;
        private float nextRefresh;

        public IUiMotionSettingsProvider Provider => provider;

        public void Configure(UiMotionTheme[] galleryThemes, UiMotionQualityProfile[] galleryQualities, Text output)
        {
            themes = galleryThemes ?? System.Array.Empty<UiMotionTheme>();
            qualities = galleryQualities ?? System.Array.Empty<UiMotionQualityProfile>();
            benchmarkText = output;
            Apply();
        }

        private void Awake()
        {
            Apply();
        }

        private void Update()
        {
            if (benchmarkText == null || Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + 0.25f;
            UiMotionMetricsSnapshot metrics = UiMotionMetrics.Capture();
            UiPoolMetricsSnapshot pools = UiPoolMetrics.Capture();
            int loops = UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Primary)
                + UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Secondary)
                + UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Accent);
            int attention = UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Attention);
            builder.Clear();
            builder.Append("Tweeners: ").Append(metrics.ActiveTweeners)
                .Append("  Sequences: ").Append(metrics.ActiveSequences)
                .Append("  Playing: ").Append(metrics.PlayingTweens)
                .Append("\nTracked channels: ").Append(metrics.TrackedChannels)
                .Append("  Loops: ").Append(loops).Append("  Attention: ").Append(attention)
                .Append("\nPools: ").Append(pools.Pools).Append("  Active pooled: ").Append(pools.Active).Append("  Inactive: ").Append(pools.Inactive)
                .Append("  Intensity: ").Append(Mathf.RoundToInt(intensity * 100f)).Append('%')
                .Append("  Reduced: ").Append(reducedMotion ? "ON" : "OFF")
                .Append("\nGC status: measure with Profiler after warm-up; this panel does not claim PASS.");
            benchmarkText.text = builder.ToString();
        }

        public void NextTheme()
        {
            if (themes.Length == 0) return;
            themeIndex = (themeIndex + 1) % themes.Length;
            Apply();
        }

        public void NextQuality()
        {
            if (qualities.Length == 0) return;
            qualityIndex = (qualityIndex + 1) % qualities.Length;
            Apply();
        }

        public void SetIntensity(float value)
        {
            intensity = Mathf.Clamp01(value);
            Apply();
        }

        public void SetReducedMotion(bool value)
        {
            reducedMotion = value;
            Apply();
        }

        private void Apply()
        {
            UiMotionTheme theme = themes.Length > 0 ? themes[Mathf.Clamp(themeIndex, 0, themes.Length - 1)] : null;
            UiMotionQualityProfile quality = qualities.Length > 0 ? qualities[Mathf.Clamp(qualityIndex, 0, qualities.Length - 1)] : null;
            provider.SetRuntimeOverride(new UiMotionSettingsSnapshot(
                theme, quality, intensity, reducedMotion, reducedMotion, !reducedMotion, true, true));
            InjectProvider();
        }

        private void InjectProvider()
        {
            foreach (UiNumberTicker item in GetComponentsInChildren<UiNumberTicker>(true)) item.SetSettingsProvider(provider);
            foreach (UiProgressMotion item in GetComponentsInChildren<UiProgressMotion>(true)) item.SetSettingsProvider(provider);
            foreach (UiNotificationBadge item in GetComponentsInChildren<UiNotificationBadge>(true)) item.SetSettingsProvider(provider);
            foreach (UiCurrencyFlyEffect item in GetComponentsInChildren<UiCurrencyFlyEffect>(true)) item.SetSettingsProvider(provider);
            foreach (UiRewardSequence item in GetComponentsInChildren<UiRewardSequence>(true)) item.SetSettingsProvider(provider);
            foreach (UiIdleMotion item in GetComponentsInChildren<UiIdleMotion>(true)) item.SetSettingsProvider(provider);
        }
    }
}
