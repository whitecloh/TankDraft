using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionTimingTokenTests
    {
        [Test]
        public void Balanced_IsMonotonic()
        {
            UiMotionTimingTokens tokens = UiMotionTimingTokens.Balanced;
            Assert.That(tokens.Get(UiMotionTiming.Fast), Is.LessThan(tokens.Get(UiMotionTiming.Normal)));
            Assert.That(tokens.Get(UiMotionTiming.Normal), Is.LessThan(tokens.Get(UiMotionTiming.Slow)));
            Assert.That(tokens.Get(UiMotionTiming.Slow), Is.LessThan(tokens.Get(UiMotionTiming.Emphasis)));
        }
    }
}
