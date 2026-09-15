using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Selected state motion for tabs and navigation items.</summary>
    public class UiNavigationItem : UiMotionElement
    {
        [SerializeField] private CanvasGroup selectionPlate;
        [SerializeField] private RectTransform iconRoot;
        [SerializeField] private CanvasGroup label;
        [SerializeField, Min(1f)] private float selectedIconScale = 1.08f;

        private Vector3 iconBaseScale = Vector3.one;
        private bool selected;

        protected virtual void Awake()
        {
            if (iconRoot != null) iconBaseScale = iconRoot.localScale;
        }

        public bool IsSelected => selected;

        public void SetSelected(bool value, bool immediate = false)
        {
            selected = value;
            if (immediate || ResolveDuration(UiMotionTiming.Fast) <= 0f)
            {
                ApplyImmediate();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy();
            float duration = ResolveDuration(UiMotionTiming.Fast);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            if (selectionPlate != null)
            {
                sequence.Join(UiMotionTweenFactory.Float(
                    this, () => selectionPlate.alpha, x => selectionPlate.alpha = x, selected ? 1f : 0f, duration, policy));
            }
            if (label != null)
            {
                sequence.Join(UiMotionTweenFactory.Float(
                    this, () => label.alpha, x => label.alpha = x, selected ? 1f : 0.72f, duration, policy));
            }
            if (iconRoot != null && Settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale)
            {
                sequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => iconRoot.localScale,
                    x => iconRoot.localScale = x,
                    iconBaseScale * (selected ? selectedIconScale : 1f),
                    duration,
                    policy));
            }
            Lifecycle.Play(UiMotionChannel.Selection, sequence);
        }

        public void ApplyImmediate()
        {
            Lifecycle.Stop(UiMotionChannel.Selection);
            if (selectionPlate != null) selectionPlate.alpha = selected ? 1f : 0f;
            if (label != null) label.alpha = selected ? 1f : 0.72f;
            if (iconRoot != null) iconRoot.localScale = iconBaseScale * (selected ? selectedIconScale : 1f);
        }
    }

    /// <summary>Semantic alias for tab-specific authoring.</summary>
    public sealed class UiTabMotion : UiNavigationItem
    {
    }
}
