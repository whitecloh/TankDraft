using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace SLVR.UIMotion.Editor
{
    public sealed class UiMotionAuditWindow : EditorWindow
    {
        private enum Severity { Info, Warning, Error }

        private sealed class Finding
        {
            public Severity Severity;
            public Object Context;
            public string Message;
            public bool SafeRaycastFix;
        }

        private readonly List<Finding> findings = new List<Finding>();
        private Vector2 scroll;

        [MenuItem("Tools/SLVR/UI Motion/Audit Selected Canvas")]
        private static void AuditSelected()
        {
            UiMotionAuditWindow window = GetWindow<UiMotionAuditWindow>("UI Motion Audit");
            Canvas canvas = Selection.activeGameObject != null ? Selection.activeGameObject.GetComponentInParent<Canvas>() : null;
            window.Run(canvas != null ? new[] { canvas } : System.Array.Empty<Canvas>());
        }

        [MenuItem("Tools/SLVR/UI Motion/Audit All UI In Open Scenes")]
        private static void AuditAll()
        {
            UiMotionAuditWindow window = GetWindow<UiMotionAuditWindow>("UI Motion Audit");
            window.Run(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Report-only by default. No scene or prefab is modified during audit.", MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Audit all open scenes")) Run(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
                if (GUILayout.Button("Audit selected canvas")) AuditSelected();
            }

            int errors = findings.Count(x => x.Severity == Severity.Error);
            int warnings = findings.Count(x => x.Severity == Severity.Warning);
            EditorGUILayout.LabelField($"Findings: {findings.Count}  Errors: {errors}  Warnings: {warnings}", EditorStyles.boldLabel);
            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (Finding finding in findings)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(finding.Severity.ToString(), GUILayout.Width(58));
                    if (GUILayout.Button(finding.Context != null ? finding.Context.name : "Scene", EditorStyles.linkLabel, GUILayout.Width(150)))
                        Selection.activeObject = finding.Context;
                    EditorGUILayout.LabelField(finding.Message, EditorStyles.wordWrappedLabel);
                }
            }
            EditorGUILayout.EndScrollView();

            bool hasSafeFix = findings.Any(x => x.SafeRaycastFix && x.Context is Graphic);
            using (new EditorGUI.DisabledScope(!hasSafeFix))
            {
                if (GUILayout.Button("Opt-in: disable proven decorative raycast targets")
                    && EditorUtility.DisplayDialog("Apply safe audit fixes?", "Disable raycastTarget only for findings classified as decorative by name and without input handlers?", "Apply", "Cancel"))
                {
                    ApplySafeRaycastFixes();
                }
            }
        }

        private void Run(IEnumerable<Canvas> canvases)
        {
            findings.Clear();
            foreach (Canvas canvas in canvases.Where(x => x != null).Distinct()) AuditCanvas(canvas);
            if (findings.Count == 0) Add(Severity.Info, null, "No issues found in the audited scope.");
            Repaint();
        }

        private void AuditCanvas(Canvas canvas)
        {
            Graphic[] graphics = canvas.GetComponentsInChildren<Graphic>(true);
            Selectable[] selectables = canvas.GetComponentsInChildren<Selectable>(true);
            UiMotionElement[] motions = canvas.GetComponentsInChildren<UiMotionElement>(true);
            Animator[] animators = canvas.GetComponentsInChildren<Animator>(true);

            if (graphics.Length > 300) Add(Severity.Warning, canvas, $"Large mixed Canvas contains {graphics.Length} Graphics; split frequently-changing regions where profiling confirms rebuild cost.");
            if (canvas.GetComponent<GraphicRaycaster>() != null && selectables.Length == 0)
                Add(Severity.Warning, canvas, "GraphicRaycaster exists on a Canvas with no Selectable controls.");

            foreach (Animator animator in animators)
            {
                if (animator.GetComponent<UiAnimatorShowcaseAdapter>() == null)
                    Add(Severity.Warning, animator, "Animator under UI is not marked as focal-art showcase; ordinary UI motion should use package components.");
            }

            foreach (Selectable selectable in selectables)
            {
                Rect rect = ((RectTransform)selectable.transform).rect;
                float scale = Mathf.Max(0.01f, canvas.scaleFactor);
                if (rect.width * scale < 48f || rect.height * scale < 48f)
                    Add(Severity.Warning, selectable, $"Touch target is approximately {rect.width * scale:0}×{rect.height * scale:0}px; validate against ~48 dp guidance on device.");
                if (selectable.navigation.mode == Navigation.Mode.None)
                    Add(Severity.Warning, selectable, "Navigation mode is None; keyboard/gamepad focus cannot reach this control automatically.");
                EventTrigger trigger = selectable.GetComponent<EventTrigger>();
                if (trigger != null && trigger.triggers != null && trigger.triggers.Any(x => x.eventID == EventTriggerType.PointerDown && x.callback != null && x.callback.GetPersistentEventCount() > 0))
                    Add(Severity.Error, selectable, "PointerDown has a persistent action; gameplay confirmation should normally execute on release/click/submit.");
            }

            foreach (Graphic graphic in graphics)
            {
                bool decorativeName = ContainsAny(graphic.name, "deco", "glow", "ray", "shine", "background", "frame", "shadow");
                bool hasInput = graphic.GetComponent<Selectable>() != null || graphic.GetComponent<EventTrigger>() != null;
                if (graphic.raycastTarget && decorativeName && !hasInput)
                    Add(Severity.Warning, graphic, "Likely decorative Graphic blocks raycasts.", true);
                if (graphic.material != null && graphic.material.name.Contains("(Instance)"))
                    Add(Severity.Warning, graphic, "Instanced UI material may break batching or leak if recreated repeatedly.");
            }

            int idleCount = motions.Count(x => x is UiIdleMotion && x.isActiveAndEnabled);
            if (idleCount > 10) Add(Severity.Warning, canvas, $"{idleCount} active idle components exceed the default combined focal/secondary/accent screen budget.");

            foreach (UiMotionElement motion in motions)
            {
                if (motion.transform.parent != null && motion.transform.parent.GetComponent<LayoutGroup>() != null && motion.GetComponent<UiVisualRoot>() == null)
                    Add(Severity.Error, motion, "Motion component is directly layout-controlled and has no UiVisualRoot isolation.");
                CanvasGroup group = motion.GetComponentInParent<CanvasGroup>();
                if (group != null && group.alpha <= 0.001f && motion.enabled)
                    Add(Severity.Warning, motion, "Motion component remains enabled beneath an invisible CanvasGroup; bridge logical visibility to stop hidden effects.");
            }

            foreach (LayoutGroup group in canvas.GetComponentsInChildren<LayoutGroup>(true))
            {
                int nestedLayouts = 0;
                Transform current = group.transform.parent;
                while (current != null && current != canvas.transform)
                {
                    if (current.GetComponent<LayoutGroup>() != null) nestedLayouts++;
                    current = current.parent;
                }
                if (nestedLayouts >= 4) Add(Severity.Warning, group, $"Deep layout chain contains {nestedLayouts + 1} nested LayoutGroups; profile rebuild cost and simplify where possible.");
            }

            int fullscreenLayers = graphics.Count(x => x is Image && CoversCanvas((RectTransform)x.transform, (RectTransform)canvas.transform));
            if (fullscreenLayers > 4) Add(Severity.Warning, canvas, $"{fullscreenLayers} near-fullscreen Graphic layers may cause mobile overdraw.");
            int uniqueMaterials = graphics.Select(x => x.materialForRendering).Where(x => x != null).Distinct().Count();
            if (uniqueMaterials > 12) Add(Severity.Warning, canvas, $"{uniqueMaterials} UI material variants may reduce batching; inspect shader variant sharing.");
        }

        private void ApplySafeRaycastFixes()
        {
            foreach (Finding finding in findings.Where(x => x.SafeRaycastFix).ToArray())
            {
                if (!(finding.Context is Graphic graphic)) continue;
                Undo.RecordObject(graphic, "Disable decorative UI raycast");
                graphic.raycastTarget = false;
                EditorUtility.SetDirty(graphic);
            }
            Run(FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None));
        }

        private void Add(Severity severity, Object context, string message, bool safeRaycastFix = false)
            => findings.Add(new Finding { Severity = severity, Context = context, Message = message, SafeRaycastFix = safeRaycastFix });

        private static bool ContainsAny(string value, params string[] terms)
        {
            string lower = value.ToLowerInvariant();
            return terms.Any(lower.Contains);
        }

        private static bool CoversCanvas(RectTransform graphic, RectTransform canvas)
        {
            if (graphic == null || canvas == null) return false;
            Rect a = graphic.rect;
            Rect b = canvas.rect;
            return a.width >= b.width * 0.85f && a.height >= b.height * 0.85f;
        }
    }
}
