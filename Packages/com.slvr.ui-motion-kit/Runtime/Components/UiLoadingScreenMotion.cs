using System;
using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Reusable indeterminate loading sequence for authored segmented progress:
    /// loop through the segments, finish the active pass, hold the completed frame, then fade.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiLoadingScreenMotion : UiMotionElement
    {
        [Header("Authored References")]
        [SerializeField] private UiSegmentedProgress progress;
        [SerializeField] private CanvasGroup canvasGroup;

        [Header("Indeterminate Loop")]
        [SerializeField] private bool indeterminate = true;
        [SerializeField, Min(0.1f)] private float loopFillDuration = 0.84f;
        [SerializeField, Min(0f)] private float loopFullHoldDuration = 0.12f;

        [Header("Determinate Progress")]
        [SerializeField, Min(0.01f)] private float minimumProgressDuration = 0.12f;
        [SerializeField, Min(0.01f)] private float maximumProgressDuration = 0.34f;

        [Header("Completion Fade")]
        [SerializeField, Min(0.01f)] private float completionFillDuration = 0.42f;
        [SerializeField, Min(0.01f)] private float minimumCompletionFillDuration = 0.16f;
        [SerializeField, Min(0f)] private float completedHoldDuration = 0.18f;
        [SerializeField, Min(0.01f)] private float fadeDuration = 0.36f;

        private bool initialized;
        private bool isCompleting;

        public event Action CompletionFinished;

        public bool IsCompleting => isCompleting;
        public bool IsIndeterminate => indeterminate;
        public float DisplayedProgress => progress != null ? progress.Value : 0f;
        public float LoopFillDuration => loopFillDuration;
        public float LoopFullHoldDuration => loopFullHoldDuration;
        public float CompletedHoldDuration => completedHoldDuration;
        public float FadeDuration => fadeDuration;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            PrepareForShow(0f);
        }

        private void OnDisable()
        {
            Lifecycle.Stop(UiMotionChannel.Value);
            Lifecycle.Stop(UiMotionChannel.Visibility);
            isCompleting = false;
        }

        public void Configure(
            UiSegmentedProgress segmentedProgress,
            CanvasGroup targetCanvasGroup,
            bool useIndeterminateLoop = true,
            float loopDuration = 0.84f,
            float loopHoldDuration = 0.12f,
            float finishDuration = 0.42f,
            float holdDuration = 0.18f,
            float exitFadeDuration = 0.36f)
        {
            progress = segmentedProgress;
            canvasGroup = targetCanvasGroup;
            indeterminate = useIndeterminateLoop;
            loopFillDuration = Mathf.Max(0.1f, loopDuration);
            loopFullHoldDuration = Mathf.Max(0f, loopHoldDuration);
            completionFillDuration = Mathf.Max(0.01f, finishDuration);
            completedHoldDuration = Mathf.Max(0f, holdDuration);
            fadeDuration = Mathf.Max(0.01f, exitFadeDuration);
            initialized = false;
            Initialize();
            if (isActiveAndEnabled)
            {
                PrepareForShow(0f);
            }
        }

        /// <summary>Restores the authored visible state and cancels an in-flight exit.</summary>
        public void PrepareForShow(float initialProgress = 0f)
        {
            Initialize();
            Lifecycle.Stop(UiMotionChannel.Value);
            Lifecycle.Stop(UiMotionChannel.Visibility);
            isCompleting = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 1f;
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = true;
            }

            progress?.SetValue(initialProgress);
            PlayIndeterminateLoop();
        }

        /// <summary>Moves the displayed value without interrupting a completion sequence.</summary>
        public void SetProgress(float value, bool immediate = false)
        {
            Initialize();
            if (progress == null || isCompleting || indeterminate)
            {
                return;
            }

            float target = Mathf.Clamp01(value);
            float current = progress.Value;
            if (immediate || !isActiveAndEnabled || Mathf.Approximately(current, target))
            {
                Lifecycle.Stop(UiMotionChannel.Value);
                progress.SetValue(target);
                return;
            }

            float delta = Mathf.Abs(target - current);
            float duration = Mathf.Lerp(minimumProgressDuration, maximumProgressDuration, delta);
            UiMotionTweenPolicy policy = ResolvePolicy(UiMotionEase.OutCubic);
            Tween tween = UiMotionTweenFactory.Float(
                this,
                () => progress.Value,
                progress.SetValue,
                target,
                duration,
                policy);
            Lifecycle.Play(UiMotionChannel.Value, tween);
        }

        /// <summary>Finishes progress and owns the complete exit before notifying the host.</summary>
        public void PlayCompletion()
        {
            Initialize();
            if (isCompleting)
            {
                return;
            }

            isCompleting = true;
            Lifecycle.Stop(UiMotionChannel.Value);
            Lifecycle.Stop(UiMotionChannel.Visibility);
            if (canvasGroup != null)
            {
                canvasGroup.interactable = false;
                canvasGroup.blocksRaycasts = true;
            }

            UiMotionSettingsSnapshot settings = Settings;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.InOutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            float current = progress != null ? progress.Value : 1f;
            float fillDuration = ResolveCompletionFillDuration(
                current,
                completionFillDuration,
                minimumCompletionFillDuration);
            if (progress != null && fillDuration > 0f)
            {
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => progress.Value,
                    progress.SetValue,
                    1f,
                    fillDuration,
                    ResolvePolicy(settings, UiMotionEase.OutCubic)));
            }
            else
            {
                progress?.SetValue(1f);
            }

            if (completedHoldDuration > 0f)
            {
                sequence.AppendInterval(completedHoldDuration);
            }

            float exitStart = sequence.Duration();
            bool animateFade = canvasGroup != null &&
                settings.ResolveEffect(UiMotionEffectKind.Fade) == UiMotionEffectKind.Fade;

            if (animateFade)
            {
                sequence.Insert(exitStart, UiMotionTweenFactory.Float(
                    this,
                    () => canvasGroup.alpha,
                    value => canvasGroup.alpha = value,
                    0f,
                    fadeDuration,
                    ResolvePolicy(settings, UiMotionEase.InQuad)));
            }

            if (!animateFade && canvasGroup != null)
            {
                sequence.AppendCallback(() => canvasGroup.alpha = 0f);
            }

            sequence.OnComplete(NotifyCompletionFinished);
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public static float ResolveCompletionFillDuration(
            float currentProgress,
            float fullDuration,
            float minimumVisibleDuration = 0.01f)
        {
            float remaining = 1f - Mathf.Clamp01(currentProgress);
            if (remaining <= 0.0001f)
            {
                return 0f;
            }

            return Mathf.Max(
                Mathf.Max(0.01f, minimumVisibleDuration),
                Mathf.Max(0.01f, fullDuration) * remaining);
        }

        private void NotifyCompletionFinished()
        {
            isCompleting = false;
            if (canvasGroup != null)
            {
                canvasGroup.alpha = 0f;
                canvasGroup.blocksRaycasts = false;
            }

            CompletionFinished?.Invoke();
        }

        private void PlayIndeterminateLoop()
        {
            if (!indeterminate || progress == null || !isActiveAndEnabled || isCompleting)
            {
                return;
            }

            UiMotionSettingsSnapshot settings = Settings;
            Sequence sequence = UiMotionTweenFactory.Sequence(
                this,
                ResolvePolicy(settings, UiMotionEase.Linear));
            Tween fillTween = UiMotionTweenFactory.Float(
                this,
                () => progress.Value,
                progress.SetValue,
                1f,
                loopFillDuration,
                ResolvePolicy(settings, UiMotionEase.Linear))
                .SetEase(Ease.Linear);
            sequence.Append(fillTween);
            if (loopFullHoldDuration > 0f)
            {
                sequence.AppendInterval(loopFullHoldDuration);
            }

            sequence.AppendCallback(() => progress.SetValue(0f));
            sequence.SetLoops(-1, LoopType.Restart);
            Lifecycle.Play(UiMotionChannel.Value, sequence);
        }

        private void Initialize()
        {
            if (initialized)
            {
                return;
            }

            if (canvasGroup == null)
            {
                canvasGroup = GetComponent<CanvasGroup>();
            }

            initialized = true;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            loopFillDuration = Mathf.Max(0.1f, loopFillDuration);
            loopFullHoldDuration = Mathf.Max(0f, loopFullHoldDuration);
            minimumProgressDuration = Mathf.Max(0.01f, minimumProgressDuration);
            maximumProgressDuration = Mathf.Max(minimumProgressDuration, maximumProgressDuration);
            completionFillDuration = Mathf.Max(0.01f, completionFillDuration);
            minimumCompletionFillDuration = Mathf.Max(0.01f, minimumCompletionFillDuration);
            completedHoldDuration = Mathf.Max(0f, completedHoldDuration);
            fadeDuration = Mathf.Max(0.01f, fadeDuration);
        }
#endif
    }
}
