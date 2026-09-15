using System;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Unit")]
    public sealed class BattleUnitAsset : ScriptableObject
    {
        [SerializeField]
        private ContentEntryAsset _content;
        [SerializeField]
        private BattleAttackKind _attack;
        [SerializeField, Min(1)]
        private int _maxHp;
        [SerializeField, Min(1)]
        private int _damage;
        [SerializeField, Min(0f)]
        private float _moveSpeed;
        [SerializeField, Min(0.01f)]
        private float _radius;
        [SerializeField, Min(0.01f)]
        private float _mass;
        [SerializeField, Min(0f)]
        private float _range;
        [SerializeField, Min(0.01f)]
        private float _cooldownSeconds;
        [SerializeField]
        private BattleProjectileAsset _projectile;
        [SerializeField]
        private BattleUnitAbilityKind _abilityKind;
        [SerializeField, Min(0f)]
        private float _abilityIntervalSeconds;
        [SerializeField, Min(0f)]
        private float _abilityRadius;
        [SerializeField, Min(0)]
        private int _abilityAmount;
        [SerializeField]
        private BattleUnitAsset _spawnUnit;
        [SerializeField, Min(0f)]
        private float _spawnLifetimeSeconds;
        [SerializeField, Min(0)]
        private int _spawnDamagePercent;
        [SerializeField] private BattleZoneAsset _contactZone;
        [SerializeField, Range(0, 10000)] private int _dotTotalDamagePercent;
        [SerializeField, Min(0.01f)] private float _dotPeriodSeconds = 1f;
        [SerializeField, Range(1, 100)] private int _dotTickCount = 3;
        [SerializeField, Range(0, 10000)] private int _shieldCapacityHpPercent;
        [SerializeField, Min(0.01f)] private float _shieldIntervalSeconds = 4f;
        [SerializeField, Min(0.01f)] private float _shieldRadius = 3f;
        [SerializeField, Min(0.01f)] private float _shieldLifetimeSeconds = 3.5f;
        [SerializeField] private bool _blocksFirstHit;
        [SerializeField] private bool _transforms;
        [SerializeField, Min(.01f)] private float _transformationDelaySeconds = 10f;
        [SerializeField, Range(100, 10000)] private int _transformationMaxHpPercent = 400;
        [SerializeField, Range(100, 10000)] private int _transformationDamagePercent = 300;
        [SerializeField, Min(.01f)] private float _transformationAttackRadius = 1.4f;
        [SerializeField, Range(0, 100)] private int _magazineShots;
        [SerializeField, Min(0.01f)] private float _magazineReloadSeconds = 3f;
        [SerializeField, Range(0, 100)] private int _lifeStealPercent;
        [SerializeField, Tooltip("Within the same row, higher priority is closer to the enemy. Each unit type occupies separate subrows.")] private int _formationPriority;
        public string Id => _content != null ? _content.Id : string.Empty;
        public FormationRow Row => _content != null ? _content.CreateDefinition().Row : default;
        internal ContentEntryAsset Content => _content;
        internal BattleProjectileAsset Projectile => _projectile;
        internal BattleZoneAsset ContactZone => _contactZone;

        public BattleUnitDefinition CreateDefinition() => new BattleUnitDefinition(Id, Row, _attack, _maxHp, _damage, _moveSpeed, _radius, _mass, _range, _cooldownSeconds,
            _projectile != null ? _projectile.Id : string.Empty, CreateAbility(),
            _contactZone != null ? _contactZone.Id : string.Empty,
            _dotTotalDamagePercent == 0 ? null : new BattleDamageOverTimeDefinition(_dotTotalDamagePercent, _dotPeriodSeconds, _dotTickCount),
            _shieldCapacityHpPercent == 0 ? null : new BattleShieldDefinition(_shieldIntervalSeconds, _shieldRadius, _shieldCapacityHpPercent, _shieldLifetimeSeconds),
            _blocksFirstHit, _magazineShots == 0 ? null : new BattleMagazineDefinition(_magazineShots, _magazineReloadSeconds), _lifeStealPercent, _formationPriority,
            _transforms ? new BattleTransformationDefinition(_transformationDelaySeconds, _transformationMaxHpPercent, _transformationDamagePercent, _transformationAttackRadius) : null);

        private BattleUnitAbilityDefinition CreateAbility()
        {
            if (_abilityKind == BattleUnitAbilityKind.None)
                return null;
            return new BattleUnitAbilityDefinition(_abilityKind, _abilityIntervalSeconds, _abilityRadius, _abilityAmount,
                _spawnUnit != null ? _spawnUnit.Id : string.Empty, _spawnLifetimeSeconds, _spawnDamagePercent);
        }
    }
}
