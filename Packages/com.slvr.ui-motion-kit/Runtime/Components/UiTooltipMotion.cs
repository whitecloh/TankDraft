using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SLVR.UIMotion
{
    /// <summary>Hover/focus/touch-owned tooltip with delay and canvas-bound positioning.</summary>
    public sealed class UiTooltipMotion : UiMotionElement,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerMoveHandler,
        ISelectHandler,
        IDeselectHandler
    {
        [SerializeField] private CanvasGroup popup;
        [SerializeField] private RectTransform popupRoot;
        [SerializeField] private RectTransform canvasRoot;
        [SerializeField, Min(0f)] private float hoverDelay = 0.35f;
        [SerializeField] private Vector2 screenOffset = new Vector2(12f, 12f);
        [SerializeField] private bool followPointer;
        [SerializeField, Range(0.5f, 1f)] private float hiddenScale = 0.92f;

        private bool visible;
        private bool pointerHoverActive;
        private Vector2 lastScreenPosition;
        private Camera lastCamera;
        private Vector3 shownScale = Vector3.one;
        private bool shownScaleCaptured;

        public bool IsVisible => visible;

        public void Configure(CanvasGroup popupGroup, RectTransform popupTransform, RectTransform canvasTransform)
        {
            popup = popupGroup;
            popupRoot = popupTransform;
            canvasRoot = canvasTransform;
            CaptureShownScale();
            if (popup != null)
            {
                popup.blocksRaycasts = false;
                popup.interactable = false;
            }
        }

        private void Awake()
        {
            if (popupRoot == null && popup != null) popupRoot = popup.transform as RectTransform;
            CaptureShownScale();
            if (popup != null)
            {
                popup.blocksRaycasts = false;
                popup.interactable = false;
                popup.alpha = 0f;
            }

            ApplyPopupScale(false);
        }

        private void OnDisable()
        {
            pointerHoverActive = false;
            visible = false;
            if (popup != null) popup.alpha = 0f;
            ApplyPopupScale(false);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (UiPointerDeviceUtility.IsTouch(eventData)) return;
            pointerHoverActive = true;
            ScheduleShow(eventData.position, eventData.enterEventCamera);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!pointerHoverActive) return;
            pointerHoverActive = false;
            Hide();
        }

        public void OnPointerMove(PointerEventData eventData)
        {
            if (!followPointer || !pointerHoverActive || eventData == null) return;
            lastScreenPosition = eventData.position;
            lastCamera = eventData.enterEventCamera;
            if (visible) PositionAt(lastScreenPosition, lastCamera);
        }

        public void OnSelect(BaseEventData eventData)
        {
            ShowForFocus();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            Hide();
        }

        public void ShowForFocus()
        {
            if (transform is RectTransform owner)
            {
                Vector3 world = owner.TransformPoint(owner.rect.center);
                Camera camera = canvasRoot != null && canvasRoot.GetComponentInParent<Canvas>()?.renderMode != RenderMode.ScreenSpaceOverlay
                    ? canvasRoot.GetComponentInParent<Canvas>()?.worldCamera
                    : null;
                ShowAt(RectTransformUtility.WorldToScreenPoint(camera, world), camera);
            }
        }

        /// <summary>Touch-owned API. The host decides when a long-press should show the tooltip.</summary>
        public void ShowAt(Vector2 screenPosition, Camera eventCamera = null)
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            lastScreenPosition = screenPosition;
            lastCamera = eventCamera;
            visible = true;
            PositionAt(screenPosition, eventCamera);
            AnimateVisibility(true);
        }

        public void Hide()
        {
            visible = false;
            AnimateVisibility(false);
        }

        private void ScheduleShow(Vector2 screenPosition, Camera eventCamera)
        {
            lastScreenPosition = screenPosition;
            lastCamera = eventCamera;
            if (popup != null) popup.alpha = 0f;
            ApplyPopupScale(false);
            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.AppendInterval(hoverDelay);
            sequence.AppendCallback(() =>
            {
                visible = true;
                PositionAt(lastScreenPosition, lastCamera);
            });
            if (popup != null)
            {
                float duration = ResolveDuration(UiMotionTiming.Fast);
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => popup.alpha,
                    x => popup.alpha = x,
                    1f,
                    duration,
                    policy));
                AppendScale(sequence, true, duration, policy);
            }
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        private void AnimateVisibility(bool show)
        {
            if (popup == null) return;
            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                popup.alpha = show ? 1f : 0f;
                ApplyPopupScale(show);
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(show ? UiMotionEase.OutBack : UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Join(UiMotionTweenFactory.Float(
                this,
                () => popup.alpha,
                x => popup.alpha = x,
                show ? 1f : 0f,
                duration,
                policy));
            AppendScale(sequence, show, duration, policy);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        private void AppendScale(
            Sequence sequence,
            bool show,
            float duration,
            UiMotionTweenPolicy policy)
        {
            if (popupRoot == null)
            {
                return;
            }

            UiMotionEffectKind resolved = Settings.ResolveEffect(UiMotionEffectKind.Scale);
            if (resolved != UiMotionEffectKind.Scale)
            {
                popupRoot.localScale = shownScale;
                return;
            }

            sequence.Join(UiMotionTweenFactory.Vector3(
                this,
                () => popupRoot.localScale,
                value => popupRoot.localScale = value,
                show ? shownScale : shownScale * hiddenScale,
                duration,
                policy));
        }

        private void CaptureShownScale()
        {
            if (shownScaleCaptured || popupRoot == null)
            {
                return;
            }

            shownScale = popupRoot.localScale;
            shownScaleCaptured = true;
        }

        private void ApplyPopupScale(bool show)
        {
            CaptureShownScale();
            if (popupRoot != null)
            {
                popupRoot.localScale = show ? shownScale : shownScale * hiddenScale;
            }
        }

        private void PositionAt(Vector2 screenPosition, Camera eventCamera)
        {
            if (popupRoot == null || canvasRoot == null) return;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRoot, screenPosition + screenOffset, eventCamera, out Vector2 local)) return;
            popupRoot.anchoredPosition = local;
            ClampToCanvas();
        }

        private void ClampToCanvas()
        {
            Vector2 position = popupRoot.anchoredPosition;
            Rect bounds = canvasRoot.rect;
            Rect tooltip = popupRoot.rect;
            position.x = Mathf.Clamp(position.x, bounds.xMin - tooltip.xMin, bounds.xMax - tooltip.xMax);
            position.y = Mathf.Clamp(position.y, bounds.yMin - tooltip.yMin, bounds.yMax - tooltip.yMax);
            popupRoot.anchoredPosition = position;
        }
    }
}
