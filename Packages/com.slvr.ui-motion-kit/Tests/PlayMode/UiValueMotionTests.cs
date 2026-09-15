using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiValueMotionTests
    {
        [UnityTest]
        public IEnumerator NumberTicker_RapidUpdatesMergeFromDisplayedValueAndReachLatestTarget()
        {
            var go = new GameObject("Ticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Text text = go.GetComponent<Text>();
            UiNumberTicker ticker = go.AddComponent<UiNumberTicker>();
            ticker.Configure(text, go.GetComponent<RectTransform>(), text);

            ticker.SetValue(100d);
            yield return null;
            double valueDuringFirstTween = ticker.DisplayedValue;
            ticker.SetValue(250d);
            Assert.That(ticker.MergeCount, Is.EqualTo(1));
            Assert.That(ticker.DisplayedValue, Is.EqualTo(valueDuringFirstTween).Within(0.001d));

            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(ticker.DisplayedValue, Is.EqualTo(250d).Within(0.001d));
            Assert.That(text.text, Is.EqualTo("250"));

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NumberTicker_UsesInjectedFormatterAndReducedMotionImmediatePath()
        {
            var go = new GameObject("Ticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Text text = go.GetComponent<Text>();
            UiNumberTicker ticker = go.AddComponent<UiNumberTicker>();
            ticker.Configure(text);
            ticker.SetFormatter(new PrefixFormatter());
            var provider = new UiMotionSettingsProvider();
            provider.SetRuntimeOverride(new UiMotionSettingsSnapshot(
                null, null, 1f, true, true, true, true, false));
            ticker.SetSettingsProvider(provider);

            ticker.SetValue(42d);
            Assert.That(ticker.IsPlaying, Is.False);
            Assert.That(ticker.DisplayedValue, Is.EqualTo(42d));
            Assert.That(text.text, Is.EqualTo("value:42"));

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NumberTicker_DelayedValueWaitsBeforeCounting()
        {
            var go = new GameObject("DelayedTicker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Text text = go.GetComponent<Text>();
            UiNumberTicker ticker = go.AddComponent<UiNumberTicker>();
            ticker.Configure(text);
            ticker.SetValue(0d, immediate: true);

            ticker.SetValueDelayed(25d, 0.15f);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.That(ticker.DisplayedValue, Is.Zero.Within(0.001d));
            Assert.That(text.text, Is.EqualTo("0"));

            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(ticker.DisplayedValue, Is.EqualTo(25d).Within(0.001d));
            Assert.That(text.text, Is.EqualTo("25"));

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator NumberTicker_CustomDurationUsesDeceleratingCurveAndExactEndpoint()
        {
            var go = new GameObject("CurvedTicker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Text text = go.GetComponent<Text>();
            UiNumberTicker ticker = go.AddComponent<UiNumberTicker>();
            ticker.Configure(text);
            ticker.ConfigureAnimation(0.5f);
            ticker.SetValue(0d, immediate: true);

            Assert.That(ticker.UsesCustomDuration, Is.True);
            Assert.That(ticker.AnimationDuration, Is.EqualTo(0.5f));
            Assert.That(ticker.CountingCurve.Evaluate(0.25f), Is.GreaterThan(0.5f));

            ticker.SetValue(100d);
            yield return new WaitForSecondsRealtime(0.13f);
            double earlyValue = ticker.DisplayedValue;
            Assert.That(earlyValue, Is.GreaterThan(40d), "The first quarter should advance quickly.");
            Assert.That(ticker.IsPlaying, Is.True);

            yield return new WaitForSecondsRealtime(0.2f);
            double lateValue = ticker.DisplayedValue;
            Assert.That(earlyValue, Is.GreaterThan(100d - lateValue),
                "The remaining distance near the end should be smaller than the early advance.");
            Assert.That(ticker.IsPlaying, Is.True);

            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(ticker.DisplayedValue, Is.EqualTo(100d).Within(0.001d));
            Assert.That(text.text, Is.EqualTo("100"));
            Assert.That(ticker.IsPlaying, Is.False);

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Progress_DecreaseUsesGhostTrailWithoutChangingRectSize()
        {
            ProgressFixture fixture = CreateProgress();
            Vector2 originalSize = fixture.Main.rectTransform.sizeDelta;
            fixture.Motion.SetValue(0.8f, true);
            fixture.Motion.SetValue(0.2f);

            Assert.That(fixture.Main.fillAmount, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(fixture.Ghost.fillAmount, Is.GreaterThan(0.2f));
            yield return new WaitForSecondsRealtime(0.6f);
            Assert.That(fixture.Main.fillAmount, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(fixture.Ghost.fillAmount, Is.EqualTo(0.2f).Within(0.001f));
            Assert.That(fixture.Main.rectTransform.sizeDelta, Is.EqualTo(originalSize));

            Object.Destroy(fixture.Owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Progress_RapidUpdatesConvergeAndFireCrossedMilestonesOnce()
        {
            ProgressFixture fixture = CreateProgress();
            int milestones = 0;
            fixture.Motion.MilestoneReached.AddListener(_ => milestones++);
            fixture.Motion.SetValue(0.2f, true);
            fixture.Motion.SetValue(0.6f);
            yield return null;
            fixture.Motion.SetValue(1f);
            Assert.That(fixture.Motion.MergeCount, Is.EqualTo(1));

            yield return new WaitForSecondsRealtime(0.7f);
            Assert.That(fixture.Motion.DisplayedValue, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fixture.Main.fillAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fixture.Ghost.fillAmount, Is.EqualTo(1f).Within(0.001f));
            Assert.That(milestones, Is.EqualTo(2), "0.75 and 1.0 milestones should be crossed from the latest target.");

            Object.Destroy(fixture.Owner);
            yield return null;
        }

        private static ProgressFixture CreateProgress()
        {
            var owner = new GameObject("Progress", typeof(RectTransform));
            Image ghost = CreateFill(owner.transform, "Ghost");
            Image main = CreateFill(owner.transform, "Main");
            var sweepObject = new GameObject("Sweep", typeof(RectTransform), typeof(CanvasGroup));
            sweepObject.transform.SetParent(owner.transform, false);
            UiProgressMotion motion = owner.AddComponent<UiProgressMotion>();
            motion.Configure(main, ghost, sweepObject.GetComponent<CanvasGroup>());
            motion.SetValue(0f, true);
            return new ProgressFixture(owner, main, ghost, motion);
        }

        private static Image CreateFill(Transform parent, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            Image image = go.GetComponent<Image>();
            image.type = Image.Type.Filled;
            image.fillMethod = Image.FillMethod.Horizontal;
            image.fillAmount = 0f;
            image.rectTransform.sizeDelta = new Vector2(200f, 24f);
            return image;
        }

        private sealed class PrefixFormatter : IUiNumberFormatter
        {
            public string Format(double value) => "value:" + ((int)value).ToString();
        }

        private readonly struct ProgressFixture
        {
            public ProgressFixture(GameObject owner, Image main, Image ghost, UiProgressMotion motion)
            {
                Owner = owner;
                Main = main;
                Ghost = ghost;
                Motion = motion;
            }

            public GameObject Owner { get; }
            public Image Main { get; }
            public Image Ghost { get; }
            public UiProgressMotion Motion { get; }
        }
    }
}
