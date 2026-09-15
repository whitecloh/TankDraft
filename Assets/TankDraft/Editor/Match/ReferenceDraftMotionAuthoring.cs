using System;
using System.IO;
using System.Reflection;
using TankDraft.Match.Presentation;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace TankDraft.Match.Editor
{
    public static class ReferenceDraftMotionAuthoring
    {
        public const string CardPrefabPath = "Assets/TankDraft/Prefabs/UI/Match/Common/MatchCardView.prefab";
        public const string TimingsPath = "Assets/TankDraft/Configs/Match/Presentation/ReferencePresentationTimings.asset";

        [MenuItem("TankDraft/Match/Author Reference Draft Motion")]
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
                throw new InvalidOperationException("Reference draft motion authoring requires a compiled editor in Edit Mode.");

            MatchPresentationTimings timings = GetOrCreateTimings();
            GameObject root = PrefabUtility.LoadPrefabContents(CardPrefabPath);
            try
            {
                MatchCardView card = root.GetComponent<MatchCardView>();
                if (card == null)
                    throw new InvalidOperationException("Match card prefab is missing MatchCardView.");

                RectTransform visualRoot = EnsureVisualRoot(root.transform);
                CanvasGroup canvasGroup = visualRoot.GetComponent<CanvasGroup>();
                if (canvasGroup == null)
                    canvasGroup = visualRoot.gameObject.AddComponent<CanvasGroup>();
                canvasGroup.alpha = 1f;

                MatchCardMotion motion = root.GetComponent<MatchCardMotion>();
                if (motion == null)
                    motion = root.AddComponent<MatchCardMotion>();
                Set(motion, "_visualRoot", visualRoot);
                Set(motion, "_visualCanvasGroup", canvasGroup);
                Set(motion, "_timings", timings);
                Set(card, "_motion", motion);
                PrefabUtility.SaveAsPrefabAsset(root, CardPrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            return ReferenceDraftMotionValidation.ValidateAsset();
        }

        private static MatchPresentationTimings GetOrCreateTimings()
        {
            MatchPresentationTimings timings = AssetDatabase.LoadAssetAtPath<MatchPresentationTimings>(TimingsPath);
            if (timings != null)
                return timings;

            EnsureFolder(Path.GetDirectoryName(TimingsPath)?.Replace('\\', '/'));
            timings = ScriptableObject.CreateInstance<MatchPresentationTimings>();
            AssetDatabase.CreateAsset(timings, TimingsPath);
            return timings;
        }

        private static RectTransform EnsureVisualRoot(Transform root)
        {
            Transform existing = root.Find("VisualRoot");
            if (existing != null)
                return (RectTransform)existing;

            var visual = new GameObject("VisualRoot", typeof(RectTransform)).GetComponent<RectTransform>();
            visual.SetParent(root, false);
            visual.anchorMin = Vector2.zero;
            visual.anchorMax = Vector2.one;
            visual.offsetMin = Vector2.zero;
            visual.offsetMax = Vector2.zero;
            visual.pivot = new Vector2(.5f, .5f);

            var children = new Transform[root.childCount - 1];
            int count = 0;
            for (int index = 0; index < root.childCount; index++)
            {
                Transform child = root.GetChild(index);
                if (child != visual)
                    children[count++] = child;
            }
            for (int index = 0; index < count; index++)
                children[index].SetParent(visual, false);

            Image rootBackground = root.GetComponent<Image>();
            if (rootBackground != null)
            {
                Image visualBackground = visual.gameObject.AddComponent<Image>();
                EditorUtility.CopySerialized(rootBackground, visualBackground);
                Button button = root.GetComponent<Button>();
                if (button != null)
                    button.targetGraphic = visualBackground;
                UnityEngine.Object.DestroyImmediate(rootBackground);
            }

            return visual;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static void Set(UnityEngine.Object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
                throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
            EditorUtility.SetDirty(target);
        }
    }
}
