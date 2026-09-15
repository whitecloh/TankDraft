using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiNotificationBadgeTests
    {
        [UnityTest]
        public IEnumerator CountAndDotMode_UpdatePresentationWithoutLayoutMutation()
        {
            BadgeFixture fixture = CreateBadge();
            try
            {
                Assert.That(fixture.Badge.UrgentScale, Is.EqualTo(1.25f).Within(0.0001f));
                fixture.Badge.SetCount(4, true);
                Assert.That(fixture.Badge.Count, Is.EqualTo(4));
                Assert.That(fixture.Visual.activeSelf, Is.True);
                Assert.That(fixture.Text.text, Is.EqualTo("4"));

                fixture.Badge.SetDotOnly(true);
                Assert.That(fixture.Badge.DotOnly, Is.True);
                Assert.That(fixture.Text.enabled, Is.False);

                fixture.Badge.SetCount(0);
                Assert.That(fixture.Visual.activeSelf, Is.False);
                Assert.That(fixture.Root.sizeDelta, Is.EqualTo(new Vector2(24f, 24f)));
            }
            finally
            {
                Object.Destroy(fixture.Owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator PositiveAndIncreasingCounts_MergeIntoOneEntranceAndRestoreBaseline()
        {
            BadgeFixture fixture = CreateBadge();
            try
            {
                fixture.Badge.SetCount(1);
                Assert.That(fixture.Badge.IsEntrancePlaying, Is.True);
                fixture.Badge.SetCount(2);
                fixture.Badge.SetCount(5);

                Assert.That(fixture.Badge.Count, Is.EqualTo(5));
                Assert.That(fixture.Badge.MergeCount, Is.EqualTo(2));
                yield return new WaitForSecondsRealtime(0.35f);

                Assert.That(fixture.Badge.IsEntrancePlaying, Is.False);
                Assert.That(Vector3.Distance(fixture.Root.localScale, Vector3.one), Is.LessThan(0.001f));

                fixture.Badge.SetCount(3);
                Assert.That(fixture.Badge.IsEntrancePlaying, Is.False, "Decreases must not demand attention.");
            }
            finally
            {
                Object.Destroy(fixture.Owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator UrgentPulse_ReleasesBudgetWhileHiddenAndRestartsWhenVisible()
        {
            BadgeFixture fixture = CreateBadge();
            try
            {
                int initialBudget = UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Accent);
                fixture.Badge.SetUrgent(true);
                fixture.Badge.SetCount(1, true);
                Assert.That(fixture.Badge.IsUrgentPlaying, Is.True);
                Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Accent), Is.EqualTo(initialBudget + 1));

                fixture.Badge.SetVisible(false);
                Assert.That(fixture.Badge.IsUrgentPlaying, Is.False);
                Assert.That(fixture.Visual.activeSelf, Is.False);
                Assert.That(UiIdleMotionRuntime.GetActive(UiMotionBudgetClass.Accent), Is.EqualTo(initialBudget));

                fixture.Badge.SetCount(2);
                Assert.That(fixture.Badge.IsEntrancePlaying, Is.False);
                fixture.Badge.SetVisible(true);
                Assert.That(fixture.Visual.activeSelf, Is.True);
                Assert.That(fixture.Badge.IsUrgentPlaying, Is.True);
            }
            finally
            {
                Object.Destroy(fixture.Owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ReducedMotion_UsesFadeWithoutScalePunch()
        {
            BadgeFixture fixture = CreateBadge();
            var provider = new UiMotionSettingsProvider();
            provider.SetRuntimeOverride(new UiMotionSettingsSnapshot(
                null, null, 1f, true, false, true, true, false));
            fixture.Badge.SetSettingsProvider(provider);
            try
            {
                fixture.Badge.SetCount(1);
                Assert.That(Vector3.Distance(fixture.Root.localScale, Vector3.one), Is.LessThan(0.001f));
                Assert.That(fixture.Group.alpha, Is.LessThan(1f));
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(fixture.Group.alpha, Is.EqualTo(1f).Within(0.001f));
            }
            finally
            {
                Object.Destroy(fixture.Owner);
            }

            yield return null;
        }

        private static BadgeFixture CreateBadge()
        {
            var owner = new GameObject("BadgeOwner", typeof(RectTransform));
            var visual = new GameObject(
                "BadgeVisual",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(CanvasGroup));
            visual.transform.SetParent(owner.transform, false);
            RectTransform root = visual.GetComponent<RectTransform>();
            root.sizeDelta = new Vector2(24f, 24f);

            var labelObject = new GameObject("Count", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            labelObject.transform.SetParent(visual.transform, false);
            Text label = labelObject.GetComponent<Text>();
            CanvasGroup group = visual.GetComponent<CanvasGroup>();

            UiNotificationBadge badge = owner.AddComponent<UiNotificationBadge>();
            badge.Configure(root, visual, label, group);
            return new BadgeFixture(owner, visual, root, label, group, badge);
        }

        private readonly struct BadgeFixture
        {
            public BadgeFixture(
                GameObject owner,
                GameObject visual,
                RectTransform root,
                Text text,
                CanvasGroup group,
                UiNotificationBadge badge)
            {
                Owner = owner;
                Visual = visual;
                Root = root;
                Text = text;
                Group = group;
                Badge = badge;
            }

            public GameObject Owner { get; }
            public GameObject Visual { get; }
            public RectTransform Root { get; }
            public Text Text { get; }
            public CanvasGroup Group { get; }
            public UiNotificationBadge Badge { get; }
        }
    }
}
