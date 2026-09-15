using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    [Serializable]
    public sealed class UiListStaggerEntry
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform visualRoot;

        public CanvasGroup CanvasGroup => canvasGroup;
        public RectTransform VisualRoot => visualRoot != null
            ? visualRoot
            : canvasGroup != null ? canvasGroup.transform as RectTransform : null;

        public UiListStaggerEntry()
        {
        }

        public UiListStaggerEntry(CanvasGroup canvasGroup)
        {
            this.canvasGroup = canvasGroup;
            visualRoot = canvasGroup != null ? canvasGroup.transform as RectTransform : null;
        }
    }

    /// <summary>Deterministic, capped stagger entrance for explicit or direct-child items.</summary>
    public sealed class UiListStagger : UiMotionElement
    {
        [SerializeField] private RectTransform itemRoot;
        [SerializeField] private RectTransform viewport;
        [SerializeField] private List<UiListStaggerEntry> items = new List<UiListStaggerEntry>();
        [SerializeField] private bool autoCollectDirectChildren;
        [SerializeField] private bool visibleItemsOnly = true;
        [SerializeField, Min(0f)] private float itemDelay = 0.035f;
        [SerializeField, Min(0f)] private float maxTotalDelay = 0.28f;
        [SerializeField] private Vector2 entranceOffset = new Vector2(0f, -16f);
        [SerializeField, Range(0.5f, 1f)] private float hiddenScale = 0.94f;

        private readonly List<UiListStaggerEntry> runtimeItems = new List<UiListStaggerEntry>(16);
        private readonly List<Vector2> shownPositions = new List<Vector2>(16);
        private readonly List<Vector3> shownScales = new List<Vector3>(16);
        private readonly Dictionary<RectTransform, Vector2> basePositions = new Dictionary<RectTransform, Vector2>(16);
        private readonly Dictionary<RectTransform, Vector3> baseScales = new Dictionary<RectTransform, Vector3>(16);

        public void SetItems(IEnumerable<CanvasGroup> value)
        {
            // A layout-driven host can reposition pooled items between binds. Restore the previous
            // entrance state before replacing the list, then discard its geometry so the next
            // Play captures positions produced by the host's current layout pass.
            ResetImmediate(true);
            items.Clear();
            basePositions.Clear();
            baseScales.Clear();
            if (value == null) return;
            foreach (CanvasGroup group in value)
            {
                if (group != null) items.Add(new UiListStaggerEntry(group));
            }
            autoCollectDirectChildren = false;
        }

        public void Play()
        {
            CollectItems();
            if (runtimeItems.Count == 0) return;

            UiMotionTweenPolicy policy = ResolvePolicy();
            float duration = ResolveDuration(UiMotionTiming.Normal);
            if (duration <= 0f)
            {
                ResetImmediate(true);
                return;
            }

            float delay = Mathf.Min(itemDelay, maxTotalDelay / Mathf.Max(1, runtimeItems.Count - 1));
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            UiMotionEffectKind translation = Settings.ResolveEffect(UiMotionEffectKind.Translation);
            UiMotionEffectKind scale = Settings.ResolveEffect(UiMotionEffectKind.Scale);
            for (int i = 0; i < runtimeItems.Count; i++)
            {
                UiListStaggerEntry entry = runtimeItems[i];
                CanvasGroup group = entry.CanvasGroup;
                RectTransform root = entry.VisualRoot;
                if (group == null || root == null || (visibleItemsOnly && !IsVisible(root))) continue;

                Vector2 shown = shownPositions[i];
                Vector3 shownScale = shownScales[i];
                group.alpha = 0f;
                if (translation == UiMotionEffectKind.Translation) root.anchoredPosition = shown + entranceOffset;
                if (scale == UiMotionEffectKind.Scale) root.localScale = shownScale * hiddenScale;
                float at = i * delay;
                sequence.Insert(at, UiMotionTweenFactory.Float(
                    this, () => group.alpha, x => group.alpha = x, 1f, duration, policy));
                if (translation == UiMotionEffectKind.Translation)
                {
                    sequence.Insert(at, UiMotionTweenFactory.Vector3(
                        this, () => root.anchoredPosition, x => root.anchoredPosition = x, shown, duration, policy));
                }
                if (scale == UiMotionEffectKind.Scale)
                {
                    sequence.Insert(at, UiMotionTweenFactory.Vector3(
                        this, () => root.localScale, x => root.localScale = x, shownScale, duration, policy));
                }
            }
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void ResetImmediate(bool visible)
        {
            CollectItems();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            for (int i = 0; i < runtimeItems.Count; i++)
            {
                UiListStaggerEntry entry = runtimeItems[i];
                if (entry.CanvasGroup != null) entry.CanvasGroup.alpha = visible ? 1f : 0f;
                if (entry.VisualRoot != null) entry.VisualRoot.anchoredPosition = shownPositions[i];
                if (entry.VisualRoot != null) entry.VisualRoot.localScale = shownScales[i];
            }
        }

        private void CollectItems()
        {
            runtimeItems.Clear();
            shownPositions.Clear();
            shownScales.Clear();
            if (autoCollectDirectChildren && itemRoot != null)
            {
                for (int i = 0; i < itemRoot.childCount; i++)
                {
                    CanvasGroup group = itemRoot.GetChild(i).GetComponent<CanvasGroup>();
                    if (group == null) continue;
                    runtimeItems.Add(new UiListStaggerEntry(group));
                }
            }
            else
            {
                runtimeItems.AddRange(items);
            }

            for (int i = 0; i < runtimeItems.Count; i++)
            {
                RectTransform root = runtimeItems[i]?.VisualRoot;
                if (root != null && !basePositions.ContainsKey(root)) basePositions.Add(root, root.anchoredPosition);
                if (root != null && !baseScales.ContainsKey(root)) baseScales.Add(root, root.localScale);
                shownPositions.Add(root != null ? basePositions[root] : Vector2.zero);
                shownScales.Add(root != null ? baseScales[root] : Vector3.one);
            }
        }

        private bool IsVisible(RectTransform item)
        {
            if (viewport == null) return true;
            Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, item);
            Bounds viewportBounds = new Bounds(viewport.rect.center, viewport.rect.size);
            return viewportBounds.Intersects(bounds);
        }

    }
}
