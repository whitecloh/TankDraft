using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    /// <summary>Deterministic pooled currency flight with captured endpoints and quality-bounded icon count.</summary>
    [DisallowMultipleComponent]
    public sealed class UiCurrencyFlyEffect : UiMotionElement
    {
        [Header("Pool")]
        [SerializeField] private UiCurrencyFlyIcon iconPrefab;
        [SerializeField] private Transform container;
        [SerializeField, Min(0)] private int preload = 8;
        [SerializeField, Min(1)] private int maximumRetained = 24;

        [Header("Path")]
        [SerializeField] private RectTransform source;
        [SerializeField] private RectTransform target;
        [SerializeField] private UiCurrencyFlyPath path = UiCurrencyFlyPath.CubicBezier;
        [SerializeField, Min(0f)] private float spread = 48f;
        [SerializeField, Min(0f)] private float arcHeight = 120f;
        [SerializeField, Min(0f)] private float iconInterval = 0.035f;
        [SerializeField, Min(0.01f)] private float duration = 0.55f;
        [SerializeField] private int deterministicSeed;

        [Header("Arrival")]
        [SerializeField] private UiAttentionPulse targetAttention;
        [SerializeField] private UnityEvent<int> iconArrived = new UnityEvent<int>();
        [SerializeField] private UnityEvent allArrived = new UnityEvent();

        private readonly List<UiCurrencyFlyIcon> activeIcons = new List<UiCurrencyFlyIcon>(24);
        private IUiMotionSettingsProvider settingsProvider;
        private UiComponentPool<UiCurrencyFlyIcon> pool;
        private int arrivedCount;
        private int launchedCount;

        public int ActiveCount => pool?.ActiveCount ?? 0;
        public int InactiveCount => pool?.InactiveCount ?? 0;
        public int ArrivedCount => arrivedCount;
        public UnityEvent<int> IconArrived => iconArrived;
        public UnityEvent AllArrived => allArrived;

        private void OnDisable()
        {
            Skip();
        }

        private void OnDestroy()
        {
            Skip();
            pool?.Dispose();
            pool = null;
        }

        public void Configure(
            UiCurrencyFlyIcon newIconPrefab,
            Transform newContainer,
            RectTransform newSource,
            RectTransform newTarget)
        {
            Skip();
            pool?.Dispose();
            pool = null;
            iconPrefab = newIconPrefab;
            container = newContainer;
            source = newSource;
            target = newTarget;
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            settingsProvider = provider;
        }

        public void ConfigureMotion(
            UiCurrencyFlyPath newPath,
            float newDuration,
            float newIconInterval,
            float newSpread,
            float newArcHeight,
            int newDeterministicSeed = 0)
        {
            path = newPath;
            duration = Mathf.Max(0.01f, newDuration);
            iconInterval = Mathf.Max(0f, newIconInterval);
            spread = Mathf.Max(0f, newSpread);
            arcHeight = Mathf.Max(0f, newArcHeight);
            deterministicSeed = newDeterministicSeed;
        }

        public int Play(int requestedCount)
        {
            Skip();
            if (!isActiveAndEnabled || iconPrefab == null || source == null || target == null) return 0;

            UiMotionSettingsSnapshot settings = ResolveSettings();
            int count = ResolveIconCount(requestedCount, settings);
            if (count <= 0) return 0;
            EnsurePool();

            Vector3 start = source.position;
            Vector3 end = target.position;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutCubic);
            arrivedCount = 0;
            launchedCount = count;
            for (int i = 0; i < count; i++)
            {
                UiCurrencyFlyIcon icon = pool.Rent(container);
                activeIcons.Add(icon);
                float phase = UiMotionDefaults.GetDeterministicPhase01(deterministicSeed + i * 7919);
                float signedSpread = (phase * 2f - 1f) * spread;
                Vector3 controlA = Vector3.Lerp(start, end, 0.33f) + Vector3.up * arcHeight + Vector3.right * signedSpread;
                Vector3 controlB = Vector3.Lerp(start, end, 0.67f) + Vector3.up * (arcHeight * 0.55f) - Vector3.right * (signedSpread * 0.35f);
                icon.Begin(
                    this,
                    start,
                    controlA,
                    controlB,
                    end,
                    path,
                    duration,
                    iconInterval * i,
                    policy,
                    HandleArrival);
            }

            return count;
        }

        public void Skip()
        {
            for (int i = activeIcons.Count - 1; i >= 0; i--)
            {
                UiCurrencyFlyIcon icon = activeIcons[i];
                if (icon != null) pool?.Return(icon);
            }

            activeIcons.Clear();
            launchedCount = 0;
        }

        private int ResolveIconCount(int requested, UiMotionSettingsSnapshot settings)
        {
            int count = Mathf.Max(0, requested);
            if (settings.Quality != null)
            {
                count = Mathf.Min(count, settings.Quality.CurrencyIconLimit);
            }

            if (settings.Intensity <= 0f) count = Mathf.Min(count, 1);
            return count;
        }

        private void EnsurePool()
        {
            if (pool != null) return;
            pool = new UiComponentPool<UiCurrencyFlyIcon>(
                () => Instantiate(iconPrefab, container),
                container,
                preload,
                maximumRetained);
        }

        private void HandleArrival(UiCurrencyFlyIcon icon)
        {
            if (icon == null || !activeIcons.Remove(icon)) return;
            pool.Return(icon);
            arrivedCount++;
            iconArrived?.Invoke(arrivedCount);
            targetAttention?.Play(UiAttentionEffect.Pulse, "currency-arrival");
            if (arrivedCount >= launchedCount)
            {
                launchedCount = 0;
                allArrived?.Invoke();
            }
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            return settingsProvider != null ? settingsProvider.Current : Settings;
        }
    }
}
