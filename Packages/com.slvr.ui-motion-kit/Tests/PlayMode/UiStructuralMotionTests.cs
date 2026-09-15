using System.Collections;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiStructuralMotionTests
    {
        [UnityTest]
        public IEnumerator ModalBackdrop_HiddenStateNeverBlocksRaycasts()
        {
            var go = new GameObject("backdrop", typeof(RectTransform), typeof(CanvasGroup), typeof(UiModalBackdrop));
            UiModalBackdrop backdrop = go.GetComponent<UiModalBackdrop>();
            CanvasGroup group = go.GetComponent<CanvasGroup>();
            yield return null;
            backdrop.HideImmediate();
            Assert.That(group.alpha, Is.Zero);
            Assert.That(group.blocksRaycasts, Is.False);
            backdrop.ShowImmediate();
            Assert.That(group.blocksRaycasts, Is.True);
            Object.Destroy(go);
        }

        [UnityTest]
        public IEnumerator ScreenTransition_MovesOnlyAssignedContentRoot()
        {
            var screen = new GameObject("screen", typeof(RectTransform), typeof(CanvasGroup), typeof(UiScreenTransition));
            var content = new GameObject("content", typeof(RectTransform));
            var navigation = new GameObject("navigation", typeof(RectTransform));
            content.transform.SetParent(screen.transform, false);
            navigation.transform.SetParent(screen.transform, false);
            var contentRect = (RectTransform)content.transform;
            var navigationRect = (RectTransform)navigation.transform;
            navigationRect.anchoredPosition = new Vector2(17f, 23f);
            UiScreenTransition transition = screen.GetComponent<UiScreenTransition>();
            yield return null;
            transition.ContentRoot = contentRect;
            Vector2 navigationPosition = navigationRect.anchoredPosition;
            transition.ExitImmediate();
            Assert.That(contentRect.anchoredPosition, Is.Not.EqualTo(Vector2.zero));
            Assert.That(navigationRect.anchoredPosition, Is.EqualTo(navigationPosition));
            Object.Destroy(screen);
        }

        [UnityTest]
        public IEnumerator LockedCard_CannotRemainSelected()
        {
            var card = new GameObject("card", typeof(RectTransform), typeof(UiCardSelectionMotion));
            UiCardSelectionMotion motion = card.GetComponent<UiCardSelectionMotion>();
            yield return null;
            motion.SetSelected(true, true);
            Assert.That(motion.IsSelected, Is.True);
            motion.SetLocked(true);
            Assert.That(motion.IsLocked, Is.True);
            Assert.That(motion.IsSelected, Is.False);
            Object.Destroy(card);
        }

        [UnityTest]
        public IEnumerator ScrollRectMotion_ResetImmediate_StopsVelocityAndReturnsToTop()
        {
            var root = new GameObject("scroll", typeof(RectTransform), typeof(ScrollRect), typeof(UiScrollRectMotion));
            var viewport = new GameObject("viewport", typeof(RectTransform));
            var content = new GameObject("content", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            content.transform.SetParent(viewport.transform, false);
            ((RectTransform)root.transform).sizeDelta = new Vector2(300f, 300f);
            ((RectTransform)viewport.transform).sizeDelta = new Vector2(300f, 300f);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.sizeDelta = new Vector2(0f, 1000f);
            ScrollRect scrollRect = root.GetComponent<ScrollRect>();
            scrollRect.viewport = (RectTransform)viewport.transform;
            scrollRect.content = contentRect;
            scrollRect.normalizedPosition = new Vector2(0.35f, 0.2f);
            scrollRect.velocity = new Vector2(0f, -240f);
            UiScrollRectMotion motion = root.GetComponent<UiScrollRectMotion>();
            motion.Configure(scrollRect, false, 0f, true, 1f);

            yield return null;
            motion.ResetImmediate();

            Assert.That(scrollRect.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(scrollRect.velocity, Is.EqualTo(Vector2.zero));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator ScrollRectMotion_SoftReveal_RestoresAuthoredMaskState()
        {
            var root = new GameObject(
                "scroll",
                typeof(RectTransform),
                typeof(RectMask2D),
                typeof(ScrollRect),
                typeof(UiScrollRectMotion));
            RectTransform rect = (RectTransform)root.transform;
            rect.sizeDelta = new Vector2(300f, 420f);
            RectMask2D mask = root.GetComponent<RectMask2D>();
            var authoredPadding = new Vector4(3f, 7f, 5f, -24f);
            var authoredSoftness = new Vector2Int(4, 20);
            mask.padding = authoredPadding;
            mask.softness = authoredSoftness;
            UiScrollRectMotion motion = root.GetComponent<UiScrollRectMotion>();

            yield return null;
            motion.ConfigureReveal(mask, 0.08f, 64, 0.25f);
            motion.PrepareReveal();

            Assert.That(motion.IsRevealPrepared, Is.True);
            Assert.That(mask.padding.y, Is.GreaterThan(authoredPadding.y));
            Assert.That(mask.softness.y, Is.EqualTo(64));
            float initiallyVisibleHeight = rect.rect.height - mask.padding.y - authoredPadding.w;
            Assert.That(initiallyVisibleHeight, Is.EqualTo(rect.rect.height * 0.25f).Within(0.001f));

            motion.PlayReveal();
            yield return new WaitForSecondsRealtime(0.16f);

            Assert.That(mask.padding, Is.EqualTo(authoredPadding));
            Assert.That(mask.softness, Is.EqualTo(authoredSoftness));
            Assert.That(motion.IsRevealPrepared, Is.False);
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator MaskedSlideReveal_PrepareAndPlay_ReachesAuthoredState()
        {
            var root = new GameObject("slot", typeof(RectTransform));
            var icon = new GameObject("icon", typeof(RectTransform));
            var label = new GameObject("label", typeof(RectTransform), typeof(CanvasGroup));
            icon.transform.SetParent(root.transform, false);
            label.transform.SetParent(root.transform, false);
            RectTransform iconRect = (RectTransform)icon.transform;
            iconRect.anchoredPosition = new Vector2(12f, 4f);
            CanvasGroup labelGroup = label.GetComponent<CanvasGroup>();
            UiMaskedSlideReveal reveal = root.AddComponent<UiMaskedSlideReveal>();
            reveal.Configure(iconRect, labelGroup, new Vector2(48f, 0f));

            yield return null;
            reveal.Prepare();
            Assert.That(iconRect.anchoredPosition.x, Is.GreaterThan(12f));
            Assert.That(labelGroup.alpha, Is.Zero);

            reveal.Play();
            yield return new WaitForSecondsRealtime(0.45f);

            Assert.That(iconRect.anchoredPosition.x, Is.EqualTo(12f).Within(0.001f));
            Assert.That(iconRect.anchoredPosition.y, Is.EqualTo(4f).Within(0.001f));
            Assert.That(labelGroup.alpha, Is.EqualTo(1f).Within(0.001f));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator PanelTransition_PrepareShow_PreventsFinalStateFlash()
        {
            var root = new GameObject(
                "popup",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(UiPanelTransition));
            var visual = new GameObject("visual", typeof(RectTransform));
            visual.transform.SetParent(root.transform, false);
            RectTransform visualRect = (RectTransform)visual.transform;
            CanvasGroup group = root.GetComponent<CanvasGroup>();
            UiPanelTransition transition = root.GetComponent<UiPanelTransition>();

            yield return null;
            transition.Configure(group, visualRect, UiPanelTransitionStyle.RewardPop, 0.2f);
            transition.PrepareShow();

            Assert.That(group.alpha, Is.Zero);
            Assert.That(group.blocksRaycasts, Is.False);
            Assert.That(visualRect.localScale.x, Is.EqualTo(0.2f).Within(0.001f));

            transition.Show();
            yield return new WaitForSecondsRealtime(0.7f);

            Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(group.blocksRaycasts, Is.True);
            Assert.That(visualRect.localScale, Is.EqualTo(Vector3.one));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator StagedReveal_PlaysFadeThenTwoScaleSteps()
        {
            var root = new GameObject("staged", typeof(RectTransform), typeof(UiStagedReveal));
            var icon = new GameObject("icon", typeof(RectTransform), typeof(CanvasGroup));
            var title = new GameObject("title", typeof(RectTransform), typeof(CanvasGroup));
            var value = new GameObject("value", typeof(RectTransform), typeof(CanvasGroup));
            icon.transform.SetParent(root.transform, false);
            title.transform.SetParent(root.transform, false);
            value.transform.SetParent(root.transform, false);
            UiStagedReveal reveal = root.GetComponent<UiStagedReveal>();
            reveal.Configure(new[]
            {
                new UiStagedRevealStep(
                    UiStagedRevealStepKind.Fade,
                    icon.GetComponent<CanvasGroup>(),
                    (RectTransform)icon.transform,
                    0.15f),
                new UiStagedRevealStep(
                    UiStagedRevealStepKind.Scale,
                    title.GetComponent<CanvasGroup>(),
                    (RectTransform)title.transform,
                    0.15f,
                    1.1f),
                new UiStagedRevealStep(
                    UiStagedRevealStepKind.Scale,
                    value.GetComponent<CanvasGroup>(),
                    (RectTransform)value.transform,
                    0.15f,
                    1.1f),
            });

            yield return null;
            reveal.Prepare();
            Assert.That(icon.GetComponent<CanvasGroup>().alpha, Is.Zero);
            Assert.That(((RectTransform)title.transform).localScale, Is.EqualTo(Vector3.zero));
            Assert.That(((RectTransform)value.transform).localScale, Is.EqualTo(Vector3.zero));

            reveal.Play();
            yield return new WaitForSecondsRealtime(0.65f);

            Assert.That(icon.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.001f));
            Assert.That(((RectTransform)title.transform).localScale, Is.EqualTo(Vector3.one));
            Assert.That(((RectTransform)value.transform).localScale, Is.EqualTo(Vector3.one));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator StagedReveal_ShowImmediateRefreshesChangedTypewriterText()
        {
            var root = new GameObject("typewriter-root", typeof(RectTransform), typeof(UiStagedReveal));
            var textObject = new GameObject("description", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(root.transform, false);
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.text = "Short";
            UiStagedReveal reveal = root.GetComponent<UiStagedReveal>();
            reveal.Configure(new[] { new UiStagedRevealStep(text, 0.15f) });
            yield return null;

            text.text = "A newly bound description with more characters";
            reveal.ShowImmediate();
            text.ForceMeshUpdate();

            Assert.That(text.maxVisibleCharacters, Is.EqualTo(text.textInfo.characterCount));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator StagedReveal_PlaySignalsCompletionAfterFinalState()
        {
            var root = new GameObject("reveal-root", typeof(RectTransform), typeof(CanvasGroup), typeof(UiStagedReveal));
            UiStagedReveal reveal = root.GetComponent<UiStagedReveal>();
            reveal.Configure(new[]
            {
                new UiStagedRevealStep(
                    UiStagedRevealStepKind.Fade,
                    root.GetComponent<CanvasGroup>(),
                    duration: 0f),
            });
            bool completed = false;
            reveal.RevealCompleted += () => completed = true;

            reveal.Prepare();
            reveal.Play();
            yield return null;

            Assert.IsTrue(completed);
            Assert.That(root.GetComponent<CanvasGroup>().alpha, Is.EqualTo(1f).Within(0.0001f));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator RuntimeDotBadge_UsesNotificationVisibilityContract()
        {
            var parent = new GameObject("parent", typeof(RectTransform));
            UiNotificationBadge badge = UiNotificationBadge.CreateRuntimeDot(
                (RectTransform)parent.transform,
                Vector2.zero);

            yield return null;
            CanvasGroup group = badge.GetComponent<CanvasGroup>();
            Assert.That(group.alpha, Is.Zero);

            badge.SetCount(1, true);
            Assert.That(group.alpha, Is.EqualTo(1f));
            badge.SetCount(0, true);
            Assert.That(group.alpha, Is.Zero);
            Object.Destroy(parent);
        }

        [UnityTest]
        public IEnumerator SelectiveGraphicDimming_LeavesExcludedSubtreeBright()
        {
            var root = new GameObject("card", typeof(RectTransform), typeof(UiSelectiveGraphicDimming));
            var cardArt = new GameObject("card-art", typeof(RectTransform), typeof(Image));
            var selectedMark = new GameObject("selected-mark", typeof(RectTransform), typeof(Image));
            var selectedIcon = new GameObject("selected-icon", typeof(RectTransform), typeof(Image));
            cardArt.transform.SetParent(root.transform, false);
            selectedMark.transform.SetParent(root.transform, false);
            selectedIcon.transform.SetParent(selectedMark.transform, false);
            UiSelectiveGraphicDimming dimming = root.GetComponent<UiSelectiveGraphicDimming>();
            Color multiplier = new Color(0.3f, 0.4f, 0.5f, 1f);
            dimming.Configure(root.transform, selectedMark.transform, multiplier);

            yield return null;
            dimming.SetDimmed(true);

            Assert.That(cardArt.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(multiplier));
            Assert.That(selectedMark.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(Color.white));
            Assert.That(selectedIcon.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(Color.white));

            dimming.SetDimmed(false);
            Assert.That(cardArt.GetComponent<Image>().canvasRenderer.GetColor(), Is.EqualTo(Color.white));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator CardTransfer_FollowsLiveDestinationWhileScrollMoves()
        {
            var root = new GameObject("transfer-root", typeof(RectTransform), typeof(UiCardTransferMotion));
            var proxyObject = new GameObject("proxy", typeof(RectTransform));
            var targetObject = new GameObject("target", typeof(RectTransform));
            proxyObject.transform.SetParent(root.transform, false);
            targetObject.transform.SetParent(root.transform, false);
            RectTransform proxy = (RectTransform)proxyObject.transform;
            RectTransform target = (RectTransform)targetObject.transform;
            proxy.sizeDelta = new Vector2(100f, 140f);
            target.sizeDelta = new Vector2(100f, 140f);
            target.position = new Vector3(160f, 40f, 0f);
            UiCardTransferMotion motion = root.GetComponent<UiCardTransferMotion>();
            motion.Configure(0.12f, 30f, 1.08f, 0.78f);
            bool completed = false;

            yield return null;
            motion.Play(
                proxy,
                Vector3.zero,
                Vector3.one,
                target,
                Vector3.one,
                () => completed = true);
            yield return new WaitForSecondsRealtime(0.04f);
            target.position = new Vector3(260f, -35f, 0f);
            yield return new WaitForSecondsRealtime(0.18f);

            Assert.That(completed, Is.True);
            Assert.That(Vector3.Distance(proxy.position, target.position), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance(proxy.localScale, Vector3.one), Is.LessThan(0.001f));
            Object.Destroy(root);
        }

        [UnityTest]
        public IEnumerator CardTransfer_DisableCancelsWithoutCompletionCallback()
        {
            var root = new GameObject("transfer-root", typeof(RectTransform), typeof(UiCardTransferMotion));
            var proxyObject = new GameObject("proxy", typeof(RectTransform));
            var targetObject = new GameObject("target", typeof(RectTransform));
            proxyObject.transform.SetParent(root.transform, false);
            targetObject.transform.SetParent(root.transform, false);
            RectTransform proxy = (RectTransform)proxyObject.transform;
            RectTransform target = (RectTransform)targetObject.transform;
            UiCardTransferMotion motion = root.GetComponent<UiCardTransferMotion>();
            motion.Configure(0.5f, 30f);
            bool completed = false;

            yield return null;
            motion.Play(proxy, Vector3.zero, Vector3.one, target, Vector3.one, () => completed = true);
            Assert.That(motion.IsPlaying, Is.True);
            root.SetActive(false);
            yield return null;

            Assert.That(motion.IsPlaying, Is.False);
            Assert.That(completed, Is.False);
            Object.Destroy(root);
        }
    }
}
