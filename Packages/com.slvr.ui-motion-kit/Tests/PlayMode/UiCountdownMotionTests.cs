using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiCountdownMotionTests
    {
        [UnityTest]
        public IEnumerator Digit_PopsThenRestoresHiddenState()
        {
            GameObject owner = CreateOwner(out RectTransform root, out CanvasGroup group, out UiCountdownMotion motion);
            try
            {
                motion.PlayDigit();
                Assert.That(motion.IsVisible, Is.True);

                yield return new WaitForSecondsRealtime(1f);

                Assert.That(motion.IsVisible, Is.False);
                Assert.That(group.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(root.localScale.sqrMagnitude, Is.LessThan(0.001f));
            }
            finally
            {
                Object.Destroy(owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FinalPhrase_HoldsUntilHostHidesIt()
        {
            GameObject owner = CreateOwner(out RectTransform root, out CanvasGroup group, out UiCountdownMotion motion);
            try
            {
                motion.PlayFinal();
                yield return new WaitForSecondsRealtime(0.5f);

                Assert.That(motion.IsVisible, Is.True);
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(Vector3.Distance(root.localScale, Vector3.one), Is.LessThan(0.001f));

                motion.HideImmediate();
                Assert.That(motion.IsVisible, Is.False);
            }
            finally
            {
                Object.Destroy(owner);
            }

            yield return null;
        }

        private static GameObject CreateOwner(
            out RectTransform root,
            out CanvasGroup group,
            out UiCountdownMotion motion)
        {
            GameObject owner = new GameObject(
                "CountdownMotion",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiMotionLifecycle),
                typeof(UiCountdownMotion));
            root = owner.GetComponent<RectTransform>();
            group = owner.GetComponent<CanvasGroup>();
            motion = owner.GetComponent<UiCountdownMotion>();
            motion.Configure(group, root);
            return owner;
        }
    }
}
