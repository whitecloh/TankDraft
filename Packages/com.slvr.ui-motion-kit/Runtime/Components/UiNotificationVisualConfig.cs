using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    [CreateAssetMenu(
        fileName = "UiNotificationVisualConfig",
        menuName = "SLVR/UI Motion/Notification Visual Config")]
    public sealed class UiNotificationVisualConfig : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [SerializeField] private UiNotificationKind kind;
            [SerializeField] private UiNotificationBadge prefab;

            public Entry(UiNotificationKind kind, UiNotificationBadge prefab)
            {
                this.kind = kind;
                this.prefab = prefab;
            }

            public UiNotificationKind Kind => kind;
            public UiNotificationBadge Prefab => prefab;
        }

        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public bool TryResolve(UiNotificationKind kind, out UiNotificationBadge prefab)
        {
            if (entries != null)
            {
                for (int i = 0; i < entries.Length; i++)
                {
                    if (entries[i].Kind != kind || entries[i].Prefab == null) continue;
                    prefab = entries[i].Prefab;
                    return true;
                }
            }

            prefab = null;
            return false;
        }

        public UiNotificationBadge Spawn(UiNotificationKind kind, Transform parent)
        {
            if (parent == null || !TryResolve(kind, out UiNotificationBadge prefab)) return null;
            return Instantiate(prefab, parent, false);
        }

        public void Configure(params Entry[] newEntries)
        {
            entries = newEntries ?? Array.Empty<Entry>();
        }
    }
}
