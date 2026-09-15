using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiHoverLiftMotionTests
    {
        [UnityTest]
        public IEnumerator MouseHover_ScalesUpAndReturnsToAuthoredScale()
        {
            EventSystem eventSystem = CreateEventSystem(out GameObject eventSystemObject);
            var card = new GameObject("card", typeof(RectTransform), typeof(UiHoverLiftMotion));
            try
            {
                yield return null;
                UiHoverLiftMotion motion = card.GetComponent<UiHoverLiftMotion>();
                Vector3 authoredScale = card.transform.localScale;
                var pointer = new PointerEventData(eventSystem) { pointerId = -1 };

                motion.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(card.transform.localScale.x, Is.GreaterThan(authoredScale.x * 1.02f));

                motion.OnPointerExit(pointer);
                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(card.transform.localScale, Is.EqualTo(authoredScale));
            }
            finally
            {
                Object.Destroy(card);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator InputSystemStylePointer_DistinguishesPositiveMouseIdFromTouch()
        {
            EventSystem eventSystem = CreateEventSystem(out GameObject eventSystemObject);
            var card = new GameObject("card", typeof(RectTransform), typeof(UiHoverLiftMotion));
            try
            {
                yield return null;
                UiHoverLiftMotion motion = card.GetComponent<UiHoverLiftMotion>();
                Vector3 authoredScale = card.transform.localScale;
                var mouse = new ExtendedLikePointerEventData(eventSystem)
                {
                    pointerId = 17,
                    touchId = 0,
                };

                motion.OnPointerEnter(mouse);
                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(motion.IsHovered, Is.True);
                Assert.That(card.transform.localScale.x, Is.GreaterThan(authoredScale.x * 1.02f));

                motion.OnPointerExit(mouse);
                yield return new WaitForSecondsRealtime(0.30f);

                var touch = new ExtendedLikePointerEventData(eventSystem)
                {
                    pointerId = (17 << 24) + 1,
                    touchId = 1,
                };
                motion.OnPointerEnter(touch);
                yield return new WaitForSecondsRealtime(0.05f);
                Assert.That(motion.IsHovered, Is.False);
                Assert.That(card.transform.localScale, Is.EqualTo(authoredScale));
            }
            finally
            {
                Object.Destroy(card);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ScaleWave_KeepsHitAreaStableAndReturnsWithoutSnapping()
        {
            EventSystem eventSystem = CreateEventSystem(out GameObject eventSystemObject);
            var card = new GameObject("card", typeof(RectTransform), typeof(UiHoverLiftMotion));
            try
            {
                RectTransform cardRect = card.GetComponent<RectTransform>();
                cardRect.sizeDelta = new Vector2(680f, 1020f);
                cardRect.anchoredPosition = new Vector2(12f, 60f);
                yield return null;

                UiHoverLiftMotion motion = card.GetComponent<UiHoverLiftMotion>();
                motion.ConfigureScaleWave(cardRect, 1.015f, 0.01f, 0.4f);
                Vector3 authoredScale = cardRect.localScale;
                Vector2 authoredPosition = cardRect.anchoredPosition;
                Vector3 authoredEulerAngles = cardRect.localEulerAngles;
                var pointer = new PointerEventData(eventSystem)
                {
                    pointerId = -1,
                    position = new Vector2(1000f, 700f),
                };

                motion.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(0.35f);
                Assert.That(cardRect.localScale.x, Is.GreaterThan(authoredScale.x * 1.014f));

                motion.OnPointerMove(pointer);
                Assert.That(cardRect.anchoredPosition, Is.EqualTo(authoredPosition));
                Assert.That(cardRect.localEulerAngles, Is.EqualTo(authoredEulerAngles));

                Vector3 scaleBeforeExit = cardRect.localScale;
                motion.OnPointerExit(pointer);
                Assert.That(cardRect.localScale, Is.EqualTo(scaleBeforeExit),
                    "Exit must start from the currently rendered wave scale instead of snapping.");

                yield return new WaitForSecondsRealtime(0.30f);
                Assert.That(cardRect.localScale, Is.EqualTo(authoredScale));
                Assert.That(cardRect.anchoredPosition, Is.EqualTo(authoredPosition));
                Assert.That(cardRect.localEulerAngles, Is.EqualTo(authoredEulerAngles));
            }
            finally
            {
                Object.Destroy(card);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator ScaleOnly_DoesNotRestorePositionOwnedByLayout()
        {
            EventSystem eventSystem = CreateEventSystem(out GameObject eventSystemObject);
            var card = new GameObject("layout-card", typeof(RectTransform), typeof(UiHoverLiftMotion));
            try
            {
                RectTransform cardRect = card.GetComponent<RectTransform>();
                UiHoverLiftMotion motion = card.GetComponent<UiHoverLiftMotion>();
                motion.ConfigureScaleOnly(cardRect, 1.03f);
                Vector3 authoredScale = cardRect.localScale;
                Vector2 layoutPosition = new Vector2(340f, -72f);
                cardRect.anchoredPosition = layoutPosition;
                yield return null;

                var pointer = new PointerEventData(eventSystem) { pointerId = -1 };
                motion.OnPointerEnter(pointer);
                yield return new WaitForSecondsRealtime(0.2f);

                Assert.That(cardRect.localScale.x, Is.GreaterThan(authoredScale.x * 1.02f));
                Assert.That(cardRect.anchoredPosition, Is.EqualTo(layoutPosition));

                motion.OnPointerExit(pointer);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(cardRect.localScale, Is.EqualTo(authoredScale));
                Assert.That(cardRect.anchoredPosition, Is.EqualTo(layoutPosition));
            }
            finally
            {
                Object.Destroy(card);
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
