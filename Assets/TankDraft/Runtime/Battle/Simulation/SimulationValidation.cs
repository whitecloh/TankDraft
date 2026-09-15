using System;
using System.Collections.Generic;
using TankDraft.Contracts.Battle;

namespace TankDraft.Simulation
{
    // Diagnostic validation only: its 60-second cap is a failing test, never a match rule.
    public static class SimulationValidation
    {
        public static string Run(BattleDefinitions definitions, BattleRules rules, BattleScenarioDefinition[] scenarios)
        {
            if (definitions == null || rules == null || scenarios == null || scenarios.Length == 0)
                throw new ArgumentException("Definitions, rules, and scenarios are required.");
            int originalUnits = definitions.Units.Count, originalProjectiles = definitions.Projectiles.Count, originalZones = definitions.Zones.Count;
            int completed = 0, observedProjectiles = 0, observedZoneTicks = 0;
            var report = new System.Text.StringBuilder();
            var left = new List<BattleEntityState>();
            var right = new List<BattleEntityState>();
            var leftEvents = new List<BattleEvent>();
            var rightEvents = new List<BattleEvent>();
            for (int index = 0; index < scenarios.Length; index++)
            {
                var scenario = scenarios[index] ?? throw new ArgumentException("Scenario cannot be null.");
                using (var a = new BattleSimulation(definitions, rules, scenario))
                using (var b = new BattleSimulation(definitions, rules, scenario))
                {
                    int maxTicks = (int)Math.Ceiling(60f / rules.TickSeconds);
                    for (int tick = 0; tick < maxTicks && a.Outcome == BattleOutcome.Running; tick++)
                    {
                        a.Step();
                        b.Step();
                        a.Capture(left);
                        b.Capture(right);
                        a.DrainEvents(leftEvents);
                        b.DrainEvents(rightEvents);
                        AssertSame(left, right, "snapshot");
                        AssertSame(leftEvents, rightEvents, "events");
                        AssertFiniteAndBounded(left, rules);
                        for (int n = 0; n < left.Count; n++)
                            if (left[n].Kind == BattleEntityKind.Projectile)
                                observedProjectiles++;
                    }

                    if (a.Outcome == BattleOutcome.Running)
                        throw new InvalidOperationException("Scenario did not terminate in 60 simulated seconds: " + scenario.Id);
                    if (a.Outcome != b.Outcome)
                        throw new InvalidOperationException("Determinism outcome mismatch.");
                    observedZoneTicks += a.TotalZoneTicks;
                    report.AppendLine(scenario.Id + ": " + a.Outcome + ", ticks=" + a.Tick + ", seconds=" + (a.Tick * rules.TickSeconds).ToString("F2", System.Globalization.CultureInfo.InvariantCulture) + ", shots=" + a.TotalShots + ", impacts=" + a.TotalImpacts + ", zoneTicks=" + a.TotalZoneTicks);
                    completed++;
                }

                ValidateCommands(definitions, rules, scenario);
            }

            if (definitions.Units.Count != originalUnits || definitions.Projectiles.Count != originalProjectiles || definitions.Zones.Count != originalZones)
                throw new InvalidOperationException("Simulation mutated authored definitions.");
            if (observedProjectiles == 0)
                throw new InvalidOperationException("Validation scenarios never exposed a projectile.");
            if (observedZoneTicks == 0 && definitions.Zones.Count > 0)
                throw new InvalidOperationException("Validation scenarios never produced a zone tick.");
            RunOutcomeTimingFixtures();
            return "Validated " + completed + " scenario(s): deterministic snapshots/events, bounded positions, command rejection, terminal outcomes and production subtick extinction fixtures.\n" + report;
        }

        // Exercise the production Resolve path with controlled equal/subtick impact times.
        // Reflection is limited to this diagnostic harness; it is not a gameplay command API.
        internal static void RunOutcomeTimingFixtures()
        {
            ValidateTimeline(false);
            ValidateTimeline(true);
            ValidateNearEqualTimeline();
        }

