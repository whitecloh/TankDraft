using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace TankDraft.Contracts.Battle
{
    public readonly struct BattleVec
    {
        public readonly float X, Y;
        public BattleVec(float x, float y)
        {
            X = x;
            Y = y;
        }

        public float Length => (float)Math.Sqrt(X * X + Y * Y);
        public float LengthSquared => X * X + Y * Y;
        public BattleVec Normalized => Length > 0.00001f ? this / Length : new BattleVec(0, 0);

        public static BattleVec operator +(BattleVec a, BattleVec b) => new BattleVec(a.X + b.X, a.Y + b.Y);
        public static BattleVec operator -(BattleVec a, BattleVec b) => new BattleVec(a.X - b.X, a.Y - b.Y);
        public static BattleVec operator *(BattleVec a, float f) => new BattleVec(a.X * f, a.Y * f);
        public static BattleVec operator /(BattleVec a, float f) => new BattleVec(a.X / f, a.Y / f);
        public static float Dot(BattleVec a, BattleVec b) => a.X * b.X + a.Y * b.Y;
    }

    public enum BattleAttackKind
    {
        ContactExplosion = 0,
        Projectile = 1,
        Passive = 2,
        Melee = 3
    }

    public enum BattleUnitAbilityKind
    {
        None = 0,
        OpeningBacklineJump = 1,
        RepairPulse = 2,
        SpawnUnit = 3
    }

    public enum BattleEntityKind
    {
        Unit,
        Projectile,
        Zone
    }

    public enum BattleOutcome
    {
        Running,
        Side0Won,
        Side1Won,
        ReviewRequired
    }

    public enum BattleEventKind
    {
        Shot,
        Impact,
        Damage,
        Death,
        ZoneCreated,
        ZoneTick,
        Outcome
    }

    public static class BattleGuard
    {
        public static void Id(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException(name + " is required.");
        }

        public static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0)
                throw new ArgumentOutOfRangeException(name);
        }

        public static void Nonnegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0)
                throw new ArgumentOutOfRangeException(name);
        }

        public static void Side(int side)
        {
            if (side != 0 && side != 1)
                throw new ArgumentOutOfRangeException(nameof(side));
        }
    }

    public sealed class BattleUnitDefinition
    {
        public string Id { get; }
        public FormationRow Row { get; }
        public int FormationPriority { get; }
        public BattleAttackKind Attack { get; }
        public int MaxHp { get; }
        public int Damage { get; }
        public float MoveSpeed { get; }
        public float Radius { get; }
        public float Mass { get; }
        public float Range { get; }
        public float CooldownSeconds { get; }
        public string ProjectileId { get; }
        public BattleUnitAbilityDefinition Ability { get; }
        public string ContactZoneId { get; }
        public BattleDamageOverTimeDefinition DamageOverTime { get; }
        public BattleShieldDefinition Shield { get; }
        public bool BlocksFirstHit { get; }
        public BattleMagazineDefinition Magazine { get; }
        public int LifeStealPercent { get; }
        public BattleTransformationDefinition Transformation { get; }

        public BattleUnitDefinition(string id, FormationRow row, BattleAttackKind attack, int maxHp, int damage, float moveSpeed, float radius, float mass, float range, float cooldownSeconds, string projectileId, BattleUnitAbilityDefinition ability = null, string contactZoneId = "", BattleDamageOverTimeDefinition damageOverTime = null, BattleShieldDefinition shield = null, bool blocksFirstHit = false, BattleMagazineDefinition magazine = null, int lifeStealPercent = 0, int formationPriority = 0, BattleTransformationDefinition transformation = null)
        {
            BattleGuard.Id(id, nameof(id));
            if (!Enum.IsDefined(typeof(FormationRow), row) || !Enum.IsDefined(typeof(BattleAttackKind), attack))
                throw new ArgumentException("Unknown battle role/attack.");
            if (maxHp <= 0 || damage < 0 || (damage == 0 && attack != BattleAttackKind.Passive))
                throw new ArgumentOutOfRangeException(nameof(maxHp));
            BattleGuard.Nonnegative(moveSpeed, nameof(moveSpeed));
            BattleGuard.Positive(radius, nameof(radius));
            BattleGuard.Positive(mass, nameof(mass));
            BattleGuard.Nonnegative(range, nameof(range));
            BattleGuard.Positive(cooldownSeconds, nameof(cooldownSeconds));
            if (attack == BattleAttackKind.Projectile)
                BattleGuard.Id(projectileId, nameof(projectileId));
            contactZoneId = contactZoneId ?? "";
            if (contactZoneId.Length > 0 && attack != BattleAttackKind.ContactExplosion)
                throw new ArgumentException("Contact zone requires contact explosion.");
            if (damageOverTime != null && attack != BattleAttackKind.Projectile && attack != BattleAttackKind.Melee)
                throw new ArgumentException("Damage over time requires projectile or melee attack.");
            if (lifeStealPercent < 0 || lifeStealPercent > 100)
                throw new ArgumentOutOfRangeException(nameof(lifeStealPercent));
            if ((magazine != null || lifeStealPercent != 0) && attack != BattleAttackKind.Projectile && attack != BattleAttackKind.Melee)
                throw new ArgumentException("Magazine and lifesteal require projectile or melee attack.");
            if (transformation != null && attack != BattleAttackKind.Melee)
                throw new ArgumentException("Transformation requires melee attack.");
            Id = id;
            Row = row;
            FormationPriority = formationPriority;
            Attack = attack;
            MaxHp = maxHp;
            Damage = damage;
            MoveSpeed = moveSpeed;
            Radius = radius;
            Mass = mass;
            Range = range;
            CooldownSeconds = cooldownSeconds;
            ProjectileId = projectileId ?? "";
            Ability = ability;
            ContactZoneId = contactZoneId;
            DamageOverTime = damageOverTime;
            Shield = shield;
            BlocksFirstHit = blocksFirstHit;
            Magazine = magazine;
            LifeStealPercent = lifeStealPercent;
            Transformation = transformation;
        }

    }

    public sealed class BattleTransformationDefinition
    {
        public float DelaySeconds { get; }
        public int MaxHpPercent { get; }
        public int DamagePercent { get; }
        public float AttackRadius { get; }
        public BattleTransformationDefinition(float delaySeconds, int maxHpPercent, int damagePercent, float attackRadius)
        {
            BattleGuard.Positive(delaySeconds, nameof(delaySeconds));
            BattleGuard.Positive(attackRadius, nameof(attackRadius));
            if (maxHpPercent < 100 || maxHpPercent > 10000 || damagePercent < 100 || damagePercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(maxHpPercent));
            DelaySeconds = delaySeconds; MaxHpPercent = maxHpPercent; DamagePercent = damagePercent; AttackRadius = attackRadius;
        }
    }

    public sealed class BattleShieldDefinition
    {
        public float IntervalSeconds { get; }
        public float Radius { get; }
        public int CapacityHpPercent { get; }
        public float LifetimeSeconds { get; }
        public BattleShieldDefinition(float intervalSeconds, float radius, int capacityHpPercent, float lifetimeSeconds)
        {
            BattleGuard.Positive(intervalSeconds, nameof(intervalSeconds));
            BattleGuard.Positive(radius, nameof(radius));
            if (capacityHpPercent < 1 || capacityHpPercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(capacityHpPercent));
            BattleGuard.Positive(lifetimeSeconds, nameof(lifetimeSeconds));
            IntervalSeconds = intervalSeconds;
            Radius = radius;
            CapacityHpPercent = capacityHpPercent;
            LifetimeSeconds = lifetimeSeconds;
        }
    }

    public sealed class BattleMagazineDefinition
    {
        public int Shots { get; }
        public float ReloadSeconds { get; }
        public BattleMagazineDefinition(int shots, float reloadSeconds)
        {
            if (shots < 1 || shots > 100)
                throw new ArgumentOutOfRangeException(nameof(shots));
            BattleGuard.Positive(reloadSeconds, nameof(reloadSeconds));
            Shots = shots;
            ReloadSeconds = reloadSeconds;
        }
    }

    public sealed class BattleDamageOverTimeDefinition
    {
        public int TotalDamagePercent { get; }
        public float PeriodSeconds { get; }
        public int TickCount { get; }
        public float DurationSeconds { get; }

        public BattleDamageOverTimeDefinition(int totalDamagePercent, float periodSeconds, int tickCount)
        {
            if (totalDamagePercent < 1 || totalDamagePercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(totalDamagePercent));
            BattleGuard.Positive(periodSeconds, nameof(periodSeconds));
            if (tickCount < 1 || tickCount > 100)
                throw new ArgumentOutOfRangeException(nameof(tickCount));
            float duration = periodSeconds * tickCount;
            BattleGuard.Positive(duration, nameof(tickCount));
            TotalDamagePercent = totalDamagePercent;
            PeriodSeconds = periodSeconds;
            TickCount = tickCount;
            DurationSeconds = duration;
        }
    }

    public sealed class BattleUnitAbilityDefinition
    {
        public BattleUnitAbilityKind Kind { get; }
        public float IntervalSeconds { get; }
        public float Radius { get; }
        public int Amount { get; }
        public string SpawnUnitId { get; }
        public float SpawnLifetimeSeconds { get; }
        public int SpawnDamagePercent { get; }

        public BattleUnitAbilityDefinition(BattleUnitAbilityKind kind = BattleUnitAbilityKind.None, float intervalSeconds = 0f,
            float radius = 0f, int amount = 0, string spawnUnitId = "", float spawnLifetimeSeconds = 0f,
            int spawnDamagePercent = 0)
        {
            if (!Enum.IsDefined(typeof(BattleUnitAbilityKind), kind))
                throw new ArgumentException("Unknown battle ability.");
            BattleGuard.Nonnegative(intervalSeconds, nameof(intervalSeconds));
            BattleGuard.Nonnegative(radius, nameof(radius));
            BattleGuard.Nonnegative(spawnLifetimeSeconds, nameof(spawnLifetimeSeconds));
            if (amount < 0 || spawnDamagePercent < 0 || spawnDamagePercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(amount));
            spawnUnitId = spawnUnitId ?? "";
            switch (kind)
            {
                case BattleUnitAbilityKind.None:
                case BattleUnitAbilityKind.OpeningBacklineJump:
                    if (intervalSeconds != 0f || radius != 0f || amount != 0 || spawnUnitId.Length != 0 || spawnLifetimeSeconds != 0f || spawnDamagePercent != 0)
                        throw new ArgumentException("Canonical ability fields are required.");
                    break;
                case BattleUnitAbilityKind.RepairPulse:
                    if (intervalSeconds <= 0f || radius <= 0f || amount <= 0 || spawnUnitId.Length != 0 || spawnLifetimeSeconds != 0f || spawnDamagePercent != 0)
                        throw new ArgumentException("Repair pulse requires interval, radius and amount.");
                    break;
                case BattleUnitAbilityKind.SpawnUnit:
                    if (intervalSeconds <= 0f || radius <= 0f || amount != 0 || spawnLifetimeSeconds <= 0f)
                        throw new ArgumentException("Spawn unit requires interval, radius and lifetime.");
                    BattleGuard.Id(spawnUnitId, nameof(spawnUnitId));
                    break;
            }
            Kind = kind;
            IntervalSeconds = intervalSeconds;
            Radius = radius;
            Amount = amount;
            SpawnUnitId = spawnUnitId;
            SpawnLifetimeSeconds = spawnLifetimeSeconds;
            SpawnDamagePercent = spawnDamagePercent;
        }
    }

    public sealed class BattleProjectileDefinition
    {
        public string Id { get; }
        public float Speed { get; }
        public float Radius { get; }
        public float ImpactRadius { get; }
        public string ZoneId { get; }
        public bool RetargetOnTargetLost { get; }

        public BattleProjectileDefinition(string id, float speed, float radius, float impactRadius, string zoneId, bool retargetOnTargetLost = false)
        {
            BattleGuard.Id(id, nameof(id));
            BattleGuard.Positive(speed, nameof(speed));
            BattleGuard.Positive(radius, nameof(radius));
            BattleGuard.Nonnegative(impactRadius, nameof(impactRadius));
            Id = id;
            Speed = speed;
            Radius = radius;
            ImpactRadius = impactRadius;
            ZoneId = zoneId ?? "";
            RetargetOnTargetLost = retargetOnTargetLost;
        }
    }

    public sealed class BattleZoneDefinition
    {
        public string Id { get; }
        public float Radius { get; }
        public int TickDamage { get; }
        public float PeriodSeconds { get; }
        public float LifetimeSeconds { get; }
        public float MoveSpeedMultiplier { get; }
        public int SourceDamagePercent { get; }

        public BattleZoneDefinition(string id, float radius, int tickDamage, float periodSeconds, float lifetimeSeconds, float moveSpeedMultiplier = 1f, int sourceDamagePercent = 0)
        {
            BattleGuard.Id(id, nameof(id));
            BattleGuard.Positive(radius, nameof(radius));
            BattleGuard.Positive(periodSeconds, nameof(periodSeconds));
            BattleGuard.Positive(lifetimeSeconds, nameof(lifetimeSeconds));
            BattleGuard.Nonnegative(moveSpeedMultiplier, nameof(moveSpeedMultiplier));
            if (tickDamage <= 0 || periodSeconds > lifetimeSeconds || moveSpeedMultiplier < 0f || moveSpeedMultiplier > 1f || sourceDamagePercent < 0 || sourceDamagePercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(tickDamage));
            Id = id;
            Radius = radius;
            TickDamage = tickDamage;
            PeriodSeconds = periodSeconds;
            LifetimeSeconds = lifetimeSeconds;
            MoveSpeedMultiplier = moveSpeedMultiplier;
            SourceDamagePercent = sourceDamagePercent;
        }
    }

    public sealed class BattleRules
    {
        public float TickSeconds { get; }
        public float HalfWidth { get; }
        public float HalfHeight { get; }
        public float FrontOffset { get; }
        public float RowGap { get; }
        public float UnitGap { get; }
        public int SeparationIterations { get; }
        public float SeparationSpeed { get; }
        public int MaxEntities { get; }
        public bool AllowDebugCommands { get; }

        public BattleRules(float tickSeconds, float halfWidth, float halfHeight, float frontOffset, float rowGap, float unitGap, int separationIterations, float separationSpeed, int maxEntities, bool allowDebugCommands)
        {
            BattleGuard.Positive(tickSeconds, nameof(tickSeconds));
            BattleGuard.Positive(halfWidth, nameof(halfWidth));
            BattleGuard.Positive(halfHeight, nameof(halfHeight));
            BattleGuard.Positive(frontOffset, nameof(frontOffset));
            BattleGuard.Positive(rowGap, nameof(rowGap));
            BattleGuard.Nonnegative(unitGap, nameof(unitGap));
            BattleGuard.Positive(separationSpeed, nameof(separationSpeed));
            if (tickSeconds > 0.1f || separationIterations < 1 || separationIterations > 12 || maxEntities < 2 || maxEntities > 10000)
                throw new ArgumentOutOfRangeException(nameof(maxEntities));
            TickSeconds = tickSeconds;
            HalfWidth = halfWidth;
            HalfHeight = halfHeight;
            FrontOffset = frontOffset;
            RowGap = rowGap;
            UnitGap = unitGap;
            SeparationIterations = separationIterations;
            SeparationSpeed = separationSpeed;
            MaxEntities = maxEntities;
            AllowDebugCommands = allowDebugCommands;
        }
    }

    public sealed class BattleDefinitions
    {
        private readonly Dictionary<string, BattleUnitDefinition> _units = new Dictionary<string, BattleUnitDefinition>();
        private readonly Dictionary<string, BattleProjectileDefinition> _projectiles = new Dictionary<string, BattleProjectileDefinition>();
        private readonly Dictionary<string, BattleZoneDefinition> _zones = new Dictionary<string, BattleZoneDefinition>();
        public ReadOnlyCollection<BattleUnitDefinition> Units { get; }
        public ReadOnlyCollection<BattleProjectileDefinition> Projectiles { get; }
        public ReadOnlyCollection<BattleZoneDefinition> Zones { get; }

        public BattleDefinitions(BattleUnitDefinition[] units, BattleProjectileDefinition[] projectiles, BattleZoneDefinition[] zones)
        {
            if (units == null || units.Length == 0 || projectiles == null || zones == null)
                throw new ArgumentException("Battle catalogs required.");
            Units = Array.AsReadOnly((BattleUnitDefinition[])units.Clone());
            Projectiles = Array.AsReadOnly((BattleProjectileDefinition[])projectiles.Clone());
            Zones = Array.AsReadOnly((BattleZoneDefinition[])zones.Clone());
            var ids = new HashSet<string>();
            foreach (var z in Zones)
            {
                if (z == null || !ids.Add(z.Id))
                    throw new ArgumentException("Duplicate/null battle definition.");
                _zones.Add(z.Id, z);
            }

            foreach (var p in Projectiles)
            {
                if (p == null || !ids.Add(p.Id))
                    throw new ArgumentException("Duplicate/null battle definition.");
                if (p.ZoneId.Length > 0 && !_zones.ContainsKey(p.ZoneId))
                    throw new ArgumentException("Missing zone: " + p.ZoneId);
                _projectiles.Add(p.Id, p);
            }

            foreach (var u in Units)
            {
                if (u == null || !ids.Add(u.Id))
                    throw new ArgumentException("Duplicate/null battle definition.");
                if (u.Attack == BattleAttackKind.Projectile && !_projectiles.ContainsKey(u.ProjectileId))
                    throw new ArgumentException("Missing projectile: " + u.ProjectileId);
                if (u.ContactZoneId.Length > 0)
                {
                    if (u.Attack != BattleAttackKind.ContactExplosion)
                        throw new ArgumentException("Contact zone requires contact explosion.");
                    if (!_zones.ContainsKey(u.ContactZoneId))
                        throw new ArgumentException("Missing zone: " + u.ContactZoneId);
                }
                _units.Add(u.Id, u);
            }

            foreach (var u in Units)
            {
                var ability = u.Ability;
                if (ability == null || ability.Kind != BattleUnitAbilityKind.SpawnUnit)
                    continue;
                if (!_units.TryGetValue(ability.SpawnUnitId, out var child))
                    throw new ArgumentException("Missing spawned unit: " + ability.SpawnUnitId);
                if (child.Id == u.Id || (child.Ability != null && child.Ability.Kind == BattleUnitAbilityKind.SpawnUnit))
                    throw new ArgumentException("Spawn ability cannot self-spawn or recurse.");
            }
        }

        public BattleUnitDefinition Unit(string id) => _units.TryGetValue(id, out var value) ? value : throw new ArgumentException("Unknown unit: " + id);
        public BattleProjectileDefinition Projectile(string id) => _projectiles.TryGetValue(id, out var value) ? value : throw new ArgumentException("Unknown projectile: " + id);
        public BattleZoneDefinition Zone(string id) => _zones.TryGetValue(id, out var value) ? value : throw new ArgumentException("Unknown zone: " + id);
    }

    public readonly struct BattleArmyStack
    {
        public int Side { get; }
        public string UnitId { get; }
        public int Count { get; }
        public int HpBonusPercent { get; }
        public int DamageBonusPercent { get; }
        public int AttackSpeedBonusPercent { get; }

        public BattleArmyStack(int side, string unitId, int count, int hpBonusPercent = 0, int damageBonusPercent = 0, int attackSpeedBonusPercent = 0)
        {
            BattleGuard.Side(side);
            BattleGuard.Id(unitId, nameof(unitId));
            if (count < 1 || count > 128)
                throw new ArgumentOutOfRangeException(nameof(count));
            Side = side;
            UnitId = unitId;
            if (hpBonusPercent < 0 || hpBonusPercent > 1000000 || damageBonusPercent < 0 || damageBonusPercent > 1000000)
                throw new ArgumentOutOfRangeException(nameof(hpBonusPercent));
            Count = count;
            HpBonusPercent = hpBonusPercent;
            DamageBonusPercent = damageBonusPercent;
            if (attackSpeedBonusPercent < 0 || attackSpeedBonusPercent > 10000)
                throw new ArgumentOutOfRangeException(nameof(attackSpeedBonusPercent));
            AttackSpeedBonusPercent = attackSpeedBonusPercent;
        }
    }

    public sealed class BattleScenarioDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public uint Seed { get; }
        public ReadOnlyCollection<BattleArmyStack> Stacks { get; }

        public BattleScenarioDefinition(string id, string title, uint seed, BattleArmyStack[] stacks, bool allowIncomplete = false)
        {
            BattleGuard.Id(id, nameof(id));
            BattleGuard.Id(title, nameof(title));
            if (stacks == null || !allowIncomplete && stacks.Length < 2)
                throw new ArgumentException("Both armies required.");
            var types = new[]
            {
                new HashSet<string>(),
                new HashSet<string>()
            };
            foreach (var s in stacks)
            {
                BattleGuard.Side(s.Side);
                BattleGuard.Id(s.UnitId, "stack unit");
                if (s.Count < 1 || s.Count > 128 || !types[s.Side].Add(s.UnitId))
                    throw new ArgumentException("Invalid/duplicate army stack.");
            }

            if ((!allowIncomplete && (types[0].Count == 0 || types[1].Count == 0)) || types[0].Count > 4 || types[1].Count > 4)
                throw new ArgumentException("Each army needs 1..4 unit types.");
            Id = id;
            Title = title;
            Seed = seed;
            Stacks = Array.AsReadOnly((BattleArmyStack[])stacks.Clone());
        }
    }

    public readonly struct BattleEntityState
    {
        public const int WireVersion = 3;
        public readonly int Id, Side, Hp, MaxHp, TargetId;
        public readonly int ShieldHp, ShieldMaxHp, FirstHitBlocks, Ammo, MagazineSize;
        public readonly float ReloadRemaining, ReloadDuration;
        public readonly BattleEntityKind Kind;
        public readonly string DefinitionId;
        public readonly BattleVec Position, PreviousPosition, Facing;
        public readonly float Radius, Progress;
        public readonly int TransformationStage;
        public BattleEntityState(int id, int side, BattleEntityKind kind, string definitionId, BattleVec position, BattleVec previousPosition, BattleVec facing, int hp, int maxHp, float radius, float progress, int targetId, int shieldHp = 0, int shieldMaxHp = 0, int firstHitBlocks = 0, int ammo = 0, int magazineSize = 0, float reloadRemaining = 0, float reloadDuration = 0, int transformationStage = 0)
        {
            if (shieldHp < 0 || shieldMaxHp < shieldHp || firstHitBlocks < 0 || firstHitBlocks > 1 ||
                magazineSize < 0 || magazineSize > 100 || ammo < 0 || ammo > magazineSize)
                throw new ArgumentOutOfRangeException(nameof(shieldHp), "Invalid defensive state.");
            BattleGuard.Nonnegative(reloadRemaining, nameof(reloadRemaining));
            BattleGuard.Nonnegative(reloadDuration, nameof(reloadDuration));
            if (reloadRemaining > reloadDuration || (reloadRemaining > 0 && ammo != 0) ||
                (magazineSize == 0 && (reloadDuration != 0 || reloadRemaining != 0)) ||
                (kind != BattleEntityKind.Unit && (shieldHp != 0 || shieldMaxHp != 0 || firstHitBlocks != 0 || magazineSize != 0)))
                throw new ArgumentException("Invalid defense or magazine state for entity.");
            Id = id;
            Side = side;
            Kind = kind;
            DefinitionId = definitionId;
            Position = position;
            PreviousPosition = previousPosition;
            Facing = facing;
            Hp = hp;
            MaxHp = maxHp;
            Radius = radius;
            Progress = progress;
            TargetId = targetId;
            if (transformationStage < 0 || transformationStage > 1 || (kind != BattleEntityKind.Unit && transformationStage != 0)) throw new ArgumentOutOfRangeException(nameof(transformationStage));
            TransformationStage = transformationStage;
            ShieldHp = shieldHp;
            ShieldMaxHp = shieldMaxHp;
            FirstHitBlocks = firstHitBlocks;
            Ammo = ammo;
            MagazineSize = magazineSize;
            ReloadRemaining = reloadRemaining;
            ReloadDuration = reloadDuration;
        }
    }

    public readonly struct BattleEvent
    {
        public readonly long Sequence, Tick;
        public readonly BattleEventKind Kind;
        public readonly int EntityId, Side, Amount;
        public readonly BattleVec Position;
        public readonly float Radius;
        public readonly float TimeWithinTick;
        public BattleEvent(long sequence, long tick, BattleEventKind kind, int entityId, int side, BattleVec position, int amount, float radius, float timeWithinTick = 0f)
        {
            if (float.IsNaN(timeWithinTick) || float.IsInfinity(timeWithinTick) || timeWithinTick < 0f || timeWithinTick > 1f)
                throw new ArgumentOutOfRangeException(nameof(timeWithinTick));
            Sequence = sequence;
            Tick = tick;
            Kind = kind;
            EntityId = entityId;
            Side = side;
            Position = position;
            Amount = amount;
            Radius = radius;
            TimeWithinTick = timeWithinTick;
        }
    }

    public readonly struct BattleResolutionHit
    {
        public readonly long Tick;
        public readonly float TimeWithinTick, GroupTime;
        public readonly int SourceId, Side, Damage, SelfId, DirectTarget;
        public readonly BattleVec Position;
        public readonly float Radius;
        public readonly bool IsZone;

        public BattleResolutionHit(long tick, float timeWithinTick, int sourceId, int side, int damage, int selfId, int directTarget, BattleVec position, float radius, bool isZone, float groupTime)
        {
            Tick = tick;
            TimeWithinTick = timeWithinTick;
            SourceId = sourceId;
            Side = side;
            Damage = damage;
            SelfId = selfId;
            DirectTarget = directTarget;
            Position = position;
            Radius = radius;
            IsZone = isZone;
            GroupTime = groupTime;
        }
    }

    public readonly struct BattleZoneCommand
    {
        public readonly int Side;
        public readonly long Sequence, ApplyTick;
        public readonly string ZoneId;
        public readonly BattleVec Position;
        public BattleZoneCommand(int side, long sequence, long applyTick, string zoneId, BattleVec position)
        {
            Side = side;
            Sequence = sequence;
            ApplyTick = applyTick;
            ZoneId = zoneId;
            Position = position;
        }
    }
}
