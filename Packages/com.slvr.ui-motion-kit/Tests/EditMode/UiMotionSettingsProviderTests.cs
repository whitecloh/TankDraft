using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionSettingsProviderTests
    {
        [Test]
        public void RuntimeOverride_NotifiesAndCanBeCleared()
        {
            var provider = new UiMotionSettingsProvider();
            int notifications = 0;
            provider.Changed += () => notifications++;
            var overridden = new UiMotionSettingsSnapshot(null, null, 0.5f, true, true, false, false, true);

            provider.SetRuntimeOverride(overridden);
            Assert.That(provider.Current.Intensity, Is.EqualTo(0.5f));
            Assert.That(provider.Current.ReducedMotion, Is.True);

            provider.ClearRuntimeOverride();
            Assert.That(provider.Current.Intensity, Is.EqualTo(1f));
            Assert.That(notifications, Is.EqualTo(2));
        }
    }
}
