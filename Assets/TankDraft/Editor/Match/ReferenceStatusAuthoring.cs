using System;
using System.Collections.Generic;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Match.Content;
using TankDraft.Simulation;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Match.Editor
{
    public static partial class ReferenceRosterAuthoring
    {
        const string ToxicZonePath = BattleRoot + "/Zones/Zone_Toxic.asset";

        [MenuItem("TankDraft/Match/Author Reference Status Mechanics")]
        public static string AuthorStatusMechanics()
        {
            RequireIdle();
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var views = Load<BattleViewCatalogAsset>(ViewCatalogPath);
            var settings = Load<MatchSettingsAsset>(SettingsPath);
            var meta = Load<MetaCatalogAsset>(MetaCatalogPath);
            var zone = AssetDatabase.LoadAssetAtPath<BattleZoneAsset>(ToxicZonePath);
            if (zone == null)
            {
                EnsureFolder(BattleRoot + "/Zones");
                zone = ScriptableObject.CreateInstance<BattleZoneAsset>();
                AssetDatabase.CreateAsset(zone, ToxicZonePath);
                Set(zone, "_id", "zone.toxic");
                Set(zone, "_radius", 1.4f);
                Set(zone, "_tickDamage", 1);
                Set(zone, "_periodSeconds", 1f);
                Set(zone, "_lifetimeSeconds", 6f);
                Set(zone, "_moveSpeedMultiplier", .5f);
                Set(zone, "_sourceDamagePercent", 20);
            }
            AppendStatusReference(catalog, "_zones", zone);
            var bomber = CreateStatusUnit(meta, "DemolitionVehicle", "unit.demolition_vehicle", "Подрывная машина",
                "Движется к врагу и взрывается при контакте.", BattleAttackKind.ContactExplosion, 75, 65, 1.8f, .15f, 1f, null, null, false);
            var chemical = CreateStatusUnit(meta, "ChemicalDemolition", "unit.chemical_demolition", "Химическая подрывная машина",
                "Контактный взрыв оставляет токсичную зону на 6 секунд: урон и замедление врагов.", BattleAttackKind.ContactExplosion, 85, 55, 1.3f, .15f, 1f, null, zone, false);
            var dart = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Dart.asset");
            var toxin = CreateStatusUnit(meta, "ToxinDestroyer", "unit.toxin_destroyer", "Химическая ПТ",
                "Попадание наносит урон и ещё 100% урона ядом за 3 секунды.", BattleAttackKind.Projectile, 90, 30, .85f, 5f, 2.4f, dart, null, true);
            var units = new[] { bomber, chemical, toxin };
            AddReferences(catalog, "_units", units);
            AddDraftEntries(settings, units);
            // Reuse primitive presentation only; art direction is owned by the other task.
            AddViewEntries(views, new[] {
                Pair(bomber, "Battle_unit_heavy_tank.prefab", Color.gray, Vector3.one),
                Pair(chemical, "Battle_unit_heavy_tank.prefab", Color.gray, Vector3.one),
                Pair(toxin, "Battle_unit_tank_destroyer.prefab", Color.gray, Vector3.one)
            });
            AddStatusZoneView(views);
            var heavy = Load<BattleUnitAsset>(UnitRoot + "/Unit_HeavyTank.asset");
            var destroyer = Load<BattleUnitAsset>(UnitRoot + "/Unit_TankDestroyer.asset");
            AddReferences(catalog, "_scenarios", new[] {
                CreateScenarioIfMissing("ReferenceStatus", "scenario.reference_status", "Toxic zone and poison", 5320, new[] { chemical, heavy, toxin, destroyer }),
                CreateScenarioIfMissing("ReferenceDemolition", "scenario.reference_demolition", "Mobile demolition", 5321, new[] { bomber, heavy, toxin, destroyer })
            });
            AssetDatabase.SaveAssets();
            return ValidateStatusMechanics();
        }

        static BattleUnitAsset CreateStatusUnit(MetaCatalogAsset meta, string name, string id, string title, string description,
            BattleAttackKind attack, int hp, int damage, float speed, float range, float cooldown, BattleProjectileAsset projectile,
            BattleZoneAsset zone, bool dot)
        {
            var row = attack == BattleAttackKind.ContactExplosion ? FormationRow.Barrier : FormationRow.TankDestroyer;
            var content = CreateMetaContentIfMissing(id, row, 1, title, description, attack == BattleAttackKind.ContactExplosion ? "Подрывник" : "ПТ-САУ");
            if (content.Icon == null)
                Set(content, "_icon", Load<ContentEntryAsset>(MetaRoot + "/Units/unit.heavy_tank.asset").Icon ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Diamond.png"));
            AddMetaEntryIfMissing(meta, content);
            bool created = AssetDatabase.LoadAssetAtPath<BattleUnitAsset>(UnitRoot + "/Unit_" + name + ".asset") == null;
            var unit = CreateUnitIfMissing(name, content, attack, hp, damage, speed, .28f, 1.2f, range, cooldown,
                projectile, BattleUnitAbilityKind.None, 0, 0, 0, null, 0);
            if (created)
            {
                Set(unit, "_contactZone", zone);
                Set(unit, "_dotTotalDamagePercent", dot ? 100 : 0);
                Set(unit, "_dotPeriodSeconds", 1f);
                Set(unit, "_dotTickCount", 3);
            }
            return unit;
        }

        static void AppendStatusReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            var entries = serialized.FindProperty(field);
            if (Contains(entries, value)) return;
            int index = entries.arraySize++;
            entries.GetArrayElementAtIndex(index).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        static void AddStatusZoneView(BattleViewCatalogAsset views)
        {
            var serialized = new SerializedObject(views);
            var entries = serialized.FindProperty("_entries");
            if (HasView(entries, "zone.toxic")) return;
            BattleEntityView source = null;
            for (int i = 0; i < entries.arraySize; i++)
                if (entries.GetArrayElementAtIndex(i).FindPropertyRelative("definitionId").stringValue == "zone.burning")
                    source = entries.GetArrayElementAtIndex(i).FindPropertyRelative("prefab").objectReferenceValue as BattleEntityView;
            if (source == null) throw new InvalidOperationException("Existing authored zone view required.");
            string path = "Assets/TankDraft/Prefabs/Battle/Zones/Battle_zone_toxic.prefab";
            EnsureFolder("Assets/TankDraft/Prefabs/Battle/Zones");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null && !AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(source), path))
                throw new InvalidOperationException("Could not create placeholder zone prefab.");
            int index = entries.arraySize++;
            var entry = entries.GetArrayElementAtIndex(index);
            entry.FindPropertyRelative("definitionId").stringValue = "zone.toxic";
            entry.FindPropertyRelative("kind").intValue = (int)BattleEntityKind.Zone;
            entry.FindPropertyRelative("prefab").objectReferenceValue = Load<GameObject>(path).GetComponent<BattleEntityView>();
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(views);
        }

        public static string ValidateStatusMechanics()
        {
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var definitions = catalog.CreateDefinitions();
            Load<BattleViewCatalogAsset>(ViewCatalogPath).Validate(definitions);
            Load<MatchSettingsAsset>(SettingsPath).Validate();
            var bomber = definitions.Unit("unit.demolition_vehicle");
            var chemical = definitions.Unit("unit.chemical_demolition");
            if (bomber.Attack != BattleAttackKind.ContactExplosion || bomber.MoveSpeed <= 0 || chemical.ContactZoneId != "zone.toxic" || definitions.Unit("unit.toxin_destroyer").DamageOverTime == null)
                throw new InvalidOperationException("Status profiles are not wired.");
            if (definitions.Unit("unit.mines").MoveSpeed != 1 || definitions.Unit("unit.mines").ContactZoneId.Length != 0 || definitions.Zone("zone.burning").MoveSpeedMultiplier != 1)
                throw new InvalidOperationException("Legacy content behavior changed.");
            return "PASS status authored configs, meta/draft entries, prepared views; original mines/burning preserved.";
        }

        public static string ValidateStatusScenarios()
        {
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var definitions = catalog.CreateDefinitions();
            var rules = catalog.CreateRules();
            var states = new List<BattleEntityState>();
            string results = "";
            foreach (var name in new[] { "ReferenceStatus", "ReferenceDemolition" })
            {
                var scenario = Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_" + name + ".asset").CreateDefinition();
                using (var sim = new BattleSimulation(definitions, rules, scenario))
                {
                    for (int i = 0; i < Math.Ceiling(120 / rules.TickSeconds) && sim.Outcome == BattleOutcome.Running; i++)
                    {
                        sim.Step(); sim.Capture(states); ValidateStates(states, rules, scenario.Id);
                    }
                    if (sim.Outcome == BattleOutcome.Running) throw new InvalidOperationException("Diagnostic watchdog: " + name);
                    results += name + "=" + sim.Outcome + ",ticks=" + sim.Tick + ",zones=" + sim.TotalZoneTicks + "; ";
                }
            }
            return "PASS status scenario simulations: " + results;
        }

        public static string ValidateStatusRuntime(BattleWorldView world)
        {
            if (!UnityEngine.Application.isPlaying || world == null) throw new InvalidOperationException("Play Mode world required.");
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var definitions = catalog.CreateDefinitions();
            var rules = catalog.CreateRules();
            var states = new List<BattleEntityState>();
            var events = new List<BattleEvent>();
            var seen = new HashSet<string>();
            int maxViews = 0, damageEvents = 0;
            foreach (var name in new[] { "ReferenceStatus", "ReferenceDemolition" })
            {
                world.Clear();
                var scenario = Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_" + name + ".asset").CreateDefinition();
                using (var sim = new BattleSimulation(definitions, rules, scenario))
                {
                    for (int i = 0; i < Math.Ceiling(120 / rules.TickSeconds) && sim.Outcome == BattleOutcome.Running; i++)
                    {
                        sim.Step(); sim.Capture(states); sim.DrainEvents(events);
                        world.Sync(states, 1, i * rules.TickSeconds);
                        world.Present(events, i * rules.TickSeconds); world.TickEffects(i * rules.TickSeconds);
                        foreach (var state in states) seen.Add(state.DefinitionId);
                        foreach (var e in events) if (e.Kind == BattleEventKind.Damage) damageEvents++;
                        int active = world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews;
                        if (active != states.Count) throw new InvalidOperationException("Authored view count differs from simulation.");
                        maxViews = Math.Max(maxViews, active);
                    }
                    if (sim.Outcome == BattleOutcome.Running) throw new InvalidOperationException("Play Mode diagnostic watchdog: " + name);
                }
            }
            foreach (var id in new[] { "unit.demolition_vehicle", "unit.chemical_demolition", "unit.toxin_destroyer", "zone.toxic" })
                if (!seen.Contains(id)) throw new InvalidOperationException("No runtime prefab coverage for " + id);
            if (damageEvents == 0) throw new InvalidOperationException("No damage events presented.");
            world.Clear();
            if (world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews + world.ActiveEffectViews != 0)
                throw new InvalidOperationException("Views did not return to pools.");
            return "PASS Play Mode status views, HP/events and pool reset; maxActiveViews=" + maxViews + "; damageEvents=" + damageEvents;
        }
    }
}
