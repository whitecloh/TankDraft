using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Finite warning timeline: opposing authored text bands, glyph impact, then fade.</summary>
    [DisallowMultipleComponent]
    public sealed class UiWarningBannerMotion : UiMotionElement
    {
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private RectTransform[] topItems;
        [SerializeField] private RectTransform[] bottomItems;
        [SerializeField] private UiGlyphPopReveal title;
        [SerializeField] private UiGlyphPopReveal subtitle;
        [SerializeField, Min(1)] private int visibleSlots = 5;
        [SerializeField, Min(0f)] private float bandSpeed = 220f;
        [SerializeField, Min(0f)] private float enterSeconds = 0.08f;
        [SerializeField, Min(0f)] private float exitSeconds = 0.25f;
        [SerializeField, Range(0.05f, 0.8f)] private float revealFraction = 0.38f;
        private Vector2[] topPositions;
        private Vector2[] bottomPositions;
        private float elapsed;
        private float duration;
        private bool translate;
        private bool scale;
        private bool paused;

        public bool IsVisible => canvasGroup != null && canvasGroup.alpha > 0.001f;

        private void Awake() => HideImmediate();
        private void OnDisable() => HideImmediate();

        public void Play(float totalSeconds)
        {
            HideImmediate();
            Initialize();
            duration = Mathf.Max(0.01f, totalSeconds);
            translate = Settings.ResolveEffect(UiMotionEffectKind.Translation) == UiMotionEffectKind.Translation;
            scale = Settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            elapsed = 0f;
            Apply(0f);
            var policy = new UiMotionTweenPolicy(UiMotionEase.Linear, Settings.UseUnscaledTime, true, true, UiMotionDisableBehaviour.Kill);
            var tween = UiMotionTweenFactory.Float(this, () => elapsed, Apply, duration, duration, policy);
            tween.OnComplete(ApplyHiddenState);
            Lifecycle.Play(UiMotionChannel.Visibility, tween);
            if (paused) tween.Pause();
        }

        public void SetPaused(bool value)
        {
            paused = value;
            if (!Lifecycle.TryGet(UiMotionChannel.Visibility, out Tween tween)) return;
            if (paused) tween.Pause();
            else tween.Play();
        }

        public void HideImmediate()
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            ApplyHiddenState();
        }

        private void ApplyHiddenState()
        {
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = false;
            }
            Restore(topItems, topPositions);
            Restore(bottomItems, bottomPositions);
            title?.Sample(1f, false);
            subtitle?.Sample(1f, false);
        }

        private void Initialize()
        {
            if (topPositions == null) topPositions = Capture(topItems);
            if (bottomPositions == null) bottomPositions = Capture(bottomItems);
        }

        private void Apply(float value)
        {
            elapsed = value;
            if (canvasGroup == null) return;
            float enter = Mathf.Min(enterSeconds, duration * 0.2f);
            float exit = Mathf.Min(exitSeconds, duration * 0.2f);
            canvasGroup.alpha = Mathf.Min(enter > 0f ? Mathf.Clamp01(elapsed / enter) : 1f,
                exit > 0f ? Mathf.Clamp01((duration - elapsed) / exit) : 1f);
            float progress = Mathf.Clamp01(elapsed / duration);
            title?.Sample(scale ? progress / revealFraction : 1f, scale);
            subtitle?.Sample(scale ? (progress - 0.08f) / revealFraction : 1f, scale);
            if (translate)
            {
                Move(topItems, topPositions, -1f);
                Move(bottomItems, bottomPositions, 1f);
            }
        }

        private void Move(RectTransform[] items, Vector2[] positions, float direction)
        {
            if (items == null || items.Length == 0 || items[0] == null) return;
            var parent = items[0].parent as RectTransform;
            float pitch = parent != null ? parent.rect.width / Mathf.Max(1, visibleSlots) : 0f;
            float offset = pitch > 0f ? Mathf.Repeat(elapsed * bandSpeed * Settings.Intensity, pitch) * direction : 0f;
            for (int i = 0; i < items.Length; i++)
                if (items[i] != null) items[i].anchoredPosition = positions[i] + new Vector2(offset, 0f);
        }

        private static Vector2[] Capture(RectTransform[] items)
        {
            var positions = new Vector2[items?.Length ?? 0];
            for (int i = 0; i < positions.Length; i++)
                if (items[i] != null) positions[i] = items[i].anchoredPosition;
            return positions;
        }

        private static void Restore(RectTransform[] items, Vector2[] positions)
        {
            if (items == null || positions == null) return;
            for (int i = 0; i < items.Length && i < positions.Length; i++)
                if (items[i] != null) items[i].anchoredPosition = positions[i];
        }
    }
}
