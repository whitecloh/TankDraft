using System;
using System.Collections.Generic;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    public sealed class BattleWorldView : MonoBehaviour
    {
        [SerializeField]
        private Transform _unitsRoot, _projectilesRoot, _zonesRoot, _effectsRoot;
        [SerializeField]
        private BattleViewportView _viewportFitter;
        private BattleViewCatalogAsset _catalog;
        private BattlePresentationSettings _settings;
        private readonly Dictionary<int, BattleEntityView> _active = new Dictionary<int, BattleEntityView>();
        private readonly Dictionary<BattleEntityView, Stack<BattleEntityView>> _available = new Dictionary<BattleEntityView, Stack<BattleEntityView>>();
        private readonly Dictionary<BattleEntityView, int> _created = new Dictionary<BattleEntityView, int>();
        private readonly Dictionary<BattleEntityView, BattleEntityView> _origins = new Dictionary<BattleEntityView, BattleEntityView>();
        private readonly List<int> _removed = new List<int>();
        private readonly HashSet<int> _seen = new HashSet<int>();
        private readonly HashSet<int> _preparationSpawnIds = new HashSet<int>();
        private readonly Dictionary<PreparationCountKey, int> _preparationCounts = new Dictionary<PreparationCountKey, int>();
        private readonly Dictionary<(int side, string type, int ordinal), int> _preparationBindings = new Dictionary<(int, string, int), int>();
        private readonly Dictionary<(int side, string type, int ordinal), int> _nextPreparationBindings = new Dictionary<(int, string, int), int>();
        private readonly Dictionary<int, BattleEntityView> _reboundPreparation = new Dictionary<int, BattleEntityView>();
        private readonly HashSet<BattleEntityView> _retainedPreparation = new HashSet<BattleEntityView>();
        private readonly List<BattleEffectView> _effects = new List<BattleEffectView>();
        private readonly Dictionary<BattleEffectView, Stack<BattleEffectView>> _availableEffects = new Dictionary<BattleEffectView, Stack<BattleEffectView>>();
        private readonly Dictionary<BattleEffectView, BattleEffectView> _effectOrigins = new Dictionary<BattleEffectView, BattleEffectView>();
        private readonly Dictionary<BattleEffectView, int> _createdEffects = new Dictionary<BattleEffectView, int>();
        public int ActiveUnitViews { get; private set; }
        public int ActiveProjectileViews { get; private set; }
        public int ActiveZoneViews { get; private set; }
        public int ActiveEffectViews => _effects.Count;
        public int CreatedViewCount { get; private set; }

        public void Initialize(BattleViewCatalogAsset catalog, BattlePresentationSettings settings)
        {
            if (catalog == null || settings == null)
                throw new ArgumentNullException(catalog == null ? nameof(catalog) : nameof(settings));
            if (_unitsRoot == null || _projectilesRoot == null || _zonesRoot == null || _effectsRoot == null)
                throw new InvalidOperationException(name + " requires world roots.");
            Dispose();
            _catalog = catalog;
            _settings = settings;
            _settings.Validate();
            for (var i = 0; i < catalog.Entries.Count; i++)
            {
                var entry = catalog.Entries[i];
                var amount = entry.kind == BattleEntityKind.Unit ? settings.PrewarmUnitsPerPrefab : entry.kind == BattleEntityKind.Projectile ? settings.PrewarmProjectilesPerPrefab : settings.PrewarmZonesPerPrefab;
                Prepare(entry.prefab, entry.kind, amount);
            }

            PrepareEffects(catalog.ImpactPrefab, settings.PrewarmEffects);
            if (catalog.DeathPrefab != catalog.ImpactPrefab)
                PrepareEffects(catalog.DeathPrefab, settings.PrewarmEffects);
        }

        public void ConfigureBounds(float halfWidth, float halfHeight)
        {
            if (_viewportFitter == null)
                throw new InvalidOperationException(name + " requires viewport fitter.");
            _viewportFitter.ConfigureBounds(halfWidth, halfHeight);
        }

        public void Sync(IReadOnlyList<BattleEntityState> states, float alpha, float now, bool animateNewUnits = false)
        {
            SyncInternal(states, alpha, now, animateNewUnits, null);
        }

        // Preparation snapshots are a visual transaction: only newly added stack occurrences
        // enter with a pop. A first load/reconnect deliberately suppresses that affordance.
        public void SyncPreparation(IReadOnlyList<BattleEntityState> states, float now, bool resetHistory, bool enteringBattle = false)
        {
            if (states == null)
                throw new ArgumentNullException(nameof(states));
            if (resetHistory)
            {
                Clear();
                _preparationCounts.Clear();
                _preparationBindings.Clear();
            }

            _preparationSpawnIds.Clear();
            _nextPreparationBindings.Clear();
            _reboundPreparation.Clear();
            _retainedPreparation.Clear();
            var next = new Dictionary<PreparationCountKey, int>();
            for (int index = 0; index < states.Count; index++)
            {
                BattleEntityState state = states[index];
                if (state.Kind != BattleEntityKind.Unit)
                    continue;
                var key = new PreparationCountKey(state.Side, state.DefinitionId);
                next.TryGetValue(key, out int occurrence);
                occurrence++;
                next[key] = occurrence;
                _preparationCounts.TryGetValue(key, out int previous);
                if (!resetHistory && occurrence > previous)
                    _preparationSpawnIds.Add(state.Id);
                var binding = (state.Side, state.DefinitionId, occurrence);
                _nextPreparationBindings.Add(binding, state.Id);
                if (_preparationBindings.TryGetValue(binding, out var oldId) && _active.TryGetValue(oldId, out var view))
                {
                    view.RebindPreparation(state.Id, state.Position, now, _settings.FormationMoveSeconds);
                    if (enteringBattle) view.FinishPreparation();
                    _reboundPreparation.Add(state.Id, view);
                    _retainedPreparation.Add(view);
                }
            }

            foreach (var view in _active.Values)
                if (!_retainedPreparation.Contains(view)) Release(view);
            _active.Clear();
            foreach (var pair in _reboundPreparation) _active.Add(pair.Key, pair.Value);
            SyncInternal(states, 1f, now, false, _preparationSpawnIds);
            _preparationBindings.Clear();
            foreach (var pair in _nextPreparationBindings) _preparationBindings.Add(pair.Key, pair.Value);
            _preparationCounts.Clear();
            foreach (var pair in next)
                _preparationCounts.Add(pair.Key, pair.Value);
        }

        private void SyncInternal(IReadOnlyList<BattleEntityState> states, float alpha, float now, bool animateNewUnits, HashSet<int> explicitSpawnIds)
        {
            if (_catalog == null)
                throw new InvalidOperationException("Initialize first.");
            _seen.Clear();
            ActiveUnitViews = ActiveProjectileViews = ActiveZoneViews = 0;
            int animatedUnitOrdinal = 0;
            for (var i = 0; i < states.Count; i++)
            {
                var state = states[i];
                _seen.Add(state.Id);
                var entry = _catalog.Get(state.DefinitionId);
                if (entry.kind != state.Kind)
                    throw new InvalidOperationException("State/view kind mismatch: " + state.DefinitionId);
                bool acquired = false;
                if (_active.TryGetValue(state.Id, out var view))
                {
                    if (!_origins.TryGetValue(view, out var origin) || origin != entry.prefab)
                    {
                        Release(view);
                        _active.Remove(state.Id);
                        view = null;
                    }
                }
                if (view == null)
                {
                    view = Acquire(entry.prefab, state.Kind);
                    view.Bind(state.Id, ColorFor(state.Side));
                    _active.Add(state.Id, view);
                    acquired = true;
                }

                if (acquired && state.Kind == BattleEntityKind.Unit && (animateNewUnits || explicitSpawnIds != null && explicitSpawnIds.Contains(state.Id)))
                {
                    view.PlaySpawn(now, Mathf.Min(_settings.UnitSpawnMaxDelay, animatedUnitOrdinal++ * _settings.UnitSpawnStagger), _settings);
                }

                view.ConfigurePresentation(entry, _settings);
                view.Render(state, alpha, now);
                if (state.Kind == BattleEntityKind.Unit)
                    ActiveUnitViews++;
                else if (state.Kind == BattleEntityKind.Projectile)
                    ActiveProjectileViews++;
                else
                    ActiveZoneViews++;
            }

            _removed.Clear();
            foreach (var pair in _active)
                if (!_seen.Contains(pair.Key))
                    _removed.Add(pair.Key);
            for (var i = 0; i < _removed.Count; i++)
            {
                var id = _removed[i];
                Release(_active[id]);
                _active.Remove(id);
            }
        }

        public void Present(IReadOnlyList<BattleEvent> events, float now)
        {
            for (var i = 0; i < events.Count; i++)
            {
                var battleEvent = events[i];
                if (battleEvent.Kind == BattleEventKind.Damage && _active.TryGetValue(battleEvent.EntityId, out var view))
                    view.Flash(now);
                else if (battleEvent.Kind == BattleEventKind.Shot && _active.TryGetValue(battleEvent.EntityId, out view))
                    view.PlayShot(now);
                else if (battleEvent.Kind == BattleEventKind.Impact)
                    SpawnEffect(_catalog.ImpactPrefab, battleEvent, now);
                else if (battleEvent.Kind == BattleEventKind.Death)
                    SpawnEffect(_catalog.DeathPrefab, battleEvent, now);
            }
        }

        public void TickEffects(float now)
        {
            for (var i = _effects.Count - 1; i >= 0; i--)
            {
                if (_effects[i].Tick(now))
                    continue;
                ReleaseEffect(_effects[i]);
                _effects.RemoveAt(i);
            }
        }

        public void Clear()
        {
            foreach (var pair in _active)
                Release(pair.Value);
            _active.Clear();
            _preparationBindings.Clear();
            _preparationCounts.Clear();
            for (var i = 0; i < _effects.Count; i++)
                ReleaseEffect(_effects[i]);
            _effects.Clear();
            ActiveUnitViews = ActiveProjectileViews = ActiveZoneViews = 0;
        }

        public void Dispose()
        {
            // Scene roots may already be destroyed when VContainer disposes its scope.
            // Destroy the pool ownership set directly; do not recycle dying scene objects.
            foreach (var view in _origins.Keys)
                if (view != null)
                    Destroy(view.gameObject);
            foreach (var effect in _effectOrigins.Keys)
                if (effect != null)
                    Destroy(effect.gameObject);
            _active.Clear();
            _effects.Clear();
            _available.Clear();
            _availableEffects.Clear();
            _created.Clear();
            _createdEffects.Clear();
            _origins.Clear();
            _effectOrigins.Clear();
            _removed.Clear();
            _seen.Clear();
            _preparationSpawnIds.Clear();
            _preparationCounts.Clear();
            ActiveUnitViews = ActiveProjectileViews = ActiveZoneViews = CreatedViewCount = 0;
            _catalog = null;
            _settings = null;
        }

        private void Prepare(BattleEntityView prefab, BattleEntityKind kind, int amount)
        {
            if (!_available.ContainsKey(prefab))
                _available.Add(prefab, new Stack<BattleEntityView>());
            for (var i = 0; i < amount; i++)
                Release(Create(prefab, kind));
        }

        private BattleEntityView Acquire(BattleEntityView prefab, BattleEntityKind kind)
        {
            if (!_available.TryGetValue(prefab, out var pool))
            {
                pool = new Stack<BattleEntityView>();
                _available.Add(prefab, pool);
            }

            if (pool.Count > 0)
                return pool.Pop();
            if (!_created.TryGetValue(prefab, out var count))
                count = 0;
            if (count >= _settings.MaxPoolPerPrefab)
                throw new InvalidOperationException("Battle view pool exhausted: " + prefab.name);
            return Create(prefab, kind);
        }

        private BattleEntityView Create(BattleEntityView prefab, BattleEntityKind kind)
        {
            var view = Instantiate(prefab, Root(kind));
            view.Clear();
            _origins.Add(view, prefab);
            _created[prefab] = _created.TryGetValue(prefab, out var count) ? count + 1 : 1;
            CreatedViewCount++;
            return view;
        }

        private void Release(BattleEntityView view)
        {
            view.Clear();
            if (!_origins.TryGetValue(view, out var prefab) || !_available.TryGetValue(prefab, out var pool))
                throw new InvalidOperationException("Unknown pooled battle view.");
            pool.Push(view);
        }

        private Transform Root(BattleEntityKind kind) => kind == BattleEntityKind.Unit ? _unitsRoot : kind == BattleEntityKind.Projectile ? _projectilesRoot : _zonesRoot;
        private Color ColorFor(int side) => side == 0 ? _settings.Side0Color : _settings.Side1Color;
        private void PrepareEffects(BattleEffectView prefab, int amount)
        {
            if (!_availableEffects.ContainsKey(prefab))
                _availableEffects.Add(prefab, new Stack<BattleEffectView>());
            for (var i = 0; i < amount; i++)
                ReleaseEffect(CreateEffect(prefab));
        }

        private BattleEffectView CreateEffect(BattleEffectView prefab)
        {
            if (!_createdEffects.TryGetValue(prefab, out var count))
                count = 0;
            if (count >= _settings.MaxPoolPerPrefab)
                throw new InvalidOperationException("Battle effect pool exhausted: " + prefab.name);
            var effect = Instantiate(prefab, _effectsRoot);
            effect.Clear();
            _effectOrigins.Add(effect, prefab);
            _createdEffects[prefab] = count + 1;
            CreatedViewCount++;
            return effect;
        }

        private void ReleaseEffect(BattleEffectView effect)
        {
            effect.Clear();
            if (!_effectOrigins.TryGetValue(effect, out var prefab) || !_availableEffects.TryGetValue(prefab, out var pool))
                throw new InvalidOperationException("Unknown pooled battle effect.");
            pool.Push(effect);
        }

        private void SpawnEffect(BattleEffectView prefab, BattleEvent e, float now)
        {
            if (!_availableEffects.TryGetValue(prefab, out var pool))
                throw new InvalidOperationException("Unprepared battle effect prefab.");
            var effect = pool.Count > 0 ? pool.Pop() : CreateEffect(prefab);
            effect.Play(e.Position, Mathf.Max(_settings.MinimumEffectRadius, e.Radius), ColorFor(e.Side), now, _settings.EffectSeconds);
            _effects.Add(effect);
        }

        private readonly struct PreparationCountKey : IEquatable<PreparationCountKey>
        {
            private readonly int _side;
            private readonly string _definitionId;
            public PreparationCountKey(int side, string definitionId)
            {
                _side = side;
                _definitionId = definitionId ?? string.Empty;
            }
            public bool Equals(PreparationCountKey other) => _side == other._side && string.Equals(_definitionId, other._definitionId, StringComparison.Ordinal);
            public override bool Equals(object obj) => obj is PreparationCountKey other && Equals(other);
            public override int GetHashCode() => (_side * 397) ^ StringComparer.Ordinal.GetHashCode(_definitionId);
        }
    }
}
