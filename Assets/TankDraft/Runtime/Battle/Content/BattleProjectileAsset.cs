using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Projectile")]
    public sealed class BattleProjectileAsset : ScriptableObject
    {
        [SerializeField]
        private string _id;
        [SerializeField, Min(0.01f)]
        private float _speed;
        [SerializeField, Min(0.01f)]
        private float _radius;
        [SerializeField, Min(0f)]
        private float _impactRadius;
        [SerializeField]
        private BattleZoneAsset _zone;
        [SerializeField] private bool _retargetOnTargetLost;
        public string Id => _id;
        internal BattleZoneAsset Zone => _zone;

        public BattleProjectileDefinition CreateDefinition() => new BattleProjectileDefinition(_id, _speed, _radius, _impactRadius, _zone != null ? _zone.Id : string.Empty, _retargetOnTargetLost);
    }
}
