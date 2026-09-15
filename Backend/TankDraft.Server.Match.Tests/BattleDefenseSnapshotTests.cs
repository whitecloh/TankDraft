using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

/// <summary>
/// Covers the defensive fields that are observable through a simulation snapshot.
/// These are intentionally public-contract tests: no ECS pool inspection is used here.
/// </summary>
public sealed class BattleDefenseSnapshotTests
{
    const float Tick = .1f;

    [Fact]
    public void Fresh_unit_snapshot_exposes_block_and_full_magazine_without_a_shield()
    {
        var defender = Unit("defender", BattleAttackKind.Melee, block: true, magazine: new BattleMagazineDefinition(6, .6f));
        var opponent = Unit("opponent", BattleAttackKind.Passive);
        using var simulation = Sim(Defs(defender, opponent), Stack(0, "defender"), Stack(1, "opponent"));

        var state = State(simulation, "defender", 0);

        Assert.Equal(0, state.ShieldHp);
        Assert.Equal(0, state.ShieldMaxHp);
        Assert.Equal(1, state.FirstHitBlocks);
        Assert.Equal(6, state.Ammo);
        Assert.Equal(6, state.MagazineSize);
        Assert.Equal(0f, state.ReloadRemaining);
        Assert.Equal(.6f, state.ReloadDuration);
    }

    [Fact]
    public void Shield_snapshot_reports_capacity_then_clears_after_expiry_without_damage()
    {
        var shielded = Unit("shielded", BattleAttackKind.Passive,
            shield: new BattleShieldDefinition(Tick * 4, 20f, 50, Tick * 2));
        var opponent = Unit("opponent", BattleAttackKind.Passive);
        using var simulation = Sim(Defs(shielded, opponent), Stack(0, "shielded"), Stack(1, "opponent"));

        for (var step = 0; step < 4; step++)
            simulation.Step();
        var active = State(simulation, "shielded", 0);
        Assert.Equal(50, active.ShieldHp);
        Assert.Equal(50, active.ShieldMaxHp);

        simulation.Step();
        simulation.Step();
        var expired = State(simulation, "shielded", 0);
        Assert.Equal(0, expired.ShieldHp);
        Assert.Equal(0, expired.ShieldMaxHp);
    }

    [Fact]
    public void Direct_hit_removes_first_hit_block_from_the_snapshot()
    {
        var attacker = Unit("attacker", BattleAttackKind.Melee, damage: 1, cooldown: Tick, range: 20f);
        var defender = Unit("defender", BattleAttackKind.Passive, block: true);
        using var simulation = Sim(Defs(attacker, defender), Stack(0, "attacker"), Stack(1, "defender"));

        for (var step = 0; step < 20 && State(simulation, "defender", 1).FirstHitBlocks != 0; step++)
            simulation.Step();

        Assert.Equal(0, State(simulation, "defender", 1).FirstHitBlocks);
    }

    [Fact]
    public void Reload_snapshot_counts_down_and_refills_magazine_after_last_shot()
    {
        var shooter = Unit("shooter", BattleAttackKind.Melee, damage: 100, cooldown: Tick, range: 4.7f,
            magazine: new BattleMagazineDefinition(1, .6f));
        var target = Unit("target", BattleAttackKind.Passive, hp: 1);
        var distant = Unit("distant", BattleAttackKind.Passive, row: FormationRow.Artillery);
        using var simulation = Sim(Defs(shooter, target, distant), Stack(0, "shooter"), Stack(1, "target"), Stack(1, "distant"));

        BattleEntityState reloading = default;
        for (var step = 0; step < 20; step++)
        {
            simulation.Step();
            var state = State(simulation, "shooter", 0);
            if (state.ReloadRemaining > 0f)
            {
                reloading = state;
                break;
            }
        }

        Assert.Equal(0, reloading.Ammo);
        Assert.Equal(1, reloading.MagazineSize);
        Assert.Equal(.6f, reloading.ReloadDuration);
        Assert.InRange(reloading.ReloadRemaining, .1f, .6f);

        simulation.Step();
        var progressed = State(simulation, "shooter", 0);
        Assert.True(progressed.ReloadRemaining < reloading.ReloadRemaining);

        for (var step = 0; step < 20 && State(simulation, "shooter", 0).ReloadRemaining > 0f; step++)
            simulation.Step();
        var ready = State(simulation, "shooter", 0);
        Assert.Equal(1, ready.Ammo);
        Assert.Equal(0f, ready.ReloadRemaining);
        Assert.Equal(.6f, ready.ReloadDuration);
    }

