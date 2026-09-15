using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Project-independent collection of semantic motion tokens.</summary>
    [CreateAssetMenu(fileName = "UiMotionTheme", menuName = "SLVR/UI Motion/Theme")]
    public sealed class UiMotionTheme : ScriptableObject
    {
        [SerializeField] private UiMotionTimingTokens timings = default;
        [SerializeField] private UiMotionEase standardEase = UiMotionEase.OutCubic;
        [SerializeField] private UiMotionEase emphasisEase = UiMotionEase.OutBack;
        [SerializeField] private UiMotionBudgetProfile budgetProfile;

        public UiMotionTimingTokens Timings => timings.Get(UiMotionTiming.Normal) > 0f
            ? timings
            : UiMotionTimingTokens.Balanced;

        public UiMotionEase StandardEase => standardEase;
        public UiMotionEase EmphasisEase => emphasisEase;
        public UiMotionBudgetProfile BudgetProfile => budgetProfile;
    }
}
