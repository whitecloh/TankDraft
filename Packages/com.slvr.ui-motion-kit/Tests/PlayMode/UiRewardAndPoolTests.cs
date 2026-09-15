using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiRewardAndPoolTests
    {
        [UnityTest]
        public IEnumerator ComponentPool_ReturnsAllAndToleratesDestroyedActiveInstance()
        {
            var root = new GameObject("PoolRoot", typeof(RectTransform));
            var pool = new UiComponentPool<UiCurrencyFlyIcon>(
                () => new GameObject("Icon", typeof(RectTransform), typeof(UiCurrencyFlyIcon))
                    .GetComponent<UiCurrencyFlyIcon>(),
                root.transform,
                2,
                4);

            UiCurrencyFlyIcon first = pool.Rent(root.transform);
            UiCurrencyFlyIcon destroyed = pool.Rent(root.transform);
            Assert.That(pool.ActiveCount, Is.EqualTo(2));
            Object.DestroyImmediate(destroyed.gameObject);
            Assert.That(pool.Return(destroyed), Is.True);
            Assert.That(pool.Return(first), Is.True);
            Assert.That(pool.ActiveCount, Is.Zero);
            Assert.That(pool.InactiveCount, Is.EqualTo(1), "The externally destroyed active instance is removed, not retained.");

            pool.Dispose();
            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CurrencyFly_CompleteSkipAndDestroyedTargetReturnEveryInstance()
        {
            CurrencyFixture fixture = CreateCurrencyFixture();
            int completed = 0;
            fixture.Effect.AllArrived.AddListener(() => completed++);

            Assert.That(fixture.Effect.Play(5), Is.EqualTo(5));
            Assert.That(fixture.Effect.ActiveCount, Is.EqualTo(5));
            Object.Destroy(fixture.Target.gameObject);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(fixture.Effect.ArrivedCount, Is.EqualTo(5));
            Assert.That(fixture.Effect.ActiveCount, Is.Zero);
            Assert.That(fixture.Effect.InactiveCount, Is.GreaterThanOrEqualTo(5));

            RectTransform replacement = new GameObject("Replacement", typeof(RectTransform)).GetComponent<RectTransform>();
            replacement.SetParent(fixture.Root.transform, false);
            fixture.Effect.Configure(fixture.Prefab, fixture.Root.transform, fixture.Source, replacement);
            fixture.Effect.ConfigureMotion(UiCurrencyFlyPath.QuadraticBezier, 0.12f, 0f, 8f, 20f, 7);
            fixture.Effect.Play(4);
            Assert.That(fixture.Effect.ActiveCount, Is.EqualTo(4));
            fixture.Effect.Skip();
            Assert.That(fixture.Effect.ActiveCount, Is.Zero);
            Assert.That(fixture.Effect.InactiveCount, Is.GreaterThanOrEqualTo(4));

            Object.Destroy(fixture.Root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RewardSequence_RespectsReadableTimeAndSkipConvergesToFinalState()
        {
            RewardFixture fixture = CreateRewardFixture(reducedMotion: false);
            fixture.Sequence.ConfigureTiming(0.12f, 4f);
            fixture.Sequence.Play();
            Assert.That(fixture.Sequence.TrySkip(), Is.False);
            Assert.That(fixture.Group.alpha, Is.LessThan(1f));

            yield return new WaitForSecondsRealtime(0.14f);
            Assert.That(fixture.Sequence.TrySkip(), Is.True);
            Assert.That(fixture.Sequence.IsPlaying, Is.False);
            Assert.That(fixture.Group.alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(fixture.RootRect.localScale, Is.EqualTo(Vector3.one));
            Assert.That(fixture.Ticker.DisplayedValue, Is.EqualTo(125d).Within(0.001d));

            Object.Destroy(fixture.Owner);
            yield return null;
        }

        [UnityTest]
        public IEnumerator RewardSequence_ReducedMotionNeverAppliesRevealScale()
        {
            RewardFixture fixture = CreateRewardFixture(reducedMotion: true);
            Vector3 baseline = fixture.RootRect.localScale;
            fixture.Sequence.Play();
            Assert.That(fixture.RootRect.localScale, Is.EqualTo(baseline));

            yield return null;
            Assert.That(fixture.RootRect.localScale, Is.EqualTo(baseline));
            fixture.Sequence.ForceSkip();
            Assert.That(fixture.RootRect.localScale, Is.EqualTo(baseline));
            Assert.That(fixture.Group.alpha, Is.EqualTo(1f).Within(0.001f));

            Object.Destroy(fixture.Owner);
            yield return null;
        }

        private static CurrencyFixture CreateCurrencyFixture()
        {
            var root = new GameObject("Currency", typeof(RectTransform));
            RectTransform source = new GameObject("Source", typeof(RectTransform)).GetComponent<RectTransform>();
            RectTransform target = new GameObject("Target", typeof(RectTransform)).GetComponent<RectTransform>();
            source.SetParent(root.transform, false);
            target.SetParent(root.transform, false);
            source.position = new Vector3(10f, 20f, 0f);
            target.position = new Vector3(210f, 160f, 0f);
            var prefabObject = new GameObject("IconPrefab", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(UiCurrencyFlyIcon));
            prefabObject.transform.SetParent(root.transform, false);
            UiCurrencyFlyIcon prefab = prefabObject.GetComponent<UiCurrencyFlyIcon>();
            prefabObject.SetActive(false);
            UiCurrencyFlyEffect effect = root.AddComponent<UiCurrencyFlyEffect>();
            effect.Configure(prefab, root.transform, source, target);
            effect.ConfigureMotion(UiCurrencyFlyPath.CubicBezier, 0.12f, 0.01f, 16f, 30f, 11);
            return new CurrencyFixture(root, source, target, prefab, effect);
        }

        private static RewardFixture CreateRewardFixture(bool reducedMotion)
        {
            var owner = new GameObject("Reward", typeof(RectTransform));
            var content = new GameObject("RewardContent", typeof(RectTransform), typeof(CanvasGroup));
            content.transform.SetParent(owner.transform, false);
            RectTransform rect = content.GetComponent<RectTransform>();
            rect.localScale = Vector3.one;
            CanvasGroup group = content.GetComponent<CanvasGroup>();

            var tickerObject = new GameObject("Ticker", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            tickerObject.transform.SetParent(owner.transform, false);
            UiNumberTicker ticker = tickerObject.AddComponent<UiNumberTicker>();
            ticker.Configure(tickerObject.GetComponent<Text>());

            var provider = new UiMotionSettingsProvider();
            provider.SetRuntimeOverride(new UiMotionSettingsSnapshot(
                null, null, 1f, reducedMotion, true, true, true, false));
            ticker.SetSettingsProvider(provider);

            UiRewardSequence sequence = owner.AddComponent<UiRewardSequence>();
            sequence.SetSettingsProvider(provider);
            sequence.Configure(new[]
            {
                new UiRewardSequenceStep(UiRewardStepKind.RewardReveal, group, rect, 1f, 1f, 1.1f),
                new UiRewardSequenceStep(UiRewardStepKind.Ticker, null, null, 0.1f),
            }, ticker);
            sequence.ConfigureValues(125d, 0);
            return new RewardFixture(owner, rect, group, ticker, sequence);
        }

        private readonly struct CurrencyFixture
        {
            public CurrencyFixture(GameObject root, RectTransform source, RectTransform target, UiCurrencyFlyIcon prefab, UiCurrencyFlyEffect effect)
            {
                Root = root;
                Source = source;
                Target = target;
                Prefab = prefab;
                Effect = effect;
            }

            public GameObject Root { get; }
            public RectTransform Source { get; }
            public RectTransform Target { get; }
            public UiCurrencyFlyIcon Prefab { get; }
            public UiCurrencyFlyEffect Effect { get; }
        }

        private readonly struct RewardFixture
        {
            public RewardFixture(GameObject owner, RectTransform rootRect, CanvasGroup group, UiNumberTicker ticker, UiRewardSequence sequence)
            {
                Owner = owner;
                RootRect = rootRect;
                Group = group;
                Ticker = ticker;
                Sequence = sequence;
            }

            public GameObject Owner { get; }
            public RectTransform RootRect { get; }
            public CanvasGroup Group { get; }
            public UiNumberTicker Ticker { get; }
            public UiRewardSequence Sequence { get; }
        }
    }
}
