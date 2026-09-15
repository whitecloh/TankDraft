using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Controls optional visual work for a quality tier.</summary>
    [CreateAssetMenu(fileName = "UiMotionQuality", menuName = "SLVR/UI Motion/Quality Profile")]
    public sealed class UiMotionQualityProfile : ScriptableObject
    {
        [SerializeField] private UiMotionQualityTier tier = UiMotionQualityTier.Medium;
        [SerializeField, Min(0)] private int currencyIconLimit = 12;
        [SerializeField] private bool particlesEnabled = true;
        [SerializeField] private bool ambientShadersEnabled = true;

        public UiMotionQualityTier Tier => tier;
        public int CurrencyIconLimit => Mathf.Max(0, currencyIconLimit);
        public bool ParticlesEnabled => particlesEnabled;
        public bool AmbientShadersEnabled => ambientShadersEnabled;
    }
}
