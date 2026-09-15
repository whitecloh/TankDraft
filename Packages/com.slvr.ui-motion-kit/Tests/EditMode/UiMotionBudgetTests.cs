using NUnit.Framework;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionBudgetTests
    {
        [Test]
        public void DefaultBudget_RejectsSecondPrimaryUntilLeaseReleased()
        {
            var budget = new UiMotionBudget();
            Assert.That(budget.TryAcquire(UiMotionBudgetClass.Primary, out UiMotionBudget.Lease first), Is.True);
            Assert.That(budget.TryAcquire(UiMotionBudgetClass.Primary, out _), Is.False);

            UiMotionBudget.Lease copiedLease = first;
            first.Dispose();
            copiedLease.Dispose();

            Assert.That(budget.TryAcquire(UiMotionBudgetClass.Primary, out UiMotionBudget.Lease second), Is.True);
            second.Dispose();
        }
    }
}
