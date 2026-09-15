using System;
using DG.Tweening;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Small fluent wrapper that keeps sequence creation on the shared policy path.</summary>
    public sealed class UiMotionSequenceBuilder
    {
        public UiMotionSequenceBuilder(Component owner, UiMotionTweenPolicy policy)
        {
            Sequence = UiMotionTweenFactory.Sequence(owner, policy);
        }

        public Sequence Sequence { get; }

        public UiMotionSequenceBuilder Append(Tween tween)
        {
            if (tween == null) throw new ArgumentNullException(nameof(tween));
            Sequence.Append(tween);
            return this;
        }

        public UiMotionSequenceBuilder Join(Tween tween)
        {
            if (tween == null) throw new ArgumentNullException(nameof(tween));
            Sequence.Join(tween);
            return this;
        }

        public UiMotionSequenceBuilder AppendInterval(float seconds)
        {
            Sequence.AppendInterval(Mathf.Max(0f, seconds));
            return this;
        }

        public UiMotionSequenceBuilder AppendCallback(TweenCallback callback)
        {
            Sequence.AppendCallback(callback);
            return this;
        }
    }
}
