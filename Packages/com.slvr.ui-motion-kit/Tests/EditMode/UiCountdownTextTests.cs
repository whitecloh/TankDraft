using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiCountdownTextTests
    {
        [TestCase(0, "0:00")]
        [TestCase(5, "0:05")]
        [TestCase(65, "1:05")]
        [TestCase(3661, "1:01:01")]
        [TestCase(90061, "25:01:01")]
        public void FormatClock_UsesCompactMinuteOrHourFormat(int seconds, string expected)
        {
            Assert.That(UiCountdownText.FormatClock(seconds), Is.EqualTo(expected));
        }

        [Test]
        public void FormatClock_ClampsNegativeValues()
        {
            Assert.That(UiCountdownText.FormatClock(-1), Is.EqualTo("0:00"));
        }
    }
}
