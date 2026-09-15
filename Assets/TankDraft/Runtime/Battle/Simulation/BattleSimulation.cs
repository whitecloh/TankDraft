using System;
using System.Collections.Generic;
using Leopotam.EcsLite;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;

namespace TankDraft.Simulation
{
    // Fixed-step, render-independent battle slice. Equal authored sub-tick hit times are batched.
    public sealed class BattleSimulation : IDisposable
    {
        const float EqualTimeEpsilon = 0.00001f;
        const long MaxCommandLeadTicks = 300;
        readonly EcsWorld _world;
        readonly IEcsSystems _systems;
        readonly Sim _sim;
        readonly EcsPool<Identity> _identity;
        readonly EcsPool<Position> _position;
        readonly EcsPool<Unit> _unit;
        readonly EcsPool<Projectile> _projectile;
        readonly EcsPool<Zone> _zone;
        readonly EcsFilter _units;
        readonly EcsFilter _projectiles;
        readonly EcsFilter _zones;
        public BattleSimulation(BattleDefinitions definitions, BattleRules rules, BattleScenarioDefinition scenario, bool preparation = false)
        {
            if (definitions == null || rules == null || scenario == null)
                throw new ArgumentNullException();
            _sim = new Sim(definitions, rules, scenario);
            _world = new EcsWorld(new EcsWorld.Config { Entities = rules.MaxEntities + 16, Pools = 8, Filters = 8 });
            _identity = _world.GetPool<Identity>();
            _position = _world.GetPool<Position>();
            _unit = _world.GetPool<Unit>();
            _projectile = _world.GetPool<Projectile>();
            _zone = _world.GetPool<Zone>();
            _units = _world.Filter<Identity>().Inc<Position>().Inc<Unit>().End(rules.MaxEntities);
            _projectiles = _world.Filter<Identity>().Inc<Position>().Inc<Projectile>().End(rules.MaxEntities);
            _zones = _world.Filter<Identity>().Inc<Position>().Inc<Zone>().End(rules.MaxEntities);
            _sim.Owner = this;
            try
            {
                if (!preparation)
                {
                    bool side0 = false, side1 = false;
                    foreach (var stack in scenario.Stacks) { side0 |= stack.Side == 0; side1 |= stack.Side == 1; }
                    if (!side0 || !side1) throw new ArgumentException("Both armies required for battle.");
                }
                SpawnScenario(preparation);
            }
            catch
            {
                _world.Destroy();
                throw;
            }

            _systems = new EcsSystems(_world, _sim).Add(new AbilitySystem()).Add(new TargetSystem()).Add(new MovementSystem()).Add(new SeparationSystem()).Add(new AttackSystem()).Add(new ProjectileSystem()).Add(new ZoneSystem()).Add(new DamageOutcomeSystem());
            _systems.Init();
        }

