using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    /// <summary>Fires with the index that just finished settling into place.</summary>
    [System.Serializable]
    public sealed class UiCarouselIndexEvent : UnityEvent<int>
    {
    }

    /// <summary>Carousel with two allocation-stable presentation modes. The default horizontal snap
    /// mode shifts a content container so the selected item is centered and supports drag-follow
    /// with edge rubber band and snap-on-release. The optional constant three-slot mode keeps only
    /// previous/current/next views and recycles the outgoing card above the viewport.
    /// Optionally lifts/scales the selected item and drops/scales the rest once items are registered
    /// through <see cref="RegisterItems"/>. Translation defaults to zero and scale defaults to one,
    /// so both presentation layers are opt-in.</summary>
    [DisallowMultipleComponent]
    public sealed class UiCardCarouselMotion : UiMotionElement
    {
        [SerializeField] private RectTransform content;
        [SerializeField, Min(1f)] private float itemSpacing = 100f;
        [SerializeField, Min(0.05f)] private float snapDuration = 0.32f;
        [SerializeField, Range(0f, 0.5f)] private float rubberBandFactor = 0.25f;
        [SerializeField, Range(0.05f, 0.9f)] private float switchThresholdFraction = 0.3f;
        [SerializeField, Min(0f)] private float flickVelocityThreshold = 800f;
        [SerializeField] private UiCarouselIndexEvent indexSettled;

        [Header("Item Lift (optional)")]
        [SerializeField] private float selectedLiftY;
        [SerializeField] private float unselectedDropY;
        [SerializeField, Min(0.05f)] private float liftDuration = 0.24f;

        [Header("Item Scale (optional)")]
        [SerializeField, Min(0.01f)] private float selectedItemScale = 1f;
        [SerializeField, Min(0.01f)] private float unselectedItemScale = 1f;

        [Header("Looping Three-Slot Transition (optional)")]
        [SerializeField, Min(0.05f)] private float loopExitDuration = 0.24f;
        [SerializeField, Min(0.05f)] private float loopShiftDuration = 0.32f;
        [SerializeField, Min(0.05f)] private float loopEnterDuration = 0.28f;
        [SerializeField, Min(1f)] private float loopOffscreenOffsetY = 1200f;

        private bool baseCaptured;
        private float contentBaseX;
        private int itemCount;
        private int currentIndex;
        private bool isDragging;
        private float dragStartX;
        private float dragDeltaX;
        private readonly List<RectTransform> items = new List<RectTransform>();
        private readonly List<float> itemBaseY = new List<float>();
        private readonly List<Vector3> itemBaseScale = new List<Vector3>();
        private readonly List<RectTransform> loopingItems = new List<RectTransform>(3);
        private readonly Vector2[] loopingSlotPositions = new Vector2[3];
        private bool loopingSlotsConfigured;
        private bool loopTransitioning;

        /// <summary>Number of items registered through <see cref="SetItemCount"/>.</summary>
        public int ItemCount => itemCount;

        /// <summary>Index currently shown, or the index being animated toward.</summary>
        public int CurrentIndex => currentIndex;

        /// <summary>True between a <see cref="BeginDrag"/> call and the matching <see cref="EndDrag"/>.</summary>
        public bool IsDragging => isDragging;

        /// <summary>True while a configured three-slot carousel is recycling one of its cards.</summary>
        public bool IsLoopTransitioning => loopTransitioning;

        /// <summary>Spacing (local X units) between adjacent item positions; the single source of
        /// truth for callers that lay out a dynamic number of items themselves.</summary>
        public float ItemSpacing => itemSpacing;

        /// <summary>Updates the spacing used both by host layout and carousel snapping.</summary>
        public void SetItemSpacing(float spacing)
        {
            itemSpacing = Mathf.Max(1f, spacing);
        }

        /// <summary>Configures optional selected/unselected scale multipliers for registered items.</summary>
        public void SetItemScales(float selectedScale, float unselectedScale)
        {
            selectedItemScale = Mathf.Max(0.01f, selectedScale);
            unselectedItemScale = Mathf.Max(0.01f, unselectedScale);
        }

        /// <summary>Raised with the settled index whenever a snap finishes, including instant snaps.</summary>
        public UiCarouselIndexEvent IndexSettled => indexSettled ??= new UiCarouselIndexEvent();

        private void Awake()
        {
            CaptureBase();
        }

        private void OnDisable()
        {
            Lifecycle.Stop(UiMotionChannel.Custom0);
            isDragging = false;
            ApplyPosition(loopingSlotsConfigured ? contentBaseX : TargetX(currentIndex));

            Lifecycle.Stop(UiMotionChannel.Custom1);
            RestoreItemBaselines();

            Lifecycle.Stop(UiMotionChannel.Custom2);
            loopTransitioning = false;
            ApplyLoopingSlotPositions();
        }

        /// <summary>
        /// Switches this component to a constant three-view looping layout. The supplied transforms
        /// are ordered left, center, right. Their resting positions are derived from
        /// <see cref="ItemSpacing"/> and the existing selected/unselected lift values, so an authored
        /// snap carousel can migrate without replacing its component or duplicating timing settings.
        /// </summary>
        /// <param name="rectTransforms">Exactly three non-null views in spatial order.</param>
        /// <param name="centerPosition">Authored resting position of the center card.</param>
        /// <returns>False when the slot contract is invalid.</returns>
        public bool ConfigureLoopingSlots(
            IReadOnlyList<RectTransform> rectTransforms,
            Vector2 centerPosition)
        {
            Lifecycle.Stop(UiMotionChannel.Custom0);
            Lifecycle.Stop(UiMotionChannel.Custom1);
            Lifecycle.Stop(UiMotionChannel.Custom2);
            isDragging = false;
            loopTransitioning = false;

            loopingItems.Clear();
            if (rectTransforms == null || rectTransforms.Count != 3)
            {
                loopingSlotsConfigured = false;
                return false;
            }

            for (int i = 0; i < 3; i++)
            {
                RectTransform item = rectTransforms[i];
                if (item == null)
                {
                    loopingItems.Clear();
                    loopingSlotsConfigured = false;
                    return false;
                }

                loopingItems.Add(item);
            }

            CaptureBase();
            ApplyPosition(contentBaseX);

            loopingSlotPositions[0] = new Vector2(
                centerPosition.x - itemSpacing,
                centerPosition.y - unselectedDropY);
            loopingSlotPositions[1] = new Vector2(
                centerPosition.x,
                centerPosition.y + selectedLiftY);
            loopingSlotPositions[2] = new Vector2(
                centerPosition.x + itemSpacing,
                centerPosition.y - unselectedDropY);

            loopingSlotsConfigured = true;
            ApplyLoopingSlotPositions();
            return true;
        }

        /// <summary>Stops three-slot motion, restores its resting layout and returns this component
        /// to the default content snap/drag mode.</summary>
        public void ClearLoopingSlots()
        {
            Lifecycle.Stop(UiMotionChannel.Custom2);
            loopTransitioning = false;
            ApplyLoopingSlotPositions();
            loopingSlotsConfigured = false;
            loopingItems.Clear();
            ApplyPosition(TargetX(currentIndex));
        }

        /// <summary>
        /// Moves a constant three-card layout by one cyclic step. Direction <c>+1</c> advances:
        /// left exits upward, center/right shift left, then the recycled left view enters the right
        /// slot from above. Direction <c>-1</c> mirrors the same choreography. The recycle callback
        /// runs while the outgoing view is off-screen, which lets the host safely replace its data
        /// without a visible content pop.
        /// </summary>
        public bool PlayLoopStep(
            int direction,
            Action<RectTransform> rebindRecycled,
            Action completed = null)
        {
            if (!loopingSlotsConfigured || loopingItems.Count != 3 || loopTransitioning || direction == 0)
            {
                return false;
            }

            int step = direction > 0 ? 1 : -1;
            int outgoingIndex = step > 0 ? 0 : 2;
            int destinationIndex = step > 0 ? 2 : 0;
            RectTransform outgoing = loopingItems[outgoingIndex];

            UiMotionSettingsSnapshot settings = Settings;
            float exitDuration = UiMotionDefaults.ScaleDuration(loopExitDuration, settings.Intensity);
            float shiftDuration = UiMotionDefaults.ScaleDuration(loopShiftDuration, settings.Intensity);
            float enterDuration = UiMotionDefaults.ScaleDuration(loopEnterDuration, settings.Intensity);
            bool skipTween = settings.ResolveEffect(UiMotionEffectKind.Translation) !=
                UiMotionEffectKind.Translation ||
                exitDuration <= 0f || shiftDuration <= 0f || enterDuration <= 0f;

            Lifecycle.Stop(UiMotionChannel.Custom2);
            loopTransitioning = true;

            if (skipTween)
            {
                rebindRecycled?.Invoke(outgoing);
                RotateLoopingItems(step);
                ApplyLoopingSlotPositions();
                loopTransitioning = false;
                completed?.Invoke();
                return true;
            }

            float offscreenY = Mathf.Max(
                    loopingSlotPositions[0].y,
                    Mathf.Max(loopingSlotPositions[1].y, loopingSlotPositions[2].y)) +
                loopOffscreenOffsetY;

            UiMotionTweenPolicy shiftPolicy = ResolvePolicy(settings, UiMotionEase.InOutCubic);
            UiMotionTweenPolicy exitPolicy = ResolvePolicy(settings, UiMotionEase.InQuad);
            UiMotionTweenPolicy enterPolicy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, shiftPolicy);

            sequence.Append(CreatePositionTween(
                outgoing,
                new Vector2(outgoing.anchoredPosition.x, offscreenY),
                exitDuration,
                exitPolicy));

            if (step > 0)
            {
                sequence.Join(CreatePositionTween(
                    loopingItems[1], loopingSlotPositions[0], shiftDuration, shiftPolicy));
                sequence.Join(CreatePositionTween(
                    loopingItems[2], loopingSlotPositions[1], shiftDuration, shiftPolicy));
            }
            else
            {
                sequence.Join(CreatePositionTween(
                    loopingItems[1], loopingSlotPositions[2], shiftDuration, shiftPolicy));
                sequence.Join(CreatePositionTween(
                    loopingItems[0], loopingSlotPositions[1], shiftDuration, shiftPolicy));
            }

            sequence.AppendCallback(() =>
            {
                outgoing.anchoredPosition = new Vector2(
                    loopingSlotPositions[destinationIndex].x,
                    offscreenY);
                rebindRecycled?.Invoke(outgoing);
            });
            sequence.Append(CreatePositionTween(
                outgoing,
                loopingSlotPositions[destinationIndex],
                enterDuration,
                enterPolicy));
            sequence.OnComplete(() =>
            {
                RotateLoopingItems(step);
                ApplyLoopingSlotPositions();
                loopTransitioning = false;
                completed?.Invoke();
            });

            Lifecycle.Play(UiMotionChannel.Custom2, sequence);
            return true;
        }

        /// <summary>Registers the item RectTransforms driven by the optional lift/scale feedback.
        /// Captures each item's current anchored Y and local scale as its resting baseline, then
        /// immediately applies the selected presentation for <see cref="CurrentIndex"/>. Call after
        /// laying out/pooling items (and after
        /// <see cref="SetItemCount"/>), typically once per <c>Bind</c>.</summary>
        public void RegisterItems(IReadOnlyList<RectTransform> rectTransforms)
        {
            Lifecycle.Stop(UiMotionChannel.Custom1);
            RestoreItemBaselines();
            items.Clear();
            itemBaseY.Clear();
            itemBaseScale.Clear();

            if (rectTransforms != null)
            {
                for (int i = 0; i < rectTransforms.Count; i++)
                {
                    RectTransform rt = rectTransforms[i];
                    items.Add(rt);
                    itemBaseY.Add(rt != null ? rt.anchoredPosition.y : 0f);
                    itemBaseScale.Add(rt != null ? rt.localScale : Vector3.one);
                }
            }

            ApplyItemPresentation(currentIndex, true);
        }

        /// <summary>Sets how many items the carousel holds and clamps the current index into range.</summary>
        public void SetItemCount(int count)
        {
            itemCount = Mathf.Max(0, count);
            currentIndex = itemCount <= 0 ? 0 : Mathf.Clamp(currentIndex, 0, itemCount - 1);
        }

        /// <summary>Programmatically transitions to the given index, clamped to the valid range.
        /// Ignored while a drag is in progress.</summary>
        public void SnapTo(int index, bool immediate = false)
        {
            if (isDragging || content == null)
            {
                return;
            }

            CaptureBase();
            currentIndex = itemCount <= 0 ? 0 : Mathf.Clamp(index, 0, itemCount - 1);
            AnimateToIndex(currentIndex, immediate);
        }

        /// <summary>Call from the host's IBeginDragHandler.OnBeginDrag.</summary>
        public void BeginDrag()
        {
            if (content == null || loopingSlotsConfigured)
            {
                return;
            }

            CaptureBase();
            Lifecycle.Stop(UiMotionChannel.Custom0);
            isDragging = true;
            dragStartX = content.anchoredPosition.x;
            dragDeltaX = 0f;
        }

        /// <summary>Call from the host's drag handler with the cumulative pointer offset (in pixels)
        /// from the position at <see cref="BeginDrag"/>.</summary>
        public void Drag(float deltaX)
        {
            if (!isDragging || content == null)
            {
                return;
            }

            dragDeltaX = deltaX;
            ApplyPosition(ApplyRubberBand(dragStartX + deltaX));
        }

        /// <summary>Call from the host's IEndDragHandler.OnEndDrag with the release velocity (px/s).
        /// Snaps to the nearest index by drag distance, or flips one item on a fast flick below the
        /// distance threshold.</summary>
        public void EndDrag(float velocityX)
        {
            if (!isDragging)
            {
                return;
            }

            isDragging = false;
            currentIndex = ResolveTargetIndex(dragDeltaX, velocityX);
            AnimateToIndex(currentIndex, false);
        }

        /// <summary>Returns the clamped index that <see cref="EndDrag"/> would choose for the current
        /// drag and release velocity. Hosts can prepare transition state before reduced-motion
        /// settings make the subsequent settle callback synchronous.</summary>
        public int PreviewReleaseTargetIndex(float velocityX)
        {
            return isDragging ? ResolveTargetIndex(dragDeltaX, velocityX) : currentIndex;
        }

        private int ResolveTargetIndex(float deltaX, float velocityX)
        {
            if (itemCount <= 0)
            {
                return 0;
            }

            float fraction = itemSpacing > 0f ? deltaX / itemSpacing : 0f;
            int step;
            if (Mathf.Abs(fraction) >= switchThresholdFraction)
            {
                int distanceInItems = Mathf.Max(
                    1,
                    Mathf.FloorToInt(Mathf.Abs(fraction) + 0.5f));
                step = fraction > 0f ? -distanceInItems : distanceInItems;
            }
            else if (Mathf.Abs(velocityX) >= flickVelocityThreshold)
            {
                step = velocityX > 0f ? -1 : 1;
            }
            else
            {
                step = 0;
            }

            return Mathf.Clamp(currentIndex + step, 0, itemCount - 1);
        }

        private void AnimateToIndex(int index, bool immediate)
        {
            ApplyItemPresentation(index, immediate);

            if (content == null)
            {
                return;
            }

            CaptureBase();
            float target = TargetX(index);
            UiMotionSettingsSnapshot settings = Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(snapDuration, settings.Intensity);
            bool skipTween = immediate || resolvedDuration <= 0f ||
                settings.ResolveEffect(UiMotionEffectKind.Translation) != UiMotionEffectKind.Translation;

            Lifecycle.Stop(UiMotionChannel.Custom0);

            if (skipTween)
            {
                ApplyPosition(target);
                IndexSettled.Invoke(index);
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Tween tween = UiMotionTweenFactory.Float(
                this,
                () => content.anchoredPosition.x,
                value =>
                {
                    Vector2 position = content.anchoredPosition;
                    position.x = value;
                    content.anchoredPosition = position;
                },
                target,
                resolvedDuration,
                policy);
            tween.OnComplete(() => IndexSettled.Invoke(index));
            Lifecycle.Play(UiMotionChannel.Custom0, tween);
        }

        private float ApplyRubberBand(float rawX)
        {
            if (itemCount <= 0)
            {
                return rawX;
            }

            float maxX = contentBaseX;
            float minX = contentBaseX - (itemCount - 1) * itemSpacing;
            if (rawX > maxX)
            {
                return maxX + (rawX - maxX) * rubberBandFactor;
            }

            if (rawX < minX)
            {
                return minX + (rawX - minX) * rubberBandFactor;
            }

            return rawX;
        }

        private float TargetX(int index) => contentBaseX - index * itemSpacing;

        private void ApplyPosition(float x)
        {
            if (content == null)
            {
                return;
            }

            Vector2 position = content.anchoredPosition;
            position.x = x;
            content.anchoredPosition = position;
        }

        private void CaptureBase()
        {
            if (baseCaptured || content == null)
            {
                return;
            }

            contentBaseX = content.anchoredPosition.x;
            baseCaptured = true;
        }

        private void ApplyItemPresentation(int selectedIndex, bool immediate)
        {
            if (items.Count == 0)
            {
                return;
            }

            Lifecycle.Stop(UiMotionChannel.Custom1);

            bool usesTranslation = selectedLiftY != 0f || unselectedDropY != 0f;
            bool usesScale = !Mathf.Approximately(selectedItemScale, 1f) ||
                !Mathf.Approximately(unselectedItemScale, 1f);
            if (!usesTranslation && !usesScale)
            {
                RestoreItemBaselines();
                return;
            }

            UiMotionSettingsSnapshot settings = Settings;
            float resolvedDuration = UiMotionDefaults.ScaleDuration(liftDuration, settings.Intensity);
            bool animateTranslation = usesTranslation &&
                settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation;
            bool animateScale = usesScale &&
                settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            bool skipTween = immediate || resolvedDuration <= 0f || (!animateTranslation && !animateScale);

            if (skipTween)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    ApplyItemY(i, ResolveLiftTargetY(i, selectedIndex));
                    ApplyItemScale(i, ResolveScaleTarget(i, selectedIndex));
                }

                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            for (int i = 0; i < items.Count; i++)
            {
                RectTransform rt = items[i];
                if (rt == null)
                {
                    continue;
                }

                float target = ResolveLiftTargetY(i, selectedIndex);
                if (animateTranslation)
                {
                    sequence.Join(UiMotionTweenFactory.Float(
                        this,
                        () => rt.anchoredPosition.y,
                        value =>
                        {
                            Vector2 position = rt.anchoredPosition;
                            position.y = value;
                            rt.anchoredPosition = position;
                        },
                        target,
                        resolvedDuration,
                        policy));
                }
                else
                {
                    ApplyItemY(i, target);
                }

                Vector3 targetScale = ResolveScaleTarget(i, selectedIndex);
                if (animateScale)
                {
                    sequence.Join(UiMotionTweenFactory.Vector3(
                        this,
                        () => rt.localScale,
                        value => rt.localScale = value,
                        targetScale,
                        resolvedDuration,
                        policy));
                }
                else
                {
                    ApplyItemScale(i, targetScale);
                }
            }

            Lifecycle.Play(UiMotionChannel.Custom1, sequence);
        }

        private float ResolveLiftTargetY(int itemIndex, int selectedIndex)
        {
            float baseY = itemIndex < itemBaseY.Count ? itemBaseY[itemIndex] : 0f;
            return itemIndex == selectedIndex ? baseY + selectedLiftY : baseY - unselectedDropY;
        }

        private void ApplyItemY(int itemIndex, float y)
        {
            RectTransform rt = items[itemIndex];
            if (rt == null)
            {
                return;
            }

            Vector2 position = rt.anchoredPosition;
            position.y = y;
            rt.anchoredPosition = position;
        }

        private Vector3 ResolveScaleTarget(int itemIndex, int selectedIndex)
        {
            Vector3 baseScale = itemIndex < itemBaseScale.Count ? itemBaseScale[itemIndex] : Vector3.one;
            return baseScale * (itemIndex == selectedIndex ? selectedItemScale : unselectedItemScale);
        }

        private void ApplyItemScale(int itemIndex, Vector3 scale)
        {
            RectTransform rt = items[itemIndex];
            if (rt != null)
                rt.localScale = scale;
        }

        private void RestoreItemBaselines()
        {
            for (int i = 0; i < items.Count; i++)
            {
                if (i < itemBaseY.Count)
                    ApplyItemY(i, itemBaseY[i]);
                if (i < itemBaseScale.Count)
                    ApplyItemScale(i, itemBaseScale[i]);
            }
        }

        private Tween CreatePositionTween(
            RectTransform target,
            Vector2 destination,
            float duration,
            UiMotionTweenPolicy policy)
        {
            return UiMotionTweenFactory.Vector3(
                this,
                () => target.anchoredPosition,
                value => target.anchoredPosition = value,
                destination,
                duration,
                policy);
        }

        private void RotateLoopingItems(int step)
        {
            if (step > 0)
            {
                RectTransform outgoing = loopingItems[0];
                loopingItems[0] = loopingItems[1];
                loopingItems[1] = loopingItems[2];
                loopingItems[2] = outgoing;
                return;
            }

            RectTransform reverseOutgoing = loopingItems[2];
            loopingItems[2] = loopingItems[1];
            loopingItems[1] = loopingItems[0];
            loopingItems[0] = reverseOutgoing;
        }

        private void ApplyLoopingSlotPositions()
        {
            if (!loopingSlotsConfigured || loopingItems.Count != 3)
            {
                return;
            }

            for (int i = 0; i < 3; i++)
            {
                RectTransform item = loopingItems[i];
                if (item != null)
                {
                    item.anchoredPosition = loopingSlotPositions[i];
                }
            }
        }
    }
}
