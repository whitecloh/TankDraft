using NUnit.Framework;
using UnityEngine;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiIntegrationContractTests
    {
        [Test]
        public void HapticsRelay_UsesInjectedHostProvider()
        {
            var go = new GameObject("HapticsRelay");
            UiMotionHapticsRelay relay = go.AddComponent<UiMotionHapticsRelay>();
            var provider = new RecordingHaptics();
            relay.SetProvider(provider);
            relay.TriggerSuccess();
            Assert.That(provider.LastCue, Is.EqualTo(UiMotionHapticCue.Success));
            Object.DestroyImmediate(go);
        }

        [Test]
        public void AudioAdapter_WithNoClipsIsSafe()
        {
            var go = new GameObject("Audio", typeof(AudioSource));
            UiAudioSourceFeedbackAdapter adapter = go.AddComponent<UiAudioSourceFeedbackAdapter>();
            adapter.Configure(go.GetComponent<AudioSource>(), null);
            Assert.DoesNotThrow(() => adapter.Play(UiMotionAudioCue.Confirm));
            Object.DestroyImmediate(go);
        }

        private sealed class RecordingHaptics : IUiMotionHaptics
        {
            public UiMotionHapticCue LastCue { get; private set; }
            public void Trigger(UiMotionHapticCue cue) => LastCue = cue;
        }
    }
}
