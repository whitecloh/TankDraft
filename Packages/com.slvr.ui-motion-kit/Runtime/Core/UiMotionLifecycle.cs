using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>MonoBehaviour owner that binds semantic channels to Unity lifetime.</summary>
    [DisallowMultipleComponent]
    public sealed class UiMotionLifecycle : MonoBehaviour
    {
        private UiMotionChannelSet channels;

        public int ActiveChannelCount => channels?.Count ?? 0;

        public T Play<T>(
            UiMotionChannel channel,
            T tween,
            UiMotionDisableBehaviour disableBehaviour = UiMotionDisableBehaviour.Kill,
            UiMotionReplacementMode replacementMode = UiMotionReplacementMode.Kill)
            where T : Tween
        {
            return GetChannels().Set(channel, tween, disableBehaviour, replacementMode);
        }

        public void Stop(UiMotionChannel channel, UiMotionReplacementMode mode = UiMotionReplacementMode.Kill)
        {
            channels?.Stop(channel, mode);
        }

        public bool TryGet(UiMotionChannel channel, out Tween tween)
        {
            if (channels != null)
            {
                return channels.TryGet(channel, out tween);
            }

            tween = null;
            return false;
        }

        private void OnEnable()
        {
            channels?.HandleEnable();
        }

        private void OnDisable()
        {
            channels?.HandleDisable();
        }

        private void OnDestroy()
        {
            channels?.Dispose();
            channels = null;
        }

        private UiMotionChannelSet GetChannels()
        {
            if (channels == null)
            {
                channels = new UiMotionChannelSet();
            }

            return channels;
        }
    }
}