        static void ValidateTimeline(bool simultaneous)
        {
            var definitions = new BattleDefinitions(new[] { new BattleUnitDefinition("fixture.unit", TankDraft.Contracts.FormationRow.Tank, BattleAttackKind.Projectile, 1, 1, 0, .2f, 1, 10, 1, "fixture.shell") }, new[] { new BattleProjectileDefinition("fixture.shell", 10, .05f, 0, "") }, Array.Empty<BattleZoneDefinition>());
            var rules = new BattleRules(1f / 30, 5, 8, 1, 1, .1f, 2, 3, 32, false);
            var scenario = new BattleScenarioDefinition("fixture", "Fixture", 1, new[] { new BattleArmyStack(0, "fixture.unit", 1), new BattleArmyStack(1, "fixture.unit", 1) });
            using (var simulation = new BattleSimulation(definitions, rules, scenario))
            {
                var states = new List<BattleEntityState>();
                simulation.Capture(states);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object sim = typeof(BattleSimulation).GetField("_sim", flags).GetValue(simulation);
                var hits = (System.Collections.IList)sim.GetType().GetField("Hits").GetValue(sim);
                var hitType = typeof(BattleSimulation).GetNestedType("Hit", System.Reflection.BindingFlags.NonPublic);
                for (int i = 0; i < 2; i++)
                {
                    var target = states[i];
                    var hit = Activator.CreateInstance(hitType);
                    hitType.GetField("Time").SetValue(hit, simultaneous ? .5f : i == 0 ? .25f : .75f);
                    hitType.GetField("Side").SetValue(hit, 1 - target.Side);
                    hitType.GetField("Source").SetValue(hit, 100 + i);
                    hitType.GetField("Position").SetValue(hit, target.Position);
                    hitType.GetField("DirectTarget").SetValue(hit, target.Id);
                    hitType.GetField("Damage").SetValue(hit, 1);
                    hits.Add(hit);
                }

                typeof(BattleSimulation).GetMethod("Resolve", flags).Invoke(simulation, null);
                if (simultaneous)
                {
                    if ((simulation.Outcome != BattleOutcome.Side0Won && simulation.Outcome != BattleOutcome.Side1Won) || !simulation.ResolutionUsedRandomTieBreak || simulation.TieBreakSeed == 0)
                        throw new InvalidOperationException("Exact-time tie-break fixture failed.");
                    if (ResolveExactTie(definitions, rules, scenario) != simulation.Outcome)
                        throw new InvalidOperationException("Exact-time seeded tie-break was not reproducible.");
                }
                else if (simulation.Outcome != BattleOutcome.Side1Won || simulation.ResolutionUsedRandomTieBreak || simulation.TieBreakSeed != 0)
                    throw new InvalidOperationException("Production damage ordering fixture failed.");
                if (!simultaneous && simulation.AliveSide1 != 1)
                    throw new InvalidOperationException("Later in-flight hit changed an already decided battle.");
                var events = new List<BattleEvent>();
                simulation.DrainEvents(events);
                int outcomes = 0, deaths = 0;
                foreach (var e in events)
                {
                    if (e.Kind == BattleEventKind.Outcome)
                        outcomes++;
                    if (e.Kind == BattleEventKind.Death)
                        deaths++;
                }

                if (outcomes != 1 || deaths != (simultaneous ? 2 : 1))
                    throw new InvalidOperationException("Terminal event sequence fixture failed.");
                if (simulation.TerminalHits.Count != (simultaneous ? 2 : 1))
                    throw new InvalidOperationException("Terminal hit trace count fixture failed.");
                if (simultaneous)
                {
                    if (simulation.TerminalHits[0].TimeWithinTick != .5f || simulation.TerminalHits[1].TimeWithinTick != .5f)
                        throw new InvalidOperationException("Equal-time terminal hit trace fixture failed.");
                }
                else if (simulation.TerminalHits[0].TimeWithinTick != .25f || simulation.TerminalHits[0].GroupTime != .25f)
                    throw new InvalidOperationException("Subtick terminal hit trace fixture failed.");
            }
        }

