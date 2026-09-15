using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using TankDraft.BattlePresentation;
using TankDraft.Content;
using TankDraft.UI;
using TankDraft.Infrastructure;
using TankDraft.Match.Content;
using TankDraft.Match.Presentation;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Editor
{
    public static class MatchAuthoring
    {
        public const string ScenePath = "Assets/TankDraft/Scenes/Gameplay/Match.unity";
        public const string SettingsPath = "Assets/TankDraft/Configs/Match/MatchSettings.asset";
        private const string UiPath = "Assets/TankDraft/Prefabs/UI/Match/";
        private static TMP_FontAsset _font;
        private static Sprite _surface;
        [MenuItem("TankDraft/Match/Author")]
        public static void Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Match authoring requires Edit Mode.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Save open scenes before authoring.");
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                    throw new IOException("Refusing to overwrite authored Match scene.");
                Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
                if (!AssetDatabase.CopyAsset("Assets/TankDraft/Scenes/Diagnostics/BattlePrototype.unity", ScenePath))
                    throw new IOException("Could not clone BattlePrototype scene.");
                _font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TankDraft/Art/UI/Fonts/TankDraftUI SDF.asset");
                _surface = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                if (_font == null || _surface == null)
                    throw new InvalidOperationException("Match authoring requires the migrated TankDraft UI font and surface sprite.");
                MatchSettingsAsset settings = CreateSettings();
                GameObject card = CreateCardPrefab();
                GameObject action = CreateActionButtonPrefab();
                GameObject screenPrefab = CreateScreenPrefab(card, action);
                AuthorScene(settings, screenPrefab);
                AddBuildScene("Assets/TankDraft/Scenes/Frontend/MainMenu.unity");
                AddBuildScene(ScenePath);
                WireMainMenu(settings);
                AssetDatabase.SaveAssets();
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        public static void WireMainMenu(MatchSettingsAsset settings)
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                foreach (string path in new[]
                {
                    "Assets/TankDraft/Scenes/Frontend/MainMenu.unity",
                    "Assets/TankDraft/Scenes/Diagnostics/QA/MainMenuRuntimeQA.unity"
                }

                )
                {
                    if (!File.Exists(path))
                        continue;
                    var scene = EditorSceneManager.OpenScene(path);
                    var scope = UnityEngine.Object.FindFirstObjectByType<TankDraft.Bootstrap.MainMenuLifetimeScope>();
                    if (!scope)
                        throw new InvalidOperationException("Main menu scope is missing.");
                    scope.GetType().GetField("_matchSettings", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(scope, settings);
                    EditorUtility.SetDirty(scope);
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
            finally
            {
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        [MenuItem("TankDraft/Match/Open")]
        public static void Open()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode first.");
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath);
        }

        [MenuItem("TankDraft/Match/Upgrade UI Structure")]
        public static void UpgradeUiStructure()
        {
            const string path = UiPath + "UIMatchScreen.prefab";
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                ConfigureScreenStructure(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static MatchSettingsAsset CreateSettings()
        {
            MatchTextAsset text = Asset<MatchTextAsset>("Assets/TankDraft/Configs/Match/MatchRussianText.asset");
            MatchSettingsAsset settings = Asset<MatchSettingsAsset>(SettingsPath);
            Set(settings, "_battleCatalog", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TankDraft/Configs/Battle/Scenarios/BattleScenarioCatalog.asset"));
            Set(settings, "_views", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset"));
            Set(settings, "_battlePresentation", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TankDraft/Configs/Battle/Presentation/BattlePresentation.asset"));
            Set(settings, "_metaCatalog", AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TankDraft/Configs/Meta/Catalogs/MetaCatalog.asset"));
            Set(settings, "_profileSettings", AssetDatabase.LoadAssetAtPath<LocalProfileSettings>("Assets/TankDraft/Configs/Meta/Profile/LocalProfileSettings.asset"));
            Set(settings, "_text", text);
            ConfigureDraftUnits(settings);
            return settings;
        }

        public static void ConfigureDraftUnits(MatchSettingsAsset settings)
        {
            var data = new SerializedObject(settings);
            var entries = data.FindProperty("_units");
            entries.arraySize = 4;
            string[] names =
            {
                "Mines",
                "HeavyTank",
                "TankDestroyer",
                "Artillery"
            };
            int[] counts =
            {
                2,
                1,
                3,
                1
            };
            for (int i = 0; i < names.Length; i++)
            {
                var entry = entries.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("unit").objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TankDraft/Configs/Battle/Units/Unit_" + names[i] + ".asset");
                entry.FindPropertyRelative("addCount").intValue = counts[i];
            }

            data.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
        }

        private static GameObject CreateCardPrefab()
        {
            RectTransform root = New("MatchCardView", null, 220, 320);
            Cell(root, 220, 320);
            Image(root, new Color(.09f, .14f, .20f, .98f), true);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            MatchCardView view = root.gameObject.AddComponent<MatchCardView>();
            Set(view, "_button", button);
            Set(view, "_hideOnAwake", false);
            RectTransform icon = New("Icon_Image", root, 144, 112);
            Top(icon, 24, 144, 112);
            Image iconImage = Image(icon, Color.white, false);
            TMP_Text title = Text("Title_Text", root, 23);
            At(title.rectTransform, 10, 148, 200, 52);
            TMP_Text description = Text("Description_Text", root, 16);
            At(description.rectTransform, 12, 204, 196, 48);
            RectTransform action = New("Action_Background", root, 176, 46);
            Bottom(action, 18, 176, 46);
            Image(action, new Color(.19f, .48f, .76f), false);
            TMP_Text actionLabel = Text("ActionLabel_Text", action, 19);
            Stretch(actionLabel.rectTransform);
            Set(view, "_titleText", title);
            Set(view, "_descriptionText", description);
            Set(view, "_actionLabel", actionLabel);
            Set(view, "_iconImage", iconImage);
            return Save(root, UiPath + "Common/MatchCardView.prefab");
        }

        private static GameObject CreateActionButtonPrefab()
        {
            RectTransform root = New("MatchActionButton", null, 242, 68);
            Cell(root, 242, 68);
            Image(root, new Color(.18f, .48f, .76f), true);
            Button button = root.gameObject.AddComponent<Button>();
            button.targetGraphic = root.GetComponent<Image>();
            UIButtonView view = root.gameObject.AddComponent<UIButtonView>();
            Set(view, "_button", button);
            Set(view, "_hideOnAwake", false);
            TMP_Text label = Text("Label_Text", root, 23);
            Stretch(label.rectTransform);
            return Save(root, UiPath + "Common/MatchActionButton.prefab");
        }

        private static GameObject CreateScreenPrefab(GameObject card, GameObject action)
        {
            RectTransform root = New("UIMatchScreen", null, 720, 1600);
            Stretch(root);
            UIMatchScreen screen = root.gameObject.AddComponent<UIMatchScreen>();
            Set(screen, "_hideOnAwake", false);
            TMP_Text title = Text("Title_Text", root, 31);
            Top(title.rectTransform, 34, 660, 54);
            TMP_Text score = Text("Score_Text", root, 25);
            Top(score.rectTransform, 91, 660, 42);
            TMP_Text status = Text("Status_Text", root, 20);
            Top(status.rectTransform, 138, 660, 36);
            TMP_Text enemy = Text("EnemyArmy_Text", root, 19);
            Top(enemy.rectTransform, 198, 660, 38);
            RectTransform viewport = New("BattleViewport_Container", root, 720, 600);
            viewport.anchorMin = new Vector2(.03f, 0);
            viewport.anchorMax = new Vector2(.97f, 1);
            viewport.offsetMin = new Vector2(0, 300);
            viewport.offsetMax = new Vector2(0, -245);
            Image(viewport, Color.clear, false);
            TMP_Text player = Text("PlayerArmy_Text", root, 19);
            Bottom(player.rectTransform, 245, 660, 38);
            TMP_Text hint = Text("Hint_Text", root, 18);
            Bottom(hint.rectTransform, 193, 660, 38);
            RectTransform cards = New("DraftCards_Container", root, 696, 320);
            Anchor(cards, .5f, .48f, 696, 320);
            HorizontalLayoutGroup group = cards.gameObject.AddComponent<HorizontalLayoutGroup>();
            group.spacing = 18;
            group.padding = new RectOffset(0, 0, 0, 0);
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = false;
            MatchCardView[] cardViews = new MatchCardView[3];
            for (int index = 0; index < cardViews.Length; index++)
            {
                RectTransform item = Instance(card, cards);
                item.name = "DraftCard_" + (index + 1);
                cardViews[index] = item.GetComponent<MatchCardView>();
            }

            RectTransform orderContainer = New("Order_Container", root, 242, 68);
            Bottom(orderContainer, 126, 300, 68);
            UIButtonView order = CreateButtonInstance(action, orderContainer, "Order_Button", out TMP_Text orderLabel);
            RectTransform nextContainer = New("Result_Container", root, 242, 68);
            Bottom(nextContainer, 126, 242, 68);
            UIButtonView next = CreateButtonInstance(action, nextContainer, "Next_Button", out TMP_Text nextLabel);
            RectTransform menuContainer = New("Menu_Container", root, 242, 58);
            Bottom(menuContainer, 48, 242, 58);
            UIButtonView menu = CreateButtonInstance(action, menuContainer, "Menu_Button", out TMP_Text menuLabel);
            ConfigureScreenStructure(root.gameObject);
            return Save(root, UiPath + "UIMatchScreen.prefab");
        }

        private static void AuthorScene(MatchSettingsAsset settings, GameObject screenPrefab)
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            foreach (GameObject root in scene.GetRootGameObjects().Where(x => x.name.Contains("UI") || x.GetComponent<UIBattlePrototypeScreen>() != null || x.name.Contains("BattlePrototypeLifetimeScope")).ToArray())
                UnityEngine.Object.DestroyImmediate(root);
            GameObject uiRoot = new GameObject("UIRoot", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            Canvas canvas = uiRoot.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = uiRoot.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(720, 1600);
            scaler.matchWidthOrHeight = 0f;
            if (scene.GetRootGameObjects().All(x => x.GetComponent<UnityEngine.EventSystems.EventSystem>() == null))
                throw new InvalidOperationException("Cloned battle scene must retain its EventSystem.");
            RectTransform safe = New("SafeArea", uiRoot.transform, 720, 1600);
            Stretch(safe);
            UiSafeArea safeArea = safe.gameObject.AddComponent<UiSafeArea>();
            Set(safeArea, "_target", safe);
            RectTransform screenRoot = ((GameObject)PrefabUtility.InstantiatePrefab(screenPrefab, safe)).GetComponent<RectTransform>();
            Stretch(screenRoot);
            UIMatchScreen screen = screenRoot.GetComponent<UIMatchScreen>();
            BattleWorldView world = UnityEngine.Object.FindFirstObjectByType<BattleWorldView>();
            if (world == null)
                throw new InvalidOperationException("Cloned battle scene requires BattleWorldView.");
            BattleViewportView viewport = world.GetComponent<BattleViewportView>();
            if (viewport == null)
                viewport = world.gameObject.AddComponent<BattleViewportView>();
            Camera camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsSortMode.None).Single(x => x.name == "BattleCamera");
            if (camera == null)
                throw new InvalidOperationException("Cloned battle scene requires a battle camera.");
            Set(viewport, "_camera", camera);
            Set(viewport, "_viewport", screenRoot.Find("UIMatchHudWindow/BattleViewport_Container") as RectTransform);
            Set(world, "_viewportFitter", viewport);
            Type scopeType = Type.GetType("TankDraft.Match.Bootstrap.MatchLifetimeScope, TankDraft.Match.Bootstrap");
            if (scopeType == null)
                throw new InvalidOperationException("TankDraft.Match.Bootstrap.MatchLifetimeScope must be compiled before authoring.");
            GameObject lifetime = new GameObject("MatchLifetimeScope");
            Component scope = lifetime.AddComponent(scopeType);
            Set(scope, "_settings", settings);
            Set(scope, "_world", world);
            Set(scope, "_screen", screen);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        private static UIButtonView CreateButtonInstance(GameObject prefab, Transform parent, string name, out TMP_Text label)
        {
            RectTransform root = Instance(prefab, parent);
            root.name = name;
            Stretch(root);
            label = root.GetComponentInChildren<TMP_Text>(true);
            return root.GetComponent<UIButtonView>();
        }

        private static void ConfigureScreenStructure(GameObject root)
        {
            var screen = root.GetComponent<UIMatchScreen>();
            if (screen == null)
                throw new InvalidOperationException("Match screen prefab is missing UIMatchScreen.");
            RectTransform hudRoot = Window(root.transform, "UIMatchHudWindow");
            RectTransform draftRoot = Window(root.transform, "UIMatchDraftWindow");
            Transform cards = FindChild(root.transform, "DraftCards_Container");
            Transform order = FindChild(root.transform, "Order_Container");
            if (cards == null || order == null)
                throw new InvalidOperationException("Match screen requires draft cards and order containers.");
            cards.SetParent(draftRoot, false);
            order.SetParent(draftRoot, false);
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (child != hudRoot && child != draftRoot)
                    child.SetParent(hudRoot, false);
            UIPanel cardsPanel = cards.GetComponent<UIPanel>();
            if (cardsPanel == null)
                cardsPanel = cards.gameObject.AddComponent<UIPanel>();
            Set(cardsPanel, "_hideOnAwake", false);
            UIMatchHudWindow hud = hudRoot.GetComponent<UIMatchHudWindow>();
            if (hud == null)
                hud = hudRoot.gameObject.AddComponent<UIMatchHudWindow>();
            UIMatchDraftWindow draft = draftRoot.GetComponent<UIMatchDraftWindow>();
            if (draft == null)
                draft = draftRoot.gameObject.AddComponent<UIMatchDraftWindow>();
            Set(hud, "_hideOnAwake", false);
            Set(draft, "_hideOnAwake", false);
            Set(hud, "_titleText", TextChild(hudRoot, "Title_Text"));
            Set(hud, "_scoreText", TextChild(hudRoot, "Score_Text"));
            Set(hud, "_statusText", TextChild(hudRoot, "Status_Text"));
            Set(hud, "_army0Text", TextChild(hudRoot, "PlayerArmy_Text"));
            Set(hud, "_army1Text", TextChild(hudRoot, "EnemyArmy_Text"));
            Set(hud, "_hintText", TextChild(hudRoot, "Hint_Text"));
            Set(hud, "_nextContainer", FindChild(hudRoot, "Result_Container").gameObject);
            Set(hud, "_nextButton", FindChild(hudRoot, "Next_Button").GetComponentInChildren<UIButtonView>(true));
            Set(hud, "_nextLabelText", TextChild(hudRoot, "Next_Button"));
            Set(hud, "_menuButton", FindChild(hudRoot, "Menu_Button").GetComponentInChildren<UIButtonView>(true));
            Set(hud, "_menuLabelText", TextChild(hudRoot, "Menu_Button"));
            SetArray(draft, "_cards", cards.GetComponentsInChildren<MatchCardView>(true));
            Set(draft, "_orderContainer", order.gameObject);
            Set(draft, "_orderButton", order.GetComponentInChildren<UIButtonView>(true));
            Set(draft, "_orderLabelText", TextChild(order, "Order_Button"));
            Set(draft, "_cardsLayout", cards.GetComponent<HorizontalLayoutGroup>());
            Set(screen, "_hud", hud);
            Set(screen, "_draft", draft);
        }

        private static RectTransform Window(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                Stretch((RectTransform)existing);
                return (RectTransform)existing;
            }

            RectTransform window = New(name, parent, 720, 1600);
            Stretch(window);
            return window;
        }

        private static Transform FindChild(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == name);
        private static TMP_Text TextChild(Transform root, string name)
        {
            Transform child = FindChild(root, name);
            if (child == null)
                throw new InvalidOperationException("Missing match text: " + name);
            TMP_Text text = child.GetComponent<TMP_Text>();
            if (text == null)
                text = child.GetComponentInChildren<TMP_Text>(true);
            if (text == null)
                throw new InvalidOperationException("Missing TMP text: " + name);
            return text;
        }

        private static RectTransform New(string name, Transform parent, float width, float height)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            RectTransform rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static Image Image(RectTransform rect, Color color, bool raycast)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.sprite = _surface;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycast;
            return image;
        }

        private static TMP_Text Text(string name, Transform parent, float size)
        {
            RectTransform rect = New(name, parent, 0, 0);
            TMP_Text text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = _font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;
            text.raycastTarget = false;
            return text;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }

        private static void Top(RectTransform rect, float top, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Bottom(RectTransform rect, float bottom, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 0);
            rect.pivot = new Vector2(.5f, 0);
            rect.anchoredPosition = new Vector2(0, bottom);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Anchor(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(x, y);
            rect.pivot = new Vector2(.5f, .5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void At(RectTransform rect, float left, float top, float width, float height)
        {
            rect.anchorMin = new Vector2(0, 1);
            rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(left, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void Cell(RectTransform rect, float width, float height)
        {
            LayoutElement cell = rect.gameObject.AddComponent<LayoutElement>();
            cell.preferredWidth = cell.minWidth = width;
            cell.preferredHeight = cell.minHeight = height;
            cell.flexibleWidth = cell.flexibleHeight = 0;
        }

        private static GameObject Save(RectTransform root, string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) != null)
                throw new IOException("Refusing to overwrite " + path);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, path);
            UnityEngine.Object.DestroyImmediate(root.gameObject);
            return prefab;
        }

        private static T Asset<T>(string path)
            where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
                return asset;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static RectTransform Instance(GameObject prefab, Transform parent) => ((GameObject)PrefabUtility.InstantiatePrefab(prefab, parent)).GetComponent<RectTransform>();
        private static void Set(UnityEngine.Object target, string field, object value)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null)
                throw new MissingFieldException(target.GetType().Name, field);
            if (value is bool flag)
                property.boolValue = flag;
            else
                property.objectReferenceValue = value as UnityEngine.Object;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetArray(UnityEngine.Object target, string field, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AddBuildScene(string path)
        {
            List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(x => x.path != path))
            {
                scenes.Add(new EditorBuildSettingsScene(path, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }
        }
    }
}
