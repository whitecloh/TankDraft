using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiPanelTransitionTests
    {
        [UnityTest]
        public IEnumerator ImmediateStates_KeepRaycastContractValid()
        {
            var panelObject = new GameObject(
                "panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();
            yield return null;

            transition.HideImmediate();
            Assert.That(canvasGroup.alpha, Is.Zero);
            Assert.That(canvasGroup.interactable, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.False);

            transition.ShowImmediate();
            Assert.That(canvasGroup.alpha, Is.EqualTo(1f));
            Assert.That(canvasGroup.interactable, Is.True);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator InactiveBeforeAwake_ShowImmediateCapturesReadableShownTransform()
        {
            var panelObject = new GameObject("panel", typeof(RectTransform), typeof(CanvasGroup));
            RectTransform rect = (RectTransform)panelObject.transform;
            rect.localScale = new Vector3(1.25f, 1.25f, 1f);
            rect.anchoredPosition = new Vector2(12f, 24f);
            panelObject.SetActive(false);
            UiPanelTransition transition = panelObject.AddComponent<UiPanelTransition>();

            transition.ShowImmediate();

            Assert.That(rect.localScale, Is.EqualTo(new Vector3(1.25f, 1.25f, 1f)));
            Assert.That(rect.anchoredPosition, Is.EqualTo(new Vector2(12f, 24f)));
            Object.Destroy(panelObject);
            yield return null;
        }

        [UnityTest]
        public IEnumerator HideThenShow_ReplacesTransitionAndEndsInteractive()
        {
            var panelObject = new GameObject(
                "panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();
            yield return null;

            transition.Hide();
            transition.Show();
            yield return new WaitForSecondsRealtime(0.3f);

            Assert.That(canvasGroup.alpha, Is.EqualTo(1f).Within(0.01f));
            Assert.That(canvasGroup.interactable, Is.True);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator ModalTransition_BlocksRaycastsForEntranceAndExit()
        {
            var panelObject = new GameObject(
                "modal-panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            CanvasGroup canvasGroup = panelObject.GetComponent<CanvasGroup>();
            transition.BlockRaycastsWhileTransitioning = true;
            yield return null;

            transition.PrepareShow();
            transition.Show();
            Assert.That(canvasGroup.interactable, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);

            transition.Hide();
            Assert.That(canvasGroup.interactable, Is.False);
            Assert.That(canvasGroup.blocksRaycasts, Is.True);

            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(canvasGroup.blocksRaycasts, Is.False);
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator HideCompleted_FiresOnlyForTheTransitionThatReachesHiddenState()
        {
            var panelObject = new GameObject(
                "panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            int completedCount = 0;
            transition.HideCompleted += () => completedCount++;
            yield return null;

            transition.Hide();
            transition.Show();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(completedCount, Is.Zero, "Interrupted hide must not deactivate the host window.");

            transition.Hide();
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.That(completedCount, Is.EqualTo(1));

            transition.ShowImmediate();
            transition.HideImmediate();
            Assert.That(completedCount, Is.EqualTo(2));
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator SemanticPresets_ResolveDistinctHiddenStatesAndRestoreShownState()
        {
            var panelObject = new GameObject(
                "panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            RectTransform rect = (RectTransform)panelObject.transform;
            rect.sizeDelta = new Vector2(240f, 180f);
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            yield return null;

            transition.Style = UiPanelTransitionStyle.BottomSheet;
            transition.HideImmediate();
            Assert.That(rect.anchoredPosition.y, Is.LessThan(0f));
            transition.ShowImmediate();
            Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));

            transition.Style = UiPanelTransitionStyle.DrawerLeft;
            transition.HideImmediate();
            Assert.That(rect.anchoredPosition.x, Is.LessThan(0f));
            transition.ShowImmediate();

            transition.Style = UiPanelTransitionStyle.DrawerRight;
            transition.HideImmediate();
            Assert.That(rect.anchoredPosition.x, Is.GreaterThan(0f));
            transition.ShowImmediate();

            transition.Style = UiPanelTransitionStyle.Tooltip;
            transition.HideImmediate();
            float tooltipScale = rect.localScale.x;
            Assert.That(tooltipScale, Is.LessThan(1f));
            transition.ShowImmediate();

            transition.Style = UiPanelTransitionStyle.RewardPop;
            transition.HideImmediate();
            Assert.That(rect.localScale.x, Is.LessThan(tooltipScale));
            transition.ShowImmediate();
            Assert.That(rect.localScale, Is.EqualTo(Vector3.one));
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator TranslationSpring_SettlesAtShownPositionAndRestoresInteraction()
        {
            var panelObject = new GameObject(
                "spring-panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            RectTransform rect = (RectTransform)panelObject.transform;
            CanvasGroup group = panelObject.GetComponent<CanvasGroup>();
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            int shownCount = 0;
            transition.ShowCompleted += () => shownCount++;
            transition.Configure(
                group,
                rect,
                UiPanelTransitionStyle.VerticalOffset,
                1f,
                new Vector2(0f, 96f));
            transition.ConfigureTranslationSpring(0.4f, 10f);
            yield return null;

            transition.PrepareShow();
            Assert.That(rect.anchoredPosition.y, Is.EqualTo(96f).Within(0.01f));
            transition.Show();
            yield return new WaitForSecondsRealtime(0.55f);

            Assert.That(rect.anchoredPosition, Is.EqualTo(Vector2.zero));
            Assert.That(group.alpha, Is.EqualTo(1f).Within(0.01f));
            Assert.That(group.interactable, Is.True);
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(shownCount, Is.EqualTo(1));
            Object.Destroy(panelObject);
        }

        [UnityTest]
        public IEnumerator OffscreenStyles_MoveTheCompletePanelPastViewportEdges()
        {
            var viewportObject = new GameObject("viewport", typeof(RectTransform));
            RectTransform viewport = (RectTransform)viewportObject.transform;
            viewport.sizeDelta = new Vector2(1000f, 500f);
            var panelObject = new GameObject(
                "panel",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            RectTransform panel = (RectTransform)panelObject.transform;
            panel.SetParent(viewport, false);
            panel.sizeDelta = new Vector2(240f, 180f);
            panel.anchoredPosition = new Vector2(-280f, 40f);
            UiPanelTransition transition = panelObject.GetComponent<UiPanelTransition>();
            transition.OffscreenMargin = 32f;
            yield return null;

            transition.Style = UiPanelTransitionStyle.OffscreenLeft;
            transition.HideImmediate();
            Bounds leftBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, panel);
            Assert.That(leftBounds.max.x, Is.LessThanOrEqualTo(viewport.rect.xMin - 31.9f));

            transition.ShowImmediate();
            transition.Style = UiPanelTransitionStyle.OffscreenRight;
            transition.HideImmediate();
            Bounds rightBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, panel);
            Assert.That(rightBounds.min.x, Is.GreaterThanOrEqualTo(viewport.rect.xMax + 31.9f));

            Object.Destroy(viewportObject);
            yield return null;
        }
    }
}
