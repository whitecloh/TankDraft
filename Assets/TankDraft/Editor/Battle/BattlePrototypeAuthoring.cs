using System;
using System.IO;
using System.Collections.Generic;
using TankDraft.Contracts;
using TankDraft.Contracts.Battle;
using TankDraft.Content;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.BattleBootstrap;
using TankDraft.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Editor.Battle
{
    public static class BattlePrototypeAuthoring
    {
        public const string ScenePath = "Assets/TankDraft/Scenes/Diagnostics/BattlePrototype.unity";
        public const string ConfigRoot = "Assets/TankDraft/Configs/Battle";
        public const string PrefabRoot = "Assets/TankDraft/Prefabs/Battle";
        public const string ArtRoot = "Assets/TankDraft/Art/Battle/Primitives";
        public const string CatalogPath = ConfigRoot + "/Scenarios/BattleScenarioCatalog.asset";
        public const string ViewsPath = ConfigRoot + "/Presentation/BattleViewCatalog.asset";
        public const string PresentationPath = ConfigRoot + "/Presentation/BattlePresentation.asset";
        static Sprite _square, _circle, _ring, _diamond;
        static readonly Color Dark = new Color(.06f, .08f, .10f, 1), Metal = new Color(.19f, .23f, .26f, 1);
        [MenuItem("TankDraft/Battle/Author Primitive Prototype")]
        public static string Author()
        {
            RequireIdle();
            var original = EditorSceneManager.GetSceneManagerSetup();
            for (int i = 0; i < UnityEngine.SceneManagement.SceneManager.sceneCount; i++)
                if (UnityEngine.SceneManagement.SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes first.");
            try
            {
                foreach (var path in new[]
                {
                    ConfigRoot,
                    PrefabRoot,
                    ArtRoot
                }

                )
                    Directory.CreateDirectory(path);
                AssetDatabase.Refresh();
                CreateSprites();
                CreateContent();
                CreateVisualPrefabs();
                CreateButtonPrefab();
                CreateScene();
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(original);
            }

            return "PASS: authored primitive battle configs, prefabs, UI and scene.";
        }

        [MenuItem("TankDraft/Battle/Open Primitive Prototype")]
        public static void Open()
        {
            RequireIdle();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            EditorSceneManager.OpenScene(ScenePath);
        }

        static T Asset<T>(string path)
            where T : ScriptableObject
        {
            var a = AssetDatabase.LoadAssetAtPath<T>(path);
            if (!a)
            {
                a = ScriptableObject.CreateInstance<T>();
                AssetDatabase.CreateAsset(a, path);
            }

            return a;
        }

        static void Set(UnityEngine.Object obj, string field, object value)
        {
            var so = new SerializedObject(obj);
            var p = so.FindProperty(field);
            if (p == null)
                throw new Exception(obj.GetType().Name + " lacks " + field);
            if (value is UnityEngine.Object asset)
                p.objectReferenceValue = asset;
            else if (value == null)
                p.objectReferenceValue = null;
            else if (value is string str)
                p.stringValue = str;
            else if (value is float f)
                p.floatValue = f;
            else if (value is int n)
                p.intValue = n;
            else if (value is bool b)
                p.boolValue = b;
            else if (value is Color c)
                p.colorValue = c;
            else
                throw new ArgumentException("Unsupported authoring property type: " + value.GetType());
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
        }

        static void SetRefs(UnityEngine.Object obj, string field, UnityEngine.Object[] values)
        {
            var so = new SerializedObject(obj);
            var p = so.FindProperty(field);
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(obj);
        }

        static void CreateContent()
        {
            var zone = Asset<BattleZoneAsset>(ConfigRoot + "/Zones/Zone_Burning.asset");
            Set(zone, "_id", "zone.burning");
            Set(zone, "_radius", 1.15f);
            Set(zone, "_tickDamage", 7);
            Set(zone, "_periodSeconds", .5f);
            Set(zone, "_lifetimeSeconds", 3.5f);
            var shell = Projectile("Shell", "projectile.shell", 8f, .075f, 0, null);
            var dart = Projectile("Dart", "projectile.dart", 13f, .055f, 0, null);
            var mortar = Projectile("Mortar", "projectile.mortar", 5.8f, .12f, 1.05f, zone);
            var mines = Unit("Mines", "unit.mines", BattleAttackKind.ContactExplosion, 35, 48, 0, .28f, 100, 1.15f, .25f, null);
            var heavy = Unit("HeavyTank", "unit.heavy_tank", BattleAttackKind.Projectile, 190, 22, 1.05f, .37f, 3, .85f, 1.1f, shell);
            var td = Unit("TankDestroyer", "unit.tank_destroyer", BattleAttackKind.Projectile, 95, 38, .8f, .30f, 1.6f, 4.1f, 1.6f, dart);
            var arty = Unit("Artillery", "unit.field_artillery", BattleAttackKind.Projectile, 75, 19, .65f, .32f, 1.4f, 6.2f, 2.7f, mortar);
            var units = new[]
            {
                mines,
                heavy,
                td,
                arty
            };
            var rules = Asset<BattleRulesAsset>(ConfigRoot + "/Rules/BattleRules.asset");
            var standard = Scenario("Standard", "Стандартный бой", 612, new[] { 4, 5, 3, 2 }, new[] { 5, 4, 3, 2 }, units);
            var dense = Scenario("Dense", "Плотные армии", 1209, new[] { 16, 16, 12, 8 }, new[] { 16, 15, 12, 8 }, units);
            var artillery = Scenario("Artillery", "Артиллерийский бой", 326, new[] { 2, 3, 2, 4 }, new[] { 3, 3, 2, 3 }, units);
            var catalog = Asset<BattleScenarioCatalogAsset>(CatalogPath);
            Set(catalog, "_rules", rules);
            SetRefs(catalog, "_units", units);
            SetRefs(catalog, "_projectiles", new[] { shell, dart, mortar });
            SetRefs(catalog, "_zones", new[] { zone });
            SetRefs(catalog, "_scenarios", new[] { standard, dense, artillery });
            var presentation = Asset<BattlePresentationSettings>(PresentationPath);
            Set(presentation, "_effectSeconds", .22f);
            Set(presentation, "_side0Color", new Color(.30f, .70f, .96f, 1));
            Set(presentation, "_side1Color", new Color(.96f, .39f, .30f, 1));
            AssetDatabase.SaveAssets();
            catalog.Validate();
        }

        static BattleProjectileAsset Projectile(string name, string id, float speed, float radius, float aoe, BattleZoneAsset zone)
        {
            var a = Asset<BattleProjectileAsset>(ConfigRoot + "/Projectiles/Projectile_" + name + ".asset");
            Set(a, "_id", id);
            Set(a, "_speed", speed);
            Set(a, "_radius", radius);
            Set(a, "_impactRadius", aoe);
            Set(a, "_zone", zone);
            return a;
        }

        static BattleUnitAsset Unit(string name, string id, BattleAttackKind attack, int hp, int damage, float speed, float radius, float mass, float range, float cooldown, BattleProjectileAsset projectile)
        {
            var a = Asset<BattleUnitAsset>(ConfigRoot + "/Units/Unit_" + name + ".asset");
            var meta = AssetDatabase.LoadAssetAtPath<ContentEntryAsset>("Assets/TankDraft/Configs/Meta/" + (id.StartsWith("unit.") ? "Units/" : "Orders/") + id + ".asset");
            if (!meta)
                throw new Exception("Missing collection reference " + id);
            Set(a, "_content", meta);
            Set(a, "_attack", (int)attack);
            Set(a, "_maxHp", hp);
            Set(a, "_damage", damage);
            Set(a, "_moveSpeed", speed);
            Set(a, "_radius", radius);
            Set(a, "_mass", mass);
            Set(a, "_range", range);
            Set(a, "_cooldownSeconds", cooldown);
            Set(a, "_projectile", projectile);
            return a;
        }

        static BattleScenarioAsset Scenario(string name, string title, int seed, int[] side0, int[] side1, BattleUnitAsset[] units)
        {
            var a = Asset<BattleScenarioAsset>(ConfigRoot + "/Scenarios/Scenario_" + name + ".asset");
            Set(a, "_id", "scenario." + name.ToLowerInvariant());
            Set(a, "_title", title);
            Set(a, "_seed", seed);
            var so = new SerializedObject(a);
            var stacks = so.FindProperty("_stacks");
            stacks.arraySize = 8;
            for (int side = 0; side < 2; side++)
                for (int i = 0; i < 4; i++)
                {
                    var p = stacks.GetArrayElementAtIndex(side * 4 + i);
                    p.FindPropertyRelative("side").intValue = side;
                    p.FindPropertyRelative("unit").objectReferenceValue = units[i];
                    p.FindPropertyRelative("count").intValue = (side == 0 ? side0 : side1)[i];
                }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(a);
            return a;
        }

        static void CreateSprites()
        {
            _square = SpriteAsset("Square", 0);
            _circle = SpriteAsset("Circle", 1);
            _ring = SpriteAsset("Ring", 2);
            _diamond = SpriteAsset("Diamond", 3);
        }

        static Sprite SpriteAsset(string name, int shape)
        {
            string path = ArtRoot + "/Primitive_" + name + ".png";
            if (!File.Exists(path))
            {
                const int size = 64;
                var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
                var pixels = new Color[size * size];
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float dx = (x + .5f - size * .5f) / (size * .5f), dy = (y + .5f - size * .5f) / (size * .5f), d = Mathf.Sqrt(dx * dx + dy * dy);
                        bool inside = shape == 0 || (shape == 1 && d <= 1) || (shape == 2 && d <= 1 && d >= .82f) || (shape == 3 && Mathf.Abs(dx) + Mathf.Abs(dy) <= 1);
                        pixels[y * size + x] = inside ? Color.white : Color.clear;
                    }

                texture.SetPixels(pixels);
                texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(texture);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 64;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.SaveAndReimport();
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (!sprite)
                throw new Exception("Sprite import failed: " + path);
            return sprite;
        }

        static Transform Child(Transform parent, string name)
        {
            var g = new GameObject(name);
            g.transform.SetParent(parent, false);
            return g.transform;
        }

        static SpriteRenderer Shape(Transform parent, string name, Sprite sprite, Vector2 pos, Vector2 scale, Color color, int order)
        {
            var t = Child(parent, name);
            t.localPosition = new Vector3(pos.x, pos.y, 0);
            t.localScale = new Vector3(scale.x, scale.y, 1);
            var r = t.gameObject.AddComponent<SpriteRenderer>();
            r.sprite = sprite;
            r.color = color;
            r.sortingOrder = order;
            return r;
        }

        static void CreateVisualPrefabs()
        {
            var catalog = Asset<BattleViewCatalogAsset>(ViewsPath);
            var ids = new[]
            {
                "unit.mines",
                "unit.heavy_tank",
                "unit.tank_destroyer",
                "unit.field_artillery",
                "projectile.shell",
                "projectile.dart",
                "projectile.mortar",
                "zone.burning"
            };
            var prefabs = new BattleEntityView[ids.Length];
            for (int i = 0; i < ids.Length; i++)
                prefabs[i] = EntityPrefab(ids[i], i);
            var impact = EffectPrefab("Impact", false);
            var death = EffectPrefab("Death", true);
            var so = new SerializedObject(catalog);
            var entries = so.FindProperty("_entries");
            entries.arraySize = ids.Length;
            for (int i = 0; i < ids.Length; i++)
            {
                var p = entries.GetArrayElementAtIndex(i);
                p.FindPropertyRelative("definitionId").stringValue = ids[i];
                p.FindPropertyRelative("kind").intValue = i < 4 ? 0 : i < 7 ? 1 : 2;
                p.FindPropertyRelative("prefab").objectReferenceValue = prefabs[i];
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            Set(catalog, "_impactPrefab", impact);
            Set(catalog, "_deathPrefab", death);
            catalog.Validate(AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>(CatalogPath).CreateDefinitions());
        }

        static BattleEntityView EntityPrefab(string id, int type)
        {
            var root = new GameObject("Battle_" + id.Replace('.', '_'));
            root.AddComponent<SortingGroup>().sortingOrder = type < 4 ? 20 : type < 7 ? 100 : 5;
            try
            {
                var view = root.AddComponent<BattleEntityView>();
                var visual = Child(root.transform, "VisualRoot");
                var team = new List<SpriteRenderer>();
                Transform turret = null, muzzle = null, health = null;
                if (type < 4)
                {
                    Shape(visual, "Shadow", _circle, new Vector2(.05f, -.09f), new Vector2(.83f, .75f), new Color(0, 0, 0, .24f), -5);
                    if (type == 0)
                    {
                        team.Add(Shape(visual, "MineBody", _circle, Vector2.zero, new Vector2(.55f, .55f), Color.white, 0));
                        Shape(visual, "PressurePlate", _circle, Vector2.zero, new Vector2(.31f, .31f), Metal, 1);
                        Shape(visual, "WarningMark", _diamond, Vector2.zero, new Vector2(.15f, .15f), new Color(1, .72f, .18f, 1), 2);
                        turret = Child(visual, "TurretRoot");
                        muzzle = Child(turret, "MuzzleAnchor");
                    }
                    else
                    {
                        float width = type == 1 ? .70f : type == 2 ? .58f : .62f;
                        Shape(visual, "LeftTrack", _square, new Vector2(-width * .45f, 0), new Vector2(.16f, .82f), Dark, 0);
                        Shape(visual, "RightTrack", _square, new Vector2(width * .45f, 0), new Vector2(.16f, .82f), Dark, 0);
                        for (int n = 0; n < 4; n++)
                        {
                            Shape(visual, "TreadL_" + n, _square, new Vector2(-width * .45f, -.28f + n * .185f), new Vector2(.16f, .055f), Metal, 1);
                            Shape(visual, "TreadR_" + n, _square, new Vector2(width * .45f, -.28f + n * .185f), new Vector2(.16f, .055f), Metal, 1);
                        }

                        team.Add(Shape(visual, "Hull", _square, Vector2.zero, new Vector2(width * .72f, .70f), Color.white, 2));
                        Shape(visual, "EngineDeck", _square, new Vector2(0, -.19f), new Vector2(width * .48f, .17f), Metal, 3);
                        turret = Child(visual, "TurretRoot");
                        turret.localPosition = new Vector3(0, .08f, 0);
                        float barrel = type == 1 ? .42f : type == 2 ? .68f : .50f;
                        team.Add(Shape(turret, "GunBarrel", _square, new Vector2(0, barrel * .5f + .06f), new Vector2(type == 2 ? .09f : .13f, barrel), Color.white, 4));
                        team.Add(Shape(turret, "Turret", type == 1 ? _circle : type == 2 ? _diamond : _square, Vector2.zero, new Vector2(.38f, .38f), Color.white, 5));
                        Shape(turret, "Hatch", _circle, Vector2.zero, new Vector2(.13f, .13f), Metal, 6);
                        muzzle = Child(turret, "MuzzleAnchor");
                        muzzle.localPosition = new Vector3(0, barrel + .08f, 0);
                    }

                    var healthRoot = Child(root.transform, "HealthBarRoot");
                    healthRoot.localPosition = new Vector3(-.30f, .58f, 0);
                    Shape(healthRoot, "HealthBackground", _square, new Vector2(.3f, 0), new Vector2(.64f, .075f), Dark, 40);
                    health = Child(healthRoot, "HealthFill");
                    Shape(health, "HealthSprite", _square, new Vector2(.3f, 0), new Vector2(.60f, .043f), new Color(.45f, .95f, .63f, 1), 41);
                }
                else if (type < 7)
                {
                    team.Add(Shape(visual, "Trail", _square, new Vector2(0, -.22f), new Vector2(.065f, .40f), new Color(1, 1, 1, .40f), 0));
                    team.Add(Shape(visual, "ShellBody", type == 5 ? _diamond : _circle, Vector2.zero, new Vector2(type == 6 ? .22f : .12f, type == 6 ? .22f : .22f), Color.white, 2));
                    Shape(visual, "HotCore", _circle, Vector2.zero, new Vector2(.065f, .065f), new Color(1, .95f, .6f, 1), 3);
                    muzzle = Child(visual, "MuzzleAnchor");
                }
                else
                {
                    team.Add(Shape(visual, "BurningGround", _circle, Vector2.zero, Vector2.one, new Color(1, 1, 1, .18f), 0));
                    team.Add(Shape(visual, "BoundaryRing", _ring, Vector2.zero, Vector2.one, new Color(1, 1, 1, .60f), 1));
                    for (int n = 0; n < 3; n++)
                        Shape(visual, "Scorch_" + n, _circle, new Vector2(-.22f + n * .22f, .08f * (n % 2 == 0 ? 1 : -1)), new Vector2(.20f, .20f), new Color(.2f, .12f, .07f, .22f), 2);
                }

                var hit = Child(root.transform, "HitAnchor");
                Set(view, "_visualRoot", visual);
                Set(view, "_turretRoot", turret);
                Set(view, "_muzzleAnchor", muzzle);
                Set(view, "_hitAnchor", hit);
                Set(view, "_healthFill", health);
                SetRefs(view, "_teamRenderers", team.ToArray());
                if (type < 4) DefenseIndicatorAuthoring.Ensure(root);
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, (type < 4 ? PrefabRoot + "/Units/" : type < 7 ? PrefabRoot + "/Projectiles/" : PrefabRoot + "/Zones/") + root.name + ".prefab");
                return prefab.GetComponent<BattleEntityView>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static BattleEffectView EffectPrefab(string name, bool death)
        {
            var root = new GameObject("BattleFx_" + name);
            root.AddComponent<SortingGroup>().sortingOrder = 150;
            try
            {
                var effect = root.AddComponent<BattleEffectView>();
                var visual = Child(root.transform, "VisualRoot");
                var renderers = new List<SpriteRenderer>
                {
                    Shape(visual, "ShockRing", _ring, Vector2.zero, Vector2.one, Color.white, 1),
                    Shape(visual, "Flash", death ? _circle : _diamond, Vector2.zero, new Vector2(.45f, .45f), Color.white, 2)
                };
                if (death)
                    for (int n = 0; n < 3; n++)
                        renderers.Add(Shape(visual, "Debris_" + n, _diamond, new Vector2(-.35f + .35f * n, .15f * (n % 2 == 0 ? 1 : -1)), new Vector2(.15f, .15f), Color.white, 3));
                Set(effect, "_visualRoot", visual);
                SetRefs(effect, "_renderers", renderers.ToArray());
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabRoot + "/Effects/" + root.name + ".prefab");
                return prefab.GetComponent<BattleEffectView>();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        static void CreateScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            // Opening a new scene can unload unreferenced assets; resolve saved assets after that boundary.
            _square = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + "/Primitive_Square.png");
            _circle = AssetDatabase.LoadAssetAtPath<Sprite>(ArtRoot + "/Primitive_Circle.png");
            var catalog = AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>(CatalogPath);
            var views = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>(ViewsPath);
            var presentation = AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>(PresentationPath);
            var rules = catalog.CreateRules();
            var worldRoot = new GameObject("BattleWorldRoot");
            var world = worldRoot.AddComponent<BattleWorldView>();
            foreach (var pair in new[]
            {
                new[]
                {
                    "_unitsRoot",
                    "Units_Container"
                },
                new[]
                {
                    "_projectilesRoot",
                    "Projectiles_Container"
                },
                new[]
                {
                    "_zonesRoot",
                    "Zones_Container"
                },
                new[]
                {
                    "_effectsRoot",
                    "Effects_Container"
                }
            }

            )
                Set(world, pair[0], Child(worldRoot.transform, pair[1]));
            var arena = Child(worldRoot.transform, "Arena_Environment");
            Shape(arena, "Border", _square, Vector2.zero, new Vector2(rules.HalfWidth * 2 + .16f, rules.HalfHeight * 2 + .16f), new Color(.34f, .4f, .37f, 1), -31);
            Shape(arena, "Ground", _square, Vector2.zero, new Vector2(rules.HalfWidth * 2, rules.HalfHeight * 2), new Color(.14f, .19f, .18f, 1), -30);
            for (int x = -4; x <= 4; x++)
                Shape(arena, "GridX_" + x, _square, new Vector2(x, 0), new Vector2(.018f, rules.HalfHeight * 2), new Color(.21f, .27f, .24f, 1), -29);
            for (int y = -7; y <= 7; y++)
                Shape(arena, "GridY_" + y, _square, new Vector2(0, y), new Vector2(rules.HalfWidth * 2, .018f), new Color(.21f, .27f, .24f, 1), -29);
            Shape(arena, "CenterLine", _square, Vector2.zero, new Vector2(rules.HalfWidth * 2, .055f), new Color(.48f, .51f, .41f, 1), -28);
            var clearCamera = new GameObject("BattleBackgroundCamera", typeof(Camera)).GetComponent<Camera>();
            clearCamera.cullingMask = 0;
            clearCamera.depth = -100;
            clearCamera.clearFlags = CameraClearFlags.SolidColor;
            clearCamera.backgroundColor = Dark;
            var cameraObject = new GameObject("BattleCamera", typeof(Camera));
            var camera = cameraObject.GetComponent<Camera>();
            cameraObject.tag = "MainCamera";
            camera.transform.position = new Vector3(0, 0, -20);
            camera.orthographic = true;
            camera.orthographicSize = 8.4f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Dark;
            camera.nearClipPlane = .1f;
            camera.farClipPlane = 100;
            camera.transparencySortMode = TransparencySortMode.CustomAxis;
            camera.transparencySortAxis = new Vector3(0, 1, 0);
            var canvasRoot = new GameObject("UIRoot", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            var canvas = canvasRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(576, 1280);
            scaler.matchWidthOrHeight = 0;
            var safe = Rect(canvasRoot.transform, "SafeArea");
            Stretch(safe);
            safe.gameObject.AddComponent<UiSafeArea>();
            var screenRect = Rect(safe, "UIBattlePrototypeScreen");
            Stretch(screenRect);
            var screen = screenRect.gameObject.AddComponent<UIBattlePrototypeScreen>();
            Set(screen, "_id", "battle.prototype.screen");
            Set(screen, "_hideOnAwake", false);
            var viewport = Rect(screenRect, "BattleViewport_Container");
            Stretch(viewport);
            viewport.offsetMin = new Vector2(0, 244);
            viewport.offsetMax = new Vector2(0, -180);
            var fitter = worldRoot.AddComponent<BattleViewportView>();
            Set(fitter, "_camera", camera);
            Set(fitter, "_viewport", viewport);
            Set(world, "_viewportFitter", fitter);
            var header = Panel(screenRect, "BattleHeader_Panel", true, 176);
            var title = Text(header, "Title_Text", 30, 46);
            var status = Text(header, "Status_Text", 24, 38);
            var armyGroup = Horizontal(header, "ArmyCounters_Layout", 44);
            var side0 = Text(armyGroup, "Side0_Text", 23, 40);
            side0.color = presentation.Side0Color;
            var side1 = Text(armyGroup, "Side1_Text", 23, 40);
            side1.color = presentation.Side1Color;
            var footer = Panel(screenRect, "BattleControls_Panel", false, 240);
            var legend = Text(footer, "Legend_Text", 18, 25);
            var statusGroup = Horizontal(footer, "Telemetry_Layout", 28);
            var stats = Text(statusGroup, "Stats_Text", 17, 28);
            var time = Text(statusGroup, "Time_Text", 17, 28);
            var row1 = Horizontal(footer, "PlaybackControls_Layout", 55);
            var start = Button(row1, "Start_Button");
            var pause = Button(row1, "Pause_Button");
            var step = Button(row1, "Step_Button");
            var reset = Button(row1, "Reset_Button");
            var row2 = Horizontal(footer, "ScenarioControls_Layout", 55);
            var scenario = Button(row2, "Scenario_Button");
            var debug = Button(row2, "DebugZone_Button");
            var scenarioLabel = Text(footer, "ScenarioLabel_Text", 18, 25);
            foreach (var pair in new (string, UnityEngine.Object)[]
            {
                ("_title", title),
                ("_status", status),
                ("_side0", side0),
                ("_side1", side1),
                ("_stats", stats),
                ("_time", time),
                ("_scenarioLabel", scenarioLabel),
                ("_legend", legend),
                ("_start", start),
                ("_pause", pause),
                ("_step", step),
                ("_reset", reset),
                ("_scenario", scenario),
                ("_debugZone", debug)
            }

            )
                Set(screen, pair.Item1, pair.Item2);
            SaveSceneRootAsPrefab(canvasRoot, "Assets/TankDraft/Prefabs/Diagnostics/Battle/UIBattlePrototypeRoot.prefab", scene, out var uiInstance);
            screen = uiInstance.GetComponentInChildren<UIBattlePrototypeScreen>(true);
            Set(fitter, "_viewport", uiInstance.transform.Find("SafeArea/UIBattlePrototypeScreen/BattleViewport_Container"));
            var scope = new GameObject("BattlePrototypeLifetimeScope").AddComponent<BattlePrototypeLifetimeScope>();
            Set(scope, "_scenarioCatalog", catalog);
            Set(scope, "_viewCatalog", views);
            Set(scope, "_presentation", presentation);
            Set(scope, "_worldView", world);
            Set(scope, "_screen", screen);
            var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void SaveSceneRootAsPrefab(GameObject root, string path, UnityEngine.SceneManagement.Scene scene, out GameObject instance)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(path), scene);
        }

        static RectTransform Rect(Transform parent, string name)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            return obj.GetComponent<RectTransform>();
        }

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        static RectTransform Panel(Transform parent, string name, bool top, float height)
        {
            var rect = Rect(parent, name);
            rect.anchorMin = new Vector2(0, top ? 1 : 0);
            rect.anchorMax = new Vector2(1, top ? 1 : 0);
            rect.pivot = new Vector2(.5f, top ? 1 : 0);
            rect.sizeDelta = new Vector2(0, height);
            rect.gameObject.AddComponent<Image>().color = new Color(.035f, .055f, .07f, 1);
            var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 8, 8);
            layout.spacing = 4;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.childAlignment = TextAnchor.MiddleCenter;
            return rect;
        }

        static RectTransform Horizontal(Transform parent, string name, float height)
        {
            var rect = Rect(parent, name);
            var e = rect.gameObject.AddComponent<LayoutElement>();
            e.preferredHeight = height;
            e.minHeight = height;
            var group = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 8;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            group.childAlignment = TextAnchor.MiddleCenter;
            return rect;
        }

        static TMP_Text Text(Transform parent, string name, float fontSize, float height)
        {
            var rect = Rect(parent, name);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.color = new Color(.88f, .91f, .90f, 1);
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            var e = rect.gameObject.AddComponent<LayoutElement>();
            e.preferredHeight = height;
            e.minHeight = height;
            e.preferredWidth = 0;
            e.flexibleWidth = 1;
            return text;
        }

        static void CreateButtonPrefab()
        {
            var view = BuildButton(null, "BattleControl_Button");
            try
            {
                PrefabUtility.SaveAsPrefabAsset(view.gameObject, "Assets/TankDraft/Prefabs/Diagnostics/Battle/BattleControl_Button.prefab");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(view.gameObject);
            }
        }

        static UIButtonView Button(Transform parent, string name)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/Diagnostics/Battle/BattleControl_Button.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            instance.name = name;
            return instance.GetComponent<UIButtonView>();
        }

        static UIButtonView BuildButton(Transform parent, string name)
        {
            var rect = Rect(parent, name);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.16f, .25f, .28f, 1);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var e = rect.gameObject.AddComponent<LayoutElement>();
            e.flexibleWidth = 1;
            e.preferredWidth = 100;
            var view = rect.gameObject.AddComponent<UIButtonView>();
            Set(view, "_hideOnAwake", false);
            var label = Text(rect, "Label_Text", 20, 50);
            Stretch(label.rectTransform);
            var so = new SerializedObject(view);
            var p = so.FindProperty("_button");
            if (p != null)
            {
                p.objectReferenceValue = button;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            return view;
        }

        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Idle compiled Editor required.");
        }
    }
}
