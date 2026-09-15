using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiSegmentedProgressTests
    {
        [TestCase(0f, 0)]
        [TestCase(0.01f, 1)]
        [TestCase(1f / 6f, 1)]
        [TestCase(0.5f, 3)]
        [TestCase(0.99f, 6)]
        [TestCase(1f, 6)]
        public void ResolveFilledCount_RevealsCurrentSegment(float progress, int expected)
        {
            Assert.That(UiSegmentedProgress.ResolveFilledCount(progress, 6), Is.EqualTo(expected));
        }

        [Test]
        public void ResolveFilledCount_ClampsInputAndSupportsCompletedOnlyMode()
        {
            Assert.That(UiSegmentedProgress.ResolveFilledCount(-1f, 6), Is.Zero);
            Assert.That(UiSegmentedProgress.ResolveFilledCount(2f, 6), Is.EqualTo(6));
            Assert.That(UiSegmentedProgress.ResolveFilledCount(0.49f, 6, false), Is.EqualTo(2));
        }

        [Test]
        public void SetValue_ColorsOnlyResolvedSegmentsAndDisablesRaycasts()
        {
            var root = new GameObject("Progress");
            try
            {
                var graphics = new Graphic[3];
                for (int i = 0; i < graphics.Length; i++)
                {
                    graphics[i] = new GameObject("Segment_" + i, typeof(RectTransform), typeof(Image))
                        .GetComponent<Image>();
                    graphics[i].transform.SetParent(root.transform, false);
                }

                var progress = root.AddComponent<UiSegmentedProgress>();
                Color filled = Color.yellow;
                Color empty = Color.blue;
                progress.Configure(graphics, filled, empty);
                progress.SetValue(0.34f);

                Assert.That(graphics[0].color, Is.EqualTo(filled));
                Assert.That(graphics[1].color, Is.EqualTo(filled));
                Assert.That(graphics[2].color, Is.EqualTo(empty));
                for (int i = 0; i < graphics.Length; i++)
                {
                    Assert.That(graphics[i].raycastTarget, Is.False);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
