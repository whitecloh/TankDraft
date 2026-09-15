using System.Text.Json;
using System.Reflection;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class BattleDefenseTests
{
    const float Tick = .1f;

    [Fact]
    public void Definitions_default_round_trip_and_validate_new_fields()
    {
        const string legacy = "{\"Id\":\"old\",\"Row\":1,\"Attack\":3,\"MaxHp\":10,\"Damage\":2,\"MoveSpeed\":1,\"Radius\":0.2,\"Mass\":1,\"Range\":2,\"CooldownSeconds\":1,\"ProjectileId\":\"\"}";
        var old = JsonSerializer.Deserialize<BattleUnitDefinition>(legacy)!;
        Assert.Null(old.Shield); Assert.False(old.BlocksFirstHit); Assert.Null(old.Magazine); Assert.Equal(0, old.LifeStealPercent);
        var unit = Unit("new", BattleAttackKind.Melee, shield: new BattleShieldDefinition(.2f, 2f, 50, .3f), block: true, magazine: new BattleMagazineDefinition(2, .4f), steal: 25);
        var restored = JsonSerializer.Deserialize<BattleUnitDefinition>(JsonSerializer.Serialize(unit))!;
        Assert.Equal(50, restored.Shield!.CapacityHpPercent); Assert.True(restored.BlocksFirstHit); Assert.Equal(2, restored.Magazine!.Shots); Assert.Equal(25, restored.LifeStealPercent);
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleShieldDefinition(float.NaN, 1, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleShieldDefinition(1, 1, 0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BattleMagazineDefinition(0, 1));
        Assert.Throws<ArgumentException>(() => Unit("bad", BattleAttackKind.Passive, magazine: new BattleMagazineDefinition(1, 1)));
    }

    [Fact]
    public void Shield_refresh_replaces_capacity_before_damage()
    {
        var shield = Unit("shield", BattleAttackKind.Passive, hp: 100, shield: new BattleShieldDefinition(Tick, 20, 50, Tick * 2));
        var enemy = Unit("enemy", BattleAttackKind.Melee, damage: 30, cooldown: Tick, range: 20);
        using var sim = Sim(Defs(shield, enemy), Stack(0, "shield"), Stack(1, "enemy"));
        sim.Step(); Assert.Equal(100, State(sim, "shield", 0).Hp); // first 30 absorbed
        sim.Step(); Assert.Equal(100, State(sim, "shield", 0).Hp); // refresh restores to capacity, not additive
        sim.Step(); Assert.Equal(100, State(sim, "shield", 0).Hp);
    }

    [Fact]
    public void First_hit_blocks_once_and_cancels_direct_poison_but_not_periodic_damage()
    {
        var poison = Unit("poison", BattleAttackKind.Melee, damage: 10, cooldown: Tick, range: 20, dot: new BattleDamageOverTimeDefinition(100, Tick, 2));
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, block: true);
        using var sim = Sim(Defs(poison, target), Stack(0, "poison"), Stack(1, "target"));
        sim.Step(); Assert.Equal(100, State(sim, "target", 1).Hp);
        sim.Step(); Assert.Equal(85, State(sim, "target", 1).Hp); // next direct hit plus its first periodic tick
    }

    [Fact]
    public void Magazine_shots_and_lifesteal_actual_damage_are_applied_together()
    {
        var attacker = Unit("attacker", BattleAttackKind.Melee, hp: 100, damage: 10, cooldown: Tick, range: 20, magazine: new BattleMagazineDefinition(2, .4f), steal: 50);
        var victim = Unit("victim", BattleAttackKind.Melee, hp: 15, damage: 30, cooldown: Tick, range: 20);
        using var sim = Sim(Defs(attacker, victim), Stack(0, "attacker"), Stack(1, "victim"));
        for (var i = 0; i < 5 && sim.Outcome == BattleOutcome.Running; i++) sim.Step();
        // The first direct hit loses only 10 HP; healing uses that actual loss, not the victim's max HP.
        Assert.True(sim.TotalShots >= 2);
        Assert.Equal(47, State(sim, "attacker", 0).Hp); // 10 direct loss heals 5 before the victim's later lethal hit.
    }

    [Fact]
    public void Shield_absorbs_excess_then_expires_without_a_new_pulse()
    {
        var protectedUnit = Unit("protected", BattleAttackKind.Passive, hp: 100,
            shield: new BattleShieldDefinition(1f, 20f, 50, Tick * 2));
        var enemy = Unit("enemy", BattleAttackKind.Melee, damage: 30, cooldown: Tick, range: 20);
        using var sim = Sim(Defs(protectedUnit, enemy), Stack(0, "protected"), Stack(1, "enemy"));
        // The first pulse is deliberately at one second, so there is no shield initially.
        for (var i = 0; i < 3; i++) sim.Step();
        Assert.Equal(10, State(sim, "protected", 0).Hp);

        // The pulse at tick 2 grants capacity 50. Its .2s expiry passes before tick 4;
        // the interval is longer, proving that the old pool is not retained or stacked.
        var pulsed = Unit("pulsed", BattleAttackKind.Passive, hp: 100,
            shield: new BattleShieldDefinition(Tick * 3, 20f, 50, Tick * 2));
        using var active = Sim(Defs(pulsed, enemy), Stack(0, "pulsed"), Stack(1, "enemy"));
        active.Step(); Assert.Equal(70, State(active, "pulsed", 0).Hp);
        active.Step(); Assert.Equal(40, State(active, "pulsed", 0).Hp);
        active.Step(); Assert.Equal(40, State(active, "pulsed", 0).Hp);
        active.Step(); Assert.Equal(30, State(active, "pulsed", 0).Hp);
        active.Step(); Assert.Equal(0, active.AliveSide0); // expiry, then excess reaches HP
    }

    [Fact]
    public void First_hit_charge_consumes_once_for_two_same_timestamp_hits()
    {
        var first = Unit("first", BattleAttackKind.Melee, damage: 10, cooldown: Tick, range: 20);
        var second = Unit("second", BattleAttackKind.Melee, damage: 10, cooldown: Tick, range: 20);
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, block: true);
        using var sim = Sim(Defs(first, second, target), Stack(0, "first"), Stack(0, "second"), Stack(1, "target"));
        sim.Step();
        Assert.Equal(90, State(sim, "target", 1).Hp);
    }

    [Fact]
    public void Six_shots_have_one_reload_gap_and_speed_bonus_shortens_it()
    {
        var shooter = Unit("shooter", BattleAttackKind.Melee, damage: 1, cooldown: Tick, range: 20,
            magazine: new BattleMagazineDefinition(6, .6f));
        var dummy = Unit("dummy", BattleAttackKind.Passive, hp: 10000);
        using var normal = Sim(Defs(shooter, dummy), Stack(0, "shooter"), Stack(1, "dummy"));
        var normalShots = ShotTicks(normal, 30);
        Assert.True(normalShots.Length >= 7);
        var ordinaryGap = normalShots[5] - normalShots[4];
        var reloadGap = normalShots[6] - normalShots[5];
        Assert.Equal(1, ordinaryGap);
        Assert.True(reloadGap >= 6, "reload gap=" + reloadGap);

        using var fast = Sim(Defs(shooter, dummy), new BattleArmyStack(0, "shooter", 1, attackSpeedBonusPercent: 100), Stack(1, "dummy"));
        var fastShots = ShotTicks(fast, 30);
        Assert.True(fastShots[6] - fastShots[5] < reloadGap);
    }

    [Fact]
    public void Lifesteal_is_direct_actual_damage_only_and_never_resurrects()
    {
        var lifesteal = Unit("lifesteal", BattleAttackKind.Melee, hp: 100, damage: 10, cooldown: Tick, range: 20, steal: 100);
        var plain = Unit("plain", BattleAttackKind.Melee, hp: 100, damage: 10, cooldown: Tick, range: 20);
        var victim = Unit("victim", BattleAttackKind.Melee, hp: 15, damage: 30, cooldown: Tick, range: 20);
        using var withSteal = Sim(Defs(lifesteal, victim), Stack(0, "lifesteal"), Stack(1, "victim"));
        using var withoutSteal = Sim(Defs(plain, victim), Stack(0, "plain"), Stack(1, "victim"));
        for (var i = 0; i < 5; i++) { withSteal.Step(); withoutSteal.Step(); }
        Assert.True(State(withSteal, "lifesteal", 0).Hp > State(withoutSteal, "plain", 0).Hp);

        var killer = Unit("killer", BattleAttackKind.Melee, hp: 10, damage: 100, cooldown: Tick, range: 20, steal: 100);
        var retaliation = Unit("retaliation", BattleAttackKind.Melee, hp: 10, damage: 100, cooldown: Tick, range: 20);
        using var simultaneous = Sim(Defs(killer, retaliation), Stack(0, "killer"), Stack(1, "retaliation"));
        simultaneous.Step();
        Assert.Equal(0, simultaneous.AliveSide0); // healing runs after deaths and cannot resurrect.
    }

    [Fact]
    public void Reload_continues_while_the_magazine_owner_has_no_target()
    {
        var shooter = Unit("shooter", BattleAttackKind.Melee, damage: 10, cooldown: Tick, range: 20,
            magazine: new BattleMagazineDefinition(1, .6f));
        var livingEnemy = Unit("living", BattleAttackKind.Passive, hp: 10000);
        using var sim = Sim(Defs(shooter, livingEnemy), Stack(0, "shooter"), Stack(1, "living"));
        for (var i = 0; i < 20 && sim.TotalShots == 0; i++) sim.Step();
        Assert.Equal(BattleOutcome.Running, sim.Outcome);
        var afterShot = UnitFloat(sim, 1, "Remaining");
        SetUnitField(sim, 1, "TargetId", 0);
        for (var i = 0; i < 3; i++) InvokePrivate(sim, "Attack");
        var whileNoTarget = UnitFloat(sim, 1, "Remaining");
        Assert.True(afterShot > whileNoTarget && whileNoTarget > 0f, $"reload {afterShot} -> {whileNoTarget}");
    }

    [Fact]
    public void Zone_damage_does_not_consume_first_hit_or_grant_lifesteal()
    {
        var direct = Unit("direct", BattleAttackKind.Melee, hp: 100, damage: 10, cooldown: 10, range: 20, steal: 100);
        var target = Unit("target", BattleAttackKind.Passive, hp: 100, block: true);
        var zone = new BattleZoneDefinition("zone", 20, 5, Tick, 1);
        using var sim = SimCommands(new BattleDefinitions([direct, target], [], [zone]), Stack(0, "direct"), Stack(1, "target"));
        var targetPosition = State(sim, "target", 1).Position;
        Assert.True(sim.TryQueueZone(new BattleZoneCommand(0, 1, 1, "zone", targetPosition), out _));
        sim.Step(); sim.Step();
        var afterZone = State(sim, "target", 1).Hp;
        Assert.True(afterZone < 100);
        SetUnitField(sim, 1, "Hp", 50);
        SetUnitField(sim, 1, "Remaining", 0f);
        SetUnitField(sim, 1, "TargetId", 2);
        InvokePrivate(sim, "Attack");
        InvokePrivate(sim, "Resolve");
        Assert.Equal(afterZone, State(sim, "target", 1).Hp); // first direct hit is still blocked
        Assert.Equal(50, State(sim, "direct", 0).Hp); // zone and blocked direct hit do not heal
    }

    [Fact]
    public void Shield_pulse_covers_caster_and_friendly_not_enemy_and_uses_upgraded_caster_max_hp()
    {
        var caster = Unit("caster", BattleAttackKind.Passive, hp: 100, shield: new BattleShieldDefinition(Tick, 20, 50, 1));
        var ally = Unit("ally", BattleAttackKind.Passive, hp: 100);
        var enemy = Unit("enemy", BattleAttackKind.Passive, hp: 100);
        using var sim = Sim(Defs(caster, ally, enemy), new BattleArmyStack(0, "caster", 1, hpBonusPercent: 100), Stack(0, "ally"), Stack(1, "enemy"));
        sim.Step();
        Assert.Equal(200, State(sim, "caster", 0).MaxHp);
        Assert.Equal(100, UnitInt(sim, 1, "ShieldHp"));
        Assert.Equal(100, UnitInt(sim, 2, "ShieldHp"));
        Assert.Equal(0, UnitInt(sim, 3, "ShieldHp"));
    }

    static BattleUnitDefinition Unit(string id, BattleAttackKind attack, int hp = 100, int damage = 1, float cooldown = 1, float range = 1,
        BattleDamageOverTimeDefinition? dot = null, BattleShieldDefinition? shield = null, bool block = false, BattleMagazineDefinition? magazine = null, int steal = 0) =>
        new(id, FormationRow.Tank, attack, hp, damage, 0, .2f, 1, range, cooldown, "", null, "", dot, shield, block, magazine, steal);
    static BattleDefinitions Defs(params BattleUnitDefinition[] units) => new(units, [], []);
    static BattleArmyStack Stack(int side, string id) => new(side, id, 1);
    static BattleSimulation Sim(BattleDefinitions definitions, params BattleArmyStack[] stacks) => new(definitions, new BattleRules(Tick, 5, 8, 1, 1.5f, .15f, 3, 4, 32, false), new BattleScenarioDefinition("s", "s", 1, stacks));
    static BattleSimulation SimCommands(BattleDefinitions definitions, params BattleArmyStack[] stacks) => new(definitions, new BattleRules(Tick, 5, 8, 1, 1.5f, .15f, 3, 4, 32, true), new BattleScenarioDefinition("s", "s", 1, stacks));
    static BattleEntityState State(BattleSimulation simulation, string id, int side) => States(simulation).Single(value => value.Kind == BattleEntityKind.Unit && value.DefinitionId == id && value.Side == side);
    static BattleEntityState[] States(BattleSimulation simulation) { var states = new List<BattleEntityState>(); simulation.Capture(states); return states.ToArray(); }
    static long[] ShotTicks(BattleSimulation simulation, int steps)
    {
        var result = new List<long>(); var events = new List<BattleEvent>();
        for (var i = 0; i < steps; i++) { simulation.Step(); simulation.DrainEvents(events); result.AddRange(events.Where(value => value.Kind == BattleEventKind.Shot).Select(value => value.Tick)); }
        return result.ToArray();
    }
    static int UnitInt(BattleSimulation simulation, int stableId, string field) => (int)UnitField(simulation, stableId, field)!;
    static float UnitFloat(BattleSimulation simulation, int stableId, string field) => (float)UnitField(simulation, stableId, field)!;
    static object? UnitField(BattleSimulation simulation, int stableId, string field)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        var type = typeof(BattleSimulation);
        var identityPool = type.GetField("_identity", flags)!.GetValue(simulation)!;
        var unitPool = type.GetField("_unit", flags)!.GetValue(simulation)!;
        var identitySparse = (int[])identityPool.GetType().GetField("_sparseItems", flags)!.GetValue(identityPool)!;
        var identityDense = (Array)identityPool.GetType().GetField("_denseItems", flags)!.GetValue(identityPool)!;
        var unitSparse = (int[])unitPool.GetType().GetField("_sparseItems", flags)!.GetValue(unitPool)!;
        var unitDense = (Array)unitPool.GetType().GetField("_denseItems", flags)!.GetValue(unitPool)!;
        for (var entity = 0; entity < identitySparse.Length; entity++)
        {
            var index = identitySparse[entity];
            if (index == 0 || (int)identityDense.GetValue(index)!.GetType().GetField("Id", flags)!.GetValue(identityDense.GetValue(index))! != stableId)
                continue;
            return unitDense.GetValue(unitSparse[entity])!.GetType().GetField(field, flags)!.GetValue(unitDense.GetValue(unitSparse[entity]));
        }
        throw new Xunit.Sdk.XunitException("Unit " + stableId + " was not found.");
    }
    static void SetUnitField(BattleSimulation simulation, int stableId, string field, object value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
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
    static void InvokePrivate(BattleSimulation simulation, string method) => typeof(BattleSimulation).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(simulation, null);
}
