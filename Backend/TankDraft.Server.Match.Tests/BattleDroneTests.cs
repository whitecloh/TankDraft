using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class BattleDroneTests
{
    [Fact]
    public void Drone_survives_carrier_death_ignores_zone_and_hits_once_then_disappears()
    {
        using var sim = Create(true);
        Launch(sim);
        var drone = Assert.Single(States(sim), s => s.Kind == BattleEntityKind.Projectile);
        var carrier = States(sim).Single(s => s.DefinitionId == "carrier");
        Assert.True(sim.TryQueueZone(new BattleZoneCommand(1, 1, sim.Tick + 1, "kill", carrier.Position), out _));
        sim.Step();
        Assert.DoesNotContain(States(sim), s => s.DefinitionId == "carrier");
        Assert.Contains(States(sim), s => s.Id == drone.Id);
        for (int i = 0; i < 100; i++) sim.Step();
        Assert.DoesNotContain(States(sim), s => s.Id == drone.Id);
        Assert.Equal(990, States(sim).Single(s => s.DefinitionId == "target").Hp);
        Assert.Equal(1, sim.TotalShots);
        Assert.Equal(1, sim.TotalImpacts);
    }

    [Theory]
    [InlineData(true, 990)]
    [InlineData(false, 1000)]
    public void Lost_target_retargeting_is_configured_and_legacy_projectiles_keep_old_behavior(bool retarget, int expectedHp)
    {
        using var sim = Create(retarget);
        Launch(sim);
        var target = States(sim).Single(s => s.DefinitionId == "target");
        Assert.True(sim.TryQueueZone(new BattleZoneCommand(0, 1, sim.Tick + 1, "kill", target.Position), out _));
        sim.Step();
        Assert.DoesNotContain(States(sim), s => s.DefinitionId == "target");
        for (int i = 0; i < 100; i++) sim.Step();
        Assert.Equal(expectedHp, States(sim).Single(s => s.DefinitionId == "reserve").Hp);
        Assert.DoesNotContain(States(sim), s => s.Kind == BattleEntityKind.Projectile);
    }

    [Fact]
    public void Authored_carrier_fires_drones_and_has_no_destructible_summon()
    {
        var defs = AuthoredMatchFixture.Load().Definitions;
        var carrier = defs.Unit("unit.drone_carrier");
        Assert.Null(carrier.Ability);
        Assert.Equal("projectile.drone", carrier.ProjectileId);
        Assert.True(defs.Projectile(carrier.ProjectileId).RetargetOnTargetLost);
        Assert.Equal(0, defs.Projectile(carrier.ProjectileId).ImpactRadius);
        Assert.NotNull(defs.Unit("unit.siege_transformer").Transformation);
    }

    static BattleSimulation Create(bool retarget)
    {
        var carrier = new BattleUnitDefinition("carrier", FormationRow.Artillery, BattleAttackKind.Projectile, 100, 10, 0, .2f, 1, 20, 100, "drone");
        BattleUnitDefinition Passive(string id, FormationRow row) => new(id, row, BattleAttackKind.Passive, 1000, 0, 0, .2f, 1, 0, 1, "");
        var defs = new BattleDefinitions([carrier, Passive("backup", FormationRow.Tank), Passive("target", FormationRow.Tank), Passive("reserve", FormationRow.Artillery)],
            [new BattleProjectileDefinition("drone", 3, .1f, 0, "", retarget)], [new BattleZoneDefinition("kill", .01f, 2000, .1f, .1f)]);
        return new BattleSimulation(defs, new BattleRules(.1f, 5, 8, 1, 1.5f, .15f, 3, 4, 64, true),
            new BattleScenarioDefinition("drone-test", "drone-test", 1, [new(0, "carrier", 1), new(0, "backup", 1), new(1, "target", 1), new(1, "reserve", 1)]));
    }

    static void Launch(BattleSimulation sim) { for (int i = 0; i < 1000 && sim.TotalShots == 0; i++) sim.Step(); Assert.Equal(1, sim.TotalShots); }
    static BattleEntityState[] States(BattleSimulation sim) { var states = new List<BattleEntityState>(); sim.Capture(states); return states.ToArray(); }
}
