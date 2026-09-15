using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiLoadingScreenMotionTests
    {
        [TestCase(0f, 0.48f)]
        [TestCase(0.25f, 0.36f)]
        [TestCase(0.5f, 0.24f)]
        [TestCase(0.9f, 0.16f)]
        [TestCase(1f, 0f)]
        public void ResolveCompletionFillDuration_ScalesWithRemainingProgress(
            float current,
            float expected)
        {
            Assert.That(
                UiLoadingScreenMotion.ResolveCompletionFillDuration(current, 0.48f, 0.16f),
                Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void PrepareForShow_RestoresAuthoredVisualStateAndInitialProgress()
        {
            var root = new GameObject(
                "Loading",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiMotionLifecycle),
                typeof(UiLoadingScreenMotion));
            var progressRoot = new GameObject("Progress", typeof(RectTransform), typeof(UiSegmentedProgress));
            try
            {
                progressRoot.transform.SetParent(root.transform, false);
                var canvasGroup = root.GetComponent<CanvasGroup>();
                var progress = progressRoot.GetComponent<UiSegmentedProgress>();
                var motion = root.GetComponent<UiLoadingScreenMotion>();
                motion.Configure(progress, canvasGroup, useIndeterminateLoop: false);

                canvasGroup.alpha = 0f;
                motion.PrepareForShow(0.35f);

                Assert.That(canvasGroup.alpha, Is.EqualTo(1f));
                Assert.That(canvasGroup.blocksRaycasts, Is.True);
                Assert.That(progress.Value, Is.EqualTo(0.35f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void IndeterminateMode_RegistersLoopAndIgnoresNumericProgress()
        {
            var root = new GameObject(
                "Loading",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiMotionLifecycle),
                typeof(UiLoadingScreenMotion));
            var progressRoot = new GameObject(
                "Progress",
                typeof(RectTransform),
                typeof(UiSegmentedProgress));
            try
            {
                progressRoot.transform.SetParent(root.transform, false);
                var progress = progressRoot.GetComponent<UiSegmentedProgress>();
                var motion = root.GetComponent<UiLoadingScreenMotion>();
                var lifecycle = root.GetComponent<UiMotionLifecycle>();
                motion.Configure(progress, root.GetComponent<CanvasGroup>());

                Assert.That(motion.IsIndeterminate, Is.True);
                Assert.That(lifecycle.TryGet(UiMotionChannel.Value, out _), Is.True);

                motion.SetProgress(0.9f, immediate: true);

                Assert.That(progress.Value, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [TestCase(0f, 0f)]
        [TestCase(0.25f, 0.25f)]
        [TestCase(0.5f, 1f)]
        [TestCase(0.75f, 0.25f)]
        [TestCase(1f, 0f)]
        public void StarFieldPulse_HasSmoothPeriodicShape(float phase, float expected)
        {
            Assert.That(UiStarFieldMotion.EvaluatePulse(phase, 2f), Is.EqualTo(expected).Within(0.0001f));
        }

        [Test]
        public void StarFieldOrbit_IsPeriodicAndUsesExpectedAxesWithoutPulse()
        {
            const float radius = 12f;
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(0f, radius, 0f), radius, 0f);
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(0.25f, radius, 0f), 0f, radius);
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(0.5f, radius, 0f), -radius, 0f);
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(0.75f, radius, 0f), 0f, -radius);
            Vector2 offset = UiStarFieldMotion.EvaluateOrbitOffset(0.37f, radius, 0.25f);
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(1.37f, radius, 0.25f), offset.x, offset.y);
        }

        [Test]
        public void StarFieldOrbit_PulseRadiusStaysWithinConfiguredBounds()
        {
            const float radius = 40f;
            for (int i = 0; i < 64; i++)
            {
                float magnitude = UiStarFieldMotion.EvaluateOrbitOffset(i / 64f, radius, 0.25f).magnitude;
                Assert.That(magnitude, Is.InRange(radius * 0.75f, radius * 1.25f));
            }
        }

        [Test]
        public void StarFieldOrbit_ZeroRadiusHasNoOffset()
        {
            AssertVector(UiStarFieldMotion.EvaluateOrbitOffset(0.37f, 0f, 1f), 0f, 0f);
        }

        private static void AssertVector(Vector2 actual, float expectedX, float expectedY)
        {
            Assert.That(actual.x, Is.EqualTo(expectedX).Within(0.0001f));
            Assert.That(actual.y, Is.EqualTo(expectedY).Within(0.0001f));
        }
    }
}
