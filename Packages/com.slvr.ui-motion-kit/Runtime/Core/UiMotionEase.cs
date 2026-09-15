using DG.Tweening;

namespace SLVR.UIMotion
{
    /// <summary>Stable package-level easing vocabulary.</summary>
    public enum UiMotionEase
    {
        Linear = 0,
        InQuad = 1,
        OutQuad = 2,
        InOutQuad = 3,
        OutCubic = 4,
        InOutCubic = 5,
        OutBack = 6,
        OutElastic = 7,
        InOutSine = 8,
    }

    public static class UiMotionEaseExtensions
    {
        public static Ease ToDotweenEase(this UiMotionEase ease)
        {
            switch (ease)
            {
                case UiMotionEase.Linear: return Ease.Linear;
                case UiMotionEase.InQuad: return Ease.InQuad;
                case UiMotionEase.OutQuad: return Ease.OutQuad;
                case UiMotionEase.InOutQuad: return Ease.InOutQuad;
                case UiMotionEase.OutCubic: return Ease.OutCubic;
                case UiMotionEase.InOutCubic: return Ease.InOutCubic;
                case UiMotionEase.OutBack: return Ease.OutBack;
                case UiMotionEase.OutElastic: return Ease.OutElastic;
                case UiMotionEase.InOutSine: return Ease.InOutSine;
                default: return Ease.OutCubic;
            }
        }
    }
}
