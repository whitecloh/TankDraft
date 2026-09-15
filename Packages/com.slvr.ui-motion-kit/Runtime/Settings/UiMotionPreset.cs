using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Reusable component-level override layered above the active theme.</summary>
    [CreateAssetMenu(fileName = "UiMotionPreset", menuName = "SLVR/UI Motion/Preset")]
    public sealed class UiMotionPreset : ScriptableObject
    {
        [SerializeField] private UiMotionTiming timing = UiMotionTiming.Normal;
        [SerializeField] private bool overrideEase;
        [SerializeField] private UiMotionEase ease = UiMotionEase.OutCubic;
        [SerializeField] private UiMotionEffectKind effect = UiMotionEffectKind.Translation;

        public UiMotionTiming Timing => timing;
        public UiMotionEase ResolveEase(UiMotionTheme theme) => overrideEase || theme == null
            ? ease
            : theme.StandardEase;
        public UiMotionEffectKind Effect => effect;
    }
}
