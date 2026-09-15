using System.Collections;
using DG.Tweening;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionLifecycleTests
    {
        [UnityTest]
        public IEnumerator DestroyOwner_KillsLinkedTween()
        {
            var owner = new GameObject("motion-owner");
            UiMotionLifecycle lifecycle = owner.AddComponent<UiMotionLifecycle>();
            float value = 0f;
            Tween tween = UiMotionTweenFactory.Float(
                lifecycle,
                () => value,
                next => value = next,
                1f,
                10f,
                UiMotionTweenPolicy.Interaction());
            lifecycle.Play(UiMotionChannel.Interaction, tween);

            Object.Destroy(owner);
            yield return null;

            Assert.That(tween.IsActive(), Is.False);
        }

        [UnityTest]
        public IEnumerator RepeatedChannel_ReplacesPreviousTween()
        {
            var owner = new GameObject("motion-owner");
            UiMotionLifecycle lifecycle = owner.AddComponent<UiMotionLifecycle>();
            float value = 0f;
            Tween first = UiMotionTweenFactory.Float(
                lifecycle, () => value, next => value = next, 1f, 10f, UiMotionTweenPolicy.Interaction());
            Tween second = UiMotionTweenFactory.Float(
                lifecycle, () => value, next => value = next, 2f, 10f, UiMotionTweenPolicy.Interaction());

            lifecycle.Play(UiMotionChannel.Value, first);
            lifecycle.Play(UiMotionChannel.Value, second);
            yield return null;

            Assert.That(first.IsActive(), Is.False);
            Assert.That(second.IsActive(), Is.True);
            Assert.That(lifecycle.ActiveChannelCount, Is.EqualTo(1));
            Object.Destroy(owner);
        }

        [UnityTest]
        public IEnumerator UnscaledTween_AdvancesWhenTimeScaleIsZero()
        {
            float previousTimeScale = Time.timeScale;
            var owner = new GameObject("motion-owner");
            UiMotionLifecycle lifecycle = owner.AddComponent<UiMotionLifecycle>();
            float value = 0f;
            Time.timeScale = 0f;
            try
            {
                Tween tween = UiMotionTweenFactory.Float(
                    lifecycle, () => value, next => value = next, 1f, 0.05f, UiMotionTweenPolicy.Interaction(true));
                lifecycle.Play(UiMotionChannel.Value, tween);
                yield return new WaitForSecondsRealtime(0.12f);
                Assert.That(value, Is.EqualTo(1f).Within(0.02f));
            }
            finally
            {
                Time.timeScale = previousTimeScale;
                Object.Destroy(owner);
            }
        }

        [UnityTest]
        public IEnumerator IdleTween_PausesAndResumesWithOwner()
        {
            var owner = new GameObject("motion-owner");
            UiMotionLifecycle lifecycle = owner.AddComponent<UiMotionLifecycle>();
            float value = 0f;
            Tween tween = UiMotionTweenFactory.Float(
                lifecycle, () => value, next => value = next, 1f, 10f, UiMotionTweenPolicy.Idle());
            lifecycle.Play(UiMotionChannel.Idle, tween, UiMotionDisableBehaviour.PauseAndResume);

            owner.SetActive(false);
            yield return null;
            Assert.That(tween.IsActive(), Is.True);
            Assert.That(tween.IsPlaying(), Is.False);

            owner.SetActive(true);
            yield return null;
            Assert.That(tween.IsPlaying(), Is.True);
            Object.Destroy(owner);
        }
    }
}