        static BattleOutcome ResolveExactTie(BattleDefinitions definitions, BattleRules rules, BattleScenarioDefinition scenario)
        {
            using (var simulation = new BattleSimulation(definitions, rules, scenario))
            {
                var states = new List<BattleEntityState>();
                simulation.Capture(states);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object sim = typeof(BattleSimulation).GetField("_sim", flags).GetValue(simulation);
                var hits = (System.Collections.IList)sim.GetType().GetField("Hits").GetValue(sim);
                var hitType = typeof(BattleSimulation).GetNestedType("Hit", System.Reflection.BindingFlags.NonPublic);
                for (int i = 0; i < 2; i++)
                {
                    var target = states[i];
                    var hit = Activator.CreateInstance(hitType);
                    hitType.GetField("Time").SetValue(hit, .5f);
                    hitType.GetField("Side").SetValue(hit, 1 - target.Side);
                    hitType.GetField("Source").SetValue(hit, 300 + i);
                    hitType.GetField("Position").SetValue(hit, target.Position);
                    hitType.GetField("DirectTarget").SetValue(hit, target.Id);
                    hitType.GetField("Damage").SetValue(hit, 1);
                    hits.Add(hit);
                }

                typeof(BattleSimulation).GetMethod("Resolve", flags).Invoke(simulation, null);
                if (!simulation.ResolutionUsedRandomTieBreak || simulation.TieBreakSeed == 0)
                    throw new InvalidOperationException("Exact-time replay did not mark a tie-break.");
                return simulation.Outcome;
            }
        }

        // This pair was formerly batched by EqualTimeEpsilon. The earlier lethal hit must now decide the outcome.
        static void ValidateNearEqualTimeline()
        {
            var definitions = new BattleDefinitions(new[] { new BattleUnitDefinition("fixture.unit", TankDraft.Contracts.FormationRow.Tank, BattleAttackKind.Projectile, 1, 1, 0, .2f, 1, 10, 1, "fixture.shell") }, new[] { new BattleProjectileDefinition("fixture.shell", 10, .05f, 0, "") }, Array.Empty<BattleZoneDefinition>());
            var rules = new BattleRules(1f / 30, 5, 8, 1, 1, .1f, 2, 3, 32, false);
            var scenario = new BattleScenarioDefinition("fixture-near", "Fixture near", 1, new[] { new BattleArmyStack(0, "fixture.unit", 1), new BattleArmyStack(1, "fixture.unit", 1) });
            using (var simulation = new BattleSimulation(definitions, rules, scenario))
            {
                var states = new List<BattleEntityState>();
                simulation.Capture(states);
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
                object sim = typeof(BattleSimulation).GetField("_sim", flags).GetValue(simulation);
                var hits = (System.Collections.IList)sim.GetType().GetField("Hits").GetValue(sim);
                var hitType = typeof(BattleSimulation).GetNestedType("Hit", System.Reflection.BindingFlags.NonPublic);
                for (int i = 0; i < 2; i++)
                {
                    var target = states[i];
                    var hit = Activator.CreateInstance(hitType);
                    hitType.GetField("Time").SetValue(hit, i == 0 ? .5f : .500005f);
                    hitType.GetField("Side").SetValue(hit, 1 - target.Side);
                    hitType.GetField("Source").SetValue(hit, 200 + i);
                    hitType.GetField("Position").SetValue(hit, target.Position);
                    hitType.GetField("DirectTarget").SetValue(hit, target.Id);
                    hitType.GetField("Damage").SetValue(hit, 1);
                    hits.Add(hit);
                }

                typeof(BattleSimulation).GetMethod("Resolve", flags).Invoke(simulation, null);
                if (simulation.Outcome != BattleOutcome.Side1Won || simulation.AliveSide1 != 1 || simulation.ResolutionUsedRandomTieBreak || simulation.TieBreakSeed != 0)
                    throw new InvalidOperationException("Near-equal terminal hit ordering fixture failed.");
                if (simulation.TerminalHits.Count != 1 || simulation.TerminalHits[0].TimeWithinTick != .5f || simulation.TerminalHits[0].GroupTime != .5f)
                    throw new InvalidOperationException("Near-equal terminal trace fixture failed.");
            }
        }

