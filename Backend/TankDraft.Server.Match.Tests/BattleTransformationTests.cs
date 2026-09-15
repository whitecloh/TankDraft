using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class BattleTransformationTests
{
    [Fact]
    public void Transformation_uses_fixed_delay_full_heal_upgraded_initial_stats_and_stage_snapshot_once()
    {
        var form = new BattleTransformationDefinition(.2f, 400, 300, 2f);
        var transformer = new BattleUnitDefinition("transformer", FormationRow.Tank, BattleAttackKind.Melee, 100, 10, 0, .2f, 1, 20, 1, "", transformation: form);
        var target = new BattleUnitDefinition("target", FormationRow.Tank, BattleAttackKind.Melee, 1000, 50, 0, .2f, 1, 20, 10, "");
        using var sim = new BattleSimulation(new BattleDefinitions([transformer, target], [], []), Rules(), new BattleScenarioDefinition("t", "t", 1, [new BattleArmyStack(0, "transformer", 1, hpBonusPercent: 100, damageBonusPercent: 50), new BattleArmyStack(1, "target", 1)]));
        SetUnitField(sim, 2, "Remaining", 0f);
        sim.Step();
        Assert.Equal(0, State(sim, "transformer", 0).TransformationStage);
        Assert.Equal(150, State(sim, "transformer", 0).Hp);
        SetUnitField(sim, 2, "Remaining", 10f);
        sim.Step();
        var transformed = State(sim, "transformer", 0);
        Assert.Equal(1, transformed.TransformationStage);
        Assert.Equal(800, transformed.MaxHp);
        Assert.Equal(800, transformed.Hp);
        var targetBeforeTransformedAttack = State(sim, "target", 1).Hp;
        SetUnitField(sim, 1, "Remaining", 0f);
        sim.Step();
        Assert.Equal(targetBeforeTransformedAttack - 45, State(sim, "target", 1).Hp);
        SetUnitField(sim, 2, "Remaining", 0f);
        sim.Step();
        Assert.Equal(750, State(sim, "transformer", 0).Hp);
    }

    [Fact]
    public void Transformation_rejects_invalid_profiles_and_never_resurrects()
    {
        var form = new BattleTransformationDefinition(.1f, 400, 300, 2f);
        Assert.Throws<ArgumentException>(() => new BattleUnitDefinition("projectile", FormationRow.Tank, BattleAttackKind.Projectile, 1, 1, 0, .2f, 1, 1, 1, "p", transformation: form));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleTransformationDefinition(.1f, 99, 300, 1));
    }

    [Fact]
    public void Melee_is_single_target_before_transform_and_area_damage_after_transform()
    {
        var form = new BattleTransformationDefinition(.2f, 400, 300, 2f);
        var transformer = new BattleUnitDefinition("transformer", FormationRow.Tank, BattleAttackKind.Melee, 100, 10, 0, .2f, 1, 20, .1f, "", transformation: form);
        var first = new BattleUnitDefinition("first", FormationRow.Tank, BattleAttackKind.Passive, 100, 0, 0, .2f, 1, 0, 1, "");
        var nearby = new BattleUnitDefinition("nearby", FormationRow.Tank, BattleAttackKind.Passive, 100, 0, 0, .2f, 1, 0, 1, "");
        using var sim = new BattleSimulation(new BattleDefinitions([transformer, first, nearby], [], []), Rules(), new BattleScenarioDefinition("aoe", "aoe", 1, [Stack(0, "transformer"), Stack(1, "first"), Stack(1, "nearby")]));
        sim.Step();
        Assert.Equal(100, State(sim, "nearby", 1).Hp);
        sim.Step();
        Assert.True(State(sim, "nearby", 1).Hp < 100);
    }

    [Fact]
    public void Dead_transformer_does_not_resurrect_and_fresh_simulation_starts_at_stage_zero()
    {
        var form = new BattleTransformationDefinition(.2f, 400, 300, 2f);
        var transformer = new BattleUnitDefinition("transformer", FormationRow.Tank, BattleAttackKind.Melee, 1, 10, 0, .2f, 1, 20, 1, "", transformation: form);
        var killer = new BattleUnitDefinition("killer", FormationRow.Tank, BattleAttackKind.Melee, 100, 10, 0, .2f, 1, 20, .1f, "");
        var definitions = new BattleDefinitions([transformer, killer], [], []);
        using var dead = new BattleSimulation(definitions, Rules(), new BattleScenarioDefinition("dead", "dead", 1, [Stack(0, "transformer"), Stack(1, "killer")]));
        dead.Step(); dead.Step();
        Assert.DoesNotContain(States(dead), value => value.DefinitionId == "transformer");
        using var fresh = new BattleSimulation(definitions, Rules(), new BattleScenarioDefinition("fresh", "fresh", 1, [Stack(0, "transformer"), Stack(1, "killer")]));
        Assert.Equal(0, State(fresh, "transformer", 0).TransformationStage);
    }

    [Fact]
    public void Legacy_units_never_transform_and_snapshot_rejects_invalid_stage_or_nonunit_stage()
    {
        var legacy = new BattleUnitDefinition("legacy", FormationRow.Tank, BattleAttackKind.Melee, 100, 10, 0, .2f, 1, 20, .1f, "");
        var target = new BattleUnitDefinition("target", FormationRow.Tank, BattleAttackKind.Passive, 1000, 0, 0, .2f, 1, 0, 1, "");
        using var sim = new BattleSimulation(new BattleDefinitions([legacy, target], [], []), Rules(), new BattleScenarioDefinition("legacy", "legacy", 1, [Stack(0, "legacy"), Stack(1, "target")]));
        for (var i = 0; i < 5; i++) sim.Step();
        Assert.Equal(0, State(sim, "legacy", 0).TransformationStage);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleEntityState(1, 0, BattleEntityKind.Unit, "u", default, default, default, 1, 1, .1f, 0, 0, transformationStage: 2));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleEntityState(1, 0, BattleEntityKind.Projectile, "p", default, default, default, 0, 0, .1f, 0, 0, transformationStage: 1));
    }

    static BattleRules Rules() => new(.1f, 5, 8, 1, 1.5f, .15f, 3, 4, 64, false);
    static BattleArmyStack Stack(int side, string id) => new(side, id, 1);
    static BattleEntityState[] States(BattleSimulation sim) { var states = new List<BattleEntityState>(); sim.Capture(states); return states.ToArray(); }
    static BattleEntityState State(BattleSimulation sim, string id, int side) => States(sim).Single(x => x.DefinitionId == id && x.Side == side);

    static void SetUnitField(BattleSimulation simulation, int stableId, string field, object value)
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
        var type = typeof(BattleSimulation);
        var identityPool = type.GetField("_identity", flags)!.GetValue(simulation)!;
        var unitPool = type.GetField("_unit", flags)!.GetValue(simulation)!;
        var identitySparse = (int[])identityPool.GetType().GetField("_sparseItems", flags)!.GetValue(identityPool)!;
        var identityDense = (Array)identityPool.GetType().GetField("_denseItems", flags)!.GetValue(identityPool)!;
        var unitSparse = (int[])unitPool.GetType().GetField("_sparseItems", flags)!.GetValue(unitPool)!;
        var unitDense = (Array)unitPool.GetType().GetField("_denseItems", flags)!.GetValue(unitPool)!;
        for (var entity = 0; entity < identitySparse.Length; entity++)
        {
            var identityIndex = identitySparse[entity];
            if (identityIndex == 0 || (int)identityDense.GetValue(identityIndex)!.GetType().GetField("Id", flags)!.GetValue(identityDense.GetValue(identityIndex))! != stableId)
                continue;
            var unitIndex = unitSparse[entity]; var unit = unitDense.GetValue(unitIndex)!;
            unit.GetType().GetField(field, flags)!.SetValue(unit, value);
            unitDense.SetValue(unit, unitIndex);
            return;
        }
        throw new Xunit.Sdk.XunitException("Unit " + stableId + " was not found.");
    }
}
