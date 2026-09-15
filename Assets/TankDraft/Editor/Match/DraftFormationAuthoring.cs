using System;
using System.Collections.Generic;
using TankDraft.BattleContent;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Match.Content;
using UnityEditor;

namespace TankDraft.Match.Editor
{
    public static class DraftFormationAuthoring
    {
        // Initial military mapping; subsequent tuning is authored on each BattleUnitAsset.
        static readonly Dictionary<string, int> Priorities = new Dictionary<string, int>
        {
            {"unit.mines", 400}, {"unit.chemical_demolition", 300}, {"unit.demolition_vehicle", 200}, {"unit.anti_tank_obstacle", 100},
            {"unit.heavy_tank", 900}, {"unit.siege_transformer", 850}, {"unit.reactive_armor_tank", 800}, {"unit.medium_tank", 700}, {"unit.recovery_assault", 600},
            {"unit.light_raider", 500}, {"unit.stealth_vehicle", 400}, {"unit.engineer_vehicle", 300}, {"unit.repair_vehicle", 200}, {"unit.sentry_turret", 100},
            {"unit.magazine_destroyer", 300}, {"unit.tank_destroyer", 200}, {"unit.toxin_destroyer", 100},
            {"unit.shield_vehicle", 600}, {"unit.drone_carrier", 500}, {"unit.mobile_mortar", 400}, {"unit.field_artillery", 300}, {"unit.rocket_artillery", 200}, {"unit.drone_scout", 100}
        };

        [MenuItem("TankDraft/Match/Author Draft Formation Priorities")]
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Idle Edit Mode required.");
            int count = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:BattleUnitAsset", new[] {"Assets/TankDraft/Configs/Battle"}))
            {
                var unit = AssetDatabase.LoadAssetAtPath<BattleUnitAsset>(AssetDatabase.GUIDToAssetPath(guid));
                if (!Priorities.TryGetValue(unit.Id, out var priority)) throw new InvalidOperationException("Missing formation priority: " + unit.Id);
                var data = new SerializedObject(unit);
                data.FindProperty("_formationPriority").intValue = priority;
                data.ApplyModifiedPropertiesWithoutUndo();
                var content = (ContentEntryAsset)data.FindProperty("_content").objectReferenceValue;
                var label = new SerializedObject(content);
                label.FindProperty("_roleLabel").stringValue = unit.Row == FormationRow.Artillery ? "Дальний бой" : unit.Row == FormationRow.TankDestroyer ? "Средний бой" : "Ближний бой";
                label.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(unit); EditorUtility.SetDirty(content); count++;
            }
            var settings = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>("Assets/TankDraft/Configs/Match/MatchSettings.asset");
            var presentation = new SerializedObject(settings.BattlePresentation);
            presentation.FindProperty("_formationMoveSeconds").floatValue = .25f;
            presentation.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings.BattlePresentation);
            AssetDatabase.SaveAssets();
            return "Formation priorities and visible lines authored: " + count;
        }
    }
}
