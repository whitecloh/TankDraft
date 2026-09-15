using System;
using System.Collections.Generic;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    [CreateAssetMenu(menuName = "TankDraft/Battle/View catalog")]
    public sealed class BattleViewCatalogAsset : ScriptableObject
    {
        [Serializable]
        public sealed class Entry
        {
            public string definitionId;
            public BattleEntityKind kind;
            public BattleEntityView prefab;
            [Min(.01f)] public float visualScale = 1f;
            [Min(.01f)] public float transformedVisualScale = 1f;
            public Vector2 visualOffset;
        }

        [SerializeField]
        private Entry[] _entries = Array.Empty<Entry>();
        [SerializeField]
        private BattleEffectView _impactPrefab, _deathPrefab;
        private readonly Dictionary<string, Entry> _byId = new Dictionary<string, Entry>();
        public IReadOnlyList<Entry> Entries => _entries;
        public BattleEffectView ImpactPrefab => _impactPrefab;
        public BattleEffectView DeathPrefab => _deathPrefab;

        public Entry Get(string definitionId)
        {
            BuildIndex();
            if (!_byId.TryGetValue(definitionId, out var entry))
                throw new ArgumentException("No view for definition: " + definitionId);
            return entry;
        }

        public void Validate(BattleDefinitions definitions)
        {
            if (definitions == null)
                throw new ArgumentNullException(nameof(definitions));
            BuildIndex();
            if (_entries.Length != definitions.Units.Count + definitions.Projectiles.Count + definitions.Zones.Count)
                throw new InvalidOperationException(name + " must map every battle definition exactly once.");
            ValidateDefinitions(definitions.Units, BattleEntityKind.Unit);
            ValidateDefinitions(definitions.Projectiles, BattleEntityKind.Projectile);
            ValidateDefinitions(definitions.Zones, BattleEntityKind.Zone);
            if (_impactPrefab == null || _deathPrefab == null)
                throw new InvalidOperationException(name + " requires effect prefabs.");
        }

        private void ValidateDefinitions<T>(IList<T> definitions, BattleEntityKind kind)
            where T : class
        {
            for (var i = 0; i < definitions.Count; i++)
            {
                var id = definitions[i] is BattleUnitDefinition u ? u.Id : definitions[i] is BattleProjectileDefinition p ? p.Id : ((BattleZoneDefinition)(object)definitions[i]).Id;
                var entry = Get(id);
                if (entry.kind != kind)
                    throw new InvalidOperationException(name + " maps " + id + " to wrong entity kind.");
            }
        }

        private void BuildIndex()
        {
            _byId.Clear();
            if (_entries == null)
                throw new InvalidOperationException(name + " requires entries.");
            for (var i = 0; i < _entries.Length; i++)
            {
                var entry = _entries[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.definitionId) || entry.prefab == null || !_byId.TryAdd(entry.definitionId, entry))
                    throw new InvalidOperationException(name + " has null or duplicate view entry.");
                if (entry.visualScale <= 0f || float.IsNaN(entry.visualScale) || float.IsInfinity(entry.visualScale) ||
                    entry.transformedVisualScale <= 0f || float.IsNaN(entry.transformedVisualScale) || float.IsInfinity(entry.transformedVisualScale) ||
                    float.IsNaN(entry.visualOffset.x) || float.IsInfinity(entry.visualOffset.x) || float.IsNaN(entry.visualOffset.y) || float.IsInfinity(entry.visualOffset.y))
                    throw new InvalidOperationException(name + " has invalid visual tuning: " + entry.definitionId);
                entry.prefab.ValidateFor(entry.kind);
            }
        }
    }
}
