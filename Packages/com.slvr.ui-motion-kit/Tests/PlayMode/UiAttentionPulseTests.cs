using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiAttentionPulseTests
    {
        [UnityTest]
        public IEnumerator EveryEffect_PlaysAndRestoresMotionBaseline()
        {
            var go = new GameObject("attention", typeof(RectTransform));
            var ringObject = new GameObject("ring", typeof(RectTransform), typeof(CanvasGroup));
            ringObject.transform.SetParent(go.transform, false);
            CanvasGroup ring = ringObject.GetComponent<CanvasGroup>();
            ring.alpha = 0f;
            UiAttentionPulse pulse = go.AddComponent<UiAttentionPulse>();
            RectTransform rect = (RectTransform)go.transform;
            pulse.Configure(rect, go.transform, ring);
            // Layout can place the item after Awake/Configure, before the first effect.
            rect.anchoredPosition = new Vector2(140f, -85f);
            Vector2 basePosition = rect.anchoredPosition;
            Vector3 baseScale = rect.localScale;

            foreach (UiAttentionEffect effect in System.Enum.GetValues(typeof(UiAttentionEffect)))
            {
                UiAttentionRequestResult result = pulse.Play(effect, "effect-" + effect);
                Assert.That(result, Is.EqualTo(UiAttentionRequestResult.Started), effect.ToString());
                yield return new WaitForSecondsRealtime(0.55f);
                Assert.That(pulse.IsPlaying, Is.False, effect.ToString());
                Assert.That(rect.anchoredPosition, Is.EqualTo(basePosition));
                Assert.That(rect.localScale, Is.EqualTo(baseScale));
                Assert.That(ring.alpha, Is.EqualTo(0f));
                pulse.ClearErrorFallback();
            }

            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ScaleEffects_DoNotRestoreLayoutPositionOnCompletionCancellationOrDisable()
        {
            var go = new GameObject("layout-attention", typeof(RectTransform));
            try
            {
                UiAttentionPulse pulse = go.AddComponent<UiAttentionPulse>();
                RectTransform rect = (RectTransform)go.transform;
                rect.anchoredPosition = new Vector2(52f, -55f);
                pulse.CancelAll();
                Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(52f, -55f)));

                foreach (UiAttentionEffect effect in new[]
                         { UiAttentionEffect.Pulse, UiAttentionEffect.SpringPulse, UiAttentionEffect.BubbleReveal })
                {
                    pulse.Play(effect);
                    Vector2 movedPosition = rect.anchoredPosition + new Vector2(10f, -100f);
                    rect.anchoredPosition = movedPosition;
                    yield return new WaitForSecondsRealtime(0.6f);
                    Assert.That(pulse.IsPlaying, Is.False);
                    Assert.That(rect.anchoredPosition, Is.EqualTo(movedPosition), effect.ToString());
                    Assert.That(rect.localScale, Is.EqualTo(Vector3.one));

                    pulse.Play(effect);
                    pulse.CancelAll();
                    go.SetActive(false);
                    Assert.That(rect.anchoredPosition, Is.EqualTo(movedPosition), effect.ToString());
                    Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
                    go.SetActive(true);
                }
            }
            finally
            {
                Object.Destroy(go);
            }
        }

        [UnityTest]
        public IEnumerator Scope_QueuesDifferentKeysAndMergesDuplicates()
        {
            var scope = new GameObject("scope", typeof(RectTransform));
            var firstObject = new GameObject("first", typeof(RectTransform));
            var secondObject = new GameObject("second", typeof(RectTransform));
            firstObject.transform.SetParent(scope.transform, false);
            secondObject.transform.SetParent(scope.transform, false);
            UiAttentionPulse first = firstObject.AddComponent<UiAttentionPulse>();
            UiAttentionPulse second = secondObject.AddComponent<UiAttentionPulse>();
            yield return null;

            Assert.That(first.Play(UiAttentionEffect.Pulse, "active"), Is.EqualTo(UiAttentionRequestResult.Started));
            Assert.That(second.Play(UiAttentionEffect.Bounce, "queued"), Is.EqualTo(UiAttentionRequestResult.Queued));
            Assert.That(second.Play(UiAttentionEffect.ErrorShake, "queued"), Is.EqualTo(UiAttentionRequestResult.Merged));
            Assert.That(first.Play(UiAttentionEffect.Nudge, "active"), Is.EqualTo(UiAttentionRequestResult.Merged));
            Assert.That(first.IsPlaying, Is.True);
            Assert.That(second.IsPlaying, Is.False);
            Assert.That(first.PendingCount, Is.EqualTo(1));
            Assert.That(first.MergeCount, Is.EqualTo(1));
            Assert.That(second.MergeCount, Is.EqualTo(1));

            yield return new WaitForSecondsRealtime(0.35f);
            Assert.That(first.IsPlaying, Is.False);
            Assert.That(second.IsPlaying, Is.False);
            Assert.That(first.PendingCount, Is.Zero);
            Object.Destroy(scope);
        }

        [UnityTest]
        public IEnumerator SpringPulse_UsesLongSettleAndRestoresBaseline()
        {
            var go = new GameObject("spring-pulse", typeof(RectTransform));
            UiAttentionPulse pulse = go.AddComponent<UiAttentionPulse>();
            RectTransform rect = (RectTransform)go.transform;
            pulse.Configure(rect, go.transform);
            Vector3 baseScale = rect.localScale;

            Assert.That(pulse.Play(UiAttentionEffect.SpringPulse, "spring"), Is.EqualTo(UiAttentionRequestResult.Started));
            yield return new WaitForSecondsRealtime(0.18f);
            Assert.That(pulse.IsPlaying, Is.True);
            yield return new WaitForSecondsRealtime(0.36f);
            Assert.That(pulse.IsPlaying, Is.False);
            Assert.That(rect.localScale, Is.EqualTo(baseScale));
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator BubbleReveal_StartsSmallAndRestoresAuthoredScale()
        {
            var go = new GameObject("bubble-reveal", typeof(RectTransform));
            UiAttentionPulse pulse = go.AddComponent<UiAttentionPulse>();
            RectTransform rect = (RectTransform)go.transform;
            pulse.Configure(rect, go.transform);
            Vector3 baseScale = rect.localScale;

            Assert.That(pulse.Play(UiAttentionEffect.BubbleReveal, "bubble"), Is.EqualTo(UiAttentionRequestResult.Started));
            Assert.That(rect.localScale.x, Is.LessThan(baseScale.x));
            yield return new WaitForSecondsRealtime(0.48f);
            Assert.That(pulse.IsPlaying, Is.False);
            Assert.That(rect.localScale, Is.EqualTo(baseScale));
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator DisablingActiveOwner_StartsNextQueuedOwner()
        {
            var scope = new GameObject("scope", typeof(RectTransform));
            var firstObject = new GameObject("first", typeof(RectTransform));
            var secondObject = new GameObject("second", typeof(RectTransform));
            firstObject.transform.SetParent(scope.transform, false);
            secondObject.transform.SetParent(scope.transform, false);
            UiAttentionPulse first = firstObject.AddComponent<UiAttentionPulse>();
            UiAttentionPulse second = secondObject.AddComponent<UiAttentionPulse>();
            yield return null;

            first.Play(UiAttentionEffect.Pulse, "first");
            second.Play(UiAttentionEffect.Bounce, "second");
            first.enabled = false;
            Assert.That(second.IsPlaying, Is.True);
            Assert.That(second.PendingCount, Is.Zero);

            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(second.IsPlaying, Is.False);
            Object.Destroy(scope);
        }

        [UnityTest]
        public IEnumerator ReducedMotionError_UsesReadableFallbackWithoutShake()
        {
            var go = new GameObject(
                "attention",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Outline));
            var ringObject = new GameObject("ring", typeof(RectTransform), typeof(CanvasGroup));
            var message = new GameObject("message", typeof(RectTransform));
            ringObject.transform.SetParent(go.transform, false);
            message.transform.SetParent(go.transform, false);
            CanvasGroup ring = ringObject.GetComponent<CanvasGroup>();
            ring.alpha = 0f;
            message.SetActive(false);
            Image image = go.GetComponent<Image>();
            image.color = Color.white;
            Outline outline = go.GetComponent<Outline>();
            outline.enabled = false;
            UiAttentionPulse pulse = go.AddComponent<UiAttentionPulse>();
            pulse.Configure((RectTransform)go.transform, go.transform, ring);
            pulse.ConfigureErrorFallback(image, outline, message);
            var provider = new UiMotionSettingsProvider();
            provider.SetRuntimeOverride(new UiMotionSettingsSnapshot(
                null,
                null,
                1f,
                true,
                false,
                true,
                true,
                false));
            pulse.SetSettingsProvider(provider);
            Vector2 basePosition = ((RectTransform)go.transform).anchoredPosition;

            Assert.That(pulse.Play(UiAttentionEffect.ErrorShake, "error"), Is.EqualTo(UiAttentionRequestResult.Started));
            Assert.That(image.color, Is.Not.EqualTo(Color.white));
            Assert.That(outline.enabled, Is.True);
            Assert.That(message.activeSelf, Is.True);
            yield return new WaitForSecondsRealtime(0.12f);
            Assert.That(((RectTransform)go.transform).anchoredPosition, Is.EqualTo(basePosition));

            pulse.ClearErrorFallback();
            Assert.That(image.color, Is.EqualTo(Color.white));
            Assert.That(outline.enabled, Is.False);
            Assert.That(message.activeSelf, Is.False);
            Object.Destroy(go);
        }
    }
}
