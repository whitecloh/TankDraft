using System;

namespace SLVR.UIMotion
{
    /// <summary>Shared admission budget and nested modal suppression state for idle effects.</summary>
    public static class UiIdleMotionRuntime
    {
        private static UiMotionBudget budget = new UiMotionBudget();
        private static UiMotionBudgetProfile configuredProfile;
        private static int modalSuppressionDepth;

        public static event Action<bool> ModalSuppressionChanged;

        public static bool IsModalSuppressed => modalSuppressionDepth > 0;

        public static bool TryAcquire(
            UiMotionSettingsSnapshot settings,
            UiMotionBudgetClass budgetClass,
            out UiMotionBudget.Lease lease)
        {
            UiMotionBudgetProfile requestedProfile = settings.Theme != null
                ? settings.Theme.BudgetProfile
                : null;
            if (!ReferenceEquals(configuredProfile, requestedProfile) && budget.TotalActive == 0)
            {
                configuredProfile = requestedProfile;
                budget = new UiMotionBudget(configuredProfile);
            }

            using (UiMotionProfiler.BudgetAdmission.Auto())
            {
                return budget.TryAcquire(budgetClass, out lease);
            }
        }

        public static int GetActive(UiMotionBudgetClass budgetClass)
        {
            return budget.GetActive(budgetClass);
        }

        public static IDisposable BeginModalSuppression()
        {
            modalSuppressionDepth++;
            if (modalSuppressionDepth == 1) ModalSuppressionChanged?.Invoke(true);
            return new ModalSuppressionLease();
        }

        private static void EndModalSuppression()
        {
            if (modalSuppressionDepth <= 0) return;
            modalSuppressionDepth--;
            if (modalSuppressionDepth == 0) ModalSuppressionChanged?.Invoke(false);
        }

        private sealed class ModalSuppressionLease : IDisposable
        {
            private bool disposed;

            public void Dispose()
            {
                if (disposed) return;
                disposed = true;
                EndModalSuppression();
            }
        }
    }
}
