using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiTransientMessageTests
    {
        [UnityTest]
        public IEnumerator Show_ReplaysAndRestoresHiddenBaseline()
        {
            var owner = new GameObject(
                "TransientMessage",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiMotionLifecycle),
                typeof(UiTransientMessage));
            try
            {
                RectTransform root = owner.GetComponent<RectTransform>();
                CanvasGroup group = owner.GetComponent<CanvasGroup>();
                UiTransientMessage message = owner.GetComponent<UiTransientMessage>();
                message.Configure(group, root, 0.05f, 0.9f);

                message.Show();

                Assert.That(message.IsVisible, Is.True);
                yield return new WaitForSecondsRealtime(0.6f);
                Assert.That(message.IsVisible, Is.False);
                Assert.That(group.alpha, Is.EqualTo(0f).Within(0.001f));
                Assert.That(Vector3.Distance(root.localScale, Vector3.one * 0.9f), Is.LessThan(0.001f));

                message.Show();
                message.Show();
                Assert.That(message.IsVisible, Is.True, "Repeated validation errors must restart one owned sequence.");
            }
            finally
            {
                Object.Destroy(owner);
            }

            yield return null;
        }
    }
}
