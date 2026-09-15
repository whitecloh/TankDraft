using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiGraphicColorFlashTests
    {
        [UnityTest]
        public IEnumerator Play_RestoresAuthoredColorAfterTransientFlash()
        {
            GameObject owner = new GameObject(
                "color-flash",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(UiGraphicColorFlash));
            try
            {
                Image image = owner.GetComponent<Image>();
                Color authored = new Color(0.8f, 0.9f, 1f, 0.75f);
                image.color = authored;
                UiGraphicColorFlash flash = owner.GetComponent<UiGraphicColorFlash>();
                flash.Configure(image, Color.red, 0.12f);

                flash.Play();
                yield return new WaitForSecondsRealtime(0.04f);

                Assert.That(image.color, Is.Not.EqualTo(authored));
                Assert.That(image.color.a, Is.EqualTo(authored.a).Within(0.001f));

                yield return new WaitForSecondsRealtime(0.18f);

                Assert.That(image.color, Is.EqualTo(authored));
                Assert.IsFalse(flash.IsPlaying);
            }
            finally
            {
                Object.Destroy(owner);
            }
        }

        [UnityTest]
        public IEnumerator ReducedMotion_UsesStaticErrorColorThenRestoresBaseline()
        {
            GameObject owner = new GameObject(
                "reduced-color-flash",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(UiGraphicColorFlash));
            try
            {
                Image image = owner.GetComponent<Image>();
                image.color = Color.white;
                UiGraphicColorFlash flash = owner.GetComponent<UiGraphicColorFlash>();
                flash.Configure(image, Color.red, 0.12f);
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
                flash.SetSettingsProvider(provider);

                flash.Play();
                Assert.That(image.color.r, Is.EqualTo(1f).Within(0.001f));
                Assert.That(image.color.g, Is.EqualTo(0f).Within(0.001f));

                yield return new WaitForSecondsRealtime(0.2f);

                Assert.That(image.color, Is.EqualTo(Color.white));
            }
            finally
            {
                Object.Destroy(owner);
            }
        }
    }
}
