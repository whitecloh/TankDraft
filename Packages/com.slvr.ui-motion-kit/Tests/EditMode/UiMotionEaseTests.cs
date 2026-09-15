using DG.Tweening;
using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionEaseTests
    {
        [TestCase(UiMotionEase.Linear, Ease.Linear)]
        [TestCase(UiMotionEase.OutCubic, Ease.OutCubic)]
        [TestCase(UiMotionEase.OutBack, Ease.OutBack)]
        [TestCase(UiMotionEase.InOutSine, Ease.InOutSine)]
        public void ToDotweenEase_MapsKnownValues(UiMotionEase source, Ease expected)
        {
            Assert.That(source.ToDotweenEase(), Is.EqualTo(expected));
        }
    }
}
