using System.Text.Json;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using Xunit;

namespace TankDraft.Server.Match.Tests;

public sealed class FormationPriorityTests
{
    [Fact]
    public void Four_same_row_groups_are_priority_ordered_independent_of_stack_input_and_mirrored_without_overlap()
    {
        var definitions = Definitions();
        var ordered = Scenario([Stack(0, "low"), Stack(0, "mid"), Stack(0, "high"), Stack(0, "top"),
            Stack(1, "low"), Stack(1, "mid"), Stack(1, "high"), Stack(1, "top")]);
        var shuffled = Scenario([Stack(1, "top"), Stack(1, "high"), Stack(1, "mid"), Stack(1, "low"),
            Stack(0, "top"), Stack(0, "high"), Stack(0, "mid"), Stack(0, "low")]);

        using var left = new BattleSimulation(definitions, Rules(), ordered);
        using var right = new BattleSimulation(definitions, Rules(), shuffled);
        var leftStates = States(left);
        var rightStates = States(right);

        Assert.Equal(new[] { "top", "high", "mid", "low" }, GroupFrontOrder(leftStates, 0));
        Assert.Equal(new[] { "top", "high", "mid", "low" }, GroupFrontOrder(leftStates, 1));
        Assert.Equal(Signature(leftStates), Signature(rightStates));
        AssertMirrored(leftStates);
        AssertNoOverlap(leftStates);
    }

    [Fact]
    public void Formation_rejects_four_maximum_groups_that_do_not_fit_the_authored_arena()
    {
        var definitions = new BattleDefinitions([Unit("a", 4, .6f), Unit("b", 3, .6f), Unit("c", 2, .6f), Unit("d", 1, .6f)], [], []);
        var stacks = new[] { Stack(0, "a", 16), Stack(0, "b", 16), Stack(0, "c", 16), Stack(0, "d", 16),
            Stack(1, "a", 16), Stack(1, "b", 16), Stack(1, "c", 16), Stack(1, "d", 16) };

        Assert.Throws<ArgumentException>(() => new BattleSimulation(definitions, Rules(), Scenario(stacks)));
    }

    [Fact]
    public void Every_authored_four_type_maximum_formation_fits_for_each_preparation_side()
    {
        var fixture = AuthoredMatchFixture.Load();
        var unitIds = fixture.Definitions.Units.Select(value => value.Id).ToArray();
        for (var a = 0; a < unitIds.Length - 3; a++)
        for (var b = a + 1; b < unitIds.Length - 2; b++)
        for (var c = b + 1; c < unitIds.Length - 1; c++)
        for (var d = c + 1; d < unitIds.Length; d++)
        {
            var groupIds = new[] { unitIds[a], unitIds[b], unitIds[c], unitIds[d] };
            for (var side = 0; side < 2; side++)
            {
                BattleSimulation? simulation = null;
                var exception = Record.Exception(() => simulation = new BattleSimulation(fixture.Definitions, fixture.BattleRules,
                    new BattleScenarioDefinition("authored-" + a + "-" + b + "-" + c + "-" + d + "-" + side,
                        "authored", 1, groupIds.Select(id => Stack(side, id, 12)).ToArray(), allowIncomplete: true), preparation: true));
                Assert.True(exception is null, "side=" + side + " groups=" + string.Join(",", groupIds) + " error=" + exception);
                var created = simulation!;
                using (created)
                {
                var states = States(created);
                Assert.Equal(48, states.Length);
                AssertNoOverlap(states);
                Assert.All(states, value =>
                {
                    Assert.InRange(Math.Abs(value.Position.X) + value.Radius, 0f, fixture.BattleRules.HalfWidth);
                    Assert.InRange(Math.Abs(value.Position.Y) + value.Radius, 0f, fixture.BattleRules.HalfHeight);
                });
                }
            }
        }
    }

