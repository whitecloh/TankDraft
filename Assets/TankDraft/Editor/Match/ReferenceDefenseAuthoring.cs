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
        [MenuItem("TankDraft/Match/Author Reference Defense Mechanics")]
        public static string AuthorDefenseMechanics()
        {
            RequireIdle();
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var views = Load<BattleViewCatalogAsset>(ViewCatalogPath);
            var settings = Load<MatchSettingsAsset>(SettingsPath);
            var meta = Load<MetaCatalogAsset>(MetaCatalogPath);
            var shell = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Shell.asset");
            var dart = Load<BattleProjectileAsset>(BattleRoot + "/Projectiles/Projectile_Dart.asset");
            var shield = CreateDefenseUnit(meta, "ShieldVehicle", "unit.shield_vehicle", "Машина активной защиты",
                "Периодически защищает ближайших союзников щитом.", FormationRow.Artillery, BattleAttackKind.Projectile,
                110, 15, .75f, 4.5f, 1.2f, shell, 40, false, 0, 0);
            var armor = CreateDefenseUnit(meta, "ReactiveArmorTank", "unit.reactive_armor_tank", "Танк с активной бронёй",
                "Блокирует первое прямое попадание в каждом раунде.", FormationRow.Tank, BattleAttackKind.Projectile,
                160, 24, .8f, 1.35f, 1.3f, shell, 0, true, 0, 0);
            var magazine = CreateDefenseUnit(meta, "MagazineDestroyer", "unit.magazine_destroyer", "Автоматическая ПТ",
                "Шесть быстрых выстрелов, затем перезарядка магазина.", FormationRow.TankDestroyer, BattleAttackKind.Projectile,
                90, 9, .85f, 4.8f, .2f, dart, 0, false, 6, 0);
            var recovery = CreateDefenseUnit(meta, "RecoveryAssault", "unit.recovery_assault", "Восстанавливающаяся штурмовая машина",
                "Ближняя атака восстанавливает часть фактически снятого здоровья врага.", FormationRow.Tank, BattleAttackKind.Melee,
                130, 25, 1.3f, .12f, 1f, null, 0, false, 0, 25);
            var units = new[] { shield, armor, magazine, recovery };
            AddReferences(catalog, "_units", units);
            AddDraftEntries(settings, units);
            AddViewEntries(views, new[] {
                Pair(shield, "Battle_unit_field_artillery.prefab", Color.gray, Vector3.one),
                Pair(armor, "Battle_unit_heavy_tank.prefab", Color.gray, Vector3.one),
                Pair(magazine, "Battle_unit_tank_destroyer.prefab", Color.gray, Vector3.one),
                Pair(recovery, "Battle_unit_heavy_tank.prefab", Color.gray, Vector3.one)
            });
            var toxin = Load<BattleUnitAsset>(UnitRoot + "/Unit_ToxinDestroyer.asset");
            AddReferences(catalog, "_scenarios", new[] {
                CreateScenarioIfMissing("ReferenceDefense", "scenario.reference_defense", "Shield, block, magazine, recovery", 5325, units),
                CreateScenarioIfMissing("ReferenceDefenseStatus", "scenario.reference_defense_status", "Defense and poison", 5326, new[] { shield, armor, toxin, recovery })
            });
            AssetDatabase.SaveAssets();
            return ValidateDefenseMechanics();
        }

        static BattleUnitAsset CreateDefenseUnit(MetaCatalogAsset meta, string name, string id, string title, string description,
            FormationRow row, BattleAttackKind attack, int hp, int damage, float speed, float range, float cooldown,
            BattleProjectileAsset projectile, int shieldPercent, bool firstBlock, int shots, int steal)
        {
            var content = CreateMetaContentIfMissing(id, row, 1, title, description, row == FormationRow.Artillery ? "Поддержка" : row == FormationRow.TankDestroyer ? "ПТ-САУ" : "Танк");
            if (content.Icon == null)
                Set(content, "_icon", Load<ContentEntryAsset>(MetaRoot + "/Units/unit.heavy_tank.asset").Icon ?? AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Diamond.png"));
            AddMetaEntryIfMissing(meta, content);
            bool created = AssetDatabase.LoadAssetAtPath<BattleUnitAsset>(UnitRoot + "/Unit_" + name + ".asset") == null;
            var unit = CreateUnitIfMissing(name, content, attack, hp, damage, speed, .3f, 1.8f, range, cooldown,
                projectile, BattleUnitAbilityKind.None, 0, 0, 0, null, 0);
            if (created)
            {
                Set(unit, "_shieldCapacityHpPercent", shieldPercent);
                Set(unit, "_shieldIntervalSeconds", 4f);
                Set(unit, "_shieldRadius", 3f);
                Set(unit, "_shieldLifetimeSeconds", 3.5f);
                Set(unit, "_blocksFirstHit", firstBlock);
                Set(unit, "_magazineShots", shots);
                Set(unit, "_magazineReloadSeconds", 3f);
                Set(unit, "_lifeStealPercent", steal);
            }
            return unit;
        }

        public static string ValidateDefenseMechanics()
        {
            var defs = Load<BattleScenarioCatalogAsset>(CatalogPath).CreateDefinitions();
            Load<BattleViewCatalogAsset>(ViewCatalogPath).Validate(defs);
            Load<MatchSettingsAsset>(SettingsPath).Validate();
            if (defs.Unit("unit.shield_vehicle").Shield == null || !defs.Unit("unit.reactive_armor_tank").BlocksFirstHit ||
                defs.Unit("unit.magazine_destroyer").Magazine == null || defs.Unit("unit.recovery_assault").LifeStealPercent <= 0)
                throw new InvalidOperationException("Defense profiles are not wired.");
            foreach (var id in new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" })
            {
                var d = defs.Unit(id);
                if (d.Shield != null || d.BlocksFirstHit || d.Magazine != null || d.LifeStealPercent != 0)
                    throw new InvalidOperationException("Legacy defense defaults changed: " + id);
            }
            return "PASS defense authored definitions, meta/draft entries and prepared views; legacy defaults preserved.";
        }

        public static string ValidateDefenseScenarios(BattleWorldView world = null)
        {
            if (world != null && !UnityEngine.Application.isPlaying) throw new InvalidOperationException("Runtime view validation requires Play Mode.");
            var catalog = Load<BattleScenarioCatalogAsset>(CatalogPath);
            var defs = catalog.CreateDefinitions(); var rules = catalog.CreateRules();
            var states = new List<BattleEntityState>(); var events = new List<BattleEvent>();
            var seen = new HashSet<string>(); string results = ""; int maxViews = 0;
            foreach (var name in new[] { "ReferenceDefense", "ReferenceDefenseStatus" })
            {
                if (world != null) world.Clear();
                var scenario = Load<BattleScenarioAsset>(BattleRoot + "/Scenarios/Scenario_" + name + ".asset").CreateDefinition();
                using (var sim = new BattleSimulation(defs, rules, scenario))
                {
                    for (int i = 0; i < Math.Ceiling(120 / rules.TickSeconds) && sim.Outcome == BattleOutcome.Running; i++)
                    {
                        sim.Step(); sim.Capture(states); ValidateStates(states, rules, scenario.Id);
                        sim.DrainEvents(events);
                        if (world != null)
                        {
                            float time = i * rules.TickSeconds;
                            world.Sync(states, 1, time); world.Present(events, time); world.TickEffects(time);
                            int count = world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews;
                            if (count != states.Count) throw new InvalidOperationException("Defense snapshot/view mismatch.");
                            maxViews = Math.Max(maxViews, count);
                        }
                        foreach (var state in states) seen.Add(state.DefinitionId);
                    }
                    if (sim.Outcome == BattleOutcome.Running) throw new InvalidOperationException("Diagnostic watchdog: " + name);
                    results += name + "=" + sim.Outcome + ",ticks=" + sim.Tick + "; ";
                }
            }
            foreach (var id in new[] { "unit.shield_vehicle", "unit.reactive_armor_tank", "unit.magazine_destroyer", "unit.recovery_assault" })
                if (!seen.Contains(id)) throw new InvalidOperationException("No diagnostic coverage for " + id);
            if (world != null)
            {
                world.Clear();
                if (world.ActiveUnitViews + world.ActiveProjectileViews + world.ActiveZoneViews + world.ActiveEffectViews != 0)
                    throw new InvalidOperationException("Defense views/effects did not return to pool.");
            }
            return "PASS " + (world == null ? "simulation " : "Play Mode views+simulation ") + results + "maxViews=" + maxViews;
        }
    }
}
