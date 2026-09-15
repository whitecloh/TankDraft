using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace SLVR.UIMotion
{
    /// <summary>Modal dim layer that owns raycast blocking only while visible.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiModalBackdrop : UiMotionElement, IPointerClickHandler
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField, Range(0f, 1f)] private float shownAlpha = 0.72f;
        [SerializeField] private bool suppressBackgroundIdle = true;
        [SerializeField] private UnityEvent pressed;

        private IDisposable idleSuppression;

        private void Awake()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }

        public void Show()
        {
            EnsureReference();
            EnsureIdleSuppression();
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
            PlayAlpha(shownAlpha);
        }

        public void Hide()
        {
            EnsureReference();
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            PlayAlpha(0f, true);
        }

        public void ShowImmediate()
        {
            EnsureReference();
            EnsureIdleSuppression();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = shownAlpha;
            canvasGroup.blocksRaycasts = true;
            canvasGroup.interactable = true;
        }

        public void HideImmediate()
        {
            EnsureReference();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 0f;
            canvasGroup.blocksRaycasts = false;
            canvasGroup.interactable = false;
            ReleaseIdleSuppression();
        }

        private void OnDisable()
        {
            ReleaseIdleSuppression();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (canvasGroup != null && canvasGroup.blocksRaycasts) pressed?.Invoke();
        }

        private void PlayAlpha(float target, bool releaseSuppressionOnComplete = false)
        {
            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                canvasGroup.alpha = target;
                if (releaseSuppressionOnComplete) ReleaseIdleSuppression();
                return;
            }

            Tweener tween = UiMotionTweenFactory.Float(
                this,
                () => canvasGroup.alpha,
                value => canvasGroup.alpha = value,
                target,
                duration,
                ResolvePolicy());
            if (releaseSuppressionOnComplete) tween.OnComplete(ReleaseIdleSuppression);
            Lifecycle.Play(UiMotionChannel.Visibility, tween);
        }

        private void EnsureIdleSuppression()
        {
            if (!suppressBackgroundIdle || idleSuppression != null) return;
            idleSuppression = UiIdleMotionRuntime.BeginModalSuppression();
        }

        private void ReleaseIdleSuppression()
        {
            idleSuppression?.Dispose();
            idleSuppression = null;
        }

        private void EnsureReference()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        }
    }
}
