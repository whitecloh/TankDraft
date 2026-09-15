using System.Text.Json;
using TankDraft.Contracts.Battle;
using TankDraft.Contracts;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class AuthoredStatusContentTests
{
    [Theory]
    [InlineData(1, .033333335f, 1f)]
    [InlineData(2, .033333335f, 1f)]
    [InlineData(10, .033333335f, 1f)]
    [InlineData(10, .1f, .13f)]
    public void One_hit_delivers_the_full_dot_total_across_fractional_tick_boundaries(int damage, float step, float period)
    {
        var poison = new BattleUnitDefinition("poison", FormationRow.Tank, BattleAttackKind.Melee, 100, damage, 0, .25f, 1, 20, 20, "", damageOverTime: new BattleDamageOverTimeDefinition(100, period, 3));
        var target = new BattleUnitDefinition("target", FormationRow.Tank, BattleAttackKind.Passive, 1000, 0, 0, .25f, 1, 0, 1, "");
        using var sim = new BattleSimulation(new BattleDefinitions([poison, target], [], []),
            new BattleRules(step, 5, 8, 1, 1.5f, .15f, 3, 4, 64, false),
            new BattleScenarioDefinition("dot-total", "dot-total", 7, [new BattleArmyStack(0, "poison", 1), new BattleArmyStack(1, "target", 1)]));
        for (int i = 0; i < 2000 && sim.TotalShots == 0; i++) sim.Step();
        Assert.Equal(1, sim.TotalShots);
        for (int i = 0; i < (int)Math.Ceiling(period * 3 / step) + 2; i++) sim.Step();
        var states = new List<BattleEntityState>(); sim.Capture(states);
        Assert.Equal(1, sim.TotalShots);
        Assert.Equal(1000 - damage * 2, states.Single(s => s.DefinitionId == "target").Hp);
    }

    [Fact]
    public void Authored_export_preserves_status_rules_without_changing_legacy_units()
    {
        var fixture = AuthoredMatchFixture.Load();
        var bomber = fixture.Definitions.Unit("unit.demolition_vehicle");
        var chemical = fixture.Definitions.Unit("unit.chemical_demolition");
        var toxin = fixture.Definitions.Unit("unit.toxin_destroyer");
        Assert.Equal(BattleAttackKind.ContactExplosion, bomber.Attack);
        Assert.True(bomber.MoveSpeed > 0);
        Assert.Equal("zone.toxic", chemical.ContactZoneId);
        Assert.NotNull(toxin.DamageOverTime);
        Assert.Equal(100, toxin.DamageOverTime.TotalDamagePercent);
        Assert.Equal(3, toxin.DamageOverTime.TickCount);
        Assert.Equal(1f, toxin.DamageOverTime.PeriodSeconds);
        var zone = fixture.Definitions.Zone(chemical.ContactZoneId);
        Assert.Equal(.5f, zone.MoveSpeedMultiplier);
        Assert.Equal(20, zone.SourceDamagePercent);
        Assert.Equal(6f, zone.LifetimeSeconds);
        Assert.Equal(1f, fixture.Definitions.Unit("unit.mines").MoveSpeed);
        Assert.Equal("", fixture.Definitions.Unit("unit.mines").ContactZoneId);
        Assert.Equal(1f, fixture.Definitions.Zone("zone.burning").MoveSpeedMultiplier);
        Assert.Equal(0, fixture.Definitions.Zone("zone.burning").SourceDamagePercent);
        foreach (var id in new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" })
            Assert.Null(fixture.Definitions.Unit(id).DamageOverTime);
        var copy = JsonSerializer.Deserialize<BattleUnitDefinition>(JsonSerializer.Serialize(toxin));
        Assert.NotNull(copy);
        Assert.Equal(100, copy.DamageOverTime.TotalDamagePercent);
    }
}
