using DG.DOTweenEditor;
using DG.Tweening;
using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Editor
{
    [InitializeOnLoad]
    public static class UiMotionPreviewUtility
    {
        static UiMotionPreviewUtility()
        {
            AssemblyReloadEvents.beforeAssemblyReload += StopAndReset;
            EditorApplication.playModeStateChanged += _ => StopAndReset();
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Press", true)]
        [MenuItem("Tools/SLVR/UI Motion/Preview/Hover", true)]
        [MenuItem("Tools/SLVR/UI Motion/Preview/Attention", true)]
        private static bool HasRectSelection() => Selection.activeTransform is RectTransform;

        [MenuItem("Tools/SLVR/UI Motion/Preview/Focus", true)]
        private static bool HasFocusRingSelection()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<UiAnimatedButton>()?.FocusRing != null;
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Open", true)]
        [MenuItem("Tools/SLVR/UI Motion/Preview/Close", true)]
        private static bool HasCanvasGroupSelection()
        {
            return Selection.activeGameObject != null
                && Selection.activeGameObject.GetComponent<CanvasGroup>() != null;
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Press")]
        private static void PreviewPress()
        {
            StopAndReset();
            RectTransform target = ResolveButtonTarget();
            Tween tween = target.DOScale(target.localScale * 0.96f, 0.1f).SetLoops(2, LoopType.Yoyo);
            Start(tween);
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Hover")]
        private static void PreviewHover()
        {
            StopAndReset();
            RectTransform target = ResolveButtonTarget();
            Tween tween = target.DOScale(target.localScale * 1.025f, 0.12f).SetLoops(2, LoopType.Yoyo);
            Start(tween);
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Focus")]
        private static void PreviewFocus()
        {
            StopAndReset();
            CanvasGroup focusRing = Selection.activeGameObject.GetComponent<UiAnimatedButton>().FocusRing;
            Tween tween = DOTween.To(() => focusRing.alpha, value => focusRing.alpha = value, 1f, 0.12f)
                .SetLoops(2, LoopType.Yoyo);
            Start(tween);
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Attention")]
        private static void PreviewAttention()
        {
            StopAndReset();
            RectTransform target = ResolveButtonTarget();
            Tween tween = target.DOScale(target.localScale * 1.06f, 0.16f)
                .SetEase(Ease.OutBack)
                .SetLoops(2, LoopType.Yoyo);
            Start(tween);
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Open")]
        private static void PreviewOpen()
        {
            StopAndReset();
            CanvasGroup target = Selection.activeGameObject.GetComponent<CanvasGroup>();
            Start(DOTween.To(() => target.alpha, x => target.alpha = x, 1f, 0.2f));
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Close")]
        private static void PreviewClose()
        {
            StopAndReset();
            CanvasGroup target = Selection.activeGameObject.GetComponent<CanvasGroup>();
            Start(DOTween.To(() => target.alpha, x => target.alpha = x, 0f, 0.2f));
        }

        [MenuItem("Tools/SLVR/UI Motion/Preview/Reset")]
        public static void StopAndReset()
        {
            if (Application.isPlaying) return;
            DOTweenEditorPreview.Stop(true, true);
            SceneView.RepaintAll();
        }

        private static void Start(Tween tween)
        {
            DOTweenEditorPreview.PrepareTweenForPreview(tween, true, true, true);
            DOTweenEditorPreview.Start(SceneView.RepaintAll);
        }

        private static RectTransform ResolveButtonTarget()
        {
            UiAnimatedButton button = Selection.activeGameObject.GetComponent<UiAnimatedButton>();
            return button != null && button.VisualTarget != null
                ? button.VisualTarget
                : (RectTransform)Selection.activeTransform;
        }
    }
}
