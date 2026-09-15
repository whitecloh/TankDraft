using System;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Content;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Match.Editor
{
    public static partial class ReferenceRosterAuthoring
    {
        [MenuItem("TankDraft/Match/Author Drone And Transformation")]
        public static string AuthorDroneTransformation()
        {
            RequireIdle();
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var views = Load<BattleViewCatalogAsset>(ViewCatalogPath);
            var settings = Load<MatchSettingsAsset>(SettingsPath);
            var meta = Load<MetaCatalogAsset>(MetaCatalogPath);
            var droneProjectile = CreateDroneProjectileIfMissing();
            var catalogSerialized = new SerializedObject(catalog);
            var projectiles = catalogSerialized.FindProperty("_projectiles");
            if (!Contains(projectiles, droneProjectile))
            {
                int index = projectiles.arraySize++;
                projectiles.GetArrayElementAtIndex(index).objectReferenceValue = droneProjectile;
                catalogSerialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var carrier = Load<BattleUnitAsset>(UnitRoot + "/Unit_DroneCarrier.asset");
            var carrierSerialized = new SerializedObject(carrier);
            var oldSpawn = carrierSerialized.FindProperty("_abilityKind").enumValueIndex == (int)BattleUnitAbilityKind.SpawnUnit;
            var wrongProjectile = carrierSerialized.FindProperty("_projectile").objectReferenceValue != droneProjectile;
            if (oldSpawn || wrongProjectile)
            {
                Set(carrier, "_projectile", droneProjectile);
                Set(carrier, "_abilityKind", (int)BattleUnitAbilityKind.None);
                Set(carrier, "_spawnUnit", null);
            }

            var content = CreateMetaContentIfMissing("unit.siege_transformer", FormationRow.Tank, 1,
                "Осадный трансформер", "Во время боя переходит в осадный режим: полностью восстанавливает здоровье, усиливает атаку и поражает область.", "Ближний бой");
            if (content.Icon == null)
                Set(content, "_icon", Load<ContentEntryAsset>(MetaRoot + "/Units/unit.heavy_tank.asset").Icon ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Diamond.png"));
            AddMetaEntryIfMissing(meta, content);
            bool created = AssetDatabase.LoadAssetAtPath<BattleUnitAsset>(UnitRoot + "/Unit_SiegeTransformer.asset") == null;
            var transformer = CreateUnitIfMissing("SiegeTransformer", content, BattleAttackKind.Melee, 140, 18, .8f, .34f, 2.4f, .15f, 1.2f,
                null, BattleUnitAbilityKind.None, 0, 0, 0, null, 0);
            if (created)
            {
                Set(transformer, "_formationPriority", 850);
                Set(transformer, "_transforms", true);
                Set(transformer, "_transformationDelaySeconds", 10f);
                Set(transformer, "_transformationMaxHpPercent", 400);
                Set(transformer, "_transformationDamagePercent", 300);
                Set(transformer, "_transformationAttackRadius", 1.4f);
            }

            AddReferences(catalog, "_units", new[] { transformer });
            AddDraftEntries(settings, new[] { transformer });
            var transformerViewCreated = !HasView(new SerializedObject(views).FindProperty("_entries"), transformer.Id);
            AddViewEntries(views, new[] { Pair(transformer, "Battle_unit_heavy_tank.prefab", Color.gray, Vector3.one) });
            if (transformerViewCreated) EnsureUnitTransformedVisualScale(views, transformer.Id, 1.5f);
            AddReferences(catalog, "_scenarios", new[] { CreateScenarioIfMissing("ReferenceDroneTransformation", "scenario.reference_drone_transformation", "Drone carrier and siege transformation", 5330,
                new[] { carrier, transformer, Load<BattleUnitAsset>(UnitRoot + "/Unit_RepairVehicle.asset"), Load<BattleUnitAsset>(UnitRoot + "/Unit_HeavyTank.asset") }) });
            EnsureProjectileViewEntry(views, droneProjectile);
            AssetDatabase.SaveAssets();
            return ValidateDroneTransformation();
        }

        public static string ValidateDroneTransformation()
        {
            var defs = Load<BattleScenarioCatalogAsset>(CatalogPath).CreateDefinitions();
            Load<BattleViewCatalogAsset>(ViewCatalogPath).Validate(defs);
            Load<MatchSettingsAsset>(SettingsPath).Validate();
            if (defs.Unit("unit.drone_carrier").Ability != null || defs.Unit("unit.drone_carrier").ProjectileId != "projectile.drone" ||
                defs.Unit("unit.siege_transformer").Transformation == null)
                throw new InvalidOperationException("Drone or transformation authored profile is not wired.");
            return "PASS drone projectile, carrier migration, transformer config, meta/draft/catalog/views and scenario.";
        }

        static BattleProjectileAsset CreateDroneProjectileIfMissing()
        {
            const string path = BattleRoot + "/Projectiles/Projectile_Drone.asset";
            var asset = AssetDatabase.LoadAssetAtPath<BattleProjectileAsset>(path);
            if (asset != null) return asset;
            EnsureFolder(BattleRoot + "/Projectiles");
            asset = ScriptableObject.CreateInstance<BattleProjectileAsset>();
            AssetDatabase.CreateAsset(asset, path);
            Set(asset, "_id", "projectile.drone"); Set(asset, "_speed", 3f); Set(asset, "_radius", .12f); Set(asset, "_impactRadius", 0f); Set(asset, "_retargetOnTargetLost", true);
            return asset;
        }

        static void EnsureProjectileViewEntry(BattleViewCatalogAsset views, BattleProjectileAsset drone)
        {
            var serialized = new SerializedObject(views); var entries = serialized.FindProperty("_entries");
            for (int i = 0; i < entries.arraySize; i++) if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("definitionId").stringValue == drone.Id) return;
            var dart = AssetDatabase.LoadAssetAtPath<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Dart.asset");
            var dartEntry = EnumerableEntry(entries, dart.Id);
            int index = entries.arraySize; entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("definitionId").stringValue = drone.Id;
            entry.FindPropertyRelative("kind").enumValueIndex = (int)BattleEntityKind.Projectile;
            entry.FindPropertyRelative("prefab").objectReferenceValue = dartEntry.FindPropertyRelative("prefab").objectReferenceValue;
            entry.FindPropertyRelative("visualScale").floatValue = 1.6f;
            entry.FindPropertyRelative("visualOffset").vector2Value = Vector2.zero;
            var transformed = entry.FindPropertyRelative("transformedVisualScale"); if (transformed != null) transformed.floatValue = 1f;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(views);
        }

        static void EnsureUnitTransformedVisualScale(BattleViewCatalogAsset views, string id, float value)
        {
            var serialized = new SerializedObject(views); var entry = EnumerableEntry(serialized.FindProperty("_entries"), id);
            var transformed = entry.FindPropertyRelative("transformedVisualScale");
            if (transformed == null) return;
            transformed.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(views);
        }

        static SerializedProperty EnumerableEntry(SerializedProperty entries, string id)
        {
            for (int i = 0; i < entries.arraySize; i++) if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("definitionId").stringValue == id) return entries.GetArrayElementAtIndex(i);
            throw new InvalidOperationException("Missing dart view entry.");
        }
    }
}
