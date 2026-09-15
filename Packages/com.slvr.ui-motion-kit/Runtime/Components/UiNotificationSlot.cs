using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Authored placement point that resolves and instantiates a semantic notification visual.
    /// Screens talk to the slot; the visual config owns the concrete prefab choice.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiNotificationSlot : MonoBehaviour
    {
        [SerializeField] private UiNotificationVisualConfig visualConfig;
        [SerializeField] private UiNotificationKind kind = UiNotificationKind.Attention;
        [SerializeField] private bool dotOnly = true;
        [SerializeField] private bool urgent = true;

        private UiNotificationBadge instance;
        private bool missingVisualReported;

        public UiNotificationKind Kind => kind;
        public UiNotificationVisualConfig VisualConfig => visualConfig;
        public UiNotificationBadge Instance => instance;

        private void Awake()
        {
            EnsureInstance();
        }

        public void Configure(UiNotificationVisualConfig config, UiNotificationKind notificationKind)
        {
            visualConfig = config;
            kind = notificationKind;
            EnsureInstance();
        }

        public void SetKind(UiNotificationKind value)
        {
            if (kind == value) return;
            kind = value;
            missingVisualReported = false;
            if (instance == null) return;

            GameObject previousVisual = instance.gameObject;
            instance = null;
            previousVisual.SetActive(false);
            if (Application.isPlaying)
            {
                Destroy(previousVisual);
            }
            else
            {
                DestroyImmediate(previousVisual);
            }
        }

        public UiNotificationBadge EnsureInstance()
        {
            if (instance != null) return instance;

            instance = visualConfig != null ? visualConfig.Spawn(kind, transform) : null;
            if (instance == null)
            {
                if (!missingVisualReported)
                {
                    Debug.LogWarning(
                        $"No notification prefab is configured for {kind} on {name}.",
                        this);
                    missingVisualReported = true;
                }

                return null;
            }

            missingVisualReported = false;
            instance.name = $"{kind}_Notification";
            if (instance.transform is RectTransform rect)
            {
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                rect.localScale = Vector3.one;
            }

            instance.SetDotOnly(dotOnly);
            instance.SetUrgent(urgent);
            instance.SetCount(0, true);
            return instance;
        }

        public void SetCount(int value, bool immediate = false)
        {
            EnsureInstance()?.SetCount(value, immediate);
        }

        public void SetDotOnly(bool value)
        {
            dotOnly = value;
            EnsureInstance()?.SetDotOnly(value);
        }

        public void SetUrgent(bool value)
        {
            urgent = value;
            EnsureInstance()?.SetUrgent(value);
        }

        public void SetVisible(bool value)
        {
            EnsureInstance()?.SetVisible(value);
        }
    }
}
