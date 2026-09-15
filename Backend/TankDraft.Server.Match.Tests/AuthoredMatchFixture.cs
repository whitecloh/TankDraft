using System.Text.Json;
using System.Text.Json.Serialization;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Domain;
using Xunit;

namespace TankDraft.Server.Match.Tests;

internal sealed class AuthoredMatchFixture
{
    public MatchRules MatchRules { get; }
    public MatchUnitDefinition[] MatchUnits { get; }
    public BattleDefinitions Definitions { get; }
    public BattleRules BattleRules { get; }
    public string[] Deck { get; }
    public string OrderId { get; }
    public uint Seed { get; }

    private AuthoredMatchFixture(ExportDto source)
    {
        ArgumentNullException.ThrowIfNull(source.MatchRules);
        ArgumentNullException.ThrowIfNull(source.BattleRules);
        ArgumentNullException.ThrowIfNull(source.MatchUnits);
        ArgumentNullException.ThrowIfNull(source.Units);
        ArgumentNullException.ThrowIfNull(source.Projectiles);
        ArgumentNullException.ThrowIfNull(source.Zones);
        ArgumentNullException.ThrowIfNull(source.Deck);

        MatchRules = new MatchRules(source.MatchRules.WinsRequired, source.MatchRules.NormalChoices,
            source.MatchRules.MaxUnitsPerType, source.MatchRules.UpgradeHpPercent,
            source.MatchRules.UpgradeDamagePercent, source.MatchRules.OrderCharges,
            source.MatchRules.OrderArmorPercent, source.MatchRules.AddWeight,
            source.MatchRules.DoubleWeight, source.MatchRules.UpgradeWeight);
        MatchUnits = source.MatchUnits.Select(value => new MatchUnitDefinition(value.Id, value.AddCount, value.CanFightAlone)).ToArray();
        BattleRules = new BattleRules(source.BattleRules.TickSeconds, source.BattleRules.HalfWidth,
            source.BattleRules.HalfHeight, source.BattleRules.FrontOffset, source.BattleRules.RowGap,
            source.BattleRules.UnitGap, source.BattleRules.SeparationIterations, source.BattleRules.SeparationSpeed,
            source.BattleRules.MaxEntities, source.BattleRules.AllowDebugCommands);
        Definitions = new BattleDefinitions(
            source.Units.Select(value => new BattleUnitDefinition(value.Id, (FormationRow)value.Row,
                (BattleAttackKind)value.Attack, value.MaxHp, value.Damage, value.MoveSpeed, value.Radius,
                value.Mass, value.Range, value.CooldownSeconds, value.ProjectileId, value.Ability == null ? null :
                new BattleUnitAbilityDefinition((BattleUnitAbilityKind)value.Ability.Kind, value.Ability.IntervalSeconds,
                    value.Ability.Radius, value.Ability.Amount, value.Ability.SpawnUnitId, value.Ability.SpawnLifetimeSeconds,
                    value.Ability.SpawnDamagePercent), value.ContactZoneId, value.DamageOverTime,
                value.Shield, value.BlocksFirstHit, value.Magazine, value.LifeStealPercent,
                value.FormationPriority, value.Transformation == null ? null : new BattleTransformationDefinition(
                    value.Transformation.DelaySeconds, value.Transformation.MaxHpPercent, value.Transformation.DamagePercent,
                    value.Transformation.AttackRadius))).ToArray(),
            source.Projectiles.Select(value => new BattleProjectileDefinition(value.Id, value.Speed, value.Radius,
                value.ImpactRadius, value.ZoneId, value.RetargetOnTargetLost)).ToArray(),
            source.Zones.Select(value => new BattleZoneDefinition(value.Id, value.Radius, value.TickDamage,
                value.PeriodSeconds, value.LifetimeSeconds, value.MoveSpeedMultiplier, value.SourceDamagePercent)).ToArray());
        Deck = source.Deck.ToArray();
        OrderId = source.OrderId;
        Seed = source.Seed;
    }

