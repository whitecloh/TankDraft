using System;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Scenario")]
    public sealed class BattleScenarioAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Stack
        {
            public int side;
            public BattleUnitAsset unit;
            public int count;
        }

        [SerializeField]
        private string _id, _title;
        [SerializeField]
        private int _seed;
        [SerializeField]
        private Stack[] _stacks = Array.Empty<Stack>();
        public string Id => _id;
        public string Title => _title;
        internal Stack[] Stacks => _stacks;

        public BattleScenarioDefinition CreateDefinition()
        {
            var stacks = new BattleArmyStack[_stacks == null ? 0 : _stacks.Length];
            for (var i = 0; i < stacks.Length; i++)
            {
                var stack = _stacks[i];
                stacks[i] = new BattleArmyStack(stack != null ? stack.side : -1, stack != null && stack.unit != null ? stack.unit.Id : string.Empty, stack != null ? stack.count : 0);
            }

            return new BattleScenarioDefinition(_id, _title, unchecked((uint)_seed), stacks);
        }
    }
}