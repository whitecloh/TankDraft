using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Zone")]
    public sealed class BattleZoneAsset : ScriptableObject
    {
        [SerializeField]
        private string _id;
        [SerializeField, Min(0.01f)]
        private float _radius;
        [SerializeField, Min(1)]
        private int _tickDamage;
        [SerializeField, Min(0.01f)]
        private float _periodSeconds;
        [SerializeField, Min(0.01f)]
        private float _lifetimeSeconds;
        [SerializeField, Range(0f, 1f)] private float _moveSpeedMultiplier = 1f;
        [SerializeField, Range(0, 10000)] private int _sourceDamagePercent;
        public string Id => _id;

        public BattleZoneDefinition CreateDefinition() => new BattleZoneDefinition(_id, _radius, _tickDamage, _periodSeconds, _lifetimeSeconds, _moveSpeedMultiplier, _sourceDamagePercent);
    }
}