        static void ValidateCommands(BattleDefinitions definitions, BattleRules rules, BattleScenarioDefinition scenario)
        {
            if (!rules.AllowDebugCommands || definitions.Zones.Count == 0)
                return;
            using (var simulation = new BattleSimulation(definitions, rules, scenario))
            {
                string reason;
                var command = new BattleZoneCommand(0, 1, simulation.Tick + 1, definitions.Zones[0].Id, new BattleVec(0, 0));
                if (!simulation.TryQueueZone(command, out reason))
                    throw new InvalidOperationException("Valid zone command rejected: " + reason);
                if (simulation.TryQueueZone(command, out reason))
                    throw new InvalidOperationException("Duplicate zone command accepted.");
                if (simulation.TryQueueZone(new BattleZoneCommand(0, 2, simulation.Tick, definitions.Zones[0].Id, new BattleVec(0, 0)), out reason))
                    throw new InvalidOperationException("Late zone command accepted.");
                simulation.Step();
                var states = new List<BattleEntityState>();
                simulation.Capture(states);
                bool found = false;
                for (int i = 0; i < states.Count; i++)
                    if (states[i].Kind == BattleEntityKind.Zone)
                        found = true;
                if (!found)
                    throw new InvalidOperationException("Queued zone did not appear on its ApplyTick.");
            }
        }

        static void AssertFiniteAndBounded(List<BattleEntityState> states, BattleRules rules)
        {
            for (int i = 0; i < states.Count; i++)
            {
                var p = states[i].Position;
                if (float.IsNaN(p.X) || float.IsNaN(p.Y) || float.IsInfinity(p.X) || float.IsInfinity(p.Y) || Math.Abs(p.X) + (states[i].Kind == BattleEntityKind.Unit ? states[i].Radius : 0) > rules.HalfWidth + .0001f || Math.Abs(p.Y) + (states[i].Kind == BattleEntityKind.Unit ? states[i].Radius : 0) > rules.HalfHeight + .0001f)
                    throw new InvalidOperationException("Invalid simulation position.");
            }
        }

        static void AssertSame(List<BattleEntityState> a, List<BattleEntityState> b, string what)
        {
            if (a.Count != b.Count)
                throw new InvalidOperationException("Determinism " + what + " count mismatch.");
            for (int i = 0; i < a.Count; i++)
                if (a[i].Id != b[i].Id || a[i].Kind != b[i].Kind || a[i].Hp != b[i].Hp || a[i].Position.X != b[i].Position.X || a[i].Position.Y != b[i].Position.Y || a[i].Progress != b[i].Progress || a[i].Side != b[i].Side || a[i].DefinitionId != b[i].DefinitionId || a[i].MaxHp != b[i].MaxHp || a[i].TargetId != b[i].TargetId || a[i].Radius != b[i].Radius || a[i].PreviousPosition.X != b[i].PreviousPosition.X || a[i].PreviousPosition.Y != b[i].PreviousPosition.Y || a[i].Facing.X != b[i].Facing.X || a[i].Facing.Y != b[i].Facing.Y)
                    throw new InvalidOperationException("Determinism " + what + " mismatch.");
        }

        static void AssertSame(List<BattleEvent> a, List<BattleEvent> b, string what)
        {
            if (a.Count != b.Count)
                throw new InvalidOperationException("Determinism " + what + " count mismatch.");
            for (int i = 0; i < a.Count; i++)
                if (a[i].Sequence != b[i].Sequence || a[i].Kind != b[i].Kind || a[i].EntityId != b[i].EntityId || a[i].Amount != b[i].Amount || a[i].Tick != b[i].Tick || a[i].Side != b[i].Side || a[i].Radius != b[i].Radius || a[i].TimeWithinTick != b[i].TimeWithinTick || a[i].Position.X != b[i].Position.X || a[i].Position.Y != b[i].Position.Y)
                    throw new InvalidOperationException("Determinism " + what + " mismatch.");
        }
    }
}
