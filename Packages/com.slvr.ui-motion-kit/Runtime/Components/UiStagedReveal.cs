using System;
using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SLVR.UIMotion
{
    public enum UiStagedRevealStepKind
    {
        Fade = 0,
        Scale = 1,
        Typewriter = 2,
    }

    [Serializable]
    public sealed class UiStagedRevealStep
    {
        [SerializeField] private UiStagedRevealStepKind kind;
        [SerializeField] private CanvasGroup group;
        [SerializeField] private RectTransform root;
        [SerializeField] private TMP_Text text;
        [SerializeField, Min(0f)] private float duration = 0.15f;
        [SerializeField, Min(1f)] private float overshootScale = 1.1f;

        public UiStagedRevealStep(
            UiStagedRevealStepKind kind,
            CanvasGroup group,
            RectTransform root = null,
            float duration = 0.15f,
            float overshootScale = 1.1f)
        {
            this.kind = kind;
            this.group = group;
            this.root = root;
            this.duration = duration;
            this.overshootScale = overshootScale;
        }

        public UiStagedRevealStep(TMP_Text text, float duration = 0.15f)
        {
            kind = UiStagedRevealStepKind.Typewriter;
            this.text = text;
            this.duration = duration;
        }

        public UiStagedRevealStepKind Kind => kind;
        public CanvasGroup Group => group;
        public RectTransform Root => root;
        public TMP_Text Text => text;
        public float Duration => Mathf.Max(0f, duration);
        public float OvershootScale => Mathf.Max(1f, overshootScale);
    }

    /// <summary>
    /// Sequential fade/scale reveal for compact UI content such as stat rows, popup fields
    /// and tooltip details. Every step owns an equal, explicit time slice.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiStagedReveal : UiMotionElement
    {
        [SerializeField] private UiStagedRevealStep[] steps = Array.Empty<UiStagedRevealStep>();

        private Vector3[] baseScales = Array.Empty<Vector3>();
        private float[] baseAlphas = Array.Empty<float>();
        private int[] visibleCharacterCounts = Array.Empty<int>();
        private bool baselinesCaptured;
        private bool prepared;

        /// <summary>Raised only after a played reveal reaches its final state.</summary>
        public event Action RevealCompleted;

        public bool IsPrepared => prepared;
        public int StepCount => steps.Length;

        public void Configure(UiStagedRevealStep[] newSteps)
        {
            Lifecycle.Stop(UiMotionChannel.Visibility);
            steps = newSteps ?? Array.Empty<UiStagedRevealStep>();
            baselinesCaptured = false;
            prepared = false;
            CaptureBaselines();
            ShowImmediate();
        }

        public void Prepare()
        {
            CaptureBaselines();
            RefreshTypewriterContent();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            for (int i = 0; i < steps.Length; i++)
            {
                UiStagedRevealStep step = steps[i];
                if (step == null)
                {
                    continue;
                }

                if (step.Group != null)
                {
                    step.Group.alpha = 0f;
                }

                if (step.Kind == UiStagedRevealStepKind.Scale && step.Root != null)
                {
                    step.Root.localScale = Vector3.zero;
                }

                if (step.Kind == UiStagedRevealStepKind.Typewriter && step.Text != null)
                {
                    step.Text.maxVisibleCharacters = 0;
                }
            }

            prepared = true;
        }

        public void Play(float initialDelay = 0f)
        {
            if (!isActiveAndEnabled)
            {
                return;
            }

            if (!prepared)
            {
                Prepare();
            }

            UiMotionSettingsSnapshot settings = Settings;
            UiMotionTweenPolicy policy = ResolvePolicy(settings, UiMotionEase.OutCubic);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            if (initialDelay > 0f)
            {
                sequence.AppendInterval(initialDelay);
            }

            for (int i = 0; i < steps.Length; i++)
            {
                AppendStep(sequence, steps[i], i, settings, policy);
            }

            sequence.OnComplete(() =>
            {
                prepared = false;
                ApplyFinalState();
                RevealCompleted?.Invoke();
            });
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void ShowImmediate()
        {
            CaptureBaselines();
            RefreshTypewriterContent();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            ApplyFinalState();
            prepared = false;
        }

        private void RefreshTypewriterContent()
        {
            for (int i = 0; i < steps.Length; i++)
            {
                UiStagedRevealStep step = steps[i];
                if (step == null || step.Kind != UiStagedRevealStepKind.Typewriter || step.Text == null)
                {
                    continue;
                }

                step.Text.ForceMeshUpdate();
                visibleCharacterCounts[i] = step.Text.textInfo.characterCount;
            }
        }

        private void ApplyFinalState()
        {
            for (int i = 0; i < steps.Length; i++)
            {
                UiStagedRevealStep step = steps[i];
                if (step == null)
                {
                    continue;
                }

                if (step.Group != null)
                {
                    step.Group.alpha = GetBaseAlpha(i);
                }

                if (step.Root != null)
                {
                    step.Root.localScale = GetBaseScale(i);
                }

                if (step.Kind == UiStagedRevealStepKind.Typewriter && step.Text != null)
                {
                    step.Text.maxVisibleCharacters = GetVisibleCharacterCount(i);
                }
            }
        }

        private void AppendStep(
            Sequence sequence,
            UiStagedRevealStep step,
            int index,
            UiMotionSettingsSnapshot settings,
            UiMotionTweenPolicy policy)
        {
            if (step == null)
            {
                return;
            }

            float duration = UiMotionDefaults.ScaleDuration(step.Duration, settings.Intensity);
            if (duration <= 0f)
            {
                sequence.AppendCallback(() => ApplyStepFinal(step, index));
                return;
            }

            if (step.Kind == UiStagedRevealStepKind.Fade)
            {
                if (step.Group == null)
                {
                    sequence.AppendInterval(duration);
                    return;
                }

                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => step.Group.alpha,
                    value => step.Group.alpha = value,
                    GetBaseAlpha(index),
                    duration,
                    policy));
                return;
            }

            if (step.Kind == UiStagedRevealStepKind.Typewriter)
            {
                if (step.Text == null)
                {
                    sequence.AppendInterval(duration);
                    return;
                }

                int count = GetVisibleCharacterCount(index);
                if (count <= 0)
                {
                    step.Text.maxVisibleCharacters = 0;
                    return;
                }

                float visibleCharacters = 0f;
                sequence.Append(UiMotionTweenFactory.Float(
                    this,
                    () => visibleCharacters,
                    value =>
                    {
                        visibleCharacters = value;
                        step.Text.maxVisibleCharacters = Mathf.Min(count, Mathf.CeilToInt(value));
                    },
                    count,
                    duration,
                    ResolvePolicy(settings, UiMotionEase.Linear)));
                return;
            }

            bool allowScale = settings.ResolveEffect(UiMotionEffectKind.Scale) == UiMotionEffectKind.Scale;
            if (!allowScale || step.Root == null)
            {
                if (step.Group != null)
                {
                    sequence.Append(UiMotionTweenFactory.Float(
                        this,
                        () => step.Group.alpha,
                        value => step.Group.alpha = value,
                        GetBaseAlpha(index),
                        duration,
                        policy));
                }
                else
                {
                    sequence.AppendInterval(duration);
                }

                if (step.Root != null)
                {
                    step.Root.localScale = GetBaseScale(index);
                }

                return;
            }

            Vector3 baseline = GetBaseScale(index);
            sequence.AppendCallback(() =>
            {
                if (step.Group != null)
                {
                    step.Group.alpha = GetBaseAlpha(index);
                }
            });
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => step.Root.localScale,
                value => step.Root.localScale = value,
                baseline * step.OvershootScale,
                duration * 0.6f,
                policy));
            sequence.Append(UiMotionTweenFactory.Vector3(
                this,
                () => step.Root.localScale,
                value => step.Root.localScale = value,
                baseline,
                duration * 0.4f,
                policy));
        }

        private void CaptureBaselines()
        {
            if (baselinesCaptured)
            {
                return;
            }

            baseScales = new Vector3[steps.Length];
            baseAlphas = new float[steps.Length];
            visibleCharacterCounts = new int[steps.Length];
            for (int i = 0; i < steps.Length; i++)
            {
                UiStagedRevealStep step = steps[i];
                baseScales[i] = step != null && step.Root != null
                    ? step.Root.localScale
                    : Vector3.one;
                baseAlphas[i] = step != null && step.Group != null
                    ? step.Group.alpha
                    : 1f;
                if (step != null && step.Text != null)
                {
                    step.Text.ForceMeshUpdate();
                    visibleCharacterCounts[i] = step.Text.textInfo.characterCount;
                }
            }

            baselinesCaptured = true;
        }

        private void ApplyStepFinal(UiStagedRevealStep step, int index)
        {
            if (step.Group != null)
            {
                step.Group.alpha = GetBaseAlpha(index);
            }

            if (step.Root != null)
            {
                step.Root.localScale = GetBaseScale(index);
            }

            if (step.Kind == UiStagedRevealStepKind.Typewriter && step.Text != null)
            {
                step.Text.maxVisibleCharacters = GetVisibleCharacterCount(index);
            }
        }

        private Vector3 GetBaseScale(int index)
        {
            return index >= 0 && index < baseScales.Length ? baseScales[index] : Vector3.one;
        }

        private float GetBaseAlpha(int index)
        {
            return index >= 0 && index < baseAlphas.Length ? baseAlphas[index] : 1f;
        }

        private int GetVisibleCharacterCount(int index)
        {
            return index >= 0 && index < visibleCharacterCounts.Length ? visibleCharacterCounts[index] : 0;
        }
    }
}