    [Fact]
    public void Defensive_snapshot_fields_are_zero_for_projectile_and_zone_entities()
    {
        var projectile = new BattleProjectileDefinition("shell", .1f, .1f, 0f, "");
        var zone = new BattleZoneDefinition("zone", 20f, 1, Tick, 1f);
        var shooter = Unit("shooter", BattleAttackKind.Projectile, damage: 1, cooldown: Tick, range: 20f, projectileId: "shell");
        var target = Unit("target", BattleAttackKind.Passive, hp: 10000);
        using var simulation = Sim(new BattleDefinitions([shooter, target], [projectile], [zone]), true, Stack(0, "shooter"), Stack(1, "target"));

        var targetPosition = State(simulation, "target", 1).Position;
        Assert.True(simulation.TryQueueZone(new BattleZoneCommand(0, 1, 1, "zone", targetPosition), out _));
        for (var step = 0; step < 20 && States(simulation).All(value => value.Kind == BattleEntityKind.Unit); step++)
            simulation.Step();

        var nonUnits = States(simulation).Where(value => value.Kind != BattleEntityKind.Unit).ToArray();
        Assert.NotEmpty(nonUnits);
        Assert.All(nonUnits, state =>
        {
            Assert.Equal(0, state.ShieldHp);
            Assert.Equal(0, state.ShieldMaxHp);
            Assert.Equal(0, state.FirstHitBlocks);
            Assert.Equal(0, state.Ammo);
            Assert.Equal(0, state.MagazineSize);
            Assert.Equal(0f, state.ReloadRemaining);
            Assert.Equal(0f, state.ReloadDuration);
        });
    }

    [Fact]
    public void Fresh_round_snapshot_restores_initial_defensive_charges()
    {
        var definition = Unit("defender", BattleAttackKind.Melee, block: true, range: 20f, magazine: new BattleMagazineDefinition(2, .5f));
        var opponent = Unit("opponent", BattleAttackKind.Passive);
        var definitions = Defs(definition, opponent);
        using var first = Sim(definitions, Stack(0, "defender"), Stack(1, "opponent"));
        using var fresh = Sim(definitions, Stack(0, "defender"), Stack(1, "opponent"));

        for (var step = 0; step < 20 && State(first, "defender", 0).Ammo == 2; step++)
            first.Step();

        Assert.NotEqual(2, State(first, "defender", 0).Ammo);
        var restarted = State(fresh, "defender", 0);
        Assert.Equal(1, restarted.FirstHitBlocks);
        Assert.Equal(2, restarted.Ammo);
        Assert.Equal(2, restarted.MagazineSize);
        Assert.Equal(0f, restarted.ReloadRemaining);
    }

    static BattleUnitDefinition Unit(string id, BattleAttackKind attack, int hp = 100, int damage = 1, float cooldown = 1f,
        float range = 1f, string projectileId = "", BattleShieldDefinition? shield = null, bool block = false,
        FormationRow row = FormationRow.Tank,
        BattleMagazineDefinition? magazine = null) =>
        new(id, row, attack, hp, damage, 0f, .2f, 1f, range, cooldown, projectileId, null, "", null,
            shield, block, magazine, 0);

    static BattleDefinitions Defs(params BattleUnitDefinition[] units) => new(units, [], []);
    static BattleArmyStack Stack(int side, string id) => new(side, id, 1);
    static BattleSimulation Sim(BattleDefinitions definitions, params BattleArmyStack[] stacks) => Sim(definitions, false, stacks);
    static BattleSimulation Sim(BattleDefinitions definitions, bool allowDebugCommands, params BattleArmyStack[] stacks) => new(definitions,
        new BattleRules(Tick, 5f, 8f, 1f, 1.5f, .15f, 3, 4f, 64, allowDebugCommands),
        new BattleScenarioDefinition("snapshot", "snapshot", 11, stacks));
    static BattleEntityState[] States(BattleSimulation simulation)
    {
        var states = new List<BattleEntityState>();
        simulation.Capture(states);
        return states.ToArray();
    }
    static BattleEntityState State(BattleSimulation simulation, string id, int side) => States(simulation).Single(value =>
        value.Kind == BattleEntityKind.Unit && value.DefinitionId == id && value.Side == side);
}