        public long Tick
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Tick;
            }
        }

        public BattleOutcome Outcome
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Outcome;
            }
        }

        public int AliveSide0
        {
            get
            {
                ThrowIfDisposed();
                return CountAlive(0);
            }
        }

        public int AliveSide1
        {
            get
            {
                ThrowIfDisposed();
                return CountAlive(1);
            }
        }

        public int TotalShots
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Shots;
            }
        }

        public int TotalImpacts
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Impacts;
            }
        }

        public int TotalZoneTicks
        {
            get
            {
                ThrowIfDisposed();
                return _sim.ZoneTicks;
            }
        }

        public int TotalDeaths
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Deaths;
            }
        }

        public BattleRules Rules
        {
            get
            {
                ThrowIfDisposed();
                return _sim.Rules;
            }
        }

        public IReadOnlyList<BattleResolutionHit> TerminalHits
        {
            get
            {
                ThrowIfDisposed();
                return (IReadOnlyList<BattleResolutionHit>)_sim.TerminalHits ?? EmptyTerminalHits;
            }
        }

        public bool ResolutionUsedRandomTieBreak
        {
            get
            {
                ThrowIfDisposed();
                return _sim.ResolutionUsedRandomTieBreak;
            }
        }

        public uint TieBreakSeed
        {
            get
            {
                ThrowIfDisposed();
                return _sim.TieBreakSeed;
            }
        }

        static readonly BattleResolutionHit[] EmptyTerminalHits = Array.Empty<BattleResolutionHit>();
        public void Step()
        {
            ThrowIfDisposed();
            if (_sim.Outcome == BattleOutcome.Running)
            {
                _sim.Tick++;
                _systems.Run();
            }
            else
                CleanupNonUnits();
        }

        public void Capture(List<BattleEntityState> target)
        {
            ThrowIfDisposed();
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            target.Clear();
            CaptureUnits(target);
            if (_sim.Outcome == BattleOutcome.Running)
            {
                CaptureProjectiles(target);
                CaptureZones(target);
            }
        }

        public void DrainEvents(List<BattleEvent> target)
        {
            ThrowIfDisposed();
            if (target == null)
                throw new ArgumentNullException(nameof(target));
            target.Clear();
            target.AddRange(_sim.Events);
            _sim.Events.Clear();
        }

        public bool TryQueueZone(BattleZoneCommand command, out string reason)
        {
            ThrowIfDisposed();
            reason = null;
            if (_sim.Outcome != BattleOutcome.Running)
            {
                reason = "Battle is finished.";
                return false;
            }

            if (!_sim.Rules.AllowDebugCommands)
            {
                reason = "Debug commands are disabled.";
                return false;
            }

            if (command.Side != 0 && command.Side != 1)
            {
                reason = "Unknown side.";
                return false;
            }

            if (command.Sequence <= _sim.LastCommand[command.Side])
            {
                reason = "Sequence is duplicate or stale.";
                return false;
            }

            if (command.ApplyTick <= Tick || command.ApplyTick > Tick + MaxCommandLeadTicks)
            {
                reason = "ApplyTick must be within the next 300 ticks.";
                return false;
            }

            if (!Finite(command.Position) || Math.Abs(command.Position.X) > Rules.HalfWidth || Math.Abs(command.Position.Y) > Rules.HalfHeight)
            {
                reason = "Position is outside arena.";
                return false;
            }

            try
            {
                _sim.Definitions.Zone(command.ZoneId);
            }
            catch (Exception)
            {
                reason = "Unknown zone.";
                return false;
            }

            if (EntityCount() + _sim.Commands.Count >= Rules.MaxEntities)
            {
                reason = "Entity capacity reached.";
                return false;
            }

            _sim.LastCommand[command.Side] = command.Sequence;
            _sim.Commands.Add(command);
            return true;
        }

        bool _disposed;
        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _systems.Destroy();
            _world.Destroy();
        }

        void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(BattleSimulation));
        }

        void SpawnScenario(bool preparation)
        {
            int total = 0;
            for (int i = 0; i < _sim.Scenario.Stacks.Count; i++)
                total += _sim.Scenario.Stacks[i].Count;
            if (total > Rules.MaxEntities)
                throw new ArgumentException("Scenario exceeds MaxEntities.");
            for (int side = 0; side < 2; side++)
            {
                float previousRearEdge = 0;
                for (int row = 0; row <= 3; row++)
                    SpawnRow(side, (FormationRow)row, ref previousRearEdge, false, false);
                bool compact = previousRearEdge > Rules.HalfHeight;
                if (compact)
                {
                    previousRearEdge = 0;
                    for (int row = 0; row <= 3; row++)
                        SpawnRow(side, (FormationRow)row, ref previousRearEdge, true, false);
                    if (previousRearEdge > Rules.HalfHeight)
                        throw new ArgumentException("Formation does not fit arena.");
                }
                previousRearEdge = 0;
                for (int row = 0; row <= 3; row++)
                    SpawnRow(side, (FormationRow)row, ref previousRearEdge, compact, true);
            }

            ValidateSpawnLayout();
            if (!preparation) ApplyOpeningBacklineJumps();
            ValidateSpawnLayout();
        }

        void ValidateSpawnLayout()
        {
            var entities = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                int a = entities[i];
                var pa = _position.Get(a).Now;
                float ra = _unit.Get(a).Radius;
                if (Math.Abs(pa.X) + ra > Rules.HalfWidth || Math.Abs(pa.Y) + ra > Rules.HalfHeight)
                    throw new ArgumentException("Formation is outside arena.");
                for (int j = i + 1; j < _units.GetEntitiesCount(); j++)
                {
                    int b = entities[j];
                    if ((_position.Get(b).Now - pa).Length + EqualTimeEpsilon < ra + _unit.Get(b).Radius)
                        throw new ArgumentException("Formation units overlap.");
                }
            }
        }

        void SpawnRow(int side, FormationRow row, ref float previousRearEdge, bool compact, bool spawn)
        {
            var groups = new List<BattleArmyStack>();
            for (int i = 0; i < _sim.Scenario.Stacks.Count; i++)
            {
                var s = _sim.Scenario.Stacks[i];
                if (s.Side == side && _sim.Definitions.Unit(s.UnitId).Row == row)
                    groups.Add(s);
            }
            groups.Sort((a, b) =>
            {
                int priority = _sim.Definitions.Unit(b.UnitId).FormationPriority.CompareTo(_sim.Definitions.Unit(a.UnitId).FormationPriority);
                return priority != 0 ? priority : string.CompareOrdinal(a.UnitId, b.UnitId);
            });
            foreach (var s in groups)
            {
                var d = _sim.Definitions.Unit(s.UnitId);
                float radius = d.Radius;
                if (radius > Rules.HalfWidth || radius > Rules.HalfHeight)
                    throw new ArgumentException("Unit radius exceeds field.");
                float spacing = 2 * radius + Rules.UnitGap;
                int per = Math.Max(1, (int)Math.Floor((2 * Rules.HalfWidth - 2 * radius) / spacing) + 1);
                int rear = (s.Count + per - 1) / per;
                float front = Math.Max(compact ? 0f : Rules.FrontOffset + (int)row * Rules.RowGap, previousRearEdge + radius + Rules.UnitGap);
                previousRearEdge = front + (rear - 1) * spacing + radius;
                if (!spawn) continue;
                for (int k = 0; k < s.Count; k++)
                {
                    int sub = k / per, col = k % per;
                    int columns = Math.Min(per, s.Count - sub * per);
                    float x = (col - (columns - 1) * .5f) * spacing;
                    float sign = side == 0 ? -1 : 1;
                    float y = sign * (front + sub * spacing);
                    SpawnUnit(side, d, new BattleVec(x, y), new BattleVec(0, -sign), s.HpBonusPercent, s.DamageBonusPercent, s.AttackSpeedBonusPercent);
                }
            }
        }

        void SpawnUnit(int side, BattleUnitDefinition d, BattleVec p, BattleVec facing, int hpBonusPercent, int damageBonusPercent, int attackSpeedBonusPercent, float lifetime = 0f, int? damageOverride = null)
        {
            int e = _world.NewEntity();
            _identity.Add(e) = new Identity
            {
                Id = _sim.NextId++,
                Side = side,
                DefinitionId = d.Id
            };
            _position.Add(e) = new Position
            {
                Now = p,
                Previous = p,
                Facing = facing
            };
            float jitter = .1f + .8f * _sim.NextRandom();
            _unit.Add(e) = new Unit
            {
                Hp = ScaleStat(d.MaxHp, hpBonusPercent),
                MaxHp = ScaleStat(d.MaxHp, hpBonusPercent),
                Damage = damageOverride ?? ScaleStat(d.Damage, damageBonusPercent),
                InitialMaxHp = ScaleStat(d.MaxHp, hpBonusPercent),
                InitialDamage = damageOverride ?? ScaleStat(d.Damage, damageBonusPercent),
                Radius = d.Radius,
                Mass = d.Mass,
                Speed = d.MoveSpeed,
                Range = d.Range,
                Cooldown = ScaleCooldown(d.CooldownSeconds, attackSpeedBonusPercent),
                Remaining = ScaleCooldown(d.CooldownSeconds, attackSpeedBonusPercent) * jitter,
                Attack = d.Attack,
                ProjectileId = d.ProjectileId,
                ContactZoneId = d.ContactZoneId,
                DotTotalDamagePercent = d.DamageOverTime == null ? 0 : d.DamageOverTime.TotalDamagePercent,
                DotPeriod = d.DamageOverTime == null ? 0f : d.DamageOverTime.PeriodSeconds,
                DotTickCount = d.DamageOverTime == null ? 0 : d.DamageOverTime.TickCount,
                ShieldInterval = d.Shield == null ? 0f : d.Shield.IntervalSeconds,
                ShieldRadius = d.Shield == null ? 0f : d.Shield.Radius,
                ShieldCapacity = d.Shield == null ? 0 : ScalePercent(ScaleStat(d.MaxHp, hpBonusPercent), d.Shield.CapacityHpPercent),
                ShieldLifetime = d.Shield == null ? 0f : d.Shield.LifetimeSeconds,
                ShieldRemaining = d.Shield == null ? 0f : d.Shield.IntervalSeconds,
                FirstHitBlock = d.BlocksFirstHit ? 1 : 0,
                MagazineShots = d.Magazine == null ? 0 : d.Magazine.Shots,
                MagazineReload = d.Magazine == null ? 0f : ScaleCooldown(d.Magazine.ReloadSeconds, attackSpeedBonusPercent),
                LifeStealPercent = d.LifeStealPercent,
                TargetId = 0,
                AbilityKind = d.Ability == null ? BattleUnitAbilityKind.None : d.Ability.Kind,
                AbilityInterval = d.Ability == null ? 0f : d.Ability.IntervalSeconds,
                AbilityRadius = d.Ability == null ? 0f : d.Ability.Radius,
                AbilityAmount = d.Ability == null ? 0 : d.Ability.Amount,
                AbilitySpawnUnitId = d.Ability == null ? "" : d.Ability.SpawnUnitId,
                AbilitySpawnLifetime = d.Ability == null ? 0f : d.Ability.SpawnLifetimeSeconds,
                AbilitySpawnDamagePercent = d.Ability == null ? 0 : d.Ability.SpawnDamagePercent,
                AbilityRemaining = d.Ability == null ? 0f : d.Ability.IntervalSeconds,
                Lifetime = lifetime
                ,TransformationRemaining = d.Transformation == null ? 0f : d.Transformation.DelaySeconds,
                TransformationMaxHpPercent = d.Transformation == null ? 0 : d.Transformation.MaxHpPercent,
                TransformationDamagePercent = d.Transformation == null ? 0 : d.Transformation.DamagePercent,
                TransformationAttackRadius = d.Transformation == null ? 0f : d.Transformation.AttackRadius
            };
        }

        void ApplyOpeningBacklineJumps()
        {
            // Freeze the two original rear targets before either side changes its formation.
            int rear0 = MostRearUnit(0), rear1 = MostRearUnit(1);
            var rearPositions = new[]
            {
                rear0 < 0 ? default : _position.Get(rear0).Now,
                rear1 < 0 ? default : _position.Get(rear1).Now
            };
            var rearRadii = new[]
            {
                rear0 < 0 ? 0f : _unit.Get(rear0).Radius,
                rear1 < 0 ? 0f : _unit.Get(rear1).Radius
            };
            var entities = _units.GetRawEntities();
            for (int index = 0; index < _units.GetEntitiesCount(); index++)
            {
                int entity = entities[index];
                ref var unit = ref _unit.Get(entity);
                if (unit.Hp <= 0 || unit.AbilityKind != BattleUnitAbilityKind.OpeningBacklineJump)
                    continue;
                int enemySide = 1 - _identity.Get(entity).Side;
                int enemy = enemySide == 0 ? rear0 : rear1;
                if (enemy < 0 || !TryFindBacklineJumpPosition(entity, rearPositions[enemySide], rearRadii[enemySide], enemySide, out var position))
                    continue;
                ref var current = ref _position.Get(entity);
                current.Now = position;
                current.Previous = position;
                current.Facing = (_position.Get(enemy).Now - position).Normalized;
            }
        }

        int MostRearUnit(int side)
        {
            int best = -1;
            float rear = float.MinValue;
            int stable = int.MaxValue;
            var entities = _units.GetRawEntities();
            float sign = side == 0 ? -1f : 1f;
            for (int index = 0; index < _units.GetEntitiesCount(); index++)
            {
                int entity = entities[index];
                if (_identity.Get(entity).Side != side || _unit.Get(entity).Hp <= 0)
                    continue;
                float score = sign * _position.Get(entity).Now.Y;
                int id = _identity.Get(entity).Id;
                if (score > rear + EqualTimeEpsilon || (Math.Abs(score - rear) <= EqualTimeEpsilon && id < stable))
                {
                    best = entity;
                    rear = score;
                    stable = id;
                }
            }
            return best;
        }

        bool TryFindBacklineJumpPosition(int jumper, BattleVec rearPosition, float rearRadius, int rearSide, out BattleVec result)
        {
            ref var jumping = ref _unit.Get(jumper);
            float sign = rearSide == 0 ? -1f : 1f;
            float step = 2f * jumping.Radius + Rules.UnitGap;
            var origin = rearPosition + new BattleVec(0, sign * (rearRadius + jumping.Radius + Rules.UnitGap));
            for (int ring = 0; ring <= 4; ring++)
                for (int y = -ring; y <= ring; y++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                            continue;
                        var candidate = origin + new BattleVec(x * step, y * step);
                        if (FitsUnit(jumper, candidate, jumping.Radius))
                        {
                            result = candidate;
                            return true;
                        }
                    }
            result = default;
            return false;
        }

        bool FitsUnit(int ignoredEntity, BattleVec position, float radius)
        {
            if (Math.Abs(position.X) + radius > Rules.HalfWidth || Math.Abs(position.Y) + radius > Rules.HalfHeight)
                return false;
            var entities = _units.GetRawEntities();
            for (int index = 0; index < _units.GetEntitiesCount(); index++)
            {
                int other = entities[index];
                if (other == ignoredEntity || _unit.Get(other).Hp <= 0)
                    continue;
                if ((_position.Get(other).Now - position).Length + EqualTimeEpsilon < radius + _unit.Get(other).Radius)
                    return false;
            }
            return true;
        }


        static int ScaleStat(int value, int bonusPercent)
        {
            long scaled = ((long)value * (100L + bonusPercent) + 99L) / 100L;
            return scaled > int.MaxValue ? int.MaxValue : (int)Math.Max(1L, scaled);
        }

        static int ScalePercent(int value, int percent)
        {
            long scaled = ((long)value * percent + 99L) / 100L;
            return scaled > int.MaxValue ? int.MaxValue : (int)Math.Max(0L, scaled);
        }

        static float ScaleCooldown(float value, int attackSpeedBonusPercent) => value / (1f + attackSpeedBonusPercent / 100f);

        void SpawnProjectile(Unit u, Identity owner, Position op)
        {
            if (EntityCount() + _sim.Commands.Count >= Rules.MaxEntities)
                return;
            int target = FindUnit(u.TargetId);
            if (target < 0)
                return;
            var d = _sim.Definitions.Projectile(u.ProjectileId);
            int e = _world.NewEntity();
            var start = Clamp(op.Now + op.Facing * u.Radius, 0);
            var endpoint = _position.Get(target).Now;
            _identity.Add(e) = new Identity
            {
                Id = _sim.NextId++,
                Side = owner.Side,
                DefinitionId = d.Id
            };
            _position.Add(e) = new Position
            {
                Now = start,
                Previous = start,
                Facing = op.Facing
            };
            _projectile.Add(e) = new Projectile
            {
                Damage = u.Damage,
                Radius = d.Radius,
                ImpactRadius = d.ImpactRadius,
                Speed = d.Speed,
                TargetId = u.TargetId,
                OwnerId = owner.Id,
                DotTotalDamagePercent = u.DotTotalDamagePercent,
                DotPeriod = u.DotPeriod,
                DotTickCount = u.DotTickCount,
                LifeStealPercent = u.LifeStealPercent,
                LastTarget = endpoint,
                RetargetOnTargetLost = d.RetargetOnTargetLost,
                InitialDistance = Math.Max(.0001f, (endpoint - start).Length),
                ZoneId = d.ZoneId
            };
        }

        void SpawnZone(int side, string id, BattleVec p, int sourceDamage = 0)
        {
            if (EntityCount() + _sim.Commands.Count >= Rules.MaxEntities)
                return;
            var d = _sim.Definitions.Zone(id);
            int e = _world.NewEntity();
            _identity.Add(e) = new Identity
            {
                Id = _sim.NextId++,
                Side = side,
                DefinitionId = id
            };
            _position.Add(e) = new Position
            {
                Now = p,
                Previous = p,
                Facing = new BattleVec(0, 0)
            };
            _zone.Add(e) = new Zone
            {
                Radius = d.Radius,
                Damage = d.SourceDamagePercent == 0 ? d.TickDamage : ScalePercent(sourceDamage, d.SourceDamagePercent),
                Period = d.PeriodSeconds,
                Lifetime = d.LifetimeSeconds,
                MoveSpeedMultiplier = d.MoveSpeedMultiplier,
                Age = 0,
                Next = d.PeriodSeconds
            };
            _sim.Emit(BattleEventKind.ZoneCreated, _identity.Get(e), p, 0, d.Radius);
        }

        int CountAlive(int side)
        {
            int c = 0;
            var es = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                var e = es[i];
                if (_identity.Get(e).Side == side && _unit.Get(e).Hp > 0)
                    c++;
            }

            return c;
        }

        int EntityCount()
        {
            return _units.GetEntitiesCount() + _projectiles.GetEntitiesCount() + _zones.GetEntitiesCount();
        }

        void CaptureUnits(List<BattleEntityState> t)
        {
            var es = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var u = ref _unit.Get(e);
                if (u.Hp > 0)
                    AddState(t, e, BattleEntityKind.Unit, u.Hp, u.MaxHp, u.Radius, 0, u.TargetId);
            }
        }

        void CaptureProjectiles(List<BattleEntityState> t)
        {
            var es = _projectiles.GetRawEntities();
            for (int i = 0; i < _projectiles.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var p = ref _projectile.Get(e);
                AddState(t, e, BattleEntityKind.Projectile, 0, 0, p.Radius, Math.Min(1, p.Travel / p.InitialDistance), p.TargetId);
            }
        }

        void CaptureZones(List<BattleEntityState> t)
        {
            var es = _zones.GetRawEntities();
            for (int i = 0; i < _zones.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var z = ref _zone.Get(e);
                AddState(t, e, BattleEntityKind.Zone, 0, 0, z.Radius, Math.Min(1, z.Age / z.Lifetime), 0);
            }
        }

        void AddState(List<BattleEntityState> t, int e, BattleEntityKind k, int hp, int max, float r, float progress, int target)
        {
            ref var id = ref _identity.Get(e);
            ref var p = ref _position.Get(e);
            if (k == BattleEntityKind.Unit)
            {
                ref var unit = ref _unit.Get(e);
                bool shieldActive = unit.ShieldHp > 0 && unit.ShieldExpiry > (double)Tick * Rules.TickSeconds;
                t.Add(new BattleEntityState(id.Id, id.Side, k, id.DefinitionId, p.Now, p.Previous, p.Facing, hp, max, r, progress, target,
                    shieldActive ? unit.ShieldHp : 0, shieldActive ? unit.ShieldMaxHp : 0, unit.FirstHitBlock,
                    unit.Reloading ? 0 : unit.MagazineShots - unit.MagazineFired, unit.MagazineShots,
                    unit.Reloading ? unit.Remaining : 0, unit.MagazineReload, unit.TransformationStage));
            }
            else
                t.Add(new BattleEntityState(id.Id, id.Side, k, id.DefinitionId, p.Now, p.Previous, p.Facing, hp, max, r, progress, target));
        }

        void CleanupNonUnits()
        {
            var ps = _projectiles.GetRawEntities();
            _sim.Delete.Clear();
            for (int i = 0; i < _projectiles.GetEntitiesCount(); i++)
                _sim.Delete.Add(ps[i]);
            var zs = _zones.GetRawEntities();
            for (int i = 0; i < _zones.GetEntitiesCount(); i++)
                _sim.Delete.Add(zs[i]);
            DeleteDeferred();
        }

        void DeleteDeferred()
        {
            for (int i = 0; i < _sim.Delete.Count; i++)
                _world.DelEntity(_sim.Delete[i]);
            _sim.Delete.Clear();
        }

        static bool Finite(BattleVec p)
        {
            return !(float.IsNaN(p.X) || float.IsInfinity(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.Y));
        }

        sealed class TargetSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                var b = (BattleSimulation)((Sim)s.GetShared<Sim>()).Owner;
                b.Target();
            }
        }

        sealed class MovementSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Move();
            }
        }

        sealed class SeparationSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Separate();
            }
        }

        sealed class AttackSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Attack();
            }
        }

        sealed class ProjectileSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Projectiles();
            }
        }

        sealed class ZoneSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Zones();
            }
        }

        sealed class DamageOutcomeSystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Resolve();
            }
        }

        sealed class AbilitySystem : IEcsRunSystem
        {
            public void Run(IEcsSystems s)
            {
                ((BattleSimulation)((Sim)s.GetShared<Sim>()).Owner).Abilities();
            }
        }

        void Abilities()
        {
            var entities = _units.GetRawEntities();
            // A summon starts its lifetime on the next fixed tick, not while this iteration is still spawning it.
            int count = _units.GetEntitiesCount();
            for (int index = 0; index < count; index++)
            {
                int entity = entities[index];
                ref var unit = ref _unit.Get(entity);
                if (unit.Hp <= 0)
                    continue;
                if (unit.TransformationStage == 0 && unit.TransformationRemaining > 0f)
                {
                    unit.TransformationRemaining -= Rules.TickSeconds;
                    if (unit.TransformationRemaining <= EqualTimeEpsilon)
                    {
                        unit.TransformationStage = 1;
                        unit.MaxHp = ScalePercent(unit.InitialMaxHp, unit.TransformationMaxHpPercent);
                        unit.Damage = ScalePercent(unit.InitialDamage, unit.TransformationDamagePercent);
                        unit.Hp = unit.MaxHp;
                        unit.AttackRadius = unit.TransformationAttackRadius;
                    }
                }
                if (unit.Lifetime > 0f)
                {
                    unit.Age += Rules.TickSeconds;
                    if (unit.Age + EqualTimeEpsilon >= unit.Lifetime)
                    {
                        unit.Hp = 0;
                        _sim.Delete.Add(entity);
                        continue;
                    }
                }

                if (unit.ShieldInterval > 0f)
                {
                    unit.ShieldRemaining -= Rules.TickSeconds;
                    while (unit.ShieldRemaining <= EqualTimeEpsilon)
                    {
                        unit.ShieldRemaining += unit.ShieldInterval;
                        ShieldPulse(entity, unit.ShieldRadius, unit.ShieldCapacity, unit.ShieldLifetime);
                    }
                }
                if (unit.AbilityKind != BattleUnitAbilityKind.RepairPulse && unit.AbilityKind != BattleUnitAbilityKind.SpawnUnit)
                    continue;
                unit.AbilityRemaining -= Rules.TickSeconds;
                if (unit.AbilityKind == BattleUnitAbilityKind.RepairPulse)
                {
                    while (unit.AbilityRemaining <= EqualTimeEpsilon)
                    {
                        unit.AbilityRemaining += unit.AbilityInterval;
                        RepairPulse(entity, unit.AbilityRadius, unit.AbilityAmount);
                    }
                }
                else if (unit.AbilityRemaining <= EqualTimeEpsilon)
                {
                    unit.AbilityRemaining += unit.AbilityInterval;
                    // Copy before NewEntity: EcsLite pool storage may resize and invalidate this ref.
                    float radius = unit.AbilityRadius;
                    float lifetime = unit.AbilitySpawnLifetime;
                    string unitId = unit.AbilitySpawnUnitId;
                    int parentDamage = unit.Damage;
                    int damagePercent = unit.AbilitySpawnDamagePercent;
                    SpawnAbilityUnit(entity, unitId, radius, lifetime, parentDamage, damagePercent);
                }
            }
        }

        void ShieldPulse(int source, float radius, int capacity, float lifetime)
        {
            int side = _identity.Get(source).Side;
            float expiry = (float)(NowAt(0f) + lifetime);
            var position = _position.Get(source).Now;
            var entities = _units.GetRawEntities();
            for (int index = 0; index < _units.GetEntitiesCount(); index++)
            {
                int entity = entities[index];
                ref var target = ref _unit.Get(entity);
                if (target.Hp <= 0 || _identity.Get(entity).Side != side || (_position.Get(entity).Now - position).Length > radius + target.Radius)
                    continue;
                if (target.ShieldExpiry <= NowAt(0f))
                    target.ShieldHp = 0;
                if (capacity >= target.ShieldHp)
                    target.ShieldMaxHp = capacity;
                target.ShieldHp = Math.Max(target.ShieldHp, capacity);
                target.ShieldExpiry = expiry;
            }
        }

        void RepairPulse(int source, float radius, int amount)
        {
            int side = _identity.Get(source).Side;
            int best = -1;
            float lowestFraction = float.MaxValue;
            int stable = int.MaxValue;
            var position = _position.Get(source).Now;
            var entities = _units.GetRawEntities();
            for (int index = 0; index < _units.GetEntitiesCount(); index++)
            {
                int entity = entities[index];
                if (entity == source)
                    continue;
                ref var candidate = ref _unit.Get(entity);
                if (candidate.Hp <= 0 || candidate.Hp >= candidate.MaxHp || _identity.Get(entity).Side != side)
                    continue;
                if ((_position.Get(entity).Now - position).Length > radius + candidate.Radius)
                    continue;
                float fraction = candidate.Hp / (float)candidate.MaxHp;
                int id = _identity.Get(entity).Id;
                if (fraction < lowestFraction - EqualTimeEpsilon || (Math.Abs(fraction - lowestFraction) <= EqualTimeEpsilon && id < stable))
                {
                    best = entity;
                    lowestFraction = fraction;
                    stable = id;
                }
            }
            if (best >= 0)
            {
                ref var target = ref _unit.Get(best);
                target.Hp = (int)Math.Min(target.MaxHp, (long)target.Hp + amount);
            }
        }

        void SpawnAbilityUnit(int parent, string unitId, float radius, float lifetime, int parentDamage, int damagePercent)
        {
            if (EntityCount() + _sim.Commands.Count >= Rules.MaxEntities)
                return;
            var definition = _sim.Definitions.Unit(unitId);
            if (!TryFindSpawnPosition(parent, definition.Radius, radius, out var position))
                return;
            int side = _identity.Get(parent).Side;
            var facing = _position.Get(parent).Facing;
            if (facing.LengthSquared <= EqualTimeEpsilon)
                facing = new BattleVec(0, side == 0 ? 1 : -1);
            SpawnUnit(side, definition, position, facing, 0, 0, 0, lifetime,
                damagePercent == 0 ? (int?)null : ScalePercent(parentDamage, damagePercent));
        }

        bool TryFindSpawnPosition(int parent, float childRadius, float radius, out BattleVec result)
        {
            var origin = _position.Get(parent).Now;
            float step = 2f * childRadius + Rules.UnitGap;
            float rearSign = _identity.Get(parent).Side == 0 ? 1f : -1f;
            int maxRing = Math.Max(1, (int)Math.Ceiling(radius / step));
            for (int ring = 1; ring <= maxRing; ring++)
                for (int y = -ring; y <= ring; y++)
                    for (int x = -ring; x <= ring; x++)
                    {
                        if (Math.Max(Math.Abs(x), Math.Abs(y)) != ring)
                            continue;
                        var candidate = origin + new BattleVec(x * step, y * step * rearSign);
                        if ((candidate - origin).Length > radius + EqualTimeEpsilon)
                            continue;
                        if (FitsUnit(-1, candidate, childRadius))
                        {
                            result = candidate;
                            return true;
                        }
                    }
            result = default;
            return false;
        }

        void Target()
        {
            var es = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var u = ref _unit.Get(e);
                if (u.Hp <= 0)
                    continue;
                int best = 0;
                float ds = float.MaxValue;
                ref var me = ref _position.Get(e);
                int side = _identity.Get(e).Side;
                for (int j = 0; j < _units.GetEntitiesCount(); j++)
                {
                    int x = es[j];
                    ref var ou = ref _unit.Get(x);
                    if (ou.Hp <= 0 || _identity.Get(x).Side == side)
                        continue;
                    var d = _position.Get(x).Now - me.Now;
                    float q = d.LengthSquared;
                    int id = _identity.Get(x).Id;
                    if (q < ds || (Math.Abs(q - ds) < EqualTimeEpsilon && id < best))
                    {
                        ds = q;
                        best = id;
                    }
                }

                u.TargetId = best;
            }
        }

        int FindUnit(int stable)
        {
            var es = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
                if (_identity.Get(es[i]).Id == stable)
                    return es[i];
            return -1;
        }

        void Move()
        {
            var es = _units.GetRawEntities();
            float dt = Rules.TickSeconds;
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var u = ref _unit.Get(e);
                ref var p = ref _position.Get(e);
                p.Previous = p.Now;
                if (u.Hp <= 0 || u.Speed <= 0)
                    continue;
                int x = FindUnit(u.TargetId);
                if (x < 0)
                    continue;
                ref var tp = ref _position.Get(x);
                var d = tp.Now - p.Now;
                float len = d.Length;
                float stop = u.Range + u.Radius + _unit.Get(x).Radius;
                if (len > stop)
                {
                    var dir = d.Normalized;
                    var steer = new BattleVec(0, 0);
                    int side = _identity.Get(e).Side;
                    for (int j = 0; j < _units.GetEntitiesCount(); j++)
                    {
                        int ally = es[j];
                        if (ally == e || _identity.Get(ally).Side != side || _unit.Get(ally).Hp <= 0)
                            continue;
                        var gap = _position.Get(ally).Now - p.Now;
                        float near = u.Radius + _unit.Get(ally).Radius + Rules.UnitGap;
                        if (gap.LengthSquared < near * near && BattleVec.Dot(gap, dir) > 0)
                        {
                            float cross = dir.X * gap.Y - dir.Y * gap.X;
                            float sign = cross == 0 ? ((_identity.Get(ally).Id < _identity.Get(e).Id) ? 1 : -1) : (cross > 0 ? -1 : 1);
                            steer = steer + new BattleVec(-dir.Y * sign, dir.X * sign) * .35f;
                        }
                    }

                    dir = (dir + steer).Normalized;
                    p.Facing = dir;
                    p.Now = Clamp(p.Now + dir * Math.Min(u.Speed * HostileZoneSpeedMultiplier(e) * dt, len - stop), u.Radius);
                }
            }
        }

        float HostileZoneSpeedMultiplier(int unit)
        {
            int side = _identity.Get(unit).Side;
            float multiplier = 1f;
            var position = _position.Get(unit).Now;
            var zones = _zones.GetRawEntities();
            for (int i = 0; i < _zones.GetEntitiesCount(); i++)
            {
                int zone = zones[i];
                ref var value = ref _zone.Get(zone);
                if (_identity.Get(zone).Side == side || value.Age >= value.Lifetime || value.MoveSpeedMultiplier >= multiplier)
                    continue;
                if ((_position.Get(zone).Now - position).Length <= value.Radius + _unit.Get(unit).Radius)
                    multiplier = value.MoveSpeedMultiplier;
            }
            return multiplier;
        }

        void Separate()
        {
            var entities = _units.GetRawEntities();
            int count = _units.GetEntitiesCount();
            float limit = Rules.SeparationSpeed * Rules.TickSeconds;
            Array.Clear(_sim.Corrections, 0, count);
            Array.Clear(_sim.SeparationSpent, 0, count);
            for (int pass = 0; pass < Rules.SeparationIterations; pass++)
            {
                Array.Clear(_sim.Corrections, 0, count);
                for (int i = 0; i < count; i++)
                {
                    int a = entities[i];
                    ref var left = ref _unit.Get(a);
                    if (left.Hp <= 0)
                        continue;
                    for (int j = i + 1; j < count; j++)
                    {
                        int b = entities[j];
                        ref var right = ref _unit.Get(b);
                        if (right.Hp <= 0)
                            continue;
                        var delta = _position.Get(b).Now - _position.Get(a).Now;
                        float distance = delta.Length, required = left.Radius + right.Radius;
                        if (distance >= required)
                            continue;
                        var direction = distance > .0001f ? delta / distance : new BattleVec((i & 1) == 0 ? 1 : -1, 0);
                        float leftWeight = left.Speed <= 0 ? 0 : 1 / left.Mass;
                        float rightWeight = right.Speed <= 0 ? 0 : 1 / right.Mass;
                        float sum = leftWeight + rightWeight;
                        if (sum <= 0)
                            continue;
                        float overlap = required - distance;
                        _sim.Corrections[i] = _sim.Corrections[i] - direction * (overlap * leftWeight / sum);
                        _sim.Corrections[j] = _sim.Corrections[j] + direction * (overlap * rightWeight / sum);
                    }
                }

                for (int i = 0; i < count; i++)
                {
                    int entity = entities[i];
                    ref var unit = ref _unit.Get(entity);
                    if (unit.Hp <= 0)
                        continue;
                    var correction = _sim.Corrections[i];
                    float length = correction.Length;
                    float remaining = limit - _sim.SeparationSpent[i];
                    if (remaining <= 0 || length <= 0)
                        continue;
                    if (length > remaining)
                        correction = correction * (remaining / length);
                    _sim.SeparationSpent[i] += correction.Length;
                    _position.Get(entity).Now = Clamp(_position.Get(entity).Now + correction, unit.Radius);
                }
            }
        }

        void Attack()
        {
            ConsumeCommands();
            var es = _units.GetRawEntities();
            for (int i = 0; i < _units.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var u = ref _unit.Get(e);
                if (u.Hp <= 0)
                    continue;
                if (u.Attack == BattleAttackKind.Passive)
                    continue;
                u.Remaining = Math.Max(0, u.Remaining - Rules.TickSeconds);
                if (u.Remaining <= 0)
                    u.Reloading = false;
                if (u.Remaining > 0 || u.TargetId == 0)
                    continue;
                int target = FindUnit(u.TargetId);
                if (target < 0)
                    continue;
                var p = _position.Get(e);
                var tp = _position.Get(target);
                if ((tp.Now - p.Now).Length > u.Range + u.Radius + _unit.Get(target).Radius + (u.Attack == BattleAttackKind.ContactExplosion ? EqualTimeEpsilon : 0f))
                    continue;
                u.Remaining = u.Cooldown;
                p.Facing = (tp.Now - p.Now).Normalized;
                _position.Get(e).Facing = p.Facing;
                var id = _identity.Get(e);
                _sim.Emit(BattleEventKind.Shot, id, p.Now, 0, 0);
                _sim.Shots++;
                if (u.Attack == BattleAttackKind.Projectile)
                    SpawnProjectile(u, id, p);
                else if (u.Attack == BattleAttackKind.Melee)
                    _sim.Hits.Add(new Hit { Time = 0, Side = id.Side, Source = id.Id, DotSourceId = id.Id, LifeStealSourceId = id.Id, LifeStealPercent = u.LifeStealPercent, Position = tp.Now, Radius = u.AttackRadius, Damage = u.Damage, DirectTarget = u.TargetId, DotTotalDamagePercent = u.DotTotalDamagePercent, DotPeriod = u.DotPeriod, DotTickCount = u.DotTickCount });
                else
                    _sim.Hits.Add(new Hit { Time = 0, Side = id.Side, Source = id.Id, Position = p.Now, Radius = u.Range + u.Radius, Damage = u.Damage, SelfId = id.Id, ZoneId = u.ContactZoneId, ZoneSourceDamage = u.Damage });
                if (u.MagazineShots > 0)
                {
                    u.MagazineFired++;
                    if (u.MagazineFired >= u.MagazineShots)
                    {
                        u.MagazineFired = 0;
                        u.Remaining = u.MagazineReload;
                        u.Reloading = true;
                    }
                }
            }
        }

        void ConsumeCommands()
        {
            for (int i = _sim.Commands.Count - 1; i >= 0; i--)
                if (_sim.Commands[i].ApplyTick == Tick)
                {
                    var c = _sim.Commands[i];
                    _sim.Commands.RemoveAt(i);
                    SpawnZone(c.Side, c.ZoneId, c.Position);
                }
        }

        void Projectiles()
        {
            var es = _projectiles.GetRawEntities();
            for (int i = 0; i < _projectiles.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var pr = ref _projectile.Get(e);
                ref var p = ref _position.Get(e);
                p.Previous = p.Now;
                int target = FindUnit(pr.TargetId);
                if (target >= 0 && _unit.Get(target).Hp <= 0) target = -1;
                if (target < 0 && pr.RetargetOnTargetLost)
                {
                    // Projectiles own their damage and team; the carrier is not required after launch.
                    float bestDistance = float.MaxValue;
                    int bestId = int.MaxValue;
                    var units = _units.GetRawEntities();
                    for (int j = 0; j < _units.GetEntitiesCount(); j++)
                    {
                        int candidate = units[j];
                        var identity = _identity.Get(candidate);
                        if (_unit.Get(candidate).Hp <= 0 || identity.Side == _identity.Get(e).Side) continue;
                        float distance = (_position.Get(candidate).Now - p.Now).LengthSquared;
                        if (distance < bestDistance || (distance == bestDistance && identity.Id < bestId))
                        {
                            target = candidate;
                            bestDistance = distance;
                            bestId = identity.Id;
                        }
                    }
                    if (target >= 0) pr.TargetId = bestId;
                }
                if (target >= 0)
                    pr.LastTarget = _position.Get(target).Now;
                var d = pr.LastTarget - p.Now;
                float len = d.Length, step = pr.Speed * Rules.TickSeconds;
                if (len <= step)
                {
                    float time = step > 0 ? Math.Max(0, Math.Min(1, len / step)) : 0;
                    pr.Travel += len;
                    _sim.Hits.Add(new Hit { Time = time, Side = _identity.Get(e).Side, Source = _identity.Get(e).Id, DotSourceId = pr.OwnerId, LifeStealSourceId = pr.OwnerId, LifeStealPercent = pr.LifeStealPercent, Position = pr.LastTarget, Radius = pr.ImpactRadius, Damage = pr.Damage, ZoneId = pr.ZoneId, ZoneSourceDamage = pr.Damage, DirectTarget = pr.TargetId, DotTotalDamagePercent = pr.DotTotalDamagePercent, DotPeriod = pr.DotPeriod, DotTickCount = pr.DotTickCount });
                    _sim.Delete.Add(e);
                }
                else
                {
                    p.Facing = d / len;
                    p.Now = p.Now + p.Facing * step;
                    pr.Travel += step;
                }
            }
        }

        void Zones()
        {
            var es = _zones.GetRawEntities();
            for (int i = 0; i < _zones.GetEntitiesCount(); i++)
            {
                int e = es[i];
                ref var z = ref _zone.Get(e);
                float prior = z.Age;
                z.Age += Rules.TickSeconds;
                while (z.Age + EqualTimeEpsilon >= z.Next && z.Next <= z.Lifetime + EqualTimeEpsilon)
                {
                    float at = Math.Max(0, Math.Min(1, (z.Next - prior) / Rules.TickSeconds));
                    z.Next += z.Period;
                    _sim.Hits.Add(new Hit { Time = at, Side = _identity.Get(e).Side, Source = _identity.Get(e).Id, Position = _position.Get(e).Now, Radius = z.Radius, Damage = z.Damage, Zone = true });
                }

                if (z.Age >= z.Lifetime)
                    _sim.Delete.Add(e);
            }
        }

        void Resolve()
        {
            try
            {
                ScheduleDotTicks(0f);
                int i = 0;
                while (i < _sim.Hits.Count && _sim.Outcome == BattleOutcome.Running)
                {
                    _sim.Hits.Sort(Hit.Compare);
                    int end = i + 1;
                    while (end < _sim.Hits.Count && _sim.Hits[end].Time == _sim.Hits[i].Time)
                        end++;
                    ApplyBatch(i, end);
                    ScheduleDotTicks(_sim.Hits[i].Time);
                    i = end;
                }
                if (_sim.Outcome == BattleOutcome.Running)
                    ResolveOutcomeIfNeeded(0f, -1, -1);
            }
            finally
            {
                _sim.EventTimeFraction = 0;
                _sim.Hits.Clear();
                DeleteDeferred();
            }
        }

        void ApplyBatch(int from, int to)
        {
            _sim.Damage.Clear();
            _sim.BatchHp.Clear();
            _sim.LifeSteal.Clear();
            float groupTime = _sim.Hits[from].Time;
            for (int h = from; h < to; h++)
            {
                var hit = _sim.Hits[h];
                _sim.EventTimeFraction = hit.Time;
                if (hit.Dot)
                {
                    ApplyDotTick(ref hit);
                    _sim.Hits[h] = hit;
                    continue;
                }
                if (!hit.Zone)
                {
                    _sim.Impacts++;
                    _sim.Emit(BattleEventKind.Impact, new Identity { Id = hit.Source, Side = hit.Side }, hit.Position, hit.Damage, hit.Radius);
                }

                if (hit.Zone)
                {
                    _sim.ZoneTicks++;
                    _sim.Emit(BattleEventKind.ZoneTick, new Identity { Id = hit.Source, Side = hit.Side }, hit.Position, hit.Damage, hit.Radius);
                }

                if (hit.ZoneId != null && hit.ZoneId.Length > 0)
                    SpawnZone(hit.Side, hit.ZoneId, hit.Position, hit.ZoneSourceDamage);
                var es = _units.GetRawEntities();
                for (int i = 0; i < _units.GetEntitiesCount(); i++)
                {
                    int e = es[i];
                    ref var u = ref _unit.Get(e);
                    if (u.Hp <= 0)
                        continue;
                    ref var id = ref _identity.Get(e);
                    if (id.Side == hit.Side && id.Id != hit.SelfId)
                        continue;
                    if (hit.DirectTarget != 0 && id.Id != hit.DirectTarget && hit.Radius <= 0)
                        continue;
                    if ((_position.Get(e).Now - hit.Position).Length <= hit.Radius + u.Radius)
                    {
                        int amount = id.Id == hit.SelfId ? int.MaxValue : hit.Damage;
                        bool blocked = QueueDamage(e, ref hit, amount);
                        if (hit.DotTotalDamagePercent > 0 && id.Id != hit.SelfId && !blocked)
                            RefreshDot(hit, id.Id);
                    }
                }
            }

            _sim.EventTimeFraction = groupTime;
            foreach (var pair in _sim.Damage)
            {
                int e = pair.Key;
                ref var u = ref _unit.Get(e);
                if (u.Hp <= 0)
                    continue;
                u.Hp = _sim.BatchHp[e];
                var id = _identity.Get(e);
                _sim.Emit(BattleEventKind.Damage, id, _position.Get(e).Now, pair.Value, 0);
                if (u.Hp == 0)
                {
                    _sim.Deaths++;
                    _sim.Emit(BattleEventKind.Death, id, _position.Get(e).Now, 0, 0);
                }
            }

            foreach (var pair in _sim.LifeSteal)
            {
                int source = FindUnit(pair.Key);
                if (source < 0 || _unit.Get(source).Hp <= 0)
                    continue;
                ref var sourceUnit = ref _unit.Get(source);
                long numerator = _sim.LifeStealRemainder.TryGetValue(pair.Key, out var carry) ? carry : 0L;
                numerator += pair.Value;
                int heal = (int)(numerator / 100L);
                _sim.LifeStealRemainder[pair.Key] = numerator % 100L;
                if (heal > 0)
                    sourceUnit.Hp = (int)Math.Min(sourceUnit.MaxHp, (long)sourceUnit.Hp + heal);
            }

            ResolveOutcomeIfNeeded(groupTime, from, to);
        }

        bool QueueDamage(int entity, ref Hit hit, int requested)
        {
            ref var target = ref _unit.Get(entity);
            int hp = _sim.BatchHp.TryGetValue(entity, out var pending) ? pending : target.Hp;
            bool self = _identity.Get(entity).Id == hit.SelfId;
            bool directHostile = !self && !hit.Dot && !hit.Zone && hit.Damage > 0 && _identity.Get(entity).Side != hit.Side;
            if (directHostile && target.FirstHitBlock != 0)
            {
                target.FirstHitBlock = 0;
                return true;
            }
            if (!self && target.ShieldExpiry <= NowAt(hit.Time))
                target.ShieldHp = 0;
            int afterShield = requested;
            if (!self && target.ShieldHp > 0)
            {
                int absorbed = Math.Min(target.ShieldHp, afterShield);
                target.ShieldHp -= absorbed;
                afterShield -= absorbed;
            }
            int actual = Math.Min(hp, afterShield);
            _sim.BatchHp[entity] = Math.Max(0, hp - afterShield);
            if (actual > 0)
            {
                _sim.Damage.TryGetValue(entity, out var total);
                _sim.Damage[entity] = (int)Math.Min(int.MaxValue, (long)total + actual);
                if (directHostile && hit.LifeStealPercent > 0)
                {
                    _sim.LifeSteal.TryGetValue(hit.LifeStealSourceId, out var stole);
                    _sim.LifeSteal[hit.LifeStealSourceId] = stole + (long)actual * hit.LifeStealPercent;
                }
            }
            return false;
        }

        double NowAt(float fraction) => ((double)Tick - 1d + fraction) * Rules.TickSeconds;

        void RefreshDot(Hit hit, int targetId)
        {
            double now = NowAt(hit.Time);
            DotStatus status = null;
            for (int i = 0; i < _sim.Dots.Count; i++)
                if (_sim.Dots[i].SourceId == hit.DotSourceId && _sim.Dots[i].TargetId == targetId)
                {
                    status = _sim.Dots[i];
                    break;
                }
            if (status == null)
            {
                status = new DotStatus { SourceId = hit.DotSourceId, TargetId = targetId, Side = hit.Side, NextTime = now + hit.DotPeriod };
                _sim.Dots.Add(status);
            }
            status.DamagePercentNumerator = (long)hit.Damage * hit.DotTotalDamagePercent;
            status.Period = hit.DotPeriod;
            status.TickCount = hit.DotTickCount;
            status.MaxTickOrdinal = status.ExecutedTicks + hit.DotTickCount;
            status.Expiry = now + hit.DotPeriod * hit.DotTickCount;
        }

        void ScheduleDotTicks(float fromFraction)
        {
            double start = NowAt(fromFraction);
            double end = NowAt(1f);
            for (int i = _sim.Dots.Count - 1; i >= 0; i--)
            {
                var status = _sim.Dots[i];
                int target = FindUnit(status.TargetId);
                if (target < 0 || _unit.Get(target).Hp <= 0 || status.Expiry < start)
                {
                    _sim.Dots.RemoveAt(i);
                    continue;
                }
                while (status.ScheduledTicks < status.MaxTickOrdinal && status.NextTime <= end && status.NextTime <= status.Expiry)
                {
                    status.ScheduledTicks++;
                    float time = (float)((status.NextTime - NowAt(0f)) / Rules.TickSeconds);
                    _sim.Hits.Add(new Hit { Time = time, Side = status.Side, Source = status.SourceId, DirectTarget = status.TargetId, Dot = true, DotOrdinal = status.ScheduledTicks });
                    status.NextTime += status.Period;
                }
            }
        }

        void ApplyDotTick(ref Hit hit)
        {
            DotStatus status = null;
            for (int i = 0; i < _sim.Dots.Count; i++)
                if (_sim.Dots[i].SourceId == hit.Source && _sim.Dots[i].TargetId == hit.DirectTarget)
                {
                    status = _sim.Dots[i];
                    break;
                }
            if (status == null || hit.DotOrdinal > status.MaxTickOrdinal || NowAt(hit.Time) > status.Expiry)
                return;
            int target = FindUnit(status.TargetId);
            if (target < 0 || _unit.Get(target).Hp <= 0 || _identity.Get(target).Side == status.Side)
                return;
            long denominator = 100L * status.TickCount;
            status.DamageRemainderNumerator += status.DamagePercentNumerator;
            long whole = status.DamageRemainderNumerator / denominator;
            status.DamageRemainderNumerator %= denominator;
            int amount = whole >= int.MaxValue ? int.MaxValue : (int)whole;
            status.ExecutedTicks++;
            if (amount <= 0)
                return;
            hit.Damage = amount;
            hit.Position = _position.Get(target).Now;
            QueueDamage(target, ref hit, amount);
        }

        void ResolveOutcomeIfNeeded(float groupTime, int from, int to)
        {
            int a = CountAlive(0), b = CountAlive(1);
            if (a == 0 || b == 0)
            {
                _sim.EventTimeFraction = groupTime;
                if (a == 0 && b == 0)
                {
                    _sim.ResolutionUsedRandomTieBreak = true;
                    _sim.TieBreakSeed = _sim.Rng;
                    _sim.Outcome = _sim.NextRandom() < .5f ? BattleOutcome.Side0Won : BattleOutcome.Side1Won;
                }
                else
                    _sim.Outcome = a == 0 ? BattleOutcome.Side1Won : BattleOutcome.Side0Won;
                if (from >= 0)
                {
                    var terminalHits = _sim.TerminalHits ?? (_sim.TerminalHits = new List<BattleResolutionHit>(to - from));
                    for (int h = from; h < to; h++)
                    {
                        var hit = _sim.Hits[h];
                        terminalHits.Add(new BattleResolutionHit(_sim.Tick, hit.Time, hit.Source, hit.Side, hit.Damage, hit.SelfId, hit.DirectTarget, hit.Position, hit.Radius, hit.Zone, groupTime));
                    }
                }

                _sim.Emit(BattleEventKind.Outcome, new Identity { Id = 0, Side = -1 }, new BattleVec(0, 0), 0, 0);
            }
        }

        BattleVec Clamp(BattleVec p, float r)
        {
            float x = Math.Max(-Rules.HalfWidth + r, Math.Min(Rules.HalfWidth - r, p.X));
            float y = Math.Max(-Rules.HalfHeight + r, Math.Min(Rules.HalfHeight - r, p.Y));
            return new BattleVec(x, y);
        }

        struct Identity
        {
            public int Id, Side;
            public string DefinitionId;
        }

        struct Position
        {
            public BattleVec Now, Previous, Facing;
        }

        struct Unit
        {
            public int Hp, MaxHp, Damage, TargetId, InitialMaxHp, InitialDamage, TransformationStage, TransformationMaxHpPercent, TransformationDamagePercent;
            public float Radius, Mass, Speed, Range, Cooldown, Remaining, Lifetime, Age, TransformationRemaining, TransformationAttackRadius, AttackRadius;
            public BattleAttackKind Attack;
            public BattleUnitAbilityKind AbilityKind;
            public float AbilityInterval, AbilityRadius, AbilityRemaining, AbilitySpawnLifetime;
            public int AbilityAmount, AbilitySpawnDamagePercent;
            public string ProjectileId, AbilitySpawnUnitId, ContactZoneId;
            public int DotTotalDamagePercent, DotTickCount;
            public float DotPeriod, ShieldInterval, ShieldRadius, ShieldLifetime, ShieldRemaining, ShieldExpiry, MagazineReload;
            public int ShieldCapacity, ShieldHp, ShieldMaxHp, FirstHitBlock, MagazineShots, MagazineFired, LifeStealPercent;
            public bool Reloading;
        }

        struct Projectile
        {
            public bool RetargetOnTargetLost;
            public int Damage, TargetId, OwnerId, DotTotalDamagePercent, DotTickCount, LifeStealPercent;
            public float Radius, ImpactRadius, Speed, Travel, InitialDistance;
            public float DotPeriod;
            public BattleVec LastTarget;
            public string ZoneId;
        }

        struct Zone
        {
            public int Damage;
            public float Radius, Period, Lifetime, Age, Next, MoveSpeedMultiplier;
        }

        struct Hit
        {
            public float Time;
            public int Side, Source, DotSourceId, LifeStealSourceId, LifeStealPercent, SelfId, Damage, DirectTarget, ZoneSourceDamage, DotTotalDamagePercent, DotTickCount, DotOrdinal;
            public BattleVec Position;
            public float Radius, DotPeriod;
            public bool Zone, Dot;
            public string ZoneId;
            public static int Compare(Hit a, Hit b)
            {
                int c = a.Time.CompareTo(b.Time);
                return c != 0 ? c : a.Source.CompareTo(b.Source);
            }
        }

        sealed class DotStatus
        {
            public int SourceId, TargetId, Side, TickCount, ScheduledTicks, ExecutedTicks, MaxTickOrdinal;
            public long DamagePercentNumerator, DamageRemainderNumerator;
            public double Period, NextTime, Expiry;
        }

        sealed class Sim
        {
            public readonly BattleDefinitions Definitions;
            public readonly BattleRules Rules;
            public readonly BattleScenarioDefinition Scenario;
            public readonly List<BattleEvent> Events = new List<BattleEvent>();
            public readonly List<Hit> Hits = new List<Hit>();
            public readonly List<DotStatus> Dots = new List<DotStatus>();
            public readonly List<int> Delete = new List<int>();
            public readonly Dictionary<int, int> Damage = new Dictionary<int, int>();
            public readonly Dictionary<int, int> BatchHp = new Dictionary<int, int>();
            public readonly Dictionary<int, long> LifeSteal = new Dictionary<int, long>();
            public readonly Dictionary<int, long> LifeStealRemainder = new Dictionary<int, long>();
            public readonly List<BattleZoneCommand> Commands = new List<BattleZoneCommand>();
            public List<BattleResolutionHit> TerminalHits;
            public readonly long[] LastCommand =
            {
                0,
                0
            };
            public readonly BattleVec[] Corrections;
            public readonly float[] SeparationSpent;
            public BattleSimulation Owner;
            public long Tick, EventSeq;
            public float EventTimeFraction;
            public int NextId = 1, Shots, Impacts, ZoneTicks, Deaths;
            public uint Rng;
            public BattleOutcome Outcome = BattleOutcome.Running;
            public bool ResolutionUsedRandomTieBreak;
            public uint TieBreakSeed;
            public Sim(BattleDefinitions d, BattleRules r, BattleScenarioDefinition s)
            {
                Definitions = d;
                Rules = r;
                Scenario = s;
                Rng = s.Seed == 0 ? 1 : s.Seed;
                Corrections = new BattleVec[r.MaxEntities];
                SeparationSpent = new float[r.MaxEntities];
            }

            public float NextRandom()
            {
                Rng ^= Rng << 13;
                Rng ^= Rng >> 17;
                Rng ^= Rng << 5;
                return (Rng & 0x00ffffff) / 16777216f;
            }

            public void Emit(BattleEventKind kind, Identity id, BattleVec p, int amount, float radius)
            {
                Events.Add(new BattleEvent(++EventSeq, Tick, kind, id.Id, id.Side, p, amount, radius, EventTimeFraction));
            }
        }
    }
}
