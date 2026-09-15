using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Priority bucket used by the runtime motion budget.</summary>
    public enum UiMotionBudgetClass
    {
        Primary = 0,
        Secondary = 1,
        Accent = 2,
        Attention = 3,
    }

    /// <summary>Maximum concurrent effects per semantic priority.</summary>
    [CreateAssetMenu(fileName = "UiMotionBudget", menuName = "SLVR/UI Motion/Budget Profile")]
    public sealed class UiMotionBudgetProfile : ScriptableObject
    {
        [SerializeField, Min(0)] private int primary = 1;
        [SerializeField, Min(0)] private int secondary = 3;
        [SerializeField, Min(0)] private int accent = 6;
        [SerializeField, Min(0)] private int attention = 1;

        public int GetLimit(UiMotionBudgetClass budgetClass)
        {
            switch (budgetClass)
            {
                case UiMotionBudgetClass.Primary: return primary;
                case UiMotionBudgetClass.Secondary: return secondary;
                case UiMotionBudgetClass.Accent: return accent;
                case UiMotionBudgetClass.Attention: return attention;
                default: return 0;
            }
        }
    }

    /// <summary>Allocation-free counter set for concurrent motion admission.</summary>
    public sealed class UiMotionBudget
    {
        private readonly int[] limits = new int[4];
        private readonly int[] active = new int[4];
        private readonly uint[] activeTokens = new uint[4];

        public UiMotionBudget(UiMotionBudgetProfile profile = null)
        {
            limits[0] = Math.Min(32, profile != null ? profile.GetLimit(UiMotionBudgetClass.Primary) : 1);
            limits[1] = Math.Min(32, profile != null ? profile.GetLimit(UiMotionBudgetClass.Secondary) : 3);
            limits[2] = Math.Min(32, profile != null ? profile.GetLimit(UiMotionBudgetClass.Accent) : 6);
            limits[3] = Math.Min(32, profile != null ? profile.GetLimit(UiMotionBudgetClass.Attention) : 1);
        }

        public bool TryAcquire(UiMotionBudgetClass budgetClass, out Lease lease)
        {
            int index = (int)budgetClass;
            if ((uint)index >= active.Length || active[index] >= limits[index])
            {
                lease = default;
                return false;
            }

            for (int slot = 0; slot < limits[index]; slot++)
            {
                uint token = 1u << slot;
                if ((activeTokens[index] & token) != 0u)
                {
                    continue;
                }

                activeTokens[index] |= token;
                active[index]++;
                lease = new Lease(this, budgetClass, token);
                return true;
            }

            lease = default;
            return false;
        }

        public int GetActive(UiMotionBudgetClass budgetClass) => active[(int)budgetClass];

        public int TotalActive => active[0] + active[1] + active[2] + active[3];

        private void Release(UiMotionBudgetClass budgetClass, uint token)
        {
            int index = (int)budgetClass;
            if ((activeTokens[index] & token) != 0u)
            {
                activeTokens[index] &= ~token;
                active[index]--;
            }
        }

        public struct Lease : IDisposable
        {
            private UiMotionBudget owner;
            private readonly UiMotionBudgetClass budgetClass;
            private readonly uint token;

            internal Lease(UiMotionBudget owner, UiMotionBudgetClass budgetClass, uint token)
            {
                this.owner = owner;
                this.budgetClass = budgetClass;
                this.token = token;
            }

            public void Dispose()
            {
                UiMotionBudget currentOwner = owner;
                owner = null;
                currentOwner?.Release(budgetClass, token);
            }
        }
    }
}
