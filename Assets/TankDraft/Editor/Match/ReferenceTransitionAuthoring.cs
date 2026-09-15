using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TankDraft.Match.Presentation;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TankDraft.Match.Editor
{
    public static class ReferenceTransitionAuthoring
    {
        public const string ScreenPrefabPath = "Assets/TankDraft/Prefabs/UI/Match/UIMatchScreen.prefab";

        [MenuItem("TankDraft/Match/Author Reference Round Transition")]
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Reference transition authoring requires a compiled editor in Edit Mode.");

            MatchPresentationTimings timings = AssetDatabase.LoadAssetAtPath<MatchPresentationTimings>(ReferenceDraftMotionAuthoring.TimingsPath);
            if (timings == null)
                throw new InvalidOperationException("Author Reference Draft Motion before authoring the round transition.");
            EnsureTransitionDefaults(timings);
            timings.Validate();

            GameObject root = PrefabUtility.LoadPrefabContents(ScreenPrefabPath);
            try
            {
                Transform hudRoot = root.transform.Find("UIMatchHudWindow");
                UIMatchHudWindow hud = hudRoot == null ? null : hudRoot.GetComponent<UIMatchHudWindow>();
                if (hud == null)
                    throw new InvalidOperationException("Match screen is missing UIMatchHudWindow.");

                CreateOrUpdateHeartRows(hudRoot, out Image[] ownHearts, out RectTransform[] ownVisuals, out Image[] opponentHearts, out RectTransform[] opponentVisuals);
                UIMatchTransitionView transition = CreateOrUpdateTransition(hudRoot, timings);
                Set(hud, "_transitions", transition);
                Set(hud, "_heartsContainer", hudRoot.Find("HeartRows_Container").gameObject);
                SetArray(hud, "_ownHearts", ownHearts);
                SetArray(hud, "_ownHeartVisuals", ownVisuals);
                SetArray(hud, "_opponentHearts", opponentHearts);
                SetArray(hud, "_opponentHeartVisuals", opponentVisuals);
                Set(hud, "_presentationTimings", timings);
                PrefabUtility.SaveAsPrefabAsset(root, ScreenPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            return ReferenceTransitionValidation.ValidateAsset();
        }

        private static void EnsureTransitionDefaults(MatchPresentationTimings timings)
        {
            SerializedObject serialized = new SerializedObject(timings);
            SetDefault(serialized.FindProperty("_roundLabelSeconds"), 1f);
            SetDefault(serialized.FindProperty("_fightLabelSeconds"), .8f);
            SetDefault(serialized.FindProperty("_heartChangeSeconds"), .3f);
            SetDefault(serialized.FindProperty("_transitionFadeSeconds"), .12f);
            SetDefault(serialized.FindProperty("_roundFormat"), "РАУНД {0}");
            SetDefault(serialized.FindProperty("_fight"), "СРАЖАЙСЯ");
            SetDefault(serialized.FindProperty("_waiting"), "Ожидание соперника");
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(timings);
        }

        private static void SetDefault(SerializedProperty property, float value)
        {
            if (property == null)
                throw new MissingFieldException(nameof(MatchPresentationTimings), "transition timing");
            if (property.floatValue <= 0f)
                property.floatValue = value;
        }

        private static void SetDefault(SerializedProperty property, string value)
        {
            if (property == null)
                throw new MissingFieldException(nameof(MatchPresentationTimings), "transition text");
            if (string.IsNullOrEmpty(property.stringValue))
                property.stringValue = value;
        }

        private static void CreateOrUpdateHeartRows(Transform hudRoot, out Image[] ownHearts, out RectTransform[] ownVisuals, out Image[] opponentHearts, out RectTransform[] opponentVisuals)
        {
            RectTransform container = FindOrCreate(hudRoot, "HeartRows_Container");
            container.anchorMin = container.anchorMax = new Vector2(.5f, 1f);
            container.pivot = new Vector2(.5f, 1f);
            container.anchoredPosition = new Vector2(0f, -145f);
            container.sizeDelta = new Vector2(620f, 40f);

            CreateOrUpdateHeartRow(container, "OwnHearts_Row", new Vector2(150f, 0f), out ownHearts, out ownVisuals);
            CreateOrUpdateHeartRow(container, "OpponentHearts_Row", new Vector2(470f, 0f), out opponentHearts, out opponentVisuals);
        }

        private static void CreateOrUpdateHeartRow(RectTransform container, string name, Vector2 position, out Image[] images, out RectTransform[] visuals)
        {
            RectTransform row = FindOrCreate(container, name);
            row.anchorMin = row.anchorMax = new Vector2(0f, 1f);
            row.pivot = new Vector2(.5f, 1f);
            row.anchoredPosition = position;
            row.sizeDelta = new Vector2(160f, 36f);
            HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
            if (layout == null)
                layout = row.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 6f;
            layout.childControlWidth = layout.childControlHeight = true;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;

            for (int index = row.childCount - 1; index >= 0; index--)
                if (row.GetChild(index).name.StartsWith("HeartSlot_", StringComparison.Ordinal))
                    UnityEngine.Object.DestroyImmediate(row.GetChild(index).gameObject);

            images = new Image[4];
            visuals = new RectTransform[4];
            Sprite circle = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            for (int index = 0; index < 4; index++)
            {
                RectTransform slot = New("HeartSlot_" + (index + 1), row);
                LayoutElement cell = slot.gameObject.AddComponent<LayoutElement>();
                cell.minWidth = cell.preferredWidth = 32f;
                cell.minHeight = cell.preferredHeight = 32f;
                cell.flexibleWidth = cell.flexibleHeight = 0f;
                RectTransform visual = New("Visual", slot);
                Stretch(visual);
                Image image = visual.gameObject.AddComponent<Image>();
                image.sprite = circle;
                image.type = Image.Type.Simple;
                image.raycastTarget = false;
                images[index] = image;
                visuals[index] = visual;
            }
        }

        private static UIMatchTransitionView CreateOrUpdateTransition(Transform hudRoot, MatchPresentationTimings timings)
        {
            RectTransform root = FindOrCreate(hudRoot, "RoundTransition_Overlay");
            root.anchorMin = root.anchorMax = new Vector2(.5f, 1f);
            root.pivot = new Vector2(.5f, 1f);
            root.anchoredPosition = new Vector2(0f, -210f);
            root.sizeDelta = new Vector2(560f, 110f);
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            if (group == null)
                group = root.gameObject.AddComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;

            TMP_Text label = root.GetComponentInChildren<TMP_Text>(true);
            if (label == null)
            {
                RectTransform textRoot = New("Label_Text", root);
                Stretch(textRoot);
                label = textRoot.gameObject.AddComponent<TextMeshProUGUI>();
                TMP_Text source = hudRoot.GetComponentsInChildren<TMP_Text>(true).FirstOrDefault();
                if (source == null || source.font == null)
                    throw new InvalidOperationException("Match HUD needs an authored TMP font before transition authoring.");
                label.font = source.font;
                label.fontSize = 42f;
                label.fontStyle = FontStyles.Bold;
                label.alignment = TextAlignmentOptions.Center;
                label.color = Color.white;
                label.raycastTarget = false;
            }

            UIMatchTransitionView transition = root.GetComponent<UIMatchTransitionView>();
            if (transition == null)
                transition = root.gameObject.AddComponent<UIMatchTransitionView>();
            Set(transition, "_canvasGroup", group);
            Set(transition, "_label", label);
            Set(transition, "_timings", timings);
            root.gameObject.SetActive(true);
            return transition;
        }

        private static RectTransform FindOrCreate(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            return existing == null ? New(name, parent) : (RectTransform)existing;
        }

        private static RectTransform New(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name, typeof(RectTransform));
            RectTransform rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static void Set(UnityEngine.Object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
            EditorUtility.SetDirty(target);
        }

        private static void SetArray(UnityEngine.Object target, string fieldName, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(fieldName);
            if (property == null || !property.isArray)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
