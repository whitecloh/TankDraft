using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    public enum UiMotionAudioCue
    {
        Hover,
        Focus,
        Press,
        Release,
        Confirm,
        Error,
        Open,
        Close,
        Reward,
        CurrencyArrival,
    }

    public interface IUiMotionAudioFeedback
    {
        void Play(UiMotionAudioCue cue);
    }

    [Serializable]
    public struct UiMotionAudioCueBinding
    {
        public UiMotionAudioCue Cue;
        public AudioClip Clip;
        [Range(0f, 1f)] public float Volume;
    }

    /// <summary>Optional semantic AudioSource bridge. The package intentionally ships with no clips.</summary>
    [DisallowMultipleComponent]
    public sealed class UiAudioSourceFeedbackAdapter : MonoBehaviour, IUiMotionAudioFeedback
    {
        [SerializeField] private AudioSource source;
        [SerializeField] private UiMotionAudioCueBinding[] bindings = Array.Empty<UiMotionAudioCueBinding>();

        public void Configure(AudioSource audioSource, UiMotionAudioCueBinding[] cueBindings)
        {
            source = audioSource;
            bindings = cueBindings ?? Array.Empty<UiMotionAudioCueBinding>();
        }

        public void Play(UiMotionAudioCue cue)
        {
            if (source == null) return;
            for (int i = 0; i < bindings.Length; i++)
            {
                if (bindings[i].Cue != cue || bindings[i].Clip == null) continue;
                source.PlayOneShot(bindings[i].Clip, Mathf.Clamp01(bindings[i].Volume));
                return;
            }
        }
    }

    public enum UiMotionHapticCue
    {
        Selection,
        Light,
        Medium,
        Strong,
        Success,
        Error,
    }

    public interface IUiMotionHaptics
    {
        void Trigger(UiMotionHapticCue cue);
    }

    public sealed class UiNullMotionHaptics : IUiMotionHaptics
    {
        public static readonly UiNullMotionHaptics Instance = new UiNullMotionHaptics();
        private UiNullMotionHaptics() { }
        public void Trigger(UiMotionHapticCue cue) { }
    }

    /// <summary>Scene-side relay that accepts any host haptics provider without a package dependency.</summary>
    [DisallowMultipleComponent]
    public sealed class UiMotionHapticsRelay : MonoBehaviour
    {
        private IUiMotionHaptics provider = UiNullMotionHaptics.Instance;

        public void SetProvider(IUiMotionHaptics newProvider)
        {
            provider = newProvider ?? UiNullMotionHaptics.Instance;
        }

        public void TriggerSelection() => provider.Trigger(UiMotionHapticCue.Selection);
        public void TriggerLight() => provider.Trigger(UiMotionHapticCue.Light);
        public void TriggerMedium() => provider.Trigger(UiMotionHapticCue.Medium);
        public void TriggerStrong() => provider.Trigger(UiMotionHapticCue.Strong);
        public void TriggerSuccess() => provider.Trigger(UiMotionHapticCue.Success);
        public void TriggerError() => provider.Trigger(UiMotionHapticCue.Error);
    }
}
