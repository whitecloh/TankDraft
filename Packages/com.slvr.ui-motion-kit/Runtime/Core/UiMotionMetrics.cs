using System.Threading;
using DG.Tweening;

namespace SLVR.UIMotion
{
    /// <summary>Read-only snapshot of global DOTween and package-owned activity.</summary>
    public readonly struct UiMotionMetricsSnapshot
    {
        public UiMotionMetricsSnapshot(int tweeners, int sequences, int playing, int trackedChannels)
        {
            ActiveTweeners = tweeners;
            ActiveSequences = sequences;
            PlayingTweens = playing;
            TrackedChannels = trackedChannels;
        }

        public int ActiveTweeners { get; }
        public int ActiveSequences { get; }
        public int PlayingTweens { get; }
        public int TrackedChannels { get; }
    }

    public static class UiMotionMetrics
    {
        private static int trackedChannels;

        public static UiMotionMetricsSnapshot Capture() => new UiMotionMetricsSnapshot(
            DOTween.TotalActiveTweeners(),
            DOTween.TotalActiveSequences(),
            DOTween.TotalPlayingTweens(),
            Volatile.Read(ref trackedChannels));

        internal static void NotifyTracked(int delta)
        {
            Interlocked.Add(ref trackedChannels, delta);
        }
    }
}