    [Fact]
    public void Incomplete_preparation_draft_skips_opening_backline_jump()
    {
        var jumper = new BattleUnitDefinition("jumper", FormationRow.Tank, BattleAttackKind.Passive, 10, 0, 0, .25f, 1,
            0, 1, "", new BattleUnitAbilityDefinition(BattleUnitAbilityKind.OpeningBacklineJump), formationPriority: 1);
        using var simulation = new BattleSimulation(new BattleDefinitions([jumper], [], []), Rules(),
            new BattleScenarioDefinition("partial", "partial", 1, [Stack(0, "jumper")], allowIncomplete: true), preparation: true);

        var state = Assert.Single(States(simulation));
        Assert.Equal(0, state.Side);
        Assert.True(state.Position.Y < 0f);
    }

    [Fact]
    public void Formation_priority_round_trips_through_json_and_authored_heavy_precedes_medium()
    {
        var serialized = JsonSerializer.Serialize(Unit("json", 17));
        var restored = JsonSerializer.Deserialize<BattleUnitDefinition>(serialized);
        Assert.NotNull(restored);
        Assert.Equal(17, restored!.FormationPriority);

        var authored = AuthoredMatchFixture.Load().Definitions;
        Assert.True(authored.Unit("unit.heavy_tank").FormationPriority > authored.Unit("unit.medium_tank").FormationPriority);
    }

    [Fact]
    public void Scenario_still_rejects_duplicate_unit_types_per_side()
    {
        Assert.Throws<ArgumentException>(() => Scenario([Stack(0, "high"), Stack(0, "high"), Stack(1, "high")]));
    }

    static BattleDefinitions Definitions() => new([Unit("low", 1), Unit("mid", 2), Unit("high", 3), Unit("top", 4)], [], []);
    static BattleUnitDefinition Unit(string id, int priority, float radius = .25f) =>
        new(id, FormationRow.Tank, BattleAttackKind.Passive, 10, 0, 0, radius, 1, 0, 1, "", formationPriority: priority);
    static BattleArmyStack Stack(int side, string id, int count = 1) => new(side, id, count);
    static BattleScenarioDefinition Scenario(BattleArmyStack[] stacks) => new("formation", "formation", 7, stacks);
    static BattleRules Rules() => new(.1f, 4.5f, 7.5f, 1.3f, 1.45f, .15f, 3, 4f, 128, false);

    static BattleEntityState[] States(BattleSimulation simulation)
    {
        var states = new List<BattleEntityState>();
        simulation.Capture(states);
        return states.Where(value => value.Kind == BattleEntityKind.Unit).ToArray();
    }

    static string[] GroupFrontOrder(IEnumerable<BattleEntityState> states, int side) => states
        .Where(value => value.Side == side)
        .OrderBy(value => Math.Abs(value.Position.Y))
        .Select(value => value.DefinitionId)
        .ToArray();

    static string[] Signature(IEnumerable<BattleEntityState> states) => states
        .OrderBy(value => value.Side).ThenBy(value => value.DefinitionId)
        .Select(value => value.Side + ":" + value.DefinitionId + ":" + value.Position.X + ":" + value.Position.Y)
        .ToArray();

    static void AssertMirrored(IEnumerable<BattleEntityState> states)
    {
        foreach (var left in states.Where(value => value.Side == 0))
        {
            var right = Assert.Single(states, value => value.Side == 1 && value.DefinitionId == left.DefinitionId);
            Assert.Equal(left.Position.X, right.Position.X);
            Assert.Equal(left.Position.Y, -right.Position.Y);
        }
    }

    static void AssertNoOverlap(IEnumerable<BattleEntityState> states)
    {
        var values = states.ToArray();
        for (var i = 0; i < values.Length; i++)
            for (var j = i + 1; j < values.Length; j++)
                if (values[i].Side == values[j].Side)
                    Assert.True((values[i].Position - values[j].Position).Length + .00001f >= values[i].Radius + values[j].Radius);
    }
}
