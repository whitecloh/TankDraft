using NUnit.Framework;
using SLVR.UIMotion.Editor;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiVisualRootTests
    {
        [Test]
        public void IsControlledByLayout_DetectsParentLayoutGroup()
        {
            var parent = new GameObject("layout", typeof(RectTransform), typeof(VerticalLayoutGroup));
            var child = new GameObject("visual", typeof(RectTransform));
            child.transform.SetParent(parent.transform, false);
            try
            {
                Assert.That(UiVisualRoot.IsControlledByLayout((RectTransform)child.transform), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(child);
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void IsControlledByLayout_StandaloneRectTransform_IsSafe()
        {
            var target = new GameObject("visual", typeof(RectTransform));
            try
            {
                Assert.That(UiVisualRoot.IsControlledByLayout((RectTransform)target.transform), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void CreateAndMigrate_PreservesRectLayoutOrderAndSerializedReferences()
        {
            var owner = new GameObject("owner", typeof(RectTransform), typeof(Image), typeof(Button));
            var first = new GameObject("first", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var second = new GameObject("second", typeof(RectTransform));
            first.transform.SetParent(owner.transform, false);
            second.transform.SetParent(owner.transform, false);
            var firstRect = (RectTransform)first.transform;
            firstRect.anchorMin = new Vector2(0.15f, 0.2f);
            firstRect.anchorMax = new Vector2(0.75f, 0.8f);
            firstRect.pivot = new Vector2(0.25f, 0.65f);
            firstRect.sizeDelta = new Vector2(37f, 49f);
            firstRect.anchoredPosition = new Vector2(13f, -17f);
            Image referencedImage = first.GetComponent<Image>();
            owner.GetComponent<Button>().targetGraphic = referencedImage;

            try
            {
                GameObject visualRoot = UiVisualRootUtility.CreateAndMigrate(owner);

                Assert.That(visualRoot, Is.Not.Null);
                Assert.That(first.transform.parent, Is.SameAs(visualRoot.transform));
                Assert.That(second.transform.parent, Is.SameAs(visualRoot.transform));
                Assert.That(first.transform.GetSiblingIndex(), Is.EqualTo(0));
                Assert.That(second.transform.GetSiblingIndex(), Is.EqualTo(1));
                Assert.That(firstRect.anchorMin, Is.EqualTo(new Vector2(0.15f, 0.2f)));
                Assert.That(firstRect.anchorMax, Is.EqualTo(new Vector2(0.75f, 0.8f)));
                Assert.That(firstRect.pivot, Is.EqualTo(new Vector2(0.25f, 0.65f)));
                Assert.That(firstRect.sizeDelta, Is.EqualTo(new Vector2(37f, 49f)));
                Assert.That(firstRect.anchoredPosition, Is.EqualTo(new Vector2(13f, -17f)));
                Assert.That(owner.GetComponent<Button>().targetGraphic, Is.SameAs(referencedImage));
            }
            finally
            {
                Object.DestroyImmediate(owner);
            }
        }
    }
}
