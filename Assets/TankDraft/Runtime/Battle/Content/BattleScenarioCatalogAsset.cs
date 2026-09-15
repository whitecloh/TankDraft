using System;
using System.Collections.Generic;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattleContent
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/Scenario catalog")]
    public sealed class BattleScenarioCatalogAsset : ScriptableObject
    {
        [SerializeField]
        private BattleRulesAsset _rules;
        [SerializeField]
        private BattleUnitAsset[] _units = Array.Empty<BattleUnitAsset>();
        [SerializeField]
        private BattleProjectileAsset[] _projectiles = Array.Empty<BattleProjectileAsset>();
        [SerializeField]
        private BattleZoneAsset[] _zones = Array.Empty<BattleZoneAsset>();
        [SerializeField]
        private BattleScenarioAsset[] _scenarios = Array.Empty<BattleScenarioAsset>();
        public int ScenarioCount => _scenarios == null ? 0 : _scenarios.Length;

        public BattleDefinitions CreateDefinitions()
        {
            Validate();
            return CreateDefinitionsUnchecked();
        }

        public BattleRules CreateRules()
        {
            Validate();
            return _rules.CreateDefinition();
        }

        public BattleScenarioDefinition CreateScenario(int index)
        {
            Validate();
            if (index < 0 || index >= ScenarioCount)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _scenarios[index].CreateDefinition();
        }

        public void Validate()
        {
            if (_rules == null)
                throw new InvalidOperationException(name + " requires rules.");
            _rules.CreateDefinition();
            ValidateCatalog(_units, "unit", true);
            ValidateCatalog(_projectiles, "projectile", false);
            ValidateCatalog(_zones, "zone", false);
            ValidateCatalog(_scenarios, "scenario", true);
            var units = new HashSet<BattleUnitAsset>(_units);
            var projectiles = new HashSet<BattleProjectileAsset>(_projectiles);
            var zones = new HashSet<BattleZoneAsset>(_zones);
            for (var i = 0; i < _units.Length; i++)
            {
                var unit = _units[i];
                if (unit.Content == null || unit.Content.CreateDefinition().Kind != ContentKind.Unit || (unit.Projectile != null && !projectiles.Contains(unit.Projectile)))
                    throw new InvalidOperationException(name + " has a missing, non-unit, or foreign unit reference.");
                if (unit.ContactZone != null && !zones.Contains(unit.ContactZone))
                    throw new InvalidOperationException(name + " has a foreign contact zone reference.");
            }

            for (var i = 0; i < _projectiles.Length; i++)
                if (_projectiles[i].Zone != null && !zones.Contains(_projectiles[i].Zone))
                    throw new InvalidOperationException(name + " has a foreign zone reference.");
            for (var i = 0; i < _scenarios.Length; i++)
            {
                var stacks = _scenarios[i].Stacks;
                if (stacks == null || stacks.Length == 0)
                    throw new InvalidOperationException(name + " has empty scenario stacks.");
                for (var j = 0; j < stacks.Length; j++)
                    if (stacks[j] == null || stacks[j].unit == null || !units.Contains(stacks[j].unit))
                        throw new InvalidOperationException(name + " has a missing or foreign scenario unit.");
                _scenarios[i].CreateDefinition();
            }

            CreateDefinitionsUnchecked();
        }

        private BattleDefinitions CreateDefinitionsUnchecked() => new BattleDefinitions(Create(_units, x => x.CreateDefinition()), Create(_projectiles, x => x.CreateDefinition()), Create(_zones, x => x.CreateDefinition()));
        private void ValidateCatalog<T>(T[] values, string label, bool nonEmpty)
            where T : UnityEngine.Object
        {
            if (values == null || (nonEmpty && values.Length == 0))
                throw new InvalidOperationException(name + " requires " + label + " catalog.");
            var ids = new HashSet<string>();
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                    throw new InvalidOperationException(name + " has null " + label + ".");
                string id = values[i] is BattleUnitAsset u ? u.Id : values[i] is BattleProjectileAsset p ? p.Id : values[i] is BattleZoneAsset z ? z.Id : ((BattleScenarioAsset)(object)values[i]).Id;
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id))
                    throw new InvalidOperationException(name + " has duplicate " + label + " id.");
            }
        }

        private static TDefinition[] Create<TAsset, TDefinition>(TAsset[] assets, Func<TAsset, TDefinition> create)
            where TAsset : UnityEngine.Object
        {
            var result = new TDefinition[assets.Length];
            for (var i = 0; i < result.Length; i++)
                result[i] = create(assets[i]);
            return result;
        }
    }
}
