using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiAnimatedButtonTests
    {
        [UnityTest]
        public IEnumerator VisualFeedback_DoesNotDuplicateButtonClick()
        {
            EventSystem eventSystem = GetEventSystem(out GameObject eventSystemObject);
            var buttonObject = new GameObject(
                "button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UiAnimatedButton));
            Button button = buttonObject.GetComponent<Button>();
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);
            var eventData = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = -1,
            };

            ExecuteEvents.Execute(buttonObject, eventData, ExecuteEvents.pointerDownHandler);
            Assert.That(clickCount, Is.Zero);
            ExecuteEvents.Execute(buttonObject, eventData, ExecuteEvents.pointerUpHandler);
            ExecuteEvents.Execute(buttonObject, eventData, ExecuteEvents.pointerClickHandler);
            yield return null;

            Assert.That(clickCount, Is.EqualTo(1));
            Object.Destroy(buttonObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }

        [UnityTest]
        public IEnumerator SemanticStates_HandleCancellationTouchSubmitAndDisabled()
        {
            EventSystem eventSystem = GetEventSystem(out GameObject eventSystemObject);
            var buttonObject = new GameObject(
                "button",
                typeof(RectTransform),
                typeof(CanvasGroup),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UiAnimatedButton));
            var focusObject = new GameObject("focus", typeof(RectTransform), typeof(CanvasGroup));
            focusObject.transform.SetParent(buttonObject.transform, false);
            Button button = buttonObject.GetComponent<Button>();
            UiAnimatedButton motion = buttonObject.GetComponent<UiAnimatedButton>();
            CanvasGroup stateGroup = buttonObject.GetComponent<CanvasGroup>();
            CanvasGroup focusRing = focusObject.GetComponent<CanvasGroup>();
            motion.ConfigureOptionalVisuals(stateGroup, focusRing);
            var events = new List<UiButtonSemanticEventKind>();
            motion.SemanticFeedback.AddListener(events.Add);
            int clickCount = 0;
            button.onClick.AddListener(() => clickCount++);
            yield return null;

            var mouse = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = -1,
            };
            ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerEnterHandler);
            ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerExitHandler);
            Assert.That(motion.IsHovered, Is.False);
            Assert.That(motion.IsPressed, Is.False);
            Assert.That(events, Does.Contain(UiButtonSemanticEventKind.Hover));
            Assert.That(events, Does.Contain(UiButtonSemanticEventKind.Press));
            Assert.That(events, Does.Contain(UiButtonSemanticEventKind.Release));
            Assert.That(events, Has.None.EqualTo(UiButtonSemanticEventKind.Confirm));

            int eventCount = events.Count;
            var touch = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = 0,
            };
            ExecuteEvents.Execute(buttonObject, touch, ExecuteEvents.pointerEnterHandler);
            Assert.That(events.Count, Is.EqualTo(eventCount));
            Assert.That(motion.IsHovered, Is.False);

            ExecuteEvents.Execute(buttonObject, new BaseEventData(eventSystem), ExecuteEvents.selectHandler);
            motion.RefreshState(true);
            Assert.That(motion.CurrentState, Is.EqualTo(UiAnimatedButtonState.Focused));
            Assert.That(focusRing.alpha, Is.EqualTo(1f));
            ExecuteEvents.Execute(buttonObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
            Assert.That(events, Does.Contain(UiButtonSemanticEventKind.Focus));
            Assert.That(events, Does.Contain(UiButtonSemanticEventKind.Confirm));
            Assert.That(clickCount, Is.EqualTo(1));

            int beforeDisabled = events.Count;
            button.interactable = false;
            motion.RefreshState(true);
            Assert.That(motion.CurrentState, Is.EqualTo(UiAnimatedButtonState.Disabled));
            Assert.That(stateGroup.alpha, Is.EqualTo(1f),
                "Disabled buttons keep their authored opacity while input remains blocked.");
            Assert.That(focusRing.alpha, Is.Zero);
            ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute(buttonObject, new BaseEventData(eventSystem), ExecuteEvents.submitHandler);
            Assert.That(events.Count, Is.EqualTo(beforeDisabled));
            Assert.That(clickCount, Is.EqualTo(1));

            Object.Destroy(buttonObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }

        [UnityTest]
        public IEnumerator RapidPressReplacement_EndsInValidState()
        {
            EventSystem eventSystem = GetEventSystem(out GameObject eventSystemObject);
            var buttonObject = new GameObject(
                "button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UiAnimatedButton));
            UiAnimatedButton motion = buttonObject.GetComponent<UiAnimatedButton>();
            var data = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = -1,
            };
            yield return null;

            for (int i = 0; i < 5; i++)
            {
                ExecuteEvents.Execute(buttonObject, data, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(buttonObject, data, ExecuteEvents.pointerUpHandler);
            }

            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(motion.IsPressed, Is.False);
            Assert.That(motion.CurrentState, Is.Not.EqualTo(UiAnimatedButtonState.Pressed));
            Assert.That(motion.CurrentState, Is.Not.EqualTo(UiAnimatedButtonState.Disabled));
            Assert.That(buttonObject.GetComponent<UiMotionLifecycle>().ActiveChannelCount, Is.LessThanOrEqualTo(2));
            Object.Destroy(buttonObject);
            if (eventSystemObject != null) Object.Destroy(eventSystemObject);
        }

        [UnityTest]
        public IEnumerator InputSystemStylePositiveMouseId_StillProducesHover()
        {
            EventSystem eventSystem = GetEventSystem(out GameObject eventSystemObject);
            var buttonObject = new GameObject(
                "button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UiAnimatedButton));
            try
            {
                UiAnimatedButton motion = buttonObject.GetComponent<UiAnimatedButton>();
                yield return null;
                var mouse = new ExtendedLikePointerEventData(eventSystem)
                {
                    pointerId = 21,
                    touchId = 0,
                };

                motion.OnPointerEnter(mouse);
                Assert.That(motion.IsHovered, Is.True);

                motion.OnPointerExit(mouse);
                Assert.That(motion.IsHovered, Is.False);
            }
            finally
            {
                Object.Destroy(buttonObject);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator DisabledButton_CanKeepHoverScaleWithoutAcceptingClicks()
        {
            EventSystem eventSystem = GetEventSystem(out GameObject eventSystemObject);
            var buttonObject = new GameObject(
                "disabled-hover-button",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(Image),
                typeof(Button),
                typeof(UiAnimatedButton));
            try
            {
                Button button = buttonObject.GetComponent<Button>();
                UiAnimatedButton motion = buttonObject.GetComponent<UiAnimatedButton>();
                int clickCount = 0;
                button.onClick.AddListener(() => clickCount++);
                motion.SetHoverWhenDisabled(true);
                button.interactable = false;
                motion.RefreshState(true);
                Vector3 authoredScale = buttonObject.transform.localScale;
                yield return null;

                var mouse = new PointerEventData(eventSystem)
                {
                    button = PointerEventData.InputButton.Left,
                    pointerId = -1,
                };
                ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerEnterHandler);
                yield return new WaitForSecondsRealtime(0.2f);

                Assert.That(motion.IsHovered, Is.True);
                Assert.That(motion.CurrentState, Is.EqualTo(UiAnimatedButtonState.Disabled));
                Assert.That(buttonObject.transform.localScale.x, Is.GreaterThan(authoredScale.x * 1.01f));

                ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerClickHandler);
                Assert.That(clickCount, Is.Zero);

                ExecuteEvents.Execute(buttonObject, mouse, ExecuteEvents.pointerExitHandler);
                yield return new WaitForSecondsRealtime(0.2f);
                Assert.That(buttonObject.transform.localScale, Is.EqualTo(authoredScale));
            }
            finally
            {
                Object.Destroy(buttonObject);
                if (eventSystemObject != null) Object.Destroy(eventSystemObject);
            }

            yield return null;
        }

        private static EventSystem GetEventSystem(out GameObject createdObject)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                createdObject = new GameObject("event-system", typeof(EventSystem));
                eventSystem = createdObject.GetComponent<EventSystem>();
            }
            else
            {
                createdObject = null;
                eventSystem.SetSelectedGameObject(null);
            }

            return eventSystem;
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
