using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionReducedMotionTests
    {
        [TestCase(UiMotionEffectKind.Translation, UiMotionEffectKind.Fade)]
        [TestCase(UiMotionEffectKind.Scale, UiMotionEffectKind.Fade)]
        [TestCase(UiMotionEffectKind.Shake, UiMotionEffectKind.Fade)]
        [TestCase(UiMotionEffectKind.Shimmer, UiMotionEffectKind.Immediate)]
        [TestCase(UiMotionEffectKind.ParticleBurst, UiMotionEffectKind.Immediate)]
        public void ResolveEffect_ReducedMotion_SubstitutesEffectKind(
            UiMotionEffectKind requested,
            UiMotionEffectKind expected)
        {
            var snapshot = new UiMotionSettingsSnapshot(null, null, 1f, true, false, true, true, false);
            Assert.That(snapshot.ResolveEffect(requested), Is.EqualTo(expected));
        }
    }
}
