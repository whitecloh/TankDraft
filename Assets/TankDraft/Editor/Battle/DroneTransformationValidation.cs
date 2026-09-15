using System;
using System.Collections.Generic;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;

using TankDraft.Simulation;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.Battle
{
    public static class DroneTransformationValidation
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Requires Play Mode.");
            var settings = AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>("Assets/TankDraft/Configs/Battle/Scenarios/BattleScenarioCatalog.asset");
            var catalog = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>("Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset");
            var entry = catalog.Get("unit.siege_transformer");
            var presentation = AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>("Assets/TankDraft/Configs/Battle/Presentation/BattlePresentation.asset");
            var view = UnityEngine.Object.Instantiate(entry.prefab);
            try
            {
                view.Bind(1, Color.blue); view.ConfigurePresentation(entry, presentation);
                view.Render(State(0), 1, 0);
                float initial = view.VisualRoot.localScale.x;
                view.Render(State(0), 1, 100);
                Require(Mathf.Approximately(view.VisualRoot.localScale.x, initial), "local time must not transform unit");
                view.Render(State(1), 1, 101);
                Require(Mathf.Approximately(view.VisualRoot.localScale.x, initial), "smooth transition starts at prior scale");
                view.Render(State(1), 1, 102);
                Require(Mathf.Approximately(view.VisualRoot.localScale.x, initial * entry.transformedVisualScale), "configured transformed scale");
                view.Clear(); view.Bind(1, Color.red); view.ConfigurePresentation(entry, presentation);
                view.Render(State(1), 1, 103);
                Require(Mathf.Approximately(view.VisualRoot.localScale.x, initial * entry.transformedVisualScale), "reconnect renders transformed stage immediately");
                view.Clear(); view.Bind(2, Color.red); view.ConfigurePresentation(entry, presentation);
                view.Render(State(0), 1, 104);
                Require(Mathf.Approximately(view.VisualRoot.localScale.x, initial), "pool resets stage");
            }
            finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
            var world = UnityEngine.Object.FindFirstObjectByType<BattleWorldView>();
            Require(world != null, "world required");
            world.Clear();
            var scenario = AssetDatabase.LoadAssetAtPath<BattleScenarioAsset>("Assets/TankDraft/Configs/Battle/Scenarios/Scenario_ReferenceDroneTransformation.asset");
            var states = new List<BattleEntityState>(); var events = new List<BattleEvent>();
            int stages = 0, drones = 0, maxViews = 0;
            using (var sim = new BattleSimulation(settings.CreateDefinitions(), settings.CreateRules(), scenario.CreateDefinition()))
            {
                while (sim.Outcome == BattleOutcome.Running && sim.Tick * sim.Rules.TickSeconds < 120f)
                {
                    sim.Step(); sim.Capture(states); sim.DrainEvents(events);
                    float now = sim.Tick * sim.Rules.TickSeconds;
                    world.Sync(states, 1, now); world.Present(events, now); world.TickEffects(now);
                    Require(world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews == states.Count, "snapshot/view count");
                    maxViews = Math.Max(maxViews, states.Count);
                    foreach (var state in states) { if (state.TransformationStage == 1) stages++; if (state.DefinitionId == "projectile.drone") drones++; }
                }
                Require(sim.Outcome != BattleOutcome.Running, "diagnostic watchdog");
            }
            world.Clear();
            Require(stages > 0 && drones > 0, "scenario must exercise both mechanics");
            Require(world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews + world.ActiveEffectViews == 0, "pool cleanup");
            return "PASS authoritative transformation visual/transition/reconnect/reset; scenario droneSamples=" + drones + ",stageSamples=" + stages + ",maxViews=" + maxViews;
        }

        static BattleEntityState State(int stage) => new BattleEntityState(1, 0, BattleEntityKind.Unit, "unit.siege_transformer", default, default, new BattleVec(0, 1), 100, 100, .34f, 0, 0, transformationStage: stage);
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    }
}
