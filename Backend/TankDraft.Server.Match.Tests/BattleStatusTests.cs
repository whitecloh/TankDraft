using System.Text.Json;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class BattleStatusTests
{
    const float Tick = .1f;

    [Fact]
    public void Legacy_unit_json_defaults_new_status_fields_and_round_trips_them()
    {
        const string legacy = "{\"Id\":\"legacy\",\"Row\":1,\"Attack\":1,\"MaxHp\":10,\"Damage\":2,\"MoveSpeed\":1,\"Radius\":0.2,\"Mass\":1,\"Range\":2,\"CooldownSeconds\":1,\"ProjectileId\":\"shell\"}";
        var restored = JsonSerializer.Deserialize<BattleUnitDefinition>(legacy);
        Assert.NotNull(restored);
        Assert.Equal("", restored!.ContactZoneId);
        Assert.Null(restored.DamageOverTime);

        var expected = Unit("poison", BattleAttackKind.Melee, dot: new BattleDamageOverTimeDefinition(100, .2f, 3));
        var actual = JsonSerializer.Deserialize<BattleUnitDefinition>(JsonSerializer.Serialize(expected));
        Assert.NotNull(actual!.DamageOverTime);
        Assert.Equal(100, actual.DamageOverTime!.TotalDamagePercent);
        Assert.Equal(.2f, actual.DamageOverTime.PeriodSeconds);
        Assert.Equal(3, actual.DamageOverTime.TickCount);
        Assert.Equal(.6f, actual.DamageOverTime.DurationSeconds);
    }

    [Fact]
    public void Status_definitions_reject_invalid_parameters_and_references()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleDamageOverTimeDefinition(0, .1f, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleDamageOverTimeDefinition(1, float.NaN, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleDamageOverTimeDefinition(1, .1f, 101));
        Assert.Throws<ArgumentException>(() => Unit("passive", BattleAttackKind.Passive, dot: new BattleDamageOverTimeDefinition(1, .1f, 1)));
        Assert.Throws<ArgumentException>(() => Unit("melee", BattleAttackKind.Melee, contactZoneId: "toxic"));
        Assert.Throws<ArgumentException>(() => new BattleDefinitions(
            [Unit("contact", BattleAttackKind.ContactExplosion, contactZoneId: "missing")], [], []));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleZoneDefinition("slow", 1, 1, .1f, .2f, float.NaN));
    }

    [Fact]
    public void Contact_explosion_moves_then_detonates_once_without_harming_a_friendly()
    {
        var zone = new BattleZoneDefinition("toxic", 2f, 1, .1f, 1f);
        var contact = Unit("contact", BattleAttackKind.ContactExplosion, hp: 20, damage: 50, speed: 20f, range: 0f, cooldown: Tick, contactZoneId: "toxic");
        var ally = Unit("ally", BattleAttackKind.Passive, hp: 20, speed: 0f);
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, speed: 0f);
        using var simulation = Simulation(new BattleDefinitions([contact, ally, target], [], [zone]),
            Stack(0, "contact"), Stack(0, "ally"), Stack(1, "target"));
        var events = new List<BattleEvent>();
        var drained = new List<BattleEvent>();
        for (int i = 0; i < 12 && simulation.Outcome == BattleOutcome.Running; i++)
        {
            simulation.Step();
            simulation.DrainEvents(drained);
            events.AddRange(drained);
        }
        Assert.True(events.Count(value => value.Kind == BattleEventKind.ZoneCreated) == 1,
            "outcome=" + simulation.Outcome + " shots=" + simulation.TotalShots + " events=" + string.Join(";", events.Select(value => value.Kind + ":" + value.EntityId + ":" + value.Amount)));
        Assert.Equal(0, State(simulation, "ally", 0).MaxHp - State(simulation, "ally", 0).Hp);
        Assert.DoesNotContain(States(simulation), value => value.DefinitionId == "contact");
    }

    [Fact]
    public void Dot_ticks_are_deterministic_and_never_emit_impact_or_zone_tick()
    {
        var poison = Unit("poison", BattleAttackKind.Melee, hp: 100, damage: 10, speed: 0f, range: 20f, cooldown: Tick,
            dot: new BattleDamageOverTimeDefinition(100, Tick, 3));
        var target = Unit("target", BattleAttackKind.Passive, hp: 1000, speed: 0f);
        var definitions = new BattleDefinitions([poison, target], [], []);
        using var left = Simulation(definitions, Stack(0, "poison"), Stack(1, "target"));
        using var right = Simulation(definitions, Stack(0, "poison"), Stack(1, "target"));
        var events = new List<BattleEvent>();
        var drained = new List<BattleEvent>();
        for (int i = 0; i < 5; i++) { left.Step(); right.Step(); left.DrainEvents(drained); events.AddRange(drained); }
        Assert.Equal(State(left, "target", 1).Hp, State(right, "target", 1).Hp);
        Assert.Equal(left.TotalImpacts, right.TotalImpacts);
        Assert.Equal(left.TotalZoneTicks, right.TotalZoneTicks);
        Assert.True(events.Count(value => value.Kind == BattleEventKind.Damage && value.Side == 1) > left.TotalImpacts);
        Assert.Equal(0, left.TotalZoneTicks);
    }

    [Fact]
    public void Strongest_hostile_slow_applies_without_stacking_and_expires_while_friendly_is_immune()
    {
        var slow = new BattleZoneDefinition("slow", 10f, 1, .1f, .3f, .5f);
        var stronger = new BattleZoneDefinition("stronger", 10f, 1, .1f, .3f, .2f);
        var left = Unit("left", BattleAttackKind.Melee, speed: 1f, range: 0f, cooldown: 10f);
        var right = Unit("right", BattleAttackKind.Melee, speed: 1f, range: 0f, cooldown: 10f);
        using var simulation = Simulation(new BattleDefinitions([left, right], [], [slow, stronger]), true, Stack(0, "left"), Stack(1, "right"));
        var startLeft = State(simulation, "left", 0).Position;
        var startRight = State(simulation, "right", 1).Position;
        Assert.True(simulation.TryQueueZone(new BattleZoneCommand(0, 1, 1, "slow", startRight), out _));
        Assert.True(simulation.TryQueueZone(new BattleZoneCommand(0, 2, 1, "stronger", startRight), out _));
        simulation.Step(); // creates zones after movement
        var beforeSlow = State(simulation, "right", 1).Position;
        simulation.Step(); // strong hostile slow is active
        var slowed = State(simulation, "right", 1).Position;
        var friendly = State(simulation, "left", 0).Position;
        Assert.InRange((slowed - beforeSlow).Length, .019f, .021f);
        Assert.InRange((friendly - startLeft).Length, .199f, .201f);
        simulation.Step(); // zones reach expiry after this movement
        var beforeRestore = State(simulation, "right", 1).Position;
        simulation.Step();
        Assert.InRange((State(simulation, "right", 1).Position - beforeRestore).Length, .099f, .101f);
    }

    [Fact]
    public void Upgraded_damage_is_snapshotted_for_zone_and_dot_even_after_source_death()
    {
        var zone = new BattleZoneDefinition("toxic", 2f, 1, Tick, 1f, 1f, 100);
        var contact = Unit("contact", BattleAttackKind.ContactExplosion, hp: 1, damage: 10, speed: 20f, range: 0f, cooldown: Tick, contactZoneId: "toxic");
        var ally = Unit("ally", BattleAttackKind.Passive, hp: 20, speed: 0f, row: FormationRow.Artillery);
        var target = Unit("target", BattleAttackKind.Passive, hp: 1000, speed: 0f);
        using var zoneSimulation = Simulation(new BattleDefinitions([contact, ally, target], [], [zone]), false,
            new BattleArmyStack(0, "contact", 1, damageBonusPercent: 50), Stack(0, "ally"), Stack(1, "target"));
        var zoneEvents = Events(zoneSimulation, 4);
        Assert.Contains(zoneEvents, value => value.Kind == BattleEventKind.ZoneTick && value.Amount == 15);

        var poison = Unit("poison", BattleAttackKind.Melee, hp: 1, damage: 10, speed: 0f, range: 20f, cooldown: Tick,
            dot: new BattleDamageOverTimeDefinition(100, Tick, 3));
        var attacker = Unit("attacker", BattleAttackKind.Melee, hp: 1000, damage: 1, speed: 0f, range: 20f, cooldown: Tick);
        using var dotSimulation = Simulation(new BattleDefinitions([poison, ally, attacker], [], []), false,
            new BattleArmyStack(0, "poison", 1, damageBonusPercent: 50), Stack(0, "ally"), Stack(1, "attacker"));
        var dotEvents = Events(dotSimulation, 5);
        Assert.DoesNotContain(States(dotSimulation), value => value.DefinitionId == "poison");
        Assert.Contains(dotEvents, value => value.Kind == BattleEventKind.Damage && value.Side == 1 && value.Amount == 5);
    }

    [Fact]
    public void Frequent_same_source_refresh_keeps_fractional_damage_and_new_simulation_has_no_status_carry()
    {
        var poison = Unit("poison", BattleAttackKind.Melee, hp: 100, damage: 1, speed: 0f, range: 20f, cooldown: Tick,
            dot: new BattleDamageOverTimeDefinition(100, Tick, 3));
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, speed: 0f);
        var definitions = new BattleDefinitions([poison, target], [], []);
        using var first = Simulation(definitions, Stack(0, "poison"), Stack(1, "target"));
        var events = Events(first, 5);
        Assert.True(events.Count(value => value.Kind == BattleEventKind.Damage && value.Side == 1) > first.TotalImpacts);
        using var fresh = Simulation(definitions, Stack(0, "poison"), Stack(1, "target"));
        Assert.Equal(100, State(fresh, "target", 1).Hp);
    }

    [Fact]
    public void Different_dot_sources_are_independent_and_dot_can_finish_after_its_source_dies()
    {
        var dot = new BattleDamageOverTimeDefinition(100, Tick, 3);
        var first = Unit("first", BattleAttackKind.Melee, hp: 1, damage: 1, speed: 0f, range: 20f, cooldown: Tick, dot: dot);
        var second = Unit("second", BattleAttackKind.Melee, hp: 100, damage: 1, speed: 0f, range: 20f, cooldown: Tick, dot: dot, row: FormationRow.Artillery);
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, speed: 0f);
        using var independent = Simulation(new BattleDefinitions([first, second, target], [], []), Stack(0, "first"), Stack(0, "second"), Stack(1, "target"));
        Events(independent, 3);
        Assert.Equal(92, State(independent, "target", 1).Hp); // six direct hits and two independent carried DOT points

        var ally = Unit("ally", BattleAttackKind.Passive, hp: 10, speed: 0f, row: FormationRow.Artillery);
        var killer = Unit("killer", BattleAttackKind.Melee, hp: 2, damage: 1, speed: 0f, range: 20f, cooldown: Tick);
        using var persisted = Simulation(new BattleDefinitions([first, ally, killer], [], []), Stack(0, "first"), Stack(0, "ally"), Stack(1, "killer"));
        Events(persisted, 3);
        Assert.DoesNotContain(States(persisted), value => value.DefinitionId == "first");
        Assert.Equal(BattleOutcome.Side0Won, persisted.Outcome);
        var terminal = Assert.Single(persisted.TerminalHits);
        Assert.Equal(1, terminal.Damage);
        Assert.Equal(1, terminal.SourceId);
        Assert.Equal(1f, terminal.TimeWithinTick);
    }

    [Theory]
    [InlineData(1, 4, 4)]
    [InlineData(2, 5, 8)]
    [InlineData(10, 6, 40)]
    public void Dot_fractional_carry_distributes_one_two_and_ten_damage_over_three_ticks(int damage, int expectedEvents, int expectedTotal)
    {
        var poison = Unit("poison", BattleAttackKind.Melee, hp: 100, damage: damage, speed: 0f, range: 20f, cooldown: Tick,
            dot: new BattleDamageOverTimeDefinition(100, Tick, 3));
        var target = Unit("target", BattleAttackKind.Passive, hp: 1000, speed: 0f);
        using var simulation = Simulation(new BattleDefinitions([poison, target], [], []), Stack(0, "poison"), Stack(1, "target"));
        var damageEvents = Events(simulation, 3).Where(value => value.Kind == BattleEventKind.Damage && value.Side == 1).Select(value => value.Amount).ToArray();
        Assert.Equal(expectedEvents, damageEvents.Length);
        Assert.Equal(expectedTotal, damageEvents.Sum());
        if (damage == 10)
            Assert.Equal(new[] { 3, 3, 4 }, damageEvents.Where(value => value < 10).ToArray());
    }

    static BattleUnitDefinition Unit(string id, BattleAttackKind attack, int hp = 100, int damage = 1, float speed = 1f,
        float range = 1f, float cooldown = 1f, string contactZoneId = "", BattleDamageOverTimeDefinition? dot = null,
        FormationRow row = FormationRow.Tank) =>
        new(id, row, attack, hp, damage, speed, .25f, 1f, range, cooldown, attack == BattleAttackKind.Projectile ? "shell" : "", null, contactZoneId, dot);
    static BattleArmyStack Stack(int side, string id) => new(side, id, 1);
    static BattleSimulation Simulation(BattleDefinitions definitions, params BattleArmyStack[] stacks) => Simulation(definitions, false, stacks);
    static BattleSimulation Simulation(BattleDefinitions definitions, bool commands, params BattleArmyStack[] stacks) => new(definitions,
        new BattleRules(Tick, 5f, 8f, 1f, 1.5f, .15f, 3, 4f, 64, commands), new BattleScenarioDefinition("scenario", "scenario", 7, stacks));
    static List<BattleEvent> Events(BattleSimulation simulation, int steps)
    {
        var result = new List<BattleEvent>();
        var batch = new List<BattleEvent>();
        for (int i = 0; i < steps && simulation.Outcome == BattleOutcome.Running; i++) { simulation.Step(); simulation.DrainEvents(batch); result.AddRange(batch); }
        return result;
    }
    static BattleEntityState[] States(BattleSimulation simulation) { var values = new List<BattleEntityState>(); simulation.Capture(values); return values.ToArray(); }
    static BattleEntityState State(BattleSimulation simulation, string id, int side) => States(simulation).Single(value => value.Kind == BattleEntityKind.Unit && value.DefinitionId == id && value.Side == side);
}
