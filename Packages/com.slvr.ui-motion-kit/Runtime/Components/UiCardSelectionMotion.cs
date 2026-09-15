using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

namespace SLVR.UIMotion
{
    [System.Serializable]
    public sealed class UiRarityEvent : UnityEvent<int>
    {
    }

    /// <summary>Selection, locked and rarity feedback for reusable card views.</summary>
    public sealed class UiCardSelectionMotion : UiMotionElement
    {
        [SerializeField] private RectTransform visualRoot;
        [SerializeField] private CanvasGroup selectionVisual;
        [SerializeField] private CanvasGroup lockedVisual;
        [SerializeField, Min(1f)] private float selectedScale = 1.035f;
        [SerializeField] private UiRarityEvent rarityChanged;

        private Vector3 baseScale = Vector3.one;
        private bool selected;
        private bool locked;

        public bool IsSelected => selected;
        public bool IsLocked => locked;

        private void Awake()
        {
            if (visualRoot == null) visualRoot = transform as RectTransform;
            if (visualRoot != null) baseScale = visualRoot.localScale;
        }

        public void SetSelected(bool value, bool immediate = false)
        {
            selected = value && !locked;
            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (immediate || duration <= 0f)
            {
                ApplyImmediate();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            if (selectionVisual != null)
            {
                sequence.Join(UiMotionTweenFactory.Float(
                    this, () => selectionVisual.alpha, x => selectionVisual.alpha = x, selected ? 1f : 0f, duration, policy));
            }
            if (visualRoot != null && Settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale)
            {
                sequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => visualRoot.localScale,
                    x => visualRoot.localScale = x,
                    baseScale * (selected ? selectedScale : 1f),
                    duration,
                    policy));
            }
            Lifecycle.Play(UiMotionChannel.Selection, sequence);
        }

        public void SetLocked(bool value)
        {
            locked = value;
            if (locked) selected = false;
            if (lockedVisual != null) lockedVisual.alpha = locked ? 1f : 0f;
            ApplyImmediate();
        }

        public void SetRarity(int rarity)
        {
            rarityChanged?.Invoke(rarity);
        }

        public void ApplyImmediate()
        {
            Lifecycle.Stop(UiMotionChannel.Selection);
            if (selectionVisual != null) selectionVisual.alpha = selected ? 1f : 0f;
            if (visualRoot != null) visualRoot.localScale = baseScale * (selected ? selectedScale : 1f);
        }
    }
}
