using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiNumberFormatterTests
    {
        [TestCase(12.6d, "13")]
        [TestCase(-12.5d, "-13")]
        public void Integer_UsesInvariantAwayFromZeroRounding(double value, string expected)
        {
            var formatter = new UiDefaultNumberFormatter(UiNumberFormatMode.Integer);
            Assert.That(formatter.Format(value), Is.EqualTo(expected));
        }

        [Test]
        public void Decimal_UsesConfiguredInvariantPrecision()
        {
            var formatter = new UiDefaultNumberFormatter(UiNumberFormatMode.Decimal, 2);
            Assert.That(formatter.Format(12.345d), Is.EqualTo("12.35"));
        }

        [TestCase(999d, "999")]
        [TestCase(1_250d, "1.3K")]
        [TestCase(2_500_000d, "2.5M")]
        [TestCase(3_100_000_000d, "3.1B")]
        public void Compact_UsesStableSuffixes(double value, string expected)
        {
            var formatter = new UiDefaultNumberFormatter(UiNumberFormatMode.Compact, 1);
            Assert.That(formatter.Format(value), Is.EqualTo(expected));
        }
    }
}
