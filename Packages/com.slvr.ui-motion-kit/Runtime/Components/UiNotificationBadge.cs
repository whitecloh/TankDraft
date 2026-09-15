using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Count/dot badge with merged one-shot attention and optional budgeted urgent pulse.</summary>
    [DisallowMultipleComponent]
    public sealed class UiNotificationBadge : UiMotionElement
    {
        [Header("Presentation")]
        [SerializeField] private GameObject badgeVisual;
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private Text countText;
        [SerializeField] private GameObject countLabelVisual;
        [SerializeField] private bool dotOnly;

        [Header("Motion")]
        [SerializeField, Min(1f)] private float entranceScale = 1.12f;
        [SerializeField, Min(1f)] private float urgentScale = 1.25f;
        [SerializeField] private bool urgentPulse;
        [SerializeField, Min(1f)] private float urgentCycleMultiplier = 8f;

        [Header("Events")]
        [SerializeField] private UnityEvent<int> countChanged = new UnityEvent<int>();
        [SerializeField] private UnityEvent<string> displayTextChanged = new UnityEvent<string>();

        private IUiMotionSettingsProvider settingsProvider;
        private UiMotionBudget.Lease urgentBudgetLease;
        private bool hasUrgentBudgetLease;
        private bool initialized;
        private bool logicalVisible = true;
        private Vector3 baseScale;
        private float baseAlpha;
        private int count;
        private int mergeCount;

        public int Count => count;
        public bool DotOnly => dotOnly;
        public bool UrgentPulse => urgentPulse;
        public float UrgentScale => urgentScale;
        public bool IsVisible => logicalVisible && count > 0;
        public int MergeCount => mergeCount;
        public UnityEvent<int> CountChanged => countChanged;
        public UnityEvent<string> DisplayTextChanged => displayTextChanged;
        public bool IsEntrancePlaying => Lifecycle.TryGet(UiMotionChannel.Attention, out Tween tween)
            && tween.IsPlaying();
        public bool IsUrgentPlaying => Lifecycle.TryGet(UiMotionChannel.Idle, out Tween tween)
            && tween.IsPlaying();

        /// <summary>
        /// Creates a lightweight dot badge for runtime-composed UI. The fallback visual uses
        /// UGUI's white texture and does not depend on legacy built-in sprites removed in Unity 6.
        /// Production screens should prefer an authored badge prefab.
        /// </summary>
        public static UiNotificationBadge CreateRuntimeDot(
            RectTransform parent,
            Vector2 anchoredPosition,
            float size = 32f,
            Color? color = null,
            string objectName = "Notification_Badge")
        {
            if (parent == null)
            {
                return null;
            }

            Transform existing = parent.Find(objectName);
            if (existing != null && existing.TryGetComponent(out UiNotificationBadge existingBadge))
            {
                return existingBadge;
            }

            GameObject root = new GameObject(
                objectName,
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            root.layer = parent.gameObject.layer;
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.one;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = Vector2.one * Mathf.Max(1f, size);

            Image image = root.GetComponent<Image>();
            image.raycastTarget = false;
            image.sprite = null;
            image.type = Image.Type.Simple;
            image.color = color ?? new Color(1f, 0.28f, 0.16f, 1f);

            CanvasGroup group = root.GetComponent<CanvasGroup>();
            group.interactable = false;
            group.blocksRaycasts = false;

            UiNotificationBadge badge = root.AddComponent<UiNotificationBadge>();
            badge.Configure(rect, root, null, group);
            badge.SetDotOnly(true);
            badge.SetUrgent(true);
            badge.SetCount(0, true);
            return badge;
        }

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            SubscribeSettings();
            ApplyPresentation();
            TryStartUrgentPulse();
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
            StopAllMotion(true);
        }

        public void Configure(
            RectTransform newVisualRoot,
            GameObject newBadgeVisual = null,
            Text newCountText = null,
            CanvasGroup newCanvasGroup = null,
            GameObject newCountLabelVisual = null)
        {
            StopAllMotion(true);
            visualRoot = newVisualRoot;
            badgeVisual = newBadgeVisual;
            countText = newCountText;
            canvasGroup = newCanvasGroup;
            countLabelVisual = newCountLabelVisual;
            initialized = false;
            Initialize();
            ApplyPresentation();
            TryStartUrgentPulse();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider)) return;
            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
            RefreshMotionPolicy();
        }

        public void SetCount(int value, bool immediate = false)
        {
            Initialize();
            int nextCount = Mathf.Max(0, value);
            int previousCount = count;
            if (nextCount == previousCount)
            {
                UpdateLabel();
                return;
            }

            count = nextCount;
            UpdateLabel();
            countChanged?.Invoke(count);

            if (count <= 0)
            {
                StopAllMotion(true);
                ApplyPresentation();
                return;
            }

            ApplyPresentation();
            bool shouldEnter = logicalVisible && (previousCount <= 0 || count > previousCount);
            if (shouldEnter && !immediate)
            {
                PlayEntrance(previousCount <= 0);
            }
            else
            {
                StopEntrance(true);
                TryStartUrgentPulse();
            }
        }

        public void SetDotOnly(bool value)
        {
            if (dotOnly == value) return;
            dotOnly = value;
            UpdateLabel();
        }

        public void SetUrgent(bool value)
        {
            if (urgentPulse == value) return;
            urgentPulse = value;
            if (value) TryStartUrgentPulse();
            else StopUrgentPulse(true);
        }

        /// <summary>Host visibility bridge for CanvasGroup-hidden owners that remain active.</summary>
        public void SetVisible(bool value)
        {
            if (logicalVisible == value) return;
            logicalVisible = value;
            if (!value)
            {
                StopAllMotion(true);
            }

            ApplyPresentation();
            if (value)
            {
                TryStartUrgentPulse();
            }
        }

        public void RefreshMotionPolicy()
        {
            StopAllMotion(true);
            ApplyPresentation();
            TryStartUrgentPulse();
        }

        private void Initialize()
        {
            if (initialized) return;
            if (visualRoot == null) visualRoot = transform as RectTransform;
            if (badgeVisual == null && visualRoot != null) badgeVisual = visualRoot.gameObject;
            if (canvasGroup == null && visualRoot != null) canvasGroup = visualRoot.GetComponent<CanvasGroup>();
            if (visualRoot != null) baseScale = visualRoot.localScale;
            baseAlpha = canvasGroup != null ? canvasGroup.alpha : 1f;
            initialized = true;
            UpdateLabel();
        }

        private void ApplyPresentation()
        {
            bool show = IsVisible;
            if (badgeVisual != null && badgeVisual != gameObject)
            {
                badgeVisual.SetActive(show);
            }
            else if (canvasGroup != null)
            {
                canvasGroup.alpha = show ? baseAlpha : 0f;
                canvasGroup.blocksRaycasts = false;
                canvasGroup.interactable = false;
            }
        }

        private void UpdateLabel()
        {
            if (countLabelVisual != null)
            {
                countLabelVisual.SetActive(!dotOnly);
            }
            else if (countText != null)
            {
                countText.enabled = !dotOnly;
            }

            if (!dotOnly)
            {
                string displayText = count.ToString();
                if (countText != null) countText.text = displayText;
                displayTextChanged?.Invoke(displayText);
            }
        }

        private void PlayEntrance(bool firstAppearance)
        {
            StopUrgentPulse(true);
            UiMotionSettingsSnapshot settings = ResolveSettings();
            UiMotionEffectKind resolvedEffect = settings.ResolveEffect(UiMotionEffectKind.Scale);
            if (settings.Intensity <= 0f || resolvedEffect == UiMotionEffectKind.Immediate)
            {
                ApplyMotionBaseline();
                TryStartUrgentPulse();
                return;
            }

            if (IsEntrancePlaying) mergeCount++;
            float duration = settings.ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                ApplyMotionBaseline();
                TryStartUrgentPulse();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutBack);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            if (resolvedEffect == UiMotionEffectKind.Fade && canvasGroup != null)
            {
                float targetAlpha = baseAlpha;
                if (firstAppearance) canvasGroup.alpha = 0f;
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => canvasGroup.alpha,
                    value => canvasGroup.alpha = value,
                    targetAlpha,
                    duration,
                    policy));
            }
            else if (visualRoot != null)
            {
                if (firstAppearance) visualRoot.localScale = baseScale * 0.75f;
                sequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    baseScale * entranceScale,
                    duration * 0.55f,
                    policy));
                sequence.Append(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    baseScale,
                    duration * 0.45f,
                    policy));
            }
            else
            {
                TryStartUrgentPulse();
                return;
            }

            sequence.OnComplete(() =>
            {
                ApplyMotionBaseline();
                TryStartUrgentPulse();
            });
            Lifecycle.Play(UiMotionChannel.Attention, sequence);
        }

        private void TryStartUrgentPulse()
        {
            if (!CanRunUrgent(out UiMotionSettingsSnapshot settings)) return;
            if (IsEntrancePlaying || IsUrgentPlaying) return;
            if (!UiIdleMotionRuntime.TryAcquire(settings, UiMotionBudgetClass.Accent, out urgentBudgetLease)) return;
            hasUrgentBudgetLease = true;

            float duration = settings.ResolveDuration(UiMotionTiming.Slow) * urgentCycleMultiplier;
            if (duration <= 0f || visualRoot == null)
            {
                ReleaseUrgentBudget();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(
                settings,
                UiMotionEase.InOutCubic,
                UiMotionDisableBehaviour.Kill);
            Tween tween = UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    value => visualRoot.localScale = value,
                    baseScale * urgentScale,
                    duration,
                    policy)
                .SetLoops(-1, LoopType.Yoyo);
            Lifecycle.Play(UiMotionChannel.Idle, tween);
        }

        private bool CanRunUrgent(out UiMotionSettingsSnapshot settings)
        {
            settings = ResolveSettings();
            if (!urgentPulse || !isActiveAndEnabled || !IsVisible || !settings.IdleEffects)
            {
                return false;
            }

            if (settings.Quality != null && settings.Quality.Tier == UiMotionQualityTier.Low)
            {
                return false;
            }

            return settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
        }

        private void StopAllMotion(bool restoreBaseline)
        {
            StopEntrance(restoreBaseline);
            StopUrgentPulse(restoreBaseline);
        }

        private void StopEntrance(bool restoreBaseline)
        {
            Lifecycle.Stop(UiMotionChannel.Attention);
            if (restoreBaseline) ApplyMotionBaseline();
        }

        private void StopUrgentPulse(bool restoreBaseline)
        {
            Lifecycle.Stop(UiMotionChannel.Idle);
            ReleaseUrgentBudget();
            if (restoreBaseline) ApplyMotionBaseline();
        }

        private void ReleaseUrgentBudget()
        {
            if (!hasUrgentBudgetLease) return;
            urgentBudgetLease.Dispose();
            hasUrgentBudgetLease = false;
        }

        private void ApplyMotionBaseline()
        {
            if (!initialized) return;
            if (visualRoot != null) visualRoot.localScale = baseScale;
            if (canvasGroup != null && IsVisible) canvasGroup.alpha = baseAlpha;
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
            RefreshMotionPolicy();
        }
    }
}
