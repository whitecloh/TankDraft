using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Editor
{
    public static class UiMotionSetupMenu
    {
        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Button", true)]
        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Panel", true)]
        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Tab", true)]
        private static bool HasSelection() => Selection.activeGameObject != null;

        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Button")]
        private static void SetupButton()
        {
            AddComponentWithUndo<UiAnimatedButton>(Selection.activeGameObject);
        }

        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Panel")]
        private static void SetupPanel()
        {
            AddComponentWithUndo<CanvasGroup>(Selection.activeGameObject);
            AddComponentWithUndo<UiPanelTransition>(Selection.activeGameObject);
        }

        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Tab")]
        private static void SetupTab()
        {
            AddComponentWithUndo<UiTabMotion>(Selection.activeGameObject);
        }

        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Assign as Parent Button Focus Ring", true)]
        private static bool CanAssignFocusRing()
        {
            GameObject selected = Selection.activeGameObject;
            return selected != null
                && selected.GetComponent<CanvasGroup>() != null
                && selected.transform.parent != null
                && selected.transform.parent.GetComponent<UiAnimatedButton>() != null;
        }

        [MenuItem("Tools/SLVR/UI Motion/Setup Selected/Assign as Parent Button Focus Ring")]
        private static void AssignFocusRing()
        {
            CanvasGroup focusRing = Selection.activeGameObject.GetComponent<CanvasGroup>();
            UiAnimatedButton button = focusRing.transform.parent.GetComponent<UiAnimatedButton>();
            Undo.RecordObject(button, "Assign button focus ring");
            Undo.RecordObject(focusRing, "Initialize button focus ring");
            button.AssignFocusRing(focusRing);
            EditorUtility.SetDirty(button);
        }

        private static void AddComponentWithUndo<T>(GameObject target) where T : Component
        {
            if (target == null || target.GetComponent<T>() != null) return;
            Undo.AddComponent<T>(target);
        }
    }
}