    public static AuthoredMatchFixture Load()
    {
        var path = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Content/local-match.json"));
        Assert.True(File.Exists(path), "Authored content export is required for server match tests: " + path);
        var source = JsonSerializer.Deserialize<ExportDto>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
        });
        Assert.NotNull(source);
        Assert.Equal(1, source.SchemaVersion);
        return new AuthoredMatchFixture(source);
    }

    private sealed class ExportDto
    {
        public int SchemaVersion { get; set; }
        public string SourceAsset { get; set; } = string.Empty;
        public MatchRulesDto? MatchRules { get; set; }
        public MatchUnitDto[]? MatchUnits { get; set; }
        public BattleRulesDto? BattleRules { get; set; }
        public UnitDto[]? Units { get; set; }
        public ProjectileDto[]? Projectiles { get; set; }
        public ZoneDto[]? Zones { get; set; }
        public string[]? Deck { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public uint Seed { get; set; }
    }

    private sealed class MatchRulesDto { public int WinsRequired { get; set; } public int NormalChoices { get; set; } public int MaxUnitsPerType { get; set; } public int UpgradeHpPercent { get; set; } public int UpgradeDamagePercent { get; set; } public int OrderCharges { get; set; } public int OrderArmorPercent { get; set; } public int AddWeight { get; set; } public int DoubleWeight { get; set; } public int UpgradeWeight { get; set; } }
    private sealed class MatchUnitDto { public string Id { get; set; } = string.Empty; public int AddCount { get; set; } public bool CanFightAlone { get; set; } }
    private sealed class BattleRulesDto { public float TickSeconds { get; set; } public float HalfWidth { get; set; } public float HalfHeight { get; set; } public float FrontOffset { get; set; } public float RowGap { get; set; } public float UnitGap { get; set; } public int SeparationIterations { get; set; } public float SeparationSpeed { get; set; } public int MaxEntities { get; set; } public bool AllowDebugCommands { get; set; } }
    private sealed class UnitDto { public string Id { get; set; } = string.Empty; public int Row { get; set; } public int Attack { get; set; } public int MaxHp { get; set; } public int Damage { get; set; } public float MoveSpeed { get; set; } public float Radius { get; set; } public float Mass { get; set; } public float Range { get; set; } public float CooldownSeconds { get; set; } public string ProjectileId { get; set; } = string.Empty; public AbilityDto? Ability { get; set; } public string ContactZoneId { get; set; } = string.Empty; public BattleDamageOverTimeDefinition? DamageOverTime { get; set; } public BattleShieldDefinition? Shield { get; set; } public bool BlocksFirstHit { get; set; } public BattleMagazineDefinition? Magazine { get; set; } public int LifeStealPercent { get; set; } public int FormationPriority { get; set; } public TransformationDto? Transformation { get; set; } }
    private sealed class TransformationDto { public float DelaySeconds { get; set; } public int MaxHpPercent { get; set; } public int DamagePercent { get; set; } public float AttackRadius { get; set; } }
    private sealed class AbilityDto { public int Kind { get; set; } public float IntervalSeconds { get; set; } public float Radius { get; set; } public int Amount { get; set; } public string SpawnUnitId { get; set; } = string.Empty; public float SpawnLifetimeSeconds { get; set; } public int SpawnDamagePercent { get; set; } }
    private sealed class ProjectileDto { public string Id { get; set; } = string.Empty; public float Speed { get; set; } public float Radius { get; set; } public float ImpactRadius { get; set; } public bool RetargetOnTargetLost { get; set; } public string ZoneId { get; set; } = string.Empty; }
    private sealed class ZoneDto { public string Id { get; set; } = string.Empty; public float Radius { get; set; } public int TickDamage { get; set; } public float PeriodSeconds { get; set; } public float LifetimeSeconds { get; set; } public float MoveSpeedMultiplier { get; set; } = 1f; public int SourceDamagePercent { get; set; } }
}
