using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiTooltipMotionTests
    {
        [UnityTest]
        public IEnumerator InputSystemStylePointer_ShowsForPositiveMouseIdButNotTouch()
        {
            EventSystem eventSystem = CreateEventSystem(out GameObject eventSystemObject);
            var canvasObject = new GameObject("canvas-root", typeof(RectTransform));
            var owner = new GameObject("owner", typeof(RectTransform), typeof(UiTooltipMotion));
            var popupObject = new GameObject("popup", typeof(RectTransform), typeof(CanvasGroup));
            try
            {
                RectTransform canvasRoot = canvasObject.GetComponent<RectTransform>();
                canvasRoot.sizeDelta = new Vector2(1000f, 1000f);
                popupObject.transform.SetParent(canvasObject.transform, false);
                CanvasGroup popup = popupObject.GetComponent<CanvasGroup>();
                popup.alpha = 0f;
                UiTooltipMotion motion = owner.GetComponent<UiTooltipMotion>();
                motion.Configure(popup, popupObject.GetComponent<RectTransform>(), canvasRoot);
                yield return null;

                var mouse = new ExtendedLikePointerEventData(eventSystem)
                {
                    pointerId = 31,
                    touchId = 0,
                    position = new Vector2(100f, 100f),
                };
                motion.OnPointerEnter(mouse);
                yield return new WaitForSecondsRealtime(0.65f);
                Assert.That(motion.IsVisible, Is.True);
                Assert.That(popup.alpha, Is.EqualTo(1f).Within(0.01f));

                motion.OnPointerExit(mouse);
                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(motion.IsVisible, Is.False);
                Assert.That(popup.alpha, Is.EqualTo(0f).Within(0.01f));

                var touch = new ExtendedLikePointerEventData(eventSystem)
                {
                    pointerId = (31 << 24) + 1,
                    touchId = 1,
                    position = new Vector2(100f, 100f),
                };
                motion.OnPointerEnter(touch);
                yield return new WaitForSecondsRealtime(0.45f);
                Assert.That(motion.IsVisible, Is.False);
                Assert.That(popup.alpha, Is.EqualTo(0f).Within(0.01f));
            }
            finally
            {
                Object.Destroy(owner);
                Object.Destroy(canvasObject);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        private static EventSystem CreateEventSystem(out GameObject createdObject)
        {
            if (EventSystem.current != null)
            {
                createdObject = null;
                return EventSystem.current;
            }

            createdObject = new GameObject("event-system", typeof(EventSystem));
            return createdObject.GetComponent<EventSystem>();
        }

        private sealed class ExtendedLikePointerEventData : PointerEventData
        {
            public ExtendedLikePointerEventData(EventSystem eventSystem) : base(eventSystem)
            {
            }

            public int touchId { get; set; }
        }
    }
}
