using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionIntensityTests
    {
        [Test]
        public void ScaleDuration_ZeroIntensity_IsImmediate()
        {
            Assert.That(UiMotionDefaults.ScaleDuration(0.25f, 0f), Is.Zero);
        }

        [Test]
        public void ScaleDuration_FullIntensity_PreservesDuration()
        {
            Assert.That(UiMotionDefaults.ScaleDuration(0.25f, 1f), Is.EqualTo(0.25f).Within(0.0001f));
        }
    }
}
