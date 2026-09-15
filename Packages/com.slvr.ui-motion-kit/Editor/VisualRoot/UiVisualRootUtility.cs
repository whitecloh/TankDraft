using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Editor
{
    public static class UiVisualRootUtility
    {
        [MenuItem("Tools/SLVR/UI Motion/Add Empty VisualRoot to Selection", true)]
        private static bool CanAddVisualRoot()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.transform is RectTransform;
        }

        [MenuItem("Tools/SLVR/UI Motion/Add Empty VisualRoot to Selection")]
        private static void AddVisualRoot()
        {
            GameObject owner = Selection.activeGameObject;
            if (owner == null || !(owner.transform is RectTransform ownerRect)) return;

            var child = new GameObject("VisualRoot", typeof(RectTransform), typeof(UiVisualRoot));
            Undo.RegisterCreatedObjectUndo(child, "Add UI VisualRoot");
            RectTransform childRect = (RectTransform)child.transform;
            childRect.SetParent(ownerRect, false);
            childRect.anchorMin = Vector2.zero;
            childRect.anchorMax = Vector2.one;
            childRect.offsetMin = Vector2.zero;
            childRect.offsetMax = Vector2.zero;
            childRect.localScale = Vector3.one;
            Selection.activeGameObject = child;
        }

        [MenuItem("Tools/SLVR/UI Motion/Add VisualRoot and Move Children", true)]
        private static bool CanMigrateToVisualRoot()
        {
            return CanAddVisualRoot() && Selection.activeGameObject.transform.childCount > 0;
        }

        [MenuItem("Tools/SLVR/UI Motion/Add VisualRoot and Move Children")]
        private static void MigrateToVisualRoot()
        {
            GameObject visualRoot = CreateAndMigrate(Selection.activeGameObject);
            if (visualRoot != null) Selection.activeGameObject = visualRoot;
        }

        /// <summary>
        /// Creates a full-stretch VisualRoot and moves direct children without replacing their
        /// GameObjects, serialized references, RectTransform values, or relative sibling order.
        /// </summary>
        public static GameObject CreateAndMigrate(GameObject owner)
        {
            if (owner == null || !(owner.transform is RectTransform ownerRect)) return null;

            int childCount = ownerRect.childCount;
            var children = new List<Transform>(childCount);
            var rectSnapshots = new List<RectTransformSnapshot>(childCount);
            for (int i = 0; i < childCount; i++)
            {
                Transform child = ownerRect.GetChild(i);
                children.Add(child);
                rectSnapshots.Add(child is RectTransform rect
                    ? new RectTransformSnapshot(rect)
                    : default);
            }

            var rootObject = new GameObject("VisualRoot", typeof(RectTransform), typeof(UiVisualRoot));
            Undo.RegisterCreatedObjectUndo(rootObject, "Add UI VisualRoot and move children");
            RectTransform rootRect = (RectTransform)rootObject.transform;
            rootRect.SetParent(ownerRect, false);
            rootRect.SetSiblingIndex(0);
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;
            rootRect.localScale = Vector3.one;

            for (int i = 0; i < children.Count; i++)
            {
                Transform child = children[i];
                Undo.SetTransformParent(child, rootRect, "Move visual child into VisualRoot");
                child.SetSiblingIndex(i);
                if (child is RectTransform rect) rectSnapshots[i].Restore(rect);
                PrefabUtility.RecordPrefabInstancePropertyModifications(child);
            }

            UiAnimatedButton animatedButton = owner.GetComponent<UiAnimatedButton>();
            if (animatedButton != null)
            {
                Undo.RecordObject(animatedButton, "Assign button VisualRoot");
                animatedButton.AssignVisualRoot(rootObject.GetComponent<UiVisualRoot>());
                EditorUtility.SetDirty(animatedButton);
            }

            PrefabUtility.RecordPrefabInstancePropertyModifications(ownerRect);
            EditorUtility.SetDirty(owner);
            return rootObject;
        }

        [MenuItem("Tools/SLVR/UI Motion/Validate Selected VisualRoot", true)]
        private static bool CanValidate()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<UiVisualRoot>() != null;
        }

        [MenuItem("Tools/SLVR/UI Motion/Validate Selected VisualRoot")]
        private static void ValidateSelected()
        {
            UiVisualRoot root = Selection.activeGameObject.GetComponent<UiVisualRoot>();
            if (root.IsLayoutControlled)
            {
                Debug.LogWarning(
                    "[SLVR UI Motion] VisualRoot is controlled by a layout component and must not be animated directly.",
                    root);
                return;
            }

            Debug.Log("[SLVR UI Motion] VisualRoot is safe for transform animation.", root);
        }

        private readonly struct RectTransformSnapshot
        {
            private readonly bool valid;
            private readonly Vector2 anchorMin;
            private readonly Vector2 anchorMax;
            private readonly Vector2 pivot;
            private readonly Vector2 sizeDelta;
            private readonly Vector3 anchoredPosition;
            private readonly Vector3 localScale;
            private readonly Quaternion localRotation;

            public RectTransformSnapshot(RectTransform rect)
            {
                valid = rect != null;
                anchorMin = rect != null ? rect.anchorMin : default;
                anchorMax = rect != null ? rect.anchorMax : default;
                pivot = rect != null ? rect.pivot : default;
                sizeDelta = rect != null ? rect.sizeDelta : default;
                anchoredPosition = rect != null ? rect.anchoredPosition3D : default;
                localScale = rect != null ? rect.localScale : Vector3.one;
                localRotation = rect != null ? rect.localRotation : Quaternion.identity;
            }

            public void Restore(RectTransform rect)
            {
                if (!valid || rect == null) return;
                rect.anchorMin = anchorMin;
                rect.anchorMax = anchorMax;
                rect.pivot = pivot;
                rect.sizeDelta = sizeDelta;
                rect.anchoredPosition3D = anchoredPosition;
                rect.localScale = localScale;
                rect.localRotation = localRotation;
            }
        }
    }
}
