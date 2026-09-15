using System;
using System.Collections.Generic;
using DG.Tweening;

namespace SLVR.UIMotion
{
    /// <summary>Owns at most one tween per semantic channel.</summary>
    public sealed class UiMotionChannelSet : IDisposable
    {
        private sealed class Entry
        {
            public Tween Tween;
            public UiMotionDisableBehaviour DisableBehaviour;
            public bool PausedByLifecycle;
        }

        private readonly Dictionary<UiMotionChannel, Entry> entries = new Dictionary<UiMotionChannel, Entry>(8);
        private readonly List<UiMotionChannel> keyBuffer = new List<UiMotionChannel>(8);
        private bool disposed;

        public int Count => entries.Count;

        public T Set<T>(
            UiMotionChannel channel,
            T tween,
            UiMotionDisableBehaviour disableBehaviour,
            UiMotionReplacementMode replacementMode = UiMotionReplacementMode.Kill)
            where T : Tween
        {
            if (disposed) throw new ObjectDisposedException(nameof(UiMotionChannelSet));
            if (tween == null) throw new ArgumentNullException(nameof(tween));

            using (UiMotionProfiler.ReplaceChannel.Auto())
            {
                Stop(channel, replacementMode);

                var entry = new Entry
                {
                    Tween = tween,
                    DisableBehaviour = disableBehaviour,
                };
                entries[channel] = entry;
                UiMotionMetrics.NotifyTracked(1);
                tween.OnKill(() => RemoveIfCurrent(channel, tween));
                return tween;
            }
        }

        public bool TryGet(UiMotionChannel channel, out Tween tween)
        {
            if (entries.TryGetValue(channel, out Entry entry) && entry.Tween != null && entry.Tween.IsActive())
            {
                tween = entry.Tween;
                return true;
            }

            tween = null;
            return false;
        }

        public void Stop(UiMotionChannel channel, UiMotionReplacementMode mode = UiMotionReplacementMode.Kill)
        {
            if (!entries.TryGetValue(channel, out Entry entry))
            {
                return;
            }

            Tween tween = entry.Tween;
            if (tween == null || !tween.IsActive())
            {
                RemoveIfCurrent(channel, tween);
                return;
            }

            switch (mode)
            {
                case UiMotionReplacementMode.Complete:
                    tween.Complete(true);
                    break;
                case UiMotionReplacementMode.Rewind:
                    tween.Rewind(true);
                    tween.Kill(false);
                    break;
                default:
                    tween.Kill(false);
                    break;
            }
        }

        public void HandleDisable()
        {
            using (UiMotionProfiler.Lifecycle.Auto())
            {
                CollectKeys();
                for (int i = 0; i < keyBuffer.Count; i++)
                {
                    UiMotionChannel channel = keyBuffer[i];
                    if (!entries.TryGetValue(channel, out Entry entry) || entry.Tween == null)
                    {
                        continue;
                    }

                    if (entry.DisableBehaviour == UiMotionDisableBehaviour.PauseAndResume && entry.Tween.IsActive())
                    {
                        entry.Tween.Pause();
                        entry.PausedByLifecycle = true;
                    }
                    else
                    {
                        entry.Tween.Kill(false);
                    }
                }
            }
        }

        public void HandleEnable()
        {
            using (UiMotionProfiler.Lifecycle.Auto())
            {
                foreach (Entry entry in entries.Values)
                {
                    if (entry.PausedByLifecycle && entry.Tween != null && entry.Tween.IsActive())
                    {
                        entry.PausedByLifecycle = false;
                        entry.Tween.Play();
                    }
                }
            }
        }

        public void KillOwned()
        {
            CollectKeys();
            for (int i = 0; i < keyBuffer.Count; i++)
            {
                Stop(keyBuffer[i]);
            }
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            KillOwned();
            disposed = true;
        }

        private void CollectKeys()
        {
            keyBuffer.Clear();
            foreach (UiMotionChannel channel in entries.Keys)
            {
                keyBuffer.Add(channel);
            }
        }

        private void RemoveIfCurrent(UiMotionChannel channel, Tween tween)
        {
            if (!entries.TryGetValue(channel, out Entry entry) || !ReferenceEquals(entry.Tween, tween))
            {
                return;
            }

            entry.Tween = null;
            entries.Remove(channel);
            UiMotionMetrics.NotifyTracked(-1);
        }
    }
}
