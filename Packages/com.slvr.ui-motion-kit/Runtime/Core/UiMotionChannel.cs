namespace SLVR.UIMotion
{
    /// <summary>Semantic lane in which only one tween can own a visual property group.</summary>
    public enum UiMotionChannel
    {
        Visibility = 0,
        Interaction = 1,
        Selection = 2,
        Attention = 3,
        Value = 4,
        Idle = 5,
        Custom0 = 100,
        Custom1 = 101,
        Custom2 = 102,
        Custom3 = 103,
    }

    public enum UiMotionDisableBehaviour
    {
        Kill = 0,
        PauseAndResume = 1,
    }

    public enum UiMotionReplacementMode
    {
        Kill = 0,
        Complete = 1,
        Rewind = 2,
    }
}
