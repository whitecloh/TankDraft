namespace SLVR.UIMotion
{
    /// <summary>Creation policy applied consistently to every package-owned tween.</summary>
    public readonly struct UiMotionTweenPolicy
    {
        public UiMotionTweenPolicy(
            UiMotionEase ease,
            bool useUnscaledTime,
            bool recyclable = true,
            bool linkToOwner = true,
            UiMotionDisableBehaviour disableBehaviour = UiMotionDisableBehaviour.Kill)
        {
            Ease = ease;
            UseUnscaledTime = useUnscaledTime;
            Recyclable = recyclable;
            LinkToOwner = linkToOwner;
            DisableBehaviour = disableBehaviour;
        }

        public UiMotionEase Ease { get; }
        public bool UseUnscaledTime { get; }
        public bool Recyclable { get; }
        public bool LinkToOwner { get; }
        public UiMotionDisableBehaviour DisableBehaviour { get; }

        public static UiMotionTweenPolicy Interaction(bool useUnscaledTime = true) =>
            new UiMotionTweenPolicy(UiMotionEase.OutCubic, useUnscaledTime);

        public static UiMotionTweenPolicy Idle(bool useUnscaledTime = true) =>
            new UiMotionTweenPolicy(
                UiMotionEase.InOutCubic,
                useUnscaledTime,
                true,
                true,
                UiMotionDisableBehaviour.PauseAndResume);
    }
}
