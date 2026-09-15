using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Rules")]
    public sealed class BattleRulesAsset : ScriptableObject
    {
        [SerializeField]
        private float _tickSeconds = 1f / 30f;
        [SerializeField]
        private float _halfWidth = 4.5f, _halfHeight = 7.5f, _frontOffset = 1.3f, _rowGap = 1.45f, _unitGap = .15f;
        [SerializeField]
        private int _separationIterations = 3, _maxEntities = 2000;
        [SerializeField]
        private float _separationSpeed = 4f;
        [SerializeField]
        private bool _allowDebugCommands = true;
        public BattleRules CreateDefinition() => new BattleRules(_tickSeconds, _halfWidth, _halfHeight, _frontOffset, _rowGap, _unitGap, _separationIterations, _separationSpeed, _maxEntities, _allowDebugCommands);
    }
}