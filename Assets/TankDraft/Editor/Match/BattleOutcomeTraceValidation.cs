using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Bootstrap;
using TankDraft.Match.Content;
using TankDraft.Match.Domain;
using TankDraft.Simulation;
using UnityEditor;

namespace TankDraft.Match.Editor
{
    // Editor-only evidence for terminal timing and deterministic authority tie-breaks.
    public static class BattleOutcomeTraceValidation
    {
        const float EqualTimeEpsilon = .00001f;
        const string Evidence = "Logs/TankDraftSetup/NetworkQA/OutcomeTraceAfterTimingFix.txt";
        public static string Run()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>(MatchAuthoring.SettingsPath);
            settings.Validate();
            var units = settings.CreateUnits();
            var deck = units.Select(x => x.Id).ToArray();
            var definitions = settings.BattleCatalog.CreateDefinitions();
            var rules = settings.BattleCatalog.CreateRules();
            int reviewCount = 0, randomTieCount = 0;
            var report = new StringBuilder();
            report.AppendLine("Battle outcome trace: exact MatchValidation scripted 40-seed choices/order branch.");
            report.AppendLine("TimeEpsilonSeconds=" + Float(rules.TickSeconds * EqualTimeEpsilon) + " bits=0x" + Bits(rules.TickSeconds * EqualTimeEpsilon));
            for (uint seed = 1; seed <= 40; seed++)
            {
                var match = new MatchService(settings.CreateRules(), units, deck, deck, "order.reinforce_armor", seed);
                int guard = 0;
                while (match.Phase != MatchPhase.MatchResult && match.Phase != MatchPhase.ReviewRequired && guard++ < 100)
                {
                    if (match.Phase == MatchPhase.Draft)
                    {
                        long token = match.ChoiceToken;
                        for (int side = 0; side < 2; side++)
                        {
                            if (match.Phase != MatchPhase.Draft || match.ChoiceToken != token)
                                break;
                            if (match.IsComeback && match.BonusSide != side || match.HasCommitted(side))
                                continue;
                            int index = (int)((seed + (uint)token + (uint)side) % 3);
                            bool accepted = side == 0 && token % 7 == 0 && match.CanUseOrder(0) ? match.TryUseOrder(side, token, out _) : match.TryChoose(side, token, index, out _);
                            if (!accepted)
                                throw new Exception("Legal scripted draft rejected.");
                        }
                    }
                    else if (match.Phase == MatchPhase.Battle)
                    {
                        var scenario = match.CreateScenario();
                        using (var simulation = new BattleSimulation(definitions, rules, scenario))
                        {
                            int beforeSide0 = simulation.AliveSide0;
                            int beforeSide1 = simulation.AliveSide1;
                            while (simulation.Outcome == BattleOutcome.Running && simulation.Tick < 3600)
                            {
                                beforeSide0 = simulation.AliveSide0;
                                beforeSide1 = simulation.AliveSide1;
                                simulation.Step();
                            }

                            if (simulation.Outcome == BattleOutcome.Running)
                                throw new Exception("Seed " + seed + " round " + match.RoundNumber + ": QA watchdog, no outcome assigned.");
                            if (simulation.Outcome == BattleOutcome.ReviewRequired)
                            {
                                reviewCount++;
                            }

                            if (simulation.ResolutionUsedRandomTieBreak)
                            {
                                randomTieCount++;
                                AppendReview(report, seed, match.RoundNumber, scenario, simulation, beforeSide0, beforeSide1, rules.TickSeconds);
                            }

                            match.ResolveBattle(simulation.Outcome);
                        }
                    }
                    else
                        match.Continue();
                }

                if (guard >= 100)
                    throw new Exception("Match progression did not terminate.");
            }

            if (reviewCount != 0)
                throw new Exception("Expected 0 ReviewRequired outcomes after exact-time tie-break; got " + reviewCount + ".");
            report.AppendLine("PASS ReviewRequired=0/40 RandomTieOutcomes=" + randomTieCount + "/40; exact-time ties use the simulation's seeded authority tie-break.");
            Directory.CreateDirectory(Path.GetDirectoryName(Evidence));
            File.WriteAllText(Evidence, report.ToString());
            return report.ToString();
        }

        static void AppendReview(StringBuilder report, uint seed, int round, BattleScenarioDefinition scenario, BattleSimulation simulation, int beforeSide0, int beforeSide1, float tickSeconds)
        {
            var hits = simulation.TerminalHits;
            bool identical = hits.Count > 1;
            bool distinctWithinEpsilon = false;
            for (int i = 1; i < hits.Count; i++)
            {
                if (Bits(hits[i].TimeWithinTick) != Bits(hits[0].TimeWithinTick))
                {
                    identical = false;
                    if (Math.Abs(hits[i].TimeWithinTick - hits[0].TimeWithinTick) <= EqualTimeEpsilon)
                        distinctWithinEpsilon = true;
                }
            }

            report.AppendLine("seed=" + seed + " round=" + round + " scenarioSeed=" + scenario.Seed + " scenario=" + scenario.Id);
            report.AppendLine("armies=" + Armies(scenario));
            report.AppendLine("terminal outcome=" + simulation.Outcome + " randomTieBreak=" + simulation.ResolutionUsedRandomTieBreak + " tieBreakSeed=" + simulation.TieBreakSeed + " preAlive=[" + beforeSide0 + "," + beforeSide1 + "] postAlive=[" + simulation.AliveSide0 + "," + simulation.AliveSide1 + "] count=" + hits.Count + " trulyIdenticalTimes=" + identical + " distinctWithinEpsilon=" + distinctWithinEpsilon + " TimeEpsilonSeconds=" + Float(tickSeconds * EqualTimeEpsilon));
            for (int i = 0; i < hits.Count; i++)
            {
                var hit = hits[i];
                float delta = hit.TimeWithinTick - hit.GroupTime;
                report.AppendLine("hit[" + i + "] tick=" + hit.Tick + " time=" + Float(hit.TimeWithinTick) + " bits=0x" + Bits(hit.TimeWithinTick) + " groupTime=" + Float(hit.GroupTime) + " groupBits=0x" + Bits(hit.GroupTime) + " delta=" + Float(delta) + " deltaBits=0x" + Bits(delta) + " source=" + hit.SourceId + " side=" + hit.Side + " damage=" + hit.Damage + " self=" + hit.SelfId + " target=" + hit.DirectTarget + " position=(" + Float(hit.Position.X) + "," + Float(hit.Position.Y) + ") radius=" + Float(hit.Radius) + " isZone=" + hit.IsZone);
            }
        }

        static string Armies(BattleScenarioDefinition scenario)
        {
            var text = new StringBuilder();
            for (int i = 0; i < scenario.Stacks.Count; i++)
            {
                if (i > 0)
                    text.Append(", ");
                var stack = scenario.Stacks[i];
                text.Append("side=").Append(stack.Side).Append(" unit=").Append(stack.UnitId).Append(" count=").Append(stack.Count).Append(" hpBonus=").Append(stack.HpBonusPercent).Append(" damageBonus=").Append(stack.DamageBonusPercent);
            }

            return text.ToString();
        }

        static string Float(float value) => value.ToString("R", CultureInfo.InvariantCulture);
        static string Bits(float value) => BitConverter.SingleToInt32Bits(value).ToString("X8", CultureInfo.InvariantCulture);
    }
}
