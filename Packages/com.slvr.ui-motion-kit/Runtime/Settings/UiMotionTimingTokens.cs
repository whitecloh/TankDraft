using System;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Semantic timing slots shared by motion presets.</summary>
    public enum UiMotionTiming
    {
        Immediate = 0,
        Fast = 1,
        Normal = 2,
        Slow = 3,
        Emphasis = 4,
    }

    /// <summary>Serializable duration values in seconds.</summary>
    [Serializable]
    public struct UiMotionTimingTokens
    {
        [SerializeField, Min(0f)] private float immediate;
        [SerializeField, Min(0f)] private float fast;
        [SerializeField, Min(0f)] private float normal;
        [SerializeField, Min(0f)] private float slow;
        [SerializeField, Min(0f)] private float emphasis;

        public UiMotionTimingTokens(float immediate, float fast, float normal, float slow, float emphasis)
        {
            this.immediate = Mathf.Max(0f, immediate);
            this.fast = Mathf.Max(0f, fast);
            this.normal = Mathf.Max(0f, normal);
            this.slow = Mathf.Max(0f, slow);
            this.emphasis = Mathf.Max(0f, emphasis);
        }

        /// <summary>Returns the duration assigned to a semantic timing slot.</summary>
        public float Get(UiMotionTiming timing)
        {
            switch (timing)
            {
                case UiMotionTiming.Immediate: return immediate;
                case UiMotionTiming.Fast: return fast;
                case UiMotionTiming.Normal: return normal;
                case UiMotionTiming.Slow: return slow;
                case UiMotionTiming.Emphasis: return emphasis;
                default: return normal;
            }
        }

        public static UiMotionTimingTokens Balanced => new UiMotionTimingTokens(0f, 0.1f, 0.2f, 0.32f, 0.48f);
    }
}
