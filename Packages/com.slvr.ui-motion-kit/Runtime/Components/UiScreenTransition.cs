using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    public enum UiScreenTransitionStyle
    {
        Fade = 0,
        Horizontal = 1,
        Vertical = 2,
    }

    /// <summary>Animates only the assigned content root so fixed navigation remains untouched.</summary>
    [RequireComponent(typeof(CanvasGroup))]
    public sealed class UiScreenTransition : UiMotionElement
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform contentRoot;
        [SerializeField] private UiScreenTransitionStyle style = UiScreenTransitionStyle.Horizontal;
        [SerializeField] private Vector2 offset = new Vector2(48f, 32f);

        private Vector2 shownPosition;

        public RectTransform ContentRoot
        {
            get => contentRoot;
            set
            {
                contentRoot = value;
                shownPosition = contentRoot != null ? contentRoot.anchoredPosition : Vector2.zero;
            }
        }

        private void Awake()
        {
            EnsureReferences();
            shownPosition = contentRoot != null ? contentRoot.anchoredPosition : Vector2.zero;
        }

        public void Enter()
        {
            EnsureReferences();
            SetInteraction(false);
            float duration = ResolveDuration();
            if (duration <= 0f)
            {
                EnterImmediate();
                return;
            }

            Sequence sequence = BuildSequence(true, duration);
            sequence.OnComplete(() => SetInteraction(true));
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void Exit()
        {
            EnsureReferences();
            SetInteraction(false);
            float duration = ResolveDuration();
            if (duration <= 0f)
            {
                ExitImmediate();
                return;
            }

            Lifecycle.Play(UiMotionChannel.Visibility, BuildSequence(false, duration));
        }

        public void EnterImmediate()
        {
            EnsureReferences();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 1f;
            if (contentRoot != null) contentRoot.anchoredPosition = shownPosition;
            SetInteraction(true);
        }

        public void ExitImmediate()
        {
            EnsureReferences();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            canvasGroup.alpha = 0f;
            if (contentRoot != null) contentRoot.anchoredPosition = ResolveHiddenPosition();
            SetInteraction(false);
        }

        private Sequence BuildSequence(bool entering, float duration)
        {
            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Join(UiMotionTweenFactory.Float(
                this,
                () => canvasGroup.alpha,
                value => canvasGroup.alpha = value,
                entering ? 1f : 0f,
                duration,
                policy));

            UiMotionEffectKind requested = style == UiScreenTransitionStyle.Fade
                ? UiMotionEffectKind.Fade
                : UiMotionEffectKind.Translation;
            if (contentRoot != null && Settings.ResolveEffect(requested) == UiMotionEffectKind.Translation)
            {
                Vector2 target = entering ? shownPosition : ResolveHiddenPosition();
                sequence.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => contentRoot.anchoredPosition,
                    value => contentRoot.anchoredPosition = value,
                    target,
                    duration,
                    policy));
            }

            return sequence;
        }

        private Vector2 ResolveHiddenPosition()
        {
            if (style == UiScreenTransitionStyle.Vertical) return shownPosition + new Vector2(0f, offset.y);
            if (style == UiScreenTransitionStyle.Horizontal) return shownPosition + new Vector2(offset.x, 0f);
            return shownPosition;
        }

        private void SetInteraction(bool value)
        {
            canvasGroup.interactable = value;
            canvasGroup.blocksRaycasts = value;
        }

        private void EnsureReferences()
        {
            if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
            if (contentRoot == null) contentRoot = transform as RectTransform;
        }
    }
}
