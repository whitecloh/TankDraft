using DG.Tweening;
using TMPro;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Allocation-free TMP typewriter reveal based on maxVisibleCharacters. The complete
    /// string is assigned once, so wrapping and layout remain stable while glyphs appear.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TMP_Text))]
    public sealed class UiTypewriterText : UiMotionElement
    {
        [SerializeField] private TMP_Text target;
        [SerializeField, Min(1f)] private float charactersPerSecond = 90f;
        [SerializeField, Min(0f)] private float minimumDuration = 0.12f;
        [SerializeField, Min(0f)] private float maximumDuration = 0.65f;

        private int totalCharacters;
        private float visibleCharacters;
        private bool prepared;

        public TMP_Text Target => target;
        public bool IsPrepared => prepared;
        public int TotalCharacters => totalCharacters;

        private void Awake()
        {
            ResolveTarget();
        }

        private void OnDisable()
        {
            if (target != null)
            {
                target.maxVisibleCharacters = totalCharacters;
            }

            prepared = false;
        }

        public void Configure(
            TMP_Text newTarget,
            float newCharactersPerSecond = 90f,
            float newMinimumDuration = 0.12f,
            float newMaximumDuration = 0.65f)
        {
            target = newTarget != null ? newTarget : GetComponent<TMP_Text>();
            charactersPerSecond = Mathf.Max(1f, newCharactersPerSecond);
            minimumDuration = Mathf.Max(0f, newMinimumDuration);
            maximumDuration = Mathf.Max(minimumDuration, newMaximumDuration);
        }

        public void Prepare(string value)
        {
            ResolveTarget();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            if (target == null)
            {
                return;
            }

            target.text = value ?? string.Empty;
            target.ForceMeshUpdate();
            totalCharacters = target.textInfo.characterCount;
            visibleCharacters = 0f;
            target.maxVisibleCharacters = 0;
            prepared = true;
        }

        public void Play(string value, float initialDelay = 0f)
        {
            Prepare(value);
            PlayPrepared(initialDelay);
        }

        public void PlayPrepared(float initialDelay = 0f)
        {
            ResolveTarget();
            if (target == null)
            {
                return;
            }

            if (!prepared)
            {
                Prepare(target.text);
            }

            float rawDuration = totalCharacters / Mathf.Max(1f, charactersPerSecond);
            float duration = UiMotionDefaults.ScaleDuration(
                Mathf.Clamp(rawDuration, minimumDuration, maximumDuration),
                Settings.Intensity);
            if (duration <= 0f || totalCharacters <= 0)
            {
                SetImmediate(target.text);
                return;
            }

            UiMotionTweenPolicy policy = ResolvePolicy(UiMotionEase.Linear);
            Sequence sequence = UiMotionTweenFactory.Sequence(this, policy);
            if (initialDelay > 0f)
            {
                sequence.AppendInterval(initialDelay);
            }

            sequence.Append(UiMotionTweenFactory.Float(
                this,
                () => visibleCharacters,
                value =>
                {
                    visibleCharacters = value;
                    target.maxVisibleCharacters = Mathf.Min(totalCharacters, Mathf.CeilToInt(value));
                },
                totalCharacters,
                duration,
                policy));
            sequence.OnComplete(() =>
            {
                target.maxVisibleCharacters = totalCharacters;
                visibleCharacters = totalCharacters;
                prepared = false;
            });
            Lifecycle.Play(UiMotionChannel.Visibility, sequence);
        }

        public void SetImmediate(string value)
        {
            ResolveTarget();
            Lifecycle.Stop(UiMotionChannel.Visibility);
            if (target == null)
            {
                return;
            }

            target.text = value ?? string.Empty;
            target.ForceMeshUpdate();
            totalCharacters = target.textInfo.characterCount;
            visibleCharacters = totalCharacters;
            target.maxVisibleCharacters = totalCharacters;
            prepared = false;
        }

        private void ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponent<TMP_Text>();
            }
        }
    }
}
