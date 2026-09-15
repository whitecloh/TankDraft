using System;
using System.IO;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Vareiko.Foundation.UI;
using TankDraft.UI;

namespace TankDraft.Editor.UI
{
    public static class ProgressionWindowAuthoring
    {
        private const string PrefabPath = "Assets/TankDraft/Prefabs/UI/MainMenuScreen/ProgressionWindow/UIProgressionWindow.prefab";
        private const string RuntimeRootPath = "Assets/TankDraft/Prefabs/UI/UIRoot.prefab";
        private const string DetailsPath = "Assets/TankDraft/Prefabs/UI/MainMenuScreen/CardDetailsWindow/UICardDetailsWindow.prefab";
        private const string CatalogPath = "Assets/TankDraft/Configs/Meta/Catalogs/MetaCatalog.asset";
        private const string RulesPath = "Assets/TankDraft/Configs/Meta/Progression/progression-rules.json";
        private const string ProgressionWindowId = "main_menu.progression";
        private static readonly Color Panel = new Color32(43, 58, 70, 255);
        private static readonly Color Card = new Color32(57, 75, 88, 255);
        private static readonly Color Accent = new Color32(193, 159, 100, 255);
        private static TMP_FontAsset font;
        private static Sprite surface;

        private static RectTransform New(string name, Transform parent, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Top(RectTransform rect, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void At(RectTransform rect, float x, float y, float width, float height)
        {
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1);
            rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static UnityEngine.UI.Image Image(RectTransform rect, Color color, bool raycastTarget = false)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = surface;
            image.type = UnityEngine.UI.Image.Type.Sliced;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        private static TMP_Text Text(string name, Transform parent, float size, TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            RectTransform rect = New(name, parent, 0, 0);
            Stretch(rect);
            var text = rect.gameObject.AddComponent<TextMeshProUGUI>();
            text.font = font;
            text.fontSize = size;
            text.color = Color.white;
            text.alignment = alignment;
            text.raycastTarget = false;
            return text;
        }

        private static UIButtonView Button(string name, Transform parent, float width, float height, out TMP_Text label)
        {
            RectTransform rect = New(name, parent, width, height);
            UnityEngine.UI.Image image = Image(rect, Panel, true);
            var button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = image;
            var view = rect.gameObject.AddComponent<UIButtonView>();
            SetReference(view, "_button", button);
            label = Text("Label_Text", rect, 21);
            return view;
        }

        private static void SetReference(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException(target.name + "." + field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFlag(UIElement target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new InvalidOperationException(target.name + "." + field);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetId(UIElement target, string id)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty("_id");
            if (property == null) throw new InvalidOperationException(target.name + "._id");
            property.stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCardSlot(UIProgressionWindow window, int index, RectTransform root, UnityEngine.UI.Image icon, TMP_Text title, TMP_Text subtitle, TMP_Text primary, TMP_Text secondary)
        {
            SerializedObject serialized = new SerializedObject(window);
            SerializedProperty slot = serialized.FindProperty("_cards").GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("_root").objectReferenceValue = root.gameObject;
            slot.FindPropertyRelative("_icon").objectReferenceValue = icon;
            slot.FindPropertyRelative("_title").objectReferenceValue = title;
            slot.FindPropertyRelative("_subtitle").objectReferenceValue = subtitle;
            slot.FindPropertyRelative("_primaryProgress").objectReferenceValue = primary;
            slot.FindPropertyRelative("_secondaryProgress").objectReferenceValue = secondary;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetActionSlot(UIProgressionWindow window, int index, RectTransform root, UIButtonView button, TMP_Text label)
        {
            SerializedObject serialized = new SerializedObject(window);
            SerializedProperty slot = serialized.FindProperty("_actions").GetArrayElementAtIndex(index);
            slot.FindPropertyRelative("_root").objectReferenceValue = root.gameObject;
            slot.FindPropertyRelative("_button").objectReferenceValue = button;
            slot.FindPropertyRelative("_label").objectReferenceValue = label;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        public static string Apply()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Edit Mode with compilation complete is required.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                EnsureWindowId();
                return "Progression window prefab already exists and has UI Id: " + PrefabPath;
            }

            font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/TankDraft/Art/UI/Fonts/TankDraftUI SDF.asset");
            surface = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            if (font == null || surface == null) throw new InvalidOperationException("Progression window style assets are missing.");
            Scene preview = EditorSceneManager.NewPreviewScene();
            try
            {
                RectTransform root = new GameObject("UIProgressionWindow", typeof(RectTransform)).GetComponent<RectTransform>();
                SceneManager.MoveGameObjectToScene(root.gameObject, preview);
                root.sizeDelta = new Vector2(576, 1280);
                var window = root.gameObject.AddComponent<UIProgressionWindow>();
                SetId(window, ProgressionWindowId);
                SetFlag(window, "_hideOnAwake", true);
                SetFlag(window, "_isModal", true);

                RectTransform blocker = New("Overlay_Blocker", root, 576, 1280);
                Stretch(blocker);
                Image(blocker, new Color(0, 0, 0, .72f), true);
                RectTransform panel = New("Progression_Panel", root, 540, 1000);
                panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(.5f, .5f);
                panel.anchoredPosition = Vector2.zero;
                Image(panel, Panel);

                RectTransform titleRoot = New("Title_Container", panel, 504, 54);
                Top(titleRoot, 34, 504, 54);
                TMP_Text title = Text("Title_Text", titleRoot, 30);
                title.fontStyle = FontStyles.Bold;
                RectTransform bodyRoot = New("Body_Container", panel, 504, 96);
                Top(bodyRoot, 98, 504, 96);
                TMP_Text body = Text("Body_Text", bodyRoot, 23);
                body.enableAutoSizing = true;
                body.fontSizeMin = 18;
                body.fontSizeMax = 23;
                body.maxVisibleLines = 4;
                body.overflowMode = TextOverflowModes.Ellipsis;
                body.enableWordWrapping = true;

                RectTransform cards = New("Cards_Container", panel, 504, 280);
                Top(cards, 210, 504, 280);
                var cardLayout = cards.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                cardLayout.spacing = 8;
                cardLayout.childAlignment = TextAnchor.MiddleCenter;
                cardLayout.childControlWidth = false;
                cardLayout.childControlHeight = false;
                cardLayout.childForceExpandWidth = false;
                cardLayout.childForceExpandHeight = false;

                SerializedObject windowSerialized = new SerializedObject(window);
                windowSerialized.FindProperty("_cards").arraySize = 4;
                windowSerialized.FindProperty("_actions").arraySize = 4;
                windowSerialized.ApplyModifiedPropertiesWithoutUndo();
                for (int index = 0; index < 4; index++)
                {
                    RectTransform card = New("Card_" + (index + 1), cards, 120, 280);
                    Image(card, Card);
                    var cell = card.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    cell.minWidth = cell.preferredWidth = 120;
                    cell.minHeight = cell.preferredHeight = 280;
                    RectTransform iconRoot = New("Icon_Container", card, 76, 76);
                    Top(iconRoot, 14, 76, 76);
                    UnityEngine.UI.Image icon = Image(iconRoot, Accent);
                    RectTransform nameRoot = New("Name_Container", card, 108, 48);
                    Top(nameRoot, 96, 108, 48);
                    TMP_Text name = Text("Name_Text", nameRoot, 15);
                    RectTransform subtitleRoot = New("Subtitle_Container", card, 108, 27);
                    Top(subtitleRoot, 146, 108, 27);
                    TMP_Text subtitle = Text("Subtitle_Text", subtitleRoot, 14);
                    RectTransform primaryRoot = New("PrimaryProgress_Container", card, 108, 38);
                    Top(primaryRoot, 178, 108, 38);
                    TMP_Text primary = Text("PrimaryProgress_Text", primaryRoot, 15);
                    RectTransform secondaryRoot = New("SecondaryProgress_Container", card, 108, 50);
                    Top(secondaryRoot, 216, 108, 50);
                    TMP_Text secondary = Text("SecondaryProgress_Text", secondaryRoot, 14);
                    SetCardSlot(window, index, card, icon, name, subtitle, primary, secondary);
                }

                RectTransform actions = New("Actions_Container", panel, 504, 280);
                Top(actions, 510, 504, 280);
                var actionLayout = actions.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
                actionLayout.spacing = 8;
                actionLayout.childAlignment = TextAnchor.UpperCenter;
                actionLayout.childControlWidth = false;
                actionLayout.childControlHeight = false;
                actionLayout.childForceExpandWidth = false;
                actionLayout.childForceExpandHeight = false;
                for (int index = 0; index < 4; index++)
                {
                    TMP_Text label;
                    UIButtonView action = Button("Action_" + (index + 1) + "_Button", actions, 504, 64, out label);
                    var cell = action.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    cell.minWidth = cell.preferredWidth = 504;
                    cell.minHeight = cell.preferredHeight = 64;
                    SetActionSlot(window, index, action.GetComponent<RectTransform>(), action, label);
                }

                TMP_Text closeLabel;
                UIButtonView close = Button("Close_Button", panel, 280, 64, out closeLabel);
                closeLabel.text = "ЗАКРЫТЬ";
                Top(close.GetComponent<RectTransform>(), 906, 280, 64);
                SetReference(window, "_title", title);
                SetReference(window, "_body", body);
                SetReference(window, "_closeButton", close);

                Directory.CreateDirectory(Path.GetDirectoryName(PrefabPath));
                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
                if (prefab == null) throw new IOException("Could not save " + PrefabPath);
                return "Created " + PrefabPath;
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(preview);
            }
        }

        // Applies only the new window and bindings to existing authored prefabs. It
        // never rebuilds the main menu or changes its existing layout groups.
        public static string ApplyAndBind()
        {
            if (EditorApplication.isPlaying || EditorApplication.isCompiling) throw new InvalidOperationException("Edit Mode with compilation complete is required.");
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) == null) Apply();
            UpdateCardTextLayout();
            GameObject progressionPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject rootPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RuntimeRootPath);
            TextAsset rules = AssetDatabase.LoadAssetAtPath<TextAsset>(RulesPath);
            var catalog = AssetDatabase.LoadAssetAtPath<TankDraft.Content.MetaCatalogAsset>(CatalogPath);
            if (!progressionPrefab || !rootPrefab || !rules || !catalog) throw new InvalidOperationException("Progression prefab, UI root, catalog and authored rules are required.");

            GameObject root = PrefabUtility.LoadPrefabContents(RuntimeRootPath);
            try
            {
                UIMainMenuRuntimeRoot refs = root.GetComponent<UIMainMenuRuntimeRoot>();
                if (!refs) throw new InvalidOperationException("UIRoot is missing UIMainMenuRuntimeRoot.");
                UIProgressionWindow window = root.GetComponentInChildren<UIProgressionWindow>(true);
                if (!window)
                {
                    Transform parent = root.transform.Find("SafeArea_Container") ?? root.transform;
                    GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(progressionPrefab, parent);
                    instance.name = "UIProgressionWindow";
                    Stretch(instance.GetComponent<RectTransform>());
                    window = instance.GetComponent<UIProgressionWindow>();
                }
                SetReference(refs, "_progression", window);
                PrefabUtility.SaveAsPrefabAsset(root, RuntimeRootPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }

            GameObject details = PrefabUtility.LoadPrefabContents(DetailsPath);
            try
            {
                UICardDetailsWindow view = details.GetComponent<UICardDetailsWindow>();
                if (!view) throw new InvalidOperationException("Card details view is missing.");
                var serialized = new SerializedObject(view);
                UIButtonView progression = serialized.FindProperty("_progressionButton").objectReferenceValue as UIButtonView;
                if (!progression)
                {
                    UIButtonView equip = serialized.FindProperty("_actionButton").objectReferenceValue as UIButtonView;
                    if (!equip) throw new InvalidOperationException("Card details equip button is missing.");
                    Transform parent = equip.transform.parent;
                    RectTransform group = New("Actions_Container", parent, 516, 72);
                    At(group, 0, 444, 516, 72);
                    var layout = group.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
                    layout.spacing = 16;
                    layout.childAlignment = TextAnchor.MiddleCenter;
                    layout.childControlWidth = true;
                    layout.childControlHeight = true;
                    layout.childForceExpandWidth = false;
                    layout.childForceExpandHeight = false;
                    equip.transform.SetParent(group, false);
                    var equipLayout = equip.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    equipLayout.minWidth = equipLayout.preferredWidth = 250;
                    equipLayout.minHeight = equipLayout.preferredHeight = 72;
                    progression = UnityEngine.Object.Instantiate(equip, group);
                    progression.name = "Progression_Button";
                    var progressionLayout = progression.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    progressionLayout.minWidth = progressionLayout.preferredWidth = 250;
                    progressionLayout.minHeight = progressionLayout.preferredHeight = 72;
                    TMP_Text label = progression.GetComponentInChildren<TMP_Text>(true);
                    if (!label) throw new InvalidOperationException("Progression button label is missing.");
                    label.text = "ПРОКАЧКА";
                    serialized.FindProperty("_progressionButton").objectReferenceValue = progression;
                    serialized.FindProperty("_progressionLabel").objectReferenceValue = label;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                PrefabUtility.SaveAsPrefabAsset(details, DetailsPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(details); }

            SetReference(catalog, "_progressionRules", rules);
            EditorUtility.SetDirty(catalog);
            AssetDatabase.SaveAssets();
            return "Progression window, details binding and authored rules reference attached.";
        }

        private static void UpdateCardTextLayout()
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                UIProgressionWindow window = prefab.GetComponent<UIProgressionWindow>();
                if (window == null) throw new InvalidOperationException("Progression window component is missing.");
                SetId(window, ProgressionWindowId);
                RectTransform cards = prefab.transform.Find("Progression_Panel/Cards_Container") as RectTransform;
                if (cards == null) throw new InvalidOperationException("Progression cards container is missing.");
                Top(cards, 210, 504, 280);
                RectTransform actions = prefab.transform.Find("Progression_Panel/Actions_Container") as RectTransform;
                if (actions == null) throw new InvalidOperationException("Progression actions container is missing.");
                Top(actions, 510, 504, 280);

                for (int index = 0; index < 4; index++)
                {
                    Transform cardTransform = cards.Find("Card_" + (index + 1));
                    if (cardTransform == null) throw new InvalidOperationException("Progression card is missing: " + (index + 1));
                    RectTransform card = cardTransform as RectTransform;
                    card.sizeDelta = new Vector2(120, 280);
                    UnityEngine.UI.LayoutElement layout = card.GetComponent<UnityEngine.UI.LayoutElement>();
                    if (layout == null) throw new InvalidOperationException("Progression card layout is missing: " + (index + 1));
                    layout.minHeight = layout.preferredHeight = 280;
                    ResizeCardText(card, "Name_Container", 96, 48);
                    ResizeCardText(card, "Subtitle_Container", 146, 27);
                    ResizeCardText(card, "PrimaryProgress_Container", 178, 38);
                    ResizeCardText(card, "SecondaryProgress_Container", 216, 50);
                }

                foreach (TMP_Text text in prefab.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "Name_Text")
                    {
                        text.enableAutoSizing = true;
                        text.fontSizeMin = 10;
                        text.fontSizeMax = 15;
                        text.maxVisibleLines = 2;
                        text.enableWordWrapping = true;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                    }
                    else if (text.name == "Subtitle_Text" || text.name == "PrimaryProgress_Text" || text.name == "SecondaryProgress_Text")
                    {
                        text.enableAutoSizing = true;
                        text.fontSizeMin = 9;
                        text.fontSizeMax = text.name == "SecondaryProgress_Text" ? 14 : 15;
                        text.maxVisibleLines = 2;
                        text.enableWordWrapping = true;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                    }
                }
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static void EnsureWindowId()
        {
            GameObject prefab = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                UIProgressionWindow window = prefab.GetComponent<UIProgressionWindow>();
                if (window == null) throw new InvalidOperationException("Progression window component is missing.");
                SetId(window, ProgressionWindowId);
                PrefabUtility.SaveAsPrefabAsset(prefab, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(prefab); }
        }

        private static void ResizeCardText(RectTransform card, string name, float y, float height)
        {
            RectTransform container = card.Find(name) as RectTransform;
            if (container == null) throw new InvalidOperationException("Progression card text container is missing: " + name);
            Top(container, y, 108, height);
        }
    }
}
