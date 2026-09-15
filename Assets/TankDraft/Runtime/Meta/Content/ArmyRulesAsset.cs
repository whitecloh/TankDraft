using UnityEngine;
using TankDraft.Contracts;
namespace TankDraft.Content
{
    [CreateAssetMenu(menuName = "TankDraft/Content/Army rules")]
    public sealed class ArmyRulesAsset : ScriptableObject
    {
        [SerializeField] private int _unitSlots = 4;
        [SerializeField] private int[] _orderUnlockLevels = {1,20,35};
        public ArmyRules CreateDefinition() => new ArmyRules(_unitSlots, _orderUnlockLevels);
    }
}
