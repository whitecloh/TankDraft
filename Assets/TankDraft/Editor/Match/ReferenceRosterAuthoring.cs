using System;
using System.Collections.Generic;
using System.IO;
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
    // Incremental roster authoring. Existing assets are never rebalanced by this tool.
    public static partial class ReferenceRosterAuthoring
    {
        private const string MetaRoot = "Assets/TankDraft/Configs/Meta";
        private const string BattleRoot = "Assets/TankDraft/Configs/Battle";
        private const string UnitRoot = BattleRoot + "/Units";
        private const string SupportRoot = BattleRoot + "/Support";
        private const string PrefabRoot = "Assets/TankDraft/Prefabs/Battle/Units";
        private const string CatalogPath = BattleRoot + "/Scenarios/BattleScenarioCatalog.asset";
        private const string ViewCatalogPath = BattleRoot + "/Presentation/BattleViewCatalog.asset";
        private const string SettingsPath = "Assets/TankDraft/Configs/Match/MatchSettings.asset";
        private const string MetaCatalogPath = MetaRoot + "/Catalogs/MetaCatalog.asset";
        private const string ReferenceScenarioPath = BattleRoot + "/Scenarios/Scenario_ReferenceRoster.asset";

        [MenuItem("TankDraft/Match/Author Reference Roster")]
        public static string Author()
        {
            RequireIdle();
            BattleScenarioCatalogAsset catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            BattleViewCatalogAsset views = Load<BattleViewCatalogAsset>(ViewCatalogPath);
            MatchSettingsAsset settings = Load<MatchSettingsAsset>(SettingsPath);
            MetaCatalogAsset meta = Load<MetaCatalogAsset>(MetaCatalogPath);

            // The initial four assets and their order/balance remain untouched.
            BattleUnitAsset mines = Load<BattleUnitAsset>(UnitRoot + "/Unit_Mines.asset");
            BattleUnitAsset heavy = Load<BattleUnitAsset>(UnitRoot + "/Unit_HeavyTank.asset");
            BattleUnitAsset destroyer = Load<BattleUnitAsset>(UnitRoot + "/Unit_TankDestroyer.asset");
            BattleUnitAsset artillery = Load<BattleUnitAsset>(UnitRoot + "/Unit_Artillery.asset");
            BattleProjectileAsset shell = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Shell.asset");
            BattleProjectileAsset dart = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Dart.asset");
            BattleProjectileAsset mortar = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Mortar.asset");

            BattleUnitAsset obstacle = CreateUnitIfMissing("AntiTankObstacle", "unit.anti_tank_obstacle", BattleAttackKind.Passive,
                150, 0, 0f, .40f, 14f, 0f, 1f, null, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f);
            BattleUnitAsset medium = CreateUnitIfMissing("MediumTank", "unit.medium_tank", BattleAttackKind.Projectile,
                135, 24, 1.22f, .34f, 2.2f, 1.35f, .92f, shell, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f);
            BattleUnitAsset light = CreateUnitIfMissing("LightRaider", "unit.light_raider", BattleAttackKind.Projectile,
                76, 16, 1.72f, .27f, 1.05f, 2.35f, .55f, dart, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f);
            BattleUnitAsset mobileMortar = CreateUnitIfMissing("MobileMortar", "unit.mobile_mortar", BattleAttackKind.Projectile,
                88, 21, .82f, .30f, 1.4f, 5.8f, 2.15f, mortar, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f);
            BattleUnitAsset rocket = CreateUnitIfMissing("RocketArtillery", "unit.rocket_artillery", BattleAttackKind.Projectile,
                68, 28, .58f, .31f, 1.3f, 6.6f, 2.85f, mortar, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f);
            BattleUnitAsset stealth = CreateUnitIfMissing("StealthVehicle", "unit.stealth_vehicle", BattleAttackKind.Projectile,
                72, 23, 1.52f, .27f, 1.1f, 2.25f, .72f, dart, BattleUnitAbilityKind.OpeningBacklineJump, 0f, 0f, 0, null, 0f);
            BattleUnitAsset repair = CreateUnitIfMissing("RepairVehicle", "unit.repair_vehicle", BattleAttackKind.Projectile,
                112, 16, .92f, .33f, 1.9f, 2.4f, 1.12f, shell, BattleUnitAbilityKind.RepairPulse, 3f, 2.45f, 18, null, 0f);

            ContentEntryAsset droneContent = CreateSupportContentIfMissing("unit.drone_scout", FormationRow.Artillery, "Дрон-разведчик", "Поддерживающий дрон носителя.", "Артиллерия");
            BattleUnitAsset drone = CreateUnitIfMissing("DroneScout", droneContent, BattleAttackKind.Projectile,
                42, 9, 2.05f, .18f, .45f, 3.0f, .75f, dart, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f, SupportRoot);
            BattleUnitAsset carrier = CreateUnitIfMissing("DroneCarrier", "unit.drone_carrier", BattleAttackKind.Projectile,
                92, 14, .62f, .34f, 1.5f, 5.1f, 1.25f, dart, BattleUnitAbilityKind.SpawnUnit, 4f, .78f, 0, drone, 6f);

            // Confirmed V2 03:14: engineer spawns a sentry every 5 seconds; sentry lasts 5 seconds,
            // fires every 1/6 second and deals 50% of the engineer's temporary base damage (22 -> 11).
            ContentEntryAsset engineerContent = CreateMetaContentIfMissing("unit.engineer_vehicle", FormationRow.Tank, 6, "Инженерная машина", "Развёртывает временную огневую точку.", "Танк");
            EnsureEngineerPlaceholderIcon(engineerContent);
            AddMetaEntryIfMissing(meta, engineerContent);
            ContentEntryAsset sentryContent = CreateSupportContentIfMissing("unit.sentry_turret", FormationRow.Tank, "Автоматическая турель", "Временная огневая точка инженера.", "Танк");
            BattleUnitAsset sentry = CreateUnitIfMissing("SentryTurret", sentryContent, BattleAttackKind.Projectile,
                50, 11, 0f, .20f, 3.5f, 3.1f, 1f / 6f, dart, BattleUnitAbilityKind.None, 0f, 0f, 0, null, 0f, SupportRoot);
            BattleUnitAsset engineer = CreateUnitIfMissing("EngineerVehicle", engineerContent, BattleAttackKind.Projectile,
                104, 22, .86f, .33f, 1.8f, 2.2f, 1.1f, shell, BattleUnitAbilityKind.SpawnUnit, 5f, .75f, 0, sentry, 5f, spawnDamagePercent: 50);

            BattleUnitAsset[] orderedPlayable = { mines, heavy, destroyer, artillery, obstacle, medium, light, mobileMortar, rocket, stealth, repair, carrier, engineer };
            BattleUnitAsset[] allUnits = { mines, heavy, destroyer, artillery, obstacle, medium, light, mobileMortar, rocket, stealth, repair, carrier, engineer, drone, sentry };
            AddReferences(catalog, "_units", allUnits);
            AddViewEntries(views, new[]
            {
                Pair(obstacle, "Battle_unit_mines.prefab", new Color(.72f, .67f, .46f), new Vector3(1.15f, .78f, 1f)),
                Pair(medium, "Battle_unit_heavy_tank.prefab", new Color(.38f, .75f, .62f), new Vector3(.84f, 1.10f, 1f)),
                Pair(light, "Battle_unit_tank_destroyer.prefab", new Color(.88f, .68f, .30f), new Vector3(.68f, .82f, 1f)),
                Pair(mobileMortar, "Battle_unit_field_artillery.prefab", new Color(.57f, .46f, .84f), new Vector3(.76f, 1.05f, 1f)),
                Pair(rocket, "Battle_unit_field_artillery.prefab", new Color(.94f, .36f, .28f), new Vector3(1.06f, .72f, 1f)),
                Pair(stealth, "Battle_unit_tank_destroyer.prefab", new Color(.25f, .61f, .57f), new Vector3(.76f, .76f, 1f)),
                Pair(repair, "Battle_unit_heavy_tank.prefab", new Color(.88f, .80f, .32f), new Vector3(.90f, .90f, 1f)),
                Pair(carrier, "Battle_unit_field_artillery.prefab", new Color(.36f, .63f, .92f), new Vector3(1.02f, .80f, 1f)),
                Pair(engineer, "Battle_unit_heavy_tank.prefab", new Color(.82f, .54f, .28f), new Vector3(.82f, 1.04f, 1f)),
                Pair(drone, "Battle_unit_tank_destroyer.prefab", new Color(.64f, .86f, .96f), new Vector3(.45f, .45f, 1f)),
                Pair(sentry, "Battle_unit_mines.prefab", new Color(.82f, .82f, .86f), new Vector3(.62f, .62f, 1f))
            });
            AddDraftEntries(settings, orderedPlayable);
            BattleScenarioAsset reference = CreateScenarioIfMissing("ReferenceRoster", "scenario.reference_roster", "Reference roster", 5314, new[] { medium, stealth, repair, carrier });
            BattleScenarioAsset frontline = CreateScenarioIfMissing("ReferenceRoster_Frontline", "scenario.reference_roster_frontline", "Reference roster frontline", 5315, new[] { obstacle, mines, heavy, destroyer });
            BattleScenarioAsset backline = CreateScenarioIfMissing("ReferenceRoster_Backline", "scenario.reference_roster_backline", "Reference roster backline", 5316, new[] { artillery, mobileMortar, rocket, carrier });
            BattleScenarioAsset engineering = CreateScenarioIfMissing("ReferenceRoster_Engineering", "scenario.reference_roster_engineering", "Reference roster engineering", 5317, new[] { stealth, repair, engineer, light });
            AddReferences(catalog, "_scenarios", new[] { reference, frontline, backline, engineering });

            AssetDatabase.SaveAssets();
            return Validate();
        }

        public static string Validate()
        {
            BattleScenarioCatalogAsset catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            BattleViewCatalogAsset views = Load<BattleViewCatalogAsset>(ViewCatalogPath);
            MatchSettingsAsset settings = Load<MatchSettingsAsset>(SettingsPath);
            catalog.Validate();
            BattleDefinitions definitions = catalog.CreateDefinitions();
            views.Validate(definitions);
            settings.Validate();
            RequireAbility(definitions, "unit.stealth_vehicle", BattleUnitAbilityKind.OpeningBacklineJump);
            RequireAbility(definitions, "unit.repair_vehicle", BattleUnitAbilityKind.RepairPulse);
            RequireAbility(definitions, "unit.drone_carrier", BattleUnitAbilityKind.None);
            if (definitions.Unit("unit.drone_carrier").ProjectileId != "projectile.drone")
                throw new InvalidOperationException("Drone carrier must fire independent drone projectiles.");
            RequireAbility(definitions, "unit.engineer_vehicle", BattleUnitAbilityKind.SpawnUnit);
            RequireSpawnDamagePercent(definitions, "unit.engineer_vehicle", 50);
            RequireAbility(definitions, "unit.drone_scout", BattleUnitAbilityKind.None);
            RequireAbility(definitions, "unit.sentry_turret", BattleUnitAbilityKind.None);
            return "PASS reference roster: catalogs, views, draft settings, support children and abilities validate. Temporary balance values are authored only for newly created assets.";
        }

        // Editor-only diagnostic. Its 120-second watchdog verifies roster data, never defines match timing.
        public static string ValidateSimulationAllScenarios()
        {
            BattleScenarioCatalogAsset catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            BattleDefinitions definitions = catalog.CreateDefinitions();
            BattleRules rules = catalog.CreateRules();
            BattleScenarioAsset[] scenarios =
            {
                Load<BattleScenarioAsset>(ReferenceScenarioPath),
                Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_ReferenceRoster_Frontline.asset"),
                Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_ReferenceRoster_Backline.asset"),
                Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_ReferenceRoster_Engineering.asset")
            };
            var states = new List<BattleEntityState>();
            int sampleTicks = Math.Max(1, Mathf.RoundToInt(.5f / rules.TickSeconds));
            int watchdog = Mathf.CeilToInt(120f / rules.TickSeconds);
            for (int index = 0; index < scenarios.Length; index++)
            {
                BattleScenarioDefinition scenario = scenarios[index].CreateDefinition();
                using (var simulation = new BattleSimulation(definitions, rules, scenario))
                {
                    for (int tick = 0; tick < watchdog && simulation.Outcome == BattleOutcome.Running; tick++)
                    {
                        simulation.Step();
                        if (tick % sampleTicks == 0)
                        {
                            simulation.Capture(states);
                            ValidateStates(states, rules, scenario.Id);
                        }
                    }
                    if (simulation.Outcome == BattleOutcome.Running)
                        throw new InvalidOperationException("Temporary roster diagnostic timed out after 120 seconds: " + scenario.Id);
                }
            }
            return "PASS reference roster simulation: 4 bounded deterministic diagnostic scenarios terminated within 120 seconds.";
        }

        private static BattleUnitAsset CreateUnitIfMissing(string name, string contentId, BattleAttackKind attack, int hp, int damage, float speed, float radius, float mass, float range, float cooldown, BattleProjectileAsset projectile, BattleUnitAbilityKind ability, float interval, float abilityRadius, int amount, BattleUnitAsset spawnUnit, float lifetime, int spawnDamagePercent = 0)
        {
            ContentEntryAsset content = Load<ContentEntryAsset>(MetaRoot + "/Units/" + contentId + ".asset");
            return CreateUnitIfMissing(name, content, attack, hp, damage, speed, radius, mass, range, cooldown, projectile, ability, interval, abilityRadius, amount, spawnUnit, lifetime, UnitRoot, spawnDamagePercent);
        }

        private static BattleUnitAsset CreateUnitIfMissing(string name, ContentEntryAsset content, BattleAttackKind attack, int hp, int damage, float speed, float radius, float mass, float range, float cooldown, BattleProjectileAsset projectile, BattleUnitAbilityKind ability, float interval, float abilityRadius, int amount, BattleUnitAsset spawnUnit, float lifetime, string root = UnitRoot, int spawnDamagePercent = 0)
        {
            string path = root + "/Unit_" + name + ".asset";
            BattleUnitAsset asset = AssetDatabase.LoadAssetAtPath<BattleUnitAsset>(path);
            if (asset != null)
                return asset;
            if (content == null)
                throw new InvalidOperationException("Missing content for " + name + ".");

            // Temporary balance for the first playable roster. Passive obstacles use zero damage
            // and never attack; BattleUnitDefinition explicitly permits that canonical pairing.
            EnsureFolder(root);
            asset = ScriptableObject.CreateInstance<BattleUnitAsset>();
            AssetDatabase.CreateAsset(asset, path);
            Set(asset, "_content", content);
            Set(asset, "_attack", (int)attack);
            Set(asset, "_maxHp", hp);
            Set(asset, "_damage", damage);
            Set(asset, "_moveSpeed", speed);
            Set(asset, "_radius", radius);
            Set(asset, "_mass", mass);
            Set(asset, "_range", range);
            Set(asset, "_cooldownSeconds", cooldown);
            Set(asset, "_projectile", projectile);
            Set(asset, "_abilityKind", (int)ability);
            Set(asset, "_abilityIntervalSeconds", interval);
            Set(asset, "_abilityRadius", abilityRadius);
            Set(asset, "_abilityAmount", amount);
            Set(asset, "_spawnUnit", spawnUnit);
            Set(asset, "_spawnLifetimeSeconds", lifetime);
            Set(asset, "_spawnDamagePercent", spawnDamagePercent);
            return asset;
        }

        private static ContentEntryAsset CreateMetaContentIfMissing(string id, FormationRow row, int requiredArena, string title, string description, string role)
        {
            return CreateContentIfMissing(MetaRoot + "/Units/" + id + ".asset", id, row, requiredArena, title, description, role);
        }

        private static ContentEntryAsset CreateSupportContentIfMissing(string id, FormationRow row, string title, string description, string role)
        {
            return CreateContentIfMissing(SupportRoot + "/" + id + ".asset", id, row, 1, title, description, role);
        }

        private static ContentEntryAsset CreateContentIfMissing(string path, string id, FormationRow row, int requiredArena, string title, string description, string role)
        {
            ContentEntryAsset asset = AssetDatabase.LoadAssetAtPath<ContentEntryAsset>(path);
            if (asset != null)
                return asset;
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            asset = ScriptableObject.CreateInstance<ContentEntryAsset>();
            AssetDatabase.CreateAsset(asset, path);
            Set(asset, "_id", id);
            Set(asset, "_kind", (int)ContentKind.Unit);
            Set(asset, "_row", (int)row);
            Set(asset, "_requiredArena", requiredArena);
            Set(asset, "_title", title);
            Set(asset, "_description", description);
            Set(asset, "_roleLabel", role);
            return asset;
        }

        private static BattleScenarioAsset CreateScenarioIfMissing(string name, string id, string title, int seed, BattleUnitAsset[] units)
        {
            string path = BattleRoot + "/Scenarios/Scenario_" + name + ".asset";
            BattleScenarioAsset scenario = AssetDatabase.LoadAssetAtPath<BattleScenarioAsset>(path);
            if (scenario != null)
                return scenario;
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));
            scenario = ScriptableObject.CreateInstance<BattleScenarioAsset>();
            AssetDatabase.CreateAsset(scenario, path);
            Set(scenario, "_id", id);
            Set(scenario, "_title", title);
            Set(scenario, "_seed", seed);
            SerializedObject serialized = new SerializedObject(scenario);
            SerializedProperty stacks = serialized.FindProperty("_stacks");
            stacks.arraySize = units.Length * 2;
            for (int side = 0; side < 2; side++)
                for (int index = 0; index < units.Length; index++)
                {
                    SerializedProperty stack = stacks.GetArrayElementAtIndex(side * units.Length + index);
                    stack.FindPropertyRelative("side").intValue = side;
                    stack.FindPropertyRelative("unit").objectReferenceValue = units[index];
                    stack.FindPropertyRelative("count").intValue = index % 2 == 0 ? 1 : 2;
                }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(scenario);
            return scenario;
        }

        private static void EnsureEngineerPlaceholderIcon(ContentEntryAsset engineer)
        {
            if (engineer.Icon != null)
                return;
            ContentEntryAsset repair = Load<ContentEntryAsset>(MetaRoot + "/Units/unit.repair_vehicle.asset");
            ContentEntryAsset heavy = Load<ContentEntryAsset>(MetaRoot + "/Units/unit.heavy_tank.asset");
            // Current placeholder entries can be iconless; the primitive fallback keeps the new
            // engineer visible until authored icon art replaces it.
            Sprite placeholder = repair.Icon ?? heavy.Icon ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Diamond.png");
            Set(engineer, "_icon", placeholder);
        }

        private static void ValidateStates(List<BattleEntityState> states, BattleRules rules, string scenarioId)
        {
            float xBound = rules.HalfWidth + 4f;
            float yBound = rules.HalfHeight + 4f;
            for (int index = 0; index < states.Count; index++)
            {
                BattleEntityState state = states[index];
                if (!Finite(state.Position.X) || !Finite(state.Position.Y) || !Finite(state.PreviousPosition.X) || !Finite(state.PreviousPosition.Y) ||
                    Mathf.Abs(state.Position.X) > xBound || Mathf.Abs(state.PreviousPosition.X) > xBound || Mathf.Abs(state.Position.Y) > yBound || Mathf.Abs(state.PreviousPosition.Y) > yBound)
                    throw new InvalidOperationException("Out-of-bounds or non-finite battle state in " + scenarioId + ".");
            }
        }

        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        private static void AddMetaEntryIfMissing(MetaCatalogAsset catalog, ContentEntryAsset entry)
        {
            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("_entries");
            if (Contains(entries, entry))
                return;
            int index = entries.arraySize;
            entries.arraySize++;
            entries.GetArrayElementAtIndex(index).objectReferenceValue = entry;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static void AddReferences(UnityEngine.Object target, string propertyName, BattleUnitAsset[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(propertyName);
            for (int index = 0; index < values.Length; index++)
            {
                if (Contains(array, values[index]))
                    continue;
                int destination = array.arraySize;
                array.arraySize++;
                array.GetArrayElementAtIndex(destination).objectReferenceValue = values[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void AddReferences(UnityEngine.Object target, string propertyName, BattleScenarioAsset[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty array = serialized.FindProperty(propertyName);
            for (int index = 0; index < values.Length; index++)
            {
                if (Contains(array, values[index]))
                    continue;
                int destination = array.arraySize;
                array.arraySize++;
                array.GetArrayElementAtIndex(destination).objectReferenceValue = values[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void AddDraftEntries(MatchSettingsAsset settings, BattleUnitAsset[] units)
        {
            SerializedObject serialized = new SerializedObject(settings);
            SerializedProperty entries = serialized.FindProperty("_units");
            for (int index = 0; index < units.Length; index++)
            {
                if (ContainsDraftUnit(entries, units[index]))
                    continue;
                int destination = entries.arraySize;
                entries.arraySize++;
                SerializedProperty entry = entries.GetArrayElementAtIndex(destination);
                entry.FindPropertyRelative("unit").objectReferenceValue = units[index];
                entry.FindPropertyRelative("addCount").intValue = 1;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
        }

        private static void AddViewEntries(BattleViewCatalogAsset catalog, ViewSpec[] specs)
        {
            SerializedObject serialized = new SerializedObject(catalog);
            SerializedProperty entries = serialized.FindProperty("_entries");
            for (int index = 0; index < specs.Length; index++)
            {
                if (HasView(entries, specs[index].unit.Id))
                    continue;
                BattleEntityView prefab = CopyVariantIfMissing(specs[index]);
                int destination = entries.arraySize;
                entries.arraySize++;
                SerializedProperty entry = entries.GetArrayElementAtIndex(destination);
                entry.FindPropertyRelative("definitionId").stringValue = specs[index].unit.Id;
                entry.FindPropertyRelative("kind").intValue = (int)BattleEntityKind.Unit;
                entry.FindPropertyRelative("prefab").objectReferenceValue = prefab;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(catalog);
        }

        private static BattleEntityView CopyVariantIfMissing(ViewSpec spec)
        {
            string destination = PrefabRoot + "/Battle_" + spec.unit.Id.Replace('.', '_') + ".prefab";
            GameObject existingRoot = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
            BattleEntityView existing = existingRoot == null ? null : existingRoot.GetComponent<BattleEntityView>();
            if (existing != null)
                return existing;
            string source = PrefabRoot + "/" + spec.sourcePrefab;
            if (!AssetDatabase.CopyAsset(source, destination))
                throw new IOException("Could not copy visual source " + source + ".");
            GameObject root = PrefabUtility.LoadPrefabContents(destination);
            try
            {
                Transform visual = root.transform.Find("VisualRoot");
                if (visual == null)
                    throw new InvalidOperationException("Copied battle visual has no VisualRoot: " + source);
                SpriteRenderer[] renderers = visual.GetComponentsInChildren<SpriteRenderer>(true);
                for (int index = 0; index < renderers.Length; index++)
                    if (renderers[index].color.a > .35f)
                        renderers[index].color = Color.Lerp(renderers[index].color, spec.color, .38f);
                Transform silhouette = visual.Find("Hull") ?? visual.Find("MineBody") ?? visual.Find("TurretRoot/Turret") ?? visual;
                silhouette.localScale = Vector3.Scale(silhouette.localScale, spec.silhouetteScale);
                PrefabUtility.SaveAsPrefabAsset(root, destination);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            GameObject saved = AssetDatabase.LoadAssetAtPath<GameObject>(destination);
            return saved == null ? null : saved.GetComponent<BattleEntityView>();
        }

        private static ViewSpec Pair(BattleUnitAsset unit, string sourcePrefab, Color color, Vector3 silhouetteScale) => new ViewSpec(unit, sourcePrefab, color, silhouetteScale);

        private static void RequireAbility(BattleDefinitions definitions, string id, BattleUnitAbilityKind expected)
        {
            if ((definitions.Unit(id).Ability?.Kind ?? BattleUnitAbilityKind.None) != expected)
                throw new InvalidOperationException(id + " does not have expected ability " + expected + ".");
        }

        private static void RequireSpawnDamagePercent(BattleDefinitions definitions, string id, int expected)
        {
            BattleUnitAbilityDefinition ability = definitions.Unit(id).Ability;
            if (ability == null || ability.SpawnDamagePercent != expected)
                throw new InvalidOperationException(id + " does not have expected spawn damage percent " + expected + ".");
        }

        private static bool Contains(SerializedProperty array, UnityEngine.Object value)
        {
            for (int index = 0; index < array.arraySize; index++)
                if (array.GetArrayElementAtIndex(index).objectReferenceValue == value)
                    return true;
            return false;
        }

        private static bool ContainsDraftUnit(SerializedProperty entries, BattleUnitAsset unit)
        {
            for (int index = 0; index < entries.arraySize; index++)
                if (entries.GetArrayElementAtIndex(index).FindPropertyRelative("unit").objectReferenceValue == unit)
                    return true;
            return false;
        }

        private static bool HasView(SerializedProperty entries, string id)
        {
            for (int index = 0; index < entries.arraySize; index++)
                if (entries.GetArrayElementAtIndex(index).FindPropertyRelative("definitionId").stringValue == id)
                    return true;
            return false;
        }

        private static T Load<T>(string path) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null)
                throw new FileNotFoundException("Required authored asset is missing.", path);
            return asset;
        }

        private static void Set(UnityEngine.Object target, string field, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            if (value is UnityEngine.Object reference)
                property.objectReferenceValue = reference;
            else if (value == null)
                property.objectReferenceValue = null;
            else if (value is string text)
                property.stringValue = text;
            else if (value is int integer)
                property.intValue = integer;
            else if (value is bool flag)
                property.boolValue = flag;
            else if (value is float number)
                property.floatValue = number;
            else
                throw new ArgumentException("Unsupported serialized value " + value.GetType().Name + ".");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Reference roster authoring requires a compiled editor in Edit Mode.");
        }

        private readonly struct ViewSpec
        {
            public readonly BattleUnitAsset unit;
            public readonly string sourcePrefab;
            public readonly Color color;
            public readonly Vector3 silhouetteScale;
            public ViewSpec(BattleUnitAsset unit, string sourcePrefab, Color color, Vector3 silhouetteScale)
            {
                this.unit = unit;
                this.sourcePrefab = sourcePrefab;
                this.color = color;
                this.silhouetteScale = silhouetteScale;
            }
        }
    }
}
