using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiNotificationVisualConfigTests
    {
        [Test]
        public void Config_ResolvesKnownKindAndRejectsUnknownKind()
        {
            GameObject prefabObject = CreateNotificationPrefab(out UiNotificationBadge prefabBadge);
            GameObject attentionObject = CreateNotificationPrefab(out UiNotificationBadge attentionBadge);
            UiNotificationVisualConfig config = ScriptableObject.CreateInstance<UiNotificationVisualConfig>();
            try
            {
                config.Configure(
                    new UiNotificationVisualConfig.Entry(UiNotificationKind.UpgradeAvailable, prefabBadge),
                    new UiNotificationVisualConfig.Entry(UiNotificationKind.Attention, attentionBadge));

                Assert.That(config.TryResolve(UiNotificationKind.UpgradeAvailable, out UiNotificationBadge resolved), Is.True);
                Assert.That(resolved, Is.SameAs(prefabBadge));
                Assert.That(config.TryResolve(UiNotificationKind.Attention, out resolved), Is.True);
                Assert.That(resolved, Is.SameAs(attentionBadge));
                Assert.That(config.TryResolve(UiNotificationKind.Purchase, out _), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(attentionObject);
                Object.DestroyImmediate(prefabObject);
            }
        }

        [Test]
        public void Slot_SpawnsConfiguredVisualOnceAndProxiesState()
        {
            GameObject prefabObject = CreateNotificationPrefab(out UiNotificationBadge prefabBadge);
            GameObject attentionObject = CreateNotificationPrefab(out UiNotificationBadge attentionBadge);
            UiNotificationVisualConfig config = ScriptableObject.CreateInstance<UiNotificationVisualConfig>();
            config.Configure(
                new UiNotificationVisualConfig.Entry(UiNotificationKind.UpgradeAvailable, prefabBadge),
                new UiNotificationVisualConfig.Entry(UiNotificationKind.Attention, attentionBadge));
            var owner = new GameObject("NotificationSlot", typeof(RectTransform));
            owner.SetActive(false);
            UiNotificationSlot slot = owner.AddComponent<UiNotificationSlot>();

            try
            {
                slot.Configure(config, UiNotificationKind.UpgradeAvailable);
                UiNotificationBadge first = slot.Instance;
                Assert.That(first, Is.Not.Null);
                Assert.That(owner.transform.childCount, Is.EqualTo(1));

                slot.SetCount(1, true);
                Assert.That(first.Count, Is.EqualTo(1));
                Assert.That(slot.EnsureInstance(), Is.SameAs(first));
                Assert.That(owner.transform.childCount, Is.EqualTo(1));

                slot.SetKind(UiNotificationKind.Attention);
                slot.SetCount(1, true);
                Assert.That(slot.Instance, Is.Not.SameAs(first));
                Assert.That(slot.Instance.Count, Is.EqualTo(1));
                Assert.That(owner.transform.childCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(owner);
                Object.DestroyImmediate(config);
                Object.DestroyImmediate(attentionObject);
                Object.DestroyImmediate(prefabObject);
            }
        }

        private static GameObject CreateNotificationPrefab(out UiNotificationBadge badge)
        {
            var prefab = new GameObject(
                "NotificationPrefab",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            prefab.SetActive(false);
            RectTransform root = prefab.GetComponent<RectTransform>();
            CanvasGroup group = prefab.GetComponent<CanvasGroup>();
            badge = prefab.AddComponent<UiNotificationBadge>();
            badge.Configure(root, prefab, null, group);
            badge.SetDotOnly(true);
            badge.SetUrgent(false);
            badge.SetCount(0, true);
            group.alpha = 1f;
            return prefab;
        }
    }
}
