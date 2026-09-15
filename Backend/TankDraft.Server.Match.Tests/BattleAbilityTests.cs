using System.Text.Json;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class BattleAbilityTests
{
    [Fact]
    public void Existing_authored_fixture_keeps_null_abilities_and_original_attack_values()
    {
        var fixture = AuthoredMatchFixture.Load();
        foreach (var id in new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" })
            Assert.Null(fixture.Definitions.Unit(id).Ability);
        Assert.Equal(BattleAttackKind.ContactExplosion, fixture.Definitions.Unit("unit.mines").Attack);
        Assert.Equal(BattleAttackKind.Projectile, fixture.Definitions.Unit("unit.heavy_tank").Attack);
    }

    [Fact]
    public void Legacy_unit_json_without_ability_deserializes_to_null_ability()
    {
        const string json = "{\"Id\":\"legacy\",\"Row\":1,\"Attack\":1,\"MaxHp\":10,\"Damage\":2,\"MoveSpeed\":1,\"Radius\":0.2,\"Mass\":1,\"Range\":2,\"CooldownSeconds\":1,\"ProjectileId\":\"projectile.shell\"}";
        var unit = JsonSerializer.Deserialize<BattleUnitDefinition>(json);
        Assert.NotNull(unit);
        Assert.Null(unit!.Ability);
        Assert.Equal(BattleAttackKind.Projectile, unit.Attack);
    }

    [Fact]
    public void Ability_definition_rejects_nonfinite_and_recursive_spawn_content()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.RepairPulse, float.NaN, 1f, 1));
        Assert.Throws<ArgumentException>(() => new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, .1f, 1f, 0, "", 1f));

        var self = Unit("self", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, .1f, 1f, 0, "self", 1f));
        Assert.Throws<ArgumentException>(() => Definitions(self));

        var child = Unit("child", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, .1f, 1f, 0, "parent", 1f));
        var parent = Unit("parent", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, .1f, 1f, 0, "child", 1f));
        Assert.Throws<ArgumentException>(() => Definitions(parent, child));
    }

    [Fact]
    public void Passive_alone_allows_zero_damage()
    {
        var passive = Unit("passive-zero", BattleAttackKind.Passive, damage: 0);
        Assert.Equal(0, passive.Damage);
        Assert.Throws<ArgumentOutOfRangeException>(() => Unit("melee-zero", BattleAttackKind.Melee, damage: 0));
    }

    [Fact]
    public void Repair_pulse_heals_lowest_damaged_friendly_only_and_clamps()
    {
        var repair = Unit("repair", BattleAttackKind.Passive, row: FormationRow.Artillery,
            ability: new BattleUnitAbilityDefinition(BattleUnitAbilityKind.RepairPulse, Tick, 20f, 50));
        var ally = Unit("ally", BattleAttackKind.Passive, hp: 20, row: FormationRow.Tank);
        var enemy = Unit("enemy", BattleAttackKind.Melee, damage: 4, speed: 0f, range: 20f, cooldown: Tick * 2f);
        using var simulation = Simulation(Definitions(repair, ally, enemy),
            Stack(0, "repair"), Stack(0, "ally"), Stack(1, "enemy"));

        int damaged = 20;
        for (int index = 0; index < 8 && damaged == 20; index++)
        {
            simulation.Step();
            damaged = UnitState(simulation, "ally", 0).Hp;
        }
        Assert.True(damaged < 20);
        simulation.Step();
        var healed = UnitState(simulation, "ally", 0);
        var hostile = UnitState(simulation, "enemy", 1);
        Assert.True(damaged < healed.MaxHp);
        Assert.True(healed.Hp > damaged);
        Assert.Equal(hostile.MaxHp, hostile.Hp);
        Assert.InRange(healed.Hp, 1, healed.MaxHp);
    }

    [Fact]
    public void Spawn_unit_uses_stable_ids_lifetime_capacity_and_no_recursive_ability()
    {
        var child = Unit("child", BattleAttackKind.Passive, hp: 4, radius: .2f);
        var spawner = Unit("spawner", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", Tick * 2f));
        var enemy = Unit("enemy", BattleAttackKind.Passive);
        using var simulation = Simulation(Definitions(spawner, child, enemy), 3,
            Stack(0, "spawner"), Stack(1, "enemy"));

        simulation.Step();
        var first = States(simulation).Single(value => value.DefinitionId == "child");
        Assert.InRange(first.Position.X, -Rules.HalfWidth + first.Radius, Rules.HalfWidth - first.Radius);
        Assert.InRange(first.Position.Y, -Rules.HalfHeight + first.Radius, Rules.HalfHeight - first.Radius);
        Assert.Equal(3, first.Id);

        simulation.Step();
        simulation.Step();
        Assert.DoesNotContain(States(simulation), value => value.Id == first.Id);
        Assert.Equal(BattleOutcome.Running, simulation.Outcome);

        var longLivedSpawner = Unit("spawner", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", 1f));
        using var roomy = Simulation(Definitions(longLivedSpawner, child, enemy), 4, Stack(0, "spawner"), Stack(1, "enemy"));
        roomy.Step();
        roomy.Step();
        var childIds = States(roomy).Where(value => value.DefinitionId == "child").Select(value => value.Id).OrderBy(value => value).ToArray();
        Assert.Equal(new[] { 3, 4 }, childIds);
    }

    [Fact]
    public void Spawn_damage_percent_uses_upgraded_parent_damage_and_not_child_authored_damage()
    {
        var child = Unit("child", BattleAttackKind.Melee, hp: 20, damage: 7, speed: 0f, range: 20f, cooldown: Tick);
        var engineer = Unit("engineer", BattleAttackKind.Passive, damage: 100, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", 5f, 50));
        var enemy = Unit("enemy", BattleAttackKind.Passive, hp: 300, speed: 0f);
        using var simulation = Simulation(Definitions(engineer, child, enemy), 3,
            new BattleArmyStack(0, "engineer", 1, damageBonusPercent: 50), Stack(1, "enemy"));

        var events = new List<BattleEvent>();
        var targetDamages = new List<int>();
        for (int index = 0; index < 8; index++)
        {
            simulation.Step();
            simulation.DrainEvents(events);
            targetDamages.AddRange(events.Where(value => value.Kind == BattleEventKind.Damage && value.Side == 1).Select(value => value.Amount));
        }
        Assert.Contains(75, targetDamages);
        Assert.DoesNotContain(7, targetDamages);
    }

    [Fact]
    public void Last_summon_expiry_resolves_the_common_outcome_without_a_death_event()
    {
        var child = Unit("child", BattleAttackKind.Passive, hp: 200);
        var spawner = Unit("spawner", BattleAttackKind.Passive, hp: 10, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", Tick * 2f));
        var enemy = Unit("enemy", BattleAttackKind.Melee, hp: 100, damage: 100, speed: 0f, range: 20f, cooldown: Tick);
        using var simulation = Simulation(Definitions(spawner, child, enemy), Stack(0, "spawner"), Stack(1, "enemy"));

        simulation.Step();
        Assert.Contains(States(simulation), value => value.DefinitionId == "child" && value.Side == 0);
        simulation.Step();
        simulation.Step();
        Assert.Equal(BattleOutcome.Side1Won, simulation.Outcome);
        Assert.Equal(1, simulation.TotalDeaths);
        Assert.DoesNotContain(States(simulation), value => value.Side == 0);
    }

    [Fact]
    public void Both_last_summons_expiring_resolve_once_with_the_common_seed_tie_break()
    {
        var child = Unit("child", BattleAttackKind.Passive, hp: 200);
        var spawner = Unit("spawner", BattleAttackKind.Melee, hp: 10, damage: 100, speed: 0f, range: 20f, cooldown: Tick,
            ability: new BattleUnitAbilityDefinition(BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", Tick * 2f));
        var definitions = Definitions(spawner, child);
        using var left = Simulation(definitions, Stack(0, "spawner"), Stack(1, "spawner"));
        using var right = Simulation(definitions, Stack(0, "spawner"), Stack(1, "spawner"));
        for (int index = 0; index < 3; index++) { left.Step(); right.Step(); }
        Assert.True(left.ResolutionUsedRandomTieBreak, left.Outcome + " deaths=" + left.TotalDeaths + " states=" + string.Join(";", States(left).Select(value => value.Side + ":" + value.DefinitionId + ":" + value.Hp)));
        Assert.Equal(left.Outcome, right.Outcome);
        Assert.Equal(left.TieBreakSeed, right.TieBreakSeed);
        Assert.True(left.Outcome is BattleOutcome.Side0Won or BattleOutcome.Side1Won);
        Assert.Equal(2, left.TotalDeaths);
    }

    [Fact]
    public void Opening_jump_is_bounded_non_overlapping_and_symmetric_for_both_sides()
    {
        var jumper = Unit("jumper", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(BattleUnitAbilityKind.OpeningBacklineJump));
        var reserve = Unit("reserve", BattleAttackKind.Passive, row: FormationRow.Artillery);
        using var simulation = Simulation(Definitions(jumper, reserve),
            Stack(0, "jumper"), Stack(0, "reserve"), Stack(1, "jumper"), Stack(1, "reserve"));

        var states = States(simulation).Where(value => value.Kind == BattleEntityKind.Unit).ToArray();
        Assert.All(states, value =>
        {
            Assert.InRange(value.Position.X, -Rules.HalfWidth + value.Radius, Rules.HalfWidth - value.Radius);
            Assert.InRange(value.Position.Y, -Rules.HalfHeight + value.Radius, Rules.HalfHeight - value.Radius);
        });
        foreach (var left in states)
            foreach (var right in states.Where(value => value.Id > left.Id))
                Assert.True((left.Position - right.Position).Length + .00001f >= left.Radius + right.Radius);
        var side0Jumper = states.Single(value => value.DefinitionId == "jumper" && value.Side == 0);
        var side1Jumper = states.Single(value => value.DefinitionId == "jumper" && value.Side == 1);
        Assert.True(side0Jumper.Position.Y > 0f);
        Assert.True(side1Jumper.Position.Y < 0f);
        Assert.Equal(side0Jumper.Position.X, side1Jumper.Position.X);
        Assert.Equal(side0Jumper.Position.Y, -side1Jumper.Position.Y);
    }

    [Fact]
    public void Passive_never_shoots_and_melee_deals_direct_damage_without_self_explosion()
    {
        var passive = Unit("passive", BattleAttackKind.Passive, speed: 0f, range: 20f);
        var melee = Unit("melee", BattleAttackKind.Melee, hp: 30, damage: 7, speed: 0f, range: 20f, cooldown: Tick);
        using var simulation = Simulation(Definitions(passive, melee), Stack(0, "melee"), Stack(1, "passive"));

        simulation.Step();
        Assert.Equal(1, simulation.TotalShots);
        Assert.Equal(30, UnitState(simulation, "melee", 0).Hp);
        Assert.True(UnitState(simulation, "passive", 1).Hp < UnitState(simulation, "passive", 1).MaxHp);
    }

    [Fact]
    public void Ability_round_trip_preserves_authored_values()
    {
        var expected = Unit("export", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, .5f, 2f, 0, "child", 5f, 50));
        var json = JsonSerializer.Serialize(expected);
        var actual = JsonSerializer.Deserialize<BattleUnitDefinition>(json);
        Assert.NotNull(actual);
        Assert.NotNull(actual!.Ability);
        Assert.Equal(BattleUnitAbilityKind.SpawnUnit, actual.Ability!.Kind);
        Assert.Equal(.5f, actual.Ability.IntervalSeconds);
        Assert.Equal(2f, actual.Ability.Radius);
        Assert.Equal("child", actual.Ability.SpawnUnitId);
        Assert.Equal(5f, actual.Ability.SpawnLifetimeSeconds);
        Assert.Equal(50, actual.Ability.SpawnDamagePercent);
    }

    [Fact]
    public void Ability_simulation_is_deterministic_for_the_same_seed_and_roster()
    {
        var child = Unit("child", BattleAttackKind.Passive, hp: 4, radius: .2f);
        var spawner = Unit("spawner", BattleAttackKind.Passive, ability: new BattleUnitAbilityDefinition(
            BattleUnitAbilityKind.SpawnUnit, Tick, 4f, 0, "child", 1f));
        var enemy = Unit("enemy", BattleAttackKind.Passive);
        using var left = Simulation(Definitions(spawner, child, enemy), 6, Stack(0, "spawner"), Stack(1, "enemy"));
        using var right = Simulation(Definitions(spawner, child, enemy), 6, Stack(0, "spawner"), Stack(1, "enemy"));
        for (int index = 0; index < 4; index++) { left.Step(); right.Step(); }
        Assert.Equal(Signature(States(left)), Signature(States(right)));
    }

    const float Tick = 1f / 30f;
    static readonly BattleRules Rules = new(Tick, 5f, 8f, 1f, 1.5f, .15f, 3, 4f, 64, false);

    static BattleArmyStack Stack(int side, string id) => new(side, id, 1);
    static BattleUnitDefinition Unit(string id, BattleAttackKind attack, int hp = 100, int damage = 1, float speed = 1f,
        float radius = .25f, float mass = 1f, float range = 1f, float cooldown = 1f, FormationRow row = FormationRow.Tank,
        BattleUnitAbilityDefinition? ability = null) => new(id, row, attack, hp, damage, speed, radius, mass, range, cooldown, "", ability);
    static BattleDefinitions Definitions(params BattleUnitDefinition[] units) => new(units, [], []);
    static BattleSimulation Simulation(BattleDefinitions definitions, params BattleArmyStack[] stacks) => Simulation(definitions, Rules.MaxEntities, stacks);
    static BattleSimulation Simulation(BattleDefinitions definitions, int maxEntities, params BattleArmyStack[] stacks) =>
        new(definitions, new BattleRules(Tick, Rules.HalfWidth, Rules.HalfHeight, Rules.FrontOffset, Rules.RowGap, Rules.UnitGap,
            Rules.SeparationIterations, Rules.SeparationSpeed, maxEntities, false), new BattleScenarioDefinition("scenario", "scenario", 7, stacks));
    static BattleEntityState[] States(BattleSimulation simulation)
    {
        var states = new List<BattleEntityState>();
        simulation.Capture(states);
        return states.ToArray();
    }
    static BattleEntityState UnitState(BattleSimulation simulation, string id, int side) =>
        States(simulation).Single(value => value.Kind == BattleEntityKind.Unit && value.DefinitionId == id && value.Side == side);
    static string[] Signature(IEnumerable<BattleEntityState> states) => states.OrderBy(value => value.Id).Select(value =>
        value.Id + ":" + value.Side + ":" + value.DefinitionId + ":" + value.Hp + ":" + value.Position.X + ":" + value.Position.Y).ToArray();
}
