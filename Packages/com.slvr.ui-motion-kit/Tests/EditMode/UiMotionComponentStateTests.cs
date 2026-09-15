using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiMotionComponentStateTests
    {
        [Test]
        public void ListEntry_UsesCanvasGroupRectTransform()
        {
            var item = new GameObject("item", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                CanvasGroup group = item.GetComponent<CanvasGroup>();
                var entry = new UiListStaggerEntry(group);
                Assert.That(entry.CanvasGroup, Is.SameAs(group));
                Assert.That(entry.VisualRoot, Is.SameAs(item.transform));
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ListStagger_PlayAndReset_RestoresAuthoredScale()
        {
            var root = new GameObject("root", typeof(RectTransform), typeof(UiListStagger));
            var item = new GameObject("item", typeof(RectTransform), typeof(CanvasGroup));
            item.transform.SetParent(root.transform, false);
            item.transform.localScale = new Vector3(1.2f, 0.8f, 1f);
            try
            {
                UiListStagger stagger = root.GetComponent<UiListStagger>();
                CanvasGroup group = item.GetComponent<CanvasGroup>();
                Vector3 authoredScale = item.transform.localScale;
                stagger.SetItems(new[] { group });

                stagger.Play();

                Assert.That(group.alpha, Is.Zero.Within(0.001f));
                Assert.That(item.transform.localScale.x, Is.LessThan(authoredScale.x));
                Assert.That(item.transform.localScale.y, Is.LessThan(authoredScale.y));

                stagger.ResetImmediate(true);
                Assert.That(group.alpha, Is.EqualTo(1f).Within(0.001f));
                Assert.That(item.transform.localScale, Is.EqualTo(authoredScale));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ListStagger_SetItems_RecapturesLayoutPositionsOnNextPlay()
        {
            var root = new GameObject("root", typeof(RectTransform), typeof(UiListStagger));
            var first = new GameObject("first", typeof(RectTransform), typeof(CanvasGroup));
            var second = new GameObject("second", typeof(RectTransform), typeof(CanvasGroup));
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            try
            {
                UiListStagger stagger = root.GetComponent<UiListStagger>();
                CanvasGroup[] groups =
                {
                    first.GetComponent<CanvasGroup>(),
                    second.GetComponent<CanvasGroup>(),
                };

                stagger.SetItems(groups);
                stagger.Play();
                stagger.ResetImmediate(true);

                stagger.SetItems(groups);
                RectTransform firstRect = (RectTransform)first.transform;
                RectTransform secondRect = (RectTransform)second.transform;
                firstRect.anchoredPosition = new Vector2(-120f, 15f);
                secondRect.anchoredPosition = new Vector2(140f, 15f);

                stagger.Play();
                stagger.ResetImmediate(true);

                Assert.That(firstRect.anchoredPosition, Is.EqualTo(new Vector2(-120f, 15f)));
                Assert.That(secondRect.anchoredPosition, Is.EqualTo(new Vector2(140f, 15f)));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void NavigationImmediate_UpdatesSelectionStateWithoutTween()
        {
            var item = new GameObject("tab", typeof(RectTransform), typeof(UiTabMotion));
            try
            {
                UiTabMotion tab = item.GetComponent<UiTabMotion>();
                tab.SetSelected(true, true);
                Assert.That(tab.IsSelected, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ShaderEffectGraphic_WritesSpatialUvIndependentOfSlicedSpriteUv()
        {
            var item = new GameObject(
                "sliced",
                typeof(RectTransform),
                typeof(Image),
                typeof(UiShaderEffectGraphic));
            try
            {
                RectTransform rect = item.GetComponent<RectTransform>();
                rect.sizeDelta = new Vector2(100f, 50f);
                var vertices = new VertexHelper();
                vertices.AddVert(new Vector3(-50f, -25f), Color.white, new Vector2(0.10f, 0.20f));
                vertices.AddVert(new Vector3(-40f, -25f), Color.white, new Vector2(0.45f, 0.20f));
                vertices.AddVert(new Vector3(50f, 25f), Color.white, new Vector2(0.90f, 0.80f));

                item.GetComponent<UiShaderEffectGraphic>().ModifyMesh(vertices);

                UIVertex vertex = default;
                vertices.PopulateUIVertex(ref vertex, 0);
                Assert.That(vertex.uv1.x, Is.EqualTo(0f).Within(0.001f));
                Assert.That(vertex.uv1.y, Is.EqualTo(0f).Within(0.001f));
                vertices.PopulateUIVertex(ref vertex, 1);
                Assert.That(vertex.uv1.x, Is.EqualTo(0.10f).Within(0.001f));
                Assert.That(vertex.uv0.x, Is.EqualTo(0.45f).Within(0.001f));
                vertices.PopulateUIVertex(ref vertex, 2);
                Assert.That(vertex.uv1.x, Is.EqualTo(1f).Within(0.001f));
                Assert.That(vertex.uv1.y, Is.EqualTo(1f).Within(0.001f));

                vertices.Dispose();
            }
            finally
            {
                Object.DestroyImmediate(item);
            }
        }

        [Test]
        public void ScrollRectMotion_ResetImmediate_ReturnsToConfiguredStart()
        {
            var root = new GameObject("scroll", typeof(RectTransform), typeof(ScrollRect), typeof(UiScrollRectMotion));
            var viewport = new GameObject("viewport", typeof(RectTransform));
            var content = new GameObject("content", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            content.transform.SetParent(viewport.transform, false);
            try
            {
                RectTransform viewportRect = (RectTransform)viewport.transform;
                RectTransform contentRect = (RectTransform)content.transform;
                viewportRect.sizeDelta = new Vector2(100f, 100f);
                contentRect.sizeDelta = new Vector2(100f, 400f);
                ScrollRect scrollRect = root.GetComponent<ScrollRect>();
                scrollRect.viewport = viewportRect;
                scrollRect.content = contentRect;
                scrollRect.vertical = true;
                scrollRect.normalizedPosition = new Vector2(0f, 0.25f);

                UiScrollRectMotion motion = root.GetComponent<UiScrollRectMotion>();
                motion.Configure(scrollRect, false, 0f, true, 1f);
                motion.ResetImmediate();

                Assert.That(scrollRect.verticalNormalizedPosition, Is.EqualTo(1f).Within(0.001f));
                Assert.That(scrollRect.velocity, Is.EqualTo(Vector2.zero));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ScrollRectMotion_CenterOnImmediate_CentersContentDescendant()
        {
            var root = new GameObject("scroll", typeof(RectTransform), typeof(ScrollRect), typeof(UiScrollRectMotion));
            var viewport = new GameObject("viewport", typeof(RectTransform));
            var content = new GameObject("content", typeof(RectTransform));
            var target = new GameObject("target", typeof(RectTransform));
            viewport.transform.SetParent(root.transform, false);
            content.transform.SetParent(viewport.transform, false);
            target.transform.SetParent(content.transform, false);
            try
            {
                RectTransform viewportRect = (RectTransform)viewport.transform;
                viewportRect.sizeDelta = new Vector2(300f, 300f);
                RectTransform contentRect = (RectTransform)content.transform;
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0.5f, 1f);
                contentRect.sizeDelta = new Vector2(0f, 1000f);
                RectTransform targetRect = (RectTransform)target.transform;
                targetRect.anchorMin = new Vector2(0.5f, 1f);
                targetRect.anchorMax = new Vector2(0.5f, 1f);
                targetRect.sizeDelta = new Vector2(200f, 100f);
                targetRect.anchoredPosition = new Vector2(0f, -600f);

                ScrollRect scrollRect = root.GetComponent<ScrollRect>();
                scrollRect.viewport = viewportRect;
                scrollRect.content = contentRect;
                scrollRect.vertical = true;
                scrollRect.verticalNormalizedPosition = 1f;
                UiScrollRectMotion motion = root.GetComponent<UiScrollRectMotion>();
                motion.Configure(scrollRect);
                motion.CenterOn(targetRect, true);

                Bounds centeredBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewportRect, targetRect);
                Assert.That(centeredBounds.center.y, Is.EqualTo(viewportRect.rect.center.y).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void MaskedSlideReveal_PrepareAndReset_PreserveAuthoredState()
        {
            var root = new GameObject("reveal", typeof(RectTransform), typeof(UiMaskedSlideReveal));
            var icon = new GameObject("icon", typeof(RectTransform));
            var label = new GameObject("label", typeof(RectTransform), typeof(CanvasGroup));
            icon.transform.SetParent(root.transform, false);
            label.transform.SetParent(root.transform, false);
            try
            {
                RectTransform iconRect = (RectTransform)icon.transform;
                iconRect.anchoredPosition = new Vector2(12f, 7f);
                CanvasGroup labelGroup = label.GetComponent<CanvasGroup>();
                labelGroup.alpha = 0.8f;
                UiMaskedSlideReveal reveal = root.GetComponent<UiMaskedSlideReveal>();
                reveal.Configure(iconRect, labelGroup, new Vector2(60f, 0f));

                reveal.Prepare();

                Assert.That(iconRect.anchoredPosition, Is.EqualTo(new Vector2(72f, 7f)));
                Assert.That(labelGroup.alpha, Is.Zero.Within(0.001f));

                reveal.ResetImmediate();

                Assert.That(iconRect.anchoredPosition, Is.EqualTo(new Vector2(12f, 7f)));
                Assert.That(labelGroup.alpha, Is.EqualTo(0.8f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}
