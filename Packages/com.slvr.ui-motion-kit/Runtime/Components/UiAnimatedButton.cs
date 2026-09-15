using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    public enum UiAnimatedButtonState
    {
        Normal = 0,
        Hovered = 1,
        Focused = 2,
        Pressed = 3,
        Disabled = 4,
    }

    public enum UiButtonSemanticEventKind
    {
        Hover = 0,
        Focus = 1,
        Press = 2,
        Release = 3,
        Confirm = 4,
        Error = 5,
    }

    [Serializable]
    public sealed class UiButtonSemanticEvent : UnityEvent<UiButtonSemanticEventKind>
    {
    }

    /// <summary>Adds visual pointer/focus feedback without invoking Button.onClick itself.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Button))]
    public sealed class UiAnimatedButton : UiMotionElement,
        IPointerEnterHandler,
        IPointerExitHandler,
        IPointerDownHandler,
        IPointerUpHandler,
        IPointerClickHandler,
        ISelectHandler,
        IDeselectHandler,
        ISubmitHandler
    {
        [Header("Motion Target")]
        [SerializeField] private UiVisualRoot visualRoot;
        [SerializeField, Min(0.01f)] private float hoverScale = 1.025f;
        [SerializeField, Min(0.01f)] private float focusScale = 1.015f;
        [SerializeField, Min(0.01f)] private float pressedScale = 0.96f;
        [SerializeField] private bool hoverWhenDisabled;

        [Header("Optional Visuals")]
        [SerializeField] private CanvasGroup stateCanvasGroup;
        [SerializeField] private CanvasGroup focusRing;
        [SerializeField] private CanvasGroup shadow;
        [SerializeField] private RectTransform iconRoot;
        [SerializeField] private CanvasGroup sheen;
        [SerializeField, Range(0f, 1f)] private float disabledAlpha = 0.6f;
        [SerializeField, Range(0f, 1f)] private float pressedShadowAlpha = 0.55f;
        [SerializeField, Min(0.01f)] private float pressedIconScale = 0.94f;

        [Header("Semantic Feedback")]
        [SerializeField] private UiButtonSemanticEvent semanticFeedback = new UiButtonSemanticEvent();

        private Button button;
        private Vector3 baseScale;
        private Vector3 baseIconScale;
        private float baseStateAlpha = 1f;
        private float baseShadowAlpha = 1f;
        private bool hovered;
        private bool focused;
        private bool pressed;
        private bool initialized;

        private RectTransform Target => visualRoot != null ? visualRoot.VisualTransform : transform as RectTransform;

        public UiAnimatedButtonState CurrentState => ResolveState();
        public bool IsHovered => hovered;
        public bool IsFocused => focused;
        public bool IsPressed => pressed;
        public RectTransform VisualTarget => Target;
        public CanvasGroup FocusRing => focusRing;
        public UiButtonSemanticEvent SemanticFeedback => semanticFeedback;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            Initialize();
            RefreshState(true);
        }

        private void OnDisable()
        {
            hovered = false;
            focused = false;
            pressed = false;
        }

        public void ConfigureOptionalVisuals(
            CanvasGroup newStateCanvasGroup,
            CanvasGroup newFocusRing,
            CanvasGroup newShadow = null,
            RectTransform newIconRoot = null,
            CanvasGroup newSheen = null)
        {
            stateCanvasGroup = newStateCanvasGroup;
            focusRing = newFocusRing;
            shadow = newShadow;
            iconRoot = newIconRoot;
            sheen = newSheen;
            initialized = false;
            Initialize();
            RefreshState(true);
        }

        public void AssignVisualRoot(UiVisualRoot newVisualRoot)
        {
            visualRoot = newVisualRoot;
            initialized = false;
            Initialize();
            RefreshState(true);
        }

        public void AssignFocusRing(CanvasGroup newFocusRing)
        {
            focusRing = newFocusRing;
            if (focusRing != null) focusRing.alpha = focused && IsInteractable() ? 1f : 0f;
        }

        /// <summary>
        /// Keeps pointer hover feedback available for a disabled button while press, submit and
        /// click remain blocked by Button.interactable.
        /// </summary>
        public void SetHoverWhenDisabled(bool value)
        {
            hoverWhenDisabled = value;
            RefreshState(true);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!CanHover() || IsTouch(eventData)) return;
            if (hovered) return;
            hovered = true;
            Emit(UiButtonSemanticEventKind.Hover);
            AnimateState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (pressed)
            {
                pressed = false;
                Emit(UiButtonSemanticEventKind.Release);
            }

            hovered = false;
            AnimateState();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (!IsInteractable() || eventData.button != PointerEventData.InputButton.Left || pressed) return;
            pressed = true;
            Emit(UiButtonSemanticEventKind.Press);
            AnimateState();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!pressed || eventData.button != PointerEventData.InputButton.Left) return;
            pressed = false;
            Emit(UiButtonSemanticEventKind.Release);
            AnimateState();
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (!IsInteractable() || eventData.button != PointerEventData.InputButton.Left) return;
            Emit(UiButtonSemanticEventKind.Confirm);
        }

        public void OnSelect(BaseEventData eventData)
        {
            if (!IsInteractable()) return;
            if (focused) return;
            focused = true;
            Emit(UiButtonSemanticEventKind.Focus);
            AnimateState();
        }

        public void OnDeselect(BaseEventData eventData)
        {
            if (pressed)
            {
                pressed = false;
                Emit(UiButtonSemanticEventKind.Release);
            }

            focused = false;
            AnimateState();
        }

        public void OnSubmit(BaseEventData eventData)
        {
            if (!IsInteractable()) return;
            PlaySubmitFeedback();
            Emit(UiButtonSemanticEventKind.Press);
            Emit(UiButtonSemanticEventKind.Release);
            Emit(UiButtonSemanticEventKind.Confirm);
        }

        /// <summary>Emits a semantic error hook and plays bounded attention feedback.</summary>
        public void NotifyError()
        {
            Emit(UiButtonSemanticEventKind.Error);
            RectTransform target = Target;
            if (target == null || Settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale) return;

            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f) return;
            UiMotionTweenPolicy policy = ResolvePolicy(UiMotionEase.OutBack);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => target.localScale,
                value => target.localScale = value,
                baseScale * 1.03f,
                duration * 0.5f,
                policy));
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => target.localScale,
                value => target.localScale = value,
                ResolveTargetScale(),
                duration * 0.5f,
                policy));
            Lifecycle.Play(UiMotionChannel.Attention, sequence);
        }

        /// <summary>Refreshes disabled/rest state after a host-side interactable change.</summary>
        public void RefreshState(bool immediate = false)
        {
            Initialize();
            if (!IsInteractable())
            {
                if (pressed) Emit(UiButtonSemanticEventKind.Release);
                if (!hoverWhenDisabled) hovered = false;
                focused = false;
                pressed = false;
            }

            if (immediate)
            {
                Lifecycle.Stop(UiMotionChannel.Interaction);
                Lifecycle.Stop(UiMotionChannel.Selection);
                ApplyImmediateState();
                return;
            }

            AnimateState();
        }

        private void Initialize()
        {
            if (initialized) return;
            button = GetComponent<Button>();
            RectTransform target = Target;
            baseScale = target != null ? target.localScale : Vector3.one;
            baseIconScale = iconRoot != null ? iconRoot.localScale : Vector3.one;
            baseStateAlpha = stateCanvasGroup != null ? stateCanvasGroup.alpha : 1f;
            baseShadowAlpha = shadow != null ? shadow.alpha : 1f;
            initialized = true;
        }

        private bool IsInteractable()
        {
            if (button == null) button = GetComponent<Button>();
            return button != null && button.IsInteractable();
        }

        private bool CanHover()
        {
            return IsInteractable() || hoverWhenDisabled;
        }

        private static bool IsTouch(PointerEventData eventData)
        {
            return UiPointerDeviceUtility.IsTouch(eventData);
        }

        private void AnimateState()
        {
            RectTransform target = Target;
            if (target == null) return;

            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                ApplyImmediateState();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence interaction = UiMotionTweenFactory.Sequence(this, policy);
            interaction.Join(UiMotionTweenFactory.Vector3(
                this,
                () => target.localScale,
                value => target.localScale = value,
                ResolveTargetScale(),
                duration,
                policy));

            if (stateCanvasGroup != null)
            {
                interaction.Join(UiMotionTweenFactory.Float(
                    this,
                    () => stateCanvasGroup.alpha,
                    value => stateCanvasGroup.alpha = value,
                    ResolveStateAlpha(),
                    duration,
                    policy));
            }

            if (shadow != null)
            {
                interaction.Join(UiMotionTweenFactory.Float(
                    this,
                    () => shadow.alpha,
                    value => shadow.alpha = value,
                    ResolveShadowAlpha(),
                    duration,
                    policy));
            }

            if (iconRoot != null && iconRoot != target)
            {
                interaction.Join(UiMotionTweenFactory.Vector3(
                    this,
                    () => iconRoot.localScale,
                    value => iconRoot.localScale = value,
                    ResolveIconScale(),
                    duration,
                    policy));
            }

            if (sheen != null)
            {
                interaction.Join(UiMotionTweenFactory.Float(
                    this,
                    () => sheen.alpha,
                    value => sheen.alpha = value,
                    ResolveSheenAlpha(),
                    duration,
                    policy));
            }

            Lifecycle.Play(UiMotionChannel.Interaction, interaction);
            AnimateFocus(duration, policy);
        }

        private void AnimateFocus(float duration, UiMotionTweenPolicy policy)
        {
            if (focusRing == null) return;
            Tweener tween = UiMotionTweenFactory.Float(
                this,
                () => focusRing.alpha,
                value => focusRing.alpha = value,
                focused && IsInteractable() ? 1f : 0f,
                duration,
                policy);
            Lifecycle.Play(UiMotionChannel.Selection, tween);
        }

        private void PlaySubmitFeedback()
        {
            RectTransform target = Target;
            if (target == null || Settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale)
            {
                ApplyImmediateState();
                return;
            }

            float duration = ResolveDuration(UiMotionTiming.Fast);
            if (duration <= 0f)
            {
                ApplyImmediateState();
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy();
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => target.localScale,
                value => target.localScale = value,
                baseScale * pressedScale,
                duration * 0.5f,
                policy));
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => target.localScale,
                value => target.localScale = value,
                ResolveTargetScale(),
                duration * 0.5f,
                policy));
            Lifecycle.Play(UiMotionChannel.Interaction, sequence);
        }

        private void ApplyImmediateState()
        {
            RectTransform target = Target;
            if (target != null) target.localScale = ResolveTargetScale();
            if (stateCanvasGroup != null) stateCanvasGroup.alpha = ResolveStateAlpha();
            if (focusRing != null) focusRing.alpha = focused && IsInteractable() ? 1f : 0f;
            if (shadow != null) shadow.alpha = ResolveShadowAlpha();
            if (iconRoot != null && iconRoot != target) iconRoot.localScale = ResolveIconScale();
            if (sheen != null) sheen.alpha = ResolveSheenAlpha();
        }

        private UiAnimatedButtonState ResolveState()
        {
            if (!IsInteractable()) return UiAnimatedButtonState.Disabled;
            if (pressed) return UiAnimatedButtonState.Pressed;
            if (hovered) return UiAnimatedButtonState.Hovered;
            if (focused) return UiAnimatedButtonState.Focused;
            return UiAnimatedButtonState.Normal;
        }

        private Vector3 ResolveTargetScale()
        {
            if (Settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale)
            {
                return baseScale;
            }

            float multiplier = pressed
                ? pressedScale
                : hovered ? hoverScale
                : focused ? focusScale
                : 1f;
            return baseScale * multiplier;
        }

        private float ResolveStateAlpha()
        {
            // Disabled is an interaction state, not a presentation fade. The host owns the
            // authored unavailable visual (sprite, label, price/reason), so motion must preserve
            // its alpha exactly as authored.
            _ = disabledAlpha; // Retained for serialized compatibility with existing prefabs.
            return baseStateAlpha;
        }

        private float ResolveShadowAlpha()
        {
            return pressed && IsInteractable() ? pressedShadowAlpha : baseShadowAlpha;
        }

        private Vector3 ResolveIconScale()
        {
            if (Settings.ResolveEffect(UiMotionEffectKind.Scale) != UiMotionEffectKind.Scale)
            {
                return baseIconScale;
            }

            return pressed && IsInteractable() ? baseIconScale * pressedIconScale : baseIconScale;
        }

        private float ResolveSheenAlpha()
        {
            return hovered && IsInteractable() && !Settings.DisableFlashes ? 1f : 0f;
        }

        private void Emit(UiButtonSemanticEventKind eventKind)
        {
            semanticFeedback?.Invoke(eventKind);
        }
    }
}
