using UnityEngine;

namespace TankDraft.Art.A1
{
    /// <summary>
    /// Authored visual-only settings for an A1 tank. This asset does not map progression,
    /// battle state, or TransformationStage to a tier.
    /// </summary>
    [CreateAssetMenu(menuName = "TankDraft/Art/A1 Tank Visual Config", fileName = "A1TankVisualConfig")]
    public sealed class A1TankVisualConfig : ScriptableObject
    {
        [SerializeField] private string _definitionId = "unit.heavy_tank";
        [SerializeField] private Color _friendlyColor = new Color(0.16f, 0.52f, 1f, 1f);
        [SerializeField] private Color _enemyColor = new Color(0.95f, 0.2f, 0.2f, 1f);
        [Range(1, 3)] [SerializeField] private int _defaultTier = 1;
        [SerializeField] private string[] _tierLabels = { "Tier 1", "Tier 2", "Tier 3" };

        public string DefinitionId => _definitionId;
        public Color FriendlyColor => _friendlyColor;
        public Color EnemyColor => _enemyColor;
        public int DefaultTier => Mathf.Clamp(_defaultTier, 1, 3);

        public string GetTierLabel(int tier)
        {
            if (tier < 1 || tier > 3 || _tierLabels == null || _tierLabels.Length < tier)
            {
                return string.Empty;
            }

            return _tierLabels[tier - 1] ?? string.Empty;
        }

        private void OnValidate()
        {
            _defaultTier = Mathf.Clamp(_defaultTier, 1, 3);
            if (_tierLabels == null || _tierLabels.Length != 3)
            {
                var labels = new string[3];
                if (_tierLabels != null)
                {
                    for (var index = 0; index < _tierLabels.Length && index < labels.Length; index++)
                    {
                        labels[index] = _tierLabels[index];
                    }
                }

                _tierLabels = labels;
            }

            for (var index = 0; index < _tierLabels.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(_tierLabels[index]))
                {
                    _tierLabels[index] = $"Tier {index + 1}";
                }
            }
        }
    }
}
