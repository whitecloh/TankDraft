using System;
using System.Collections.Generic;
using System.Reflection;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Match.Editor
{
    // Kept separate from roster authoring: this only migrates the newly serialized
    // presentation defaults on the already-authored settings asset.
    public static class ReferenceArmyPresentationValidation
    {
        private const string SettingsPath = "Assets/TankDraft/Configs/Battle/Presentation/BattlePresentation.asset";

        [MenuItem("TankDraft/Match/Author Reference Army Presentation")]
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Army presentation authoring requires a compiled editor in Edit Mode.");

            BattlePresentationSettings settings = AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>(SettingsPath);
            if (settings == null)
                throw new InvalidOperationException("Missing battle presentation settings: " + SettingsPath);

            SerializedObject serialized = new SerializedObject(settings);
            SetIfNotPositive(serialized, "_unitSpawnSeconds", .22f);
            SetIfNotPositive(serialized, "_unitSpawnStartScale", .72f);
            SetIfNotPositive(serialized, "_unitSpawnOvershoot", 1.05f);
            SetIfNotPositive(serialized, "_unitSpawnStagger", .035f);
            SetIfNotPositive(serialized, "_unitSpawnMaxDelay", .18f);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Validate();
            return "PASS authored army pop-in defaults: .22s, .72→1.05, .035s stagger, .18s cap.";
        }

        public static string Validate()
        {
            BattlePresentationSettings settings = AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>(SettingsPath);
            if (settings == null)
                throw new InvalidOperationException("Missing battle presentation settings: " + SettingsPath);
            settings.Validate();
            return "PASS army presentation settings validated.";
        }

        // Runtime-only diagnostic. The caller owns the initialized offline world and may use this
        // after authoring its catalog; this method clears the world before returning.
        public static string ValidateRuntime(BattleWorldView world)
        {
            if (world == null)
                throw new ArgumentNullException(nameof(world));
            BattleScenarioCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>("Assets/TankDraft/Configs/Battle/Scenarios/BattleScenarioCatalog.asset");
            BattleViewCatalogAsset views = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>("Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset");
            if (catalog == null || views == null)
                throw new InvalidOperationException("Reference army presentation runtime QA requires authored battle catalogs.");
            BattleDefinitions definitions = catalog.CreateDefinitions();
            BattleUnitDefinition heavy = definitions.Unit("unit.heavy_tank");
            BattleUnitDefinition repair = definitions.Unit("unit.repair_vehicle");
            var first = new List<BattleEntityState> { State(1, heavy, new BattleVec(-1f, 0f)) };
            var expanded = new List<BattleEntityState>
            {
                State(1, heavy, new BattleVec(-1f, 0f)),
                State(2, heavy, new BattleVec(-.2f, 0f)),
                State(3, repair, new BattleVec(.8f, 0f))
            };

            try
            {
                world.SyncPreparation(first, 1f, true);
                BattleEntityView firstView = Active(world, 1);
                Check(!SpawnActive(firstView), "First preparation snapshot must not pop in.");
                Check(Vector3.Distance(firstView.transform.position, new Vector3(-1f, 0f, 0f)) < .001f, "Spawn animation changed unit root position.");

                world.SyncPreparation(expanded, 2f, false);
                Check(!SpawnActive(Active(world, 1)), "Existing preparation unit restarted its pop-in.");
                Check(SpawnActive(Active(world, 2)) && SpawnActive(Active(world, 3)), "Only new preparation units must pop in.");

                world.SyncPreparation(expanded, 3f, false);
                Check(!SpawnActive(Active(world, 1)) && !SpawnActive(Active(world, 2)) && !SpawnActive(Active(world, 3)), "Unchanged preparation inventory must not animate.");

                // Inserting a group changes authoritative numeric ids. Preserve each occurrence's
                // view, interpolate to the new position, and never restart its purchase pop.
                var repairView = Active(world, 3);
                var shifted = new List<BattleEntityState>
                {
                    State(10, heavy, new BattleVec(-2f, -1f)),
                    State(11, heavy, new BattleVec(-1.2f, -1f)),
                    State(12, repair, new BattleVec(0f, -2f))
                };
                Vector3 before = firstView.transform.position;
                world.SyncPreparation(shifted, 3.1f, false);
                Check(Active(world, 10) == firstView && Active(world, 12) == repairView, "Reflow replaced surviving unit views.");
                Check(firstView.RuntimeId == 10 && !SpawnActive(firstView), "Reflow identity or pop was reset.");
                Check(Vector3.Distance(firstView.transform.position, before) < .001f, "Reflow teleported on first frame.");
                world.Sync(shifted, 1f, 3.225f);
                Check(Vector3.Distance(firstView.transform.position, before) > .01f && Vector3.Distance(firstView.transform.position, new Vector3(-2, -1, 0)) > .01f, "Reflow did not interpolate.");
                world.SyncPreparation(shifted, 3.23f, false);
                world.Sync(shifted, 1f, 3.36f);
                Check(Vector3.Distance(firstView.transform.position, new Vector3(-2, -1, 0)) < .001f, "Repeated snapshot extended reflow.");
                world.SyncPreparation(shifted, 3.4f, false, true);
                Check(Active(world, 10) == firstView && !Private<bool>(firstView, "_formationMoving"), "Battle entry lost view or retained draft motion.");

                var remapped = new List<BattleEntityState> { State(1, repair, new BattleVec(1.25f, .5f)) };
                world.Sync(remapped, 1f, 4f);
                BattleEntityView remappedView = Active(world, 1);
                Check(remappedView.RuntimeId == 1, "Remapped view lost its runtime id.");
                Check(Origin(world, remappedView) == views.Get(repair.Id).prefab, "Reused entity id retained a wrong unit prefab.");
                Check(Vector3.Distance(remappedView.transform.position, new Vector3(1.25f, .5f, 0f)) < .001f, "Live remap changed the unit root position.");
                Check(Vector3.Distance(remappedView.transform.localScale, BaseScale(remappedView)) < .001f, "Spawn effect changed the unit root scale.");

                world.Clear();
                foreach (BattleEntityView view in Origins(world).Keys)
                {
                    Check(!SpawnActive(view) && view.RuntimeId == -1, "Clear left a pooled spawn animation active.");
                    Check(Vector3.Distance(view.transform.localPosition, BasePosition(view)) < .001f && Vector3.Distance(view.transform.localScale, BaseScale(view)) < .001f, "Clear did not restore pooled unit transform state.");
                }
                return "PASS runtime army presentation: first-load suppression, new-unit pop-in, stable occurrence rebind, reflow/interpolation/duplicate snapshot, battle transition, id remap and pooled reset.";
            }
            finally
            {
                world.Clear();
            }
        }

        private static void SetIfNotPositive(SerializedObject target, string propertyName, float value)
        {
            SerializedProperty property = Require(target, propertyName);
            if (property.floatValue <= 0f || float.IsNaN(property.floatValue) || float.IsInfinity(property.floatValue))
                property.floatValue = value;
        }

        private static SerializedProperty Require(SerializedObject target, string propertyName)
        {
            SerializedProperty property = target.FindProperty(propertyName);
            if (property == null)
                throw new MissingFieldException(target.targetObject.GetType().Name, propertyName);
            return property;
        }

        private static BattleEntityState State(int id, BattleUnitDefinition definition, BattleVec position) =>
            new BattleEntityState(id, 0, BattleEntityKind.Unit, definition.Id, position, position, new BattleVec(0f, 1f), definition.MaxHp, definition.MaxHp, definition.Radius, 0f, -1);

        private static BattleEntityView Active(BattleWorldView world, int id)
        {
            if (!ActiveViews(world).TryGetValue(id, out BattleEntityView view))
                throw new InvalidOperationException("Expected active view " + id + ".");
            return view;
        }

        private static bool SpawnActive(BattleEntityView view) => Private<bool>(view, "_spawnActive");
        private static Vector3 BasePosition(BattleEntityView view) => Private<Vector3>(view, "_basePosition");
        private static Vector3 BaseScale(BattleEntityView view) => Private<Vector3>(view, "_baseScale");
        private static BattleEntityView Origin(BattleWorldView world, BattleEntityView view) => Origins(world)[view];
        private static Dictionary<int, BattleEntityView> ActiveViews(BattleWorldView world) => Private<Dictionary<int, BattleEntityView>>(world, "_active");
        private static Dictionary<BattleEntityView, BattleEntityView> Origins(BattleWorldView world) => Private<Dictionary<BattleEntityView, BattleEntityView>>(world, "_origins");

        private static T Private<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null || field.GetValue(target) is not T value)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            return value;
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
