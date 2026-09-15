using System;
using System.Collections.Generic;
using UnityEngine;

namespace TankDraft.Art.A1
{
    /// <summary>
    /// Reusable visual-only A1 tank view. It owns mesh selection, palette colour and local art poses;
    /// battle state and progression remain outside this component. Models import with +Z as forward.
    /// </summary>
    [ExecuteAlways]
    public sealed class A1TankVisual : MonoBehaviour
    {
        [Serializable]
        public sealed class TierBinding
        {
            public GameObject root;
            public Transform turret;
            public Transform barrelRecoil;
            public Transform muzzle;
            public Transform hit;
            public Renderer[] renderers;
        }

        private const string TeamColorProperty = "_TeamColor";
        private static readonly int TeamColorPropertyId = Shader.PropertyToID(TeamColorProperty);
        private static readonly int HitFlashPropertyId = Shader.PropertyToID("_HitFlash");

        [SerializeField] private A1TankVisualConfig _config;
        [Range(1, 3)] [SerializeField] private int _visualTier = 1;
        [SerializeField] private bool _enemyTeam;
        [SerializeField] private TierBinding[] _tiers = new TierBinding[3];

        private readonly Quaternion[] _turretBindRotations = new Quaternion[3];
        private readonly Vector3[] _barrelBindPositions = new Vector3[3];
        private readonly bool[] _hasTurretBindPose = new bool[3];
        private readonly bool[] _hasBarrelBindPose = new bool[3];
        private MaterialPropertyBlock _propertyBlock;
        private bool _initialized;
        private bool _hasTeamColor;
        private bool _needsConfigTeamColor;
        private bool _needsRefresh = true;
        private Color _teamColor;
        private float _yawDegrees;
        private float _recoilDistance;
        private float _hitFlash;

        public A1TankVisualConfig Config => _config;
        public int VisualTier => _visualTier;
        public bool IsEnemyTeam => _enemyTeam;
        public Transform ActiveMuzzle => GetActiveBinding()?.muzzle;
        public Transform ActiveHit => GetActiveBinding()?.hit;
        public Transform ActiveTurret => GetActiveBinding()?.turret;
        public Transform ActiveBarrel => GetActiveBinding()?.barrelRecoil;

        private void OnEnable()
        {
            InitializeOnce();
            RefreshPreview();
            _needsRefresh = true;
        }

        private void Awake()
        {
            InitializeOnce();
        }

#if UNITY_EDITOR
        private void Update()
        {
            if (!_needsRefresh)
            {
                return;
            }

            InitializeOnce();
            if (_needsConfigTeamColor && _config != null)
            {
                _teamColor = _enemyTeam ? _config.EnemyColor : _config.FriendlyColor;
                _hasTeamColor = true;
                _needsConfigTeamColor = false;
            }

            RefreshPreview();
            _needsRefresh = false;
        }
#endif

        private void OnValidate()
        {
            _visualTier = Mathf.Clamp(_visualTier, 1, 3);
            // Do not capture poses here: an inspector edit can happen after an authored rotation.
            _needsConfigTeamColor = true;
            _needsRefresh = true;
        }

        /// <summary>Authoring entry point for Unity MCP or editor tooling.</summary>
        public void Configure(A1TankVisualConfig config, TierBinding[] tiers)
        {
            _config = config;
            _tiers = tiers;
            _initialized = false;
            _hasTeamColor = false;
            _enemyTeam = false;
            _teamColor = default;
            _yawDegrees = 0f;
            _recoilDistance = 0f;
            _needsConfigTeamColor = false;
            _propertyBlock = null;
            Array.Clear(_hasTurretBindPose, 0, _hasTurretBindPose.Length);
            Array.Clear(_hasBarrelBindPose, 0, _hasBarrelBindPose.Length);
            InitializeOnce();
            _needsRefresh = true;
        }

        public void SetVisualTier(int tier)
        {
            if (tier < 1 || tier > 3)
            {
                throw new ArgumentOutOfRangeException(nameof(tier), tier, "Visual tier must be in range 1..3.");
            }

            InitializeOnce();
            _visualTier = tier;
            SetTierRootsActive();
            ApplyActivePose();
        }

        public void SetEnemyTeam(bool enemyTeam)
        {
            InitializeOnce();
            _enemyTeam = enemyTeam;
            if (_config != null)
            {
                SetTeamColor(enemyTeam ? _config.EnemyColor : _config.FriendlyColor);
            }
        }

        /// <summary>
        /// Sets _TeamColor through property blocks on every tier renderer, including inactive tiers,
        /// without creating material instances. The shared vertex-palette shader reserves mask alpha 0
        /// for the team-cap region.
        /// </summary>
        public void SetTeamColor(Color color)
        {
            InitializeOnce();
            _needsConfigTeamColor = false;
            if (_hasTeamColor && _teamColor == color) return;
            _teamColor = color;
            _hasTeamColor = true;
            ApplyTeamColor(color);
        }

        /// <summary>Flashes armor while the team cap and ink remain readable.</summary>
        public void SetFlash(float amount)
        {
            InitializeOnce();
            amount = Mathf.Clamp01(amount);
            if (Mathf.Approximately(_hitFlash, amount)) return;
            _hitFlash = amount;
            ApplyTeamColor(_teamColor);
        }

        /// <summary>Applies yaw from the captured local bind rotation around Unity's local +Y axis.</summary>
        public void SetTurretYaw(float yawDegrees)
        {
            InitializeOnce();
            _yawDegrees = yawDegrees;
            var tierIndex = _visualTier - 1;
            ApplyTurretYaw(tierIndex);
        }

        /// <summary>
        /// Positive distance retracts the barrel along local -Z from its captured bind pose.
        /// A1 mesh imports are expected to use +Z as model forward.
        /// </summary>
        public void SetRecoil(float distance)
        {
            InitializeOnce();
            _recoilDistance = distance;
            var tierIndex = _visualTier - 1;
            ApplyBarrelRecoil(tierIndex);
        }

        /// <summary>Returns this visual to its authored tier, friendly palette, and bind poses.</summary>
        public void ResetForPool()
        {
            InitializeOnce();
            _visualTier = _config != null ? _config.DefaultTier : 1;
            _enemyTeam = false;
            _yawDegrees = 0f;
            _recoilDistance = 0f;
            SetFlash(0f);
            _needsConfigTeamColor = false;
            ResetAllBindPoses();
            SetTierRootsActive();
            SetTeamColor(_config != null ? _config.FriendlyColor : Color.white);
        }

        /// <summary>Validates visual hierarchy only; it does not validate gameplay or catalog bindings.</summary>
        public bool Validate(out string message)
        {
            var errors = new List<string>();
            if (_config == null)
            {
                errors.Add("Visual config is required.");
            }

            if (_tiers == null || _tiers.Length != 3)
            {
                errors.Add("Exactly three tier bindings are required.");
                message = string.Join("\n", errors);
                return false;
            }

            var roots = new HashSet<Transform>();
            for (var index = 0; index < _tiers.Length; index++)
            {
                var binding = _tiers[index];
                if (binding == null || binding.root == null)
                {
                    errors.Add($"Tier {index + 1} has no root.");
                    continue;
                }

                var root = binding.root.transform;
                if (root == transform || !root.IsChildOf(transform))
                {
                    errors.Add($"Tier {index + 1} root must be a descendant of this visual transform.");
                }

                if (!roots.Add(root))
                {
                    errors.Add($"Tier {index + 1} root must be unique.");
                }

                for (var otherIndex = 0; otherIndex < _tiers.Length; otherIndex++)
                {
                    if (otherIndex == index || _tiers[otherIndex] == null || _tiers[otherIndex].root == null)
                    {
                        continue;
                    }

                    var otherRoot = _tiers[otherIndex].root.transform;
                    if (root.IsChildOf(otherRoot) || otherRoot.IsChildOf(root))
                    {
                        errors.Add($"Tier {index + 1} root must not be a descendant of another tier root.");
                        break;
                    }
                }

                ValidateAnchorHierarchy(binding, root, index, errors);
                ValidateRenderers(binding, root, index, errors);
            }

            message = string.Join("\n", errors);
            return errors.Count == 0;
        }

        private void InitializeOnce()
        {
            if (_initialized)
            {
                return;
            }

            CaptureBindPosesOnce();
            _visualTier = Mathf.Clamp(_visualTier, 1, 3);
            if (!_hasTeamColor && _config != null)
            {
                _teamColor = _enemyTeam ? _config.EnemyColor : _config.FriendlyColor;
                _hasTeamColor = true;
            }

            _initialized = true;
            RefreshPreview();
        }

        private void RefreshPreview()
        {
            SetTierRootsActive();
            if (_hasTeamColor)
            {
                ApplyTeamColor(_teamColor);
            }
        }

        private void CaptureBindPosesOnce()
        {
            for (var index = 0; index < 3; index++)
            {
                var binding = GetBinding(index);
                if (binding == null)
                {
                    continue;
                }

                if (binding.turret != null)
                {
                    _turretBindRotations[index] = binding.turret.localRotation;
                    _hasTurretBindPose[index] = true;
                }

                if (binding.barrelRecoil != null)
                {
                    _barrelBindPositions[index] = binding.barrelRecoil.localPosition;
                    _hasBarrelBindPose[index] = true;
                }
            }
        }

        private void ResetAllBindPoses()
        {
            for (var index = 0; index < 3; index++)
            {
                var binding = GetBinding(index);
                if (binding == null)
                {
                    continue;
                }

                if (binding.turret != null && HasTurretBindPose(index))
                {
                    binding.turret.localRotation = _turretBindRotations[index];
                }

                if (binding.barrelRecoil != null && HasBarrelBindPose(index))
                {
                    binding.barrelRecoil.localPosition = _barrelBindPositions[index];
                }
            }
        }

        private void ApplyActivePose()
        {
            var tierIndex = _visualTier - 1;
            ApplyTurretYaw(tierIndex);
            ApplyBarrelRecoil(tierIndex);
        }

        private void ApplyTurretYaw(int tierIndex)
        {
            var turret = GetBinding(tierIndex)?.turret;
            if (turret != null && HasTurretBindPose(tierIndex))
            {
                turret.localRotation = _turretBindRotations[tierIndex] * Quaternion.AngleAxis(_yawDegrees, Vector3.up);
            }
        }

        private void ApplyBarrelRecoil(int tierIndex)
        {
            var barrel = GetBinding(tierIndex)?.barrelRecoil;
            if (barrel != null && HasBarrelBindPose(tierIndex))
            {
                barrel.localPosition = _barrelBindPositions[tierIndex] - Vector3.forward * _recoilDistance;
            }
        }

        private void SetTierRootsActive()
        {
            for (var index = 0; index < 3; index++)
            {
                var root = GetBinding(index)?.root;
                if (root != null)
                {
                    root.SetActive(index == _visualTier - 1);
                }
            }
        }

        private void ApplyTeamColor(Color color)
        {
            _propertyBlock ??= new MaterialPropertyBlock();
            for (var tierIndex = 0; tierIndex < 3; tierIndex++)
            {
                var renderers = GetBinding(tierIndex)?.renderers;
                if (renderers == null)
                {
                    continue;
                }

                for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
                {
                    var renderer = renderers[rendererIndex];
                    if (renderer == null)
                    {
                        continue;
                    }

                    renderer.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(TeamColorPropertyId, color);
                    _propertyBlock.SetFloat(HitFlashPropertyId, _hitFlash);
                    renderer.SetPropertyBlock(_propertyBlock);
                }
            }
        }

        private TierBinding GetActiveBinding()
        {
            return GetBinding(_visualTier - 1);
        }

        private TierBinding GetBinding(int index)
        {
            return _tiers != null && index >= 0 && index < _tiers.Length ? _tiers[index] : null;
        }

        private bool HasTurretBindPose(int index)
        {
            return index >= 0 && index < _hasTurretBindPose.Length && _hasTurretBindPose[index];
        }

        private bool HasBarrelBindPose(int index)
        {
            return index >= 0 && index < _hasBarrelBindPose.Length && _hasBarrelBindPose[index];
        }

        private static void ValidateAnchorHierarchy(TierBinding binding, Transform root, int index, List<string> errors)
        {
            if (!IsSameOrChildOf(binding.turret, root))
            {
                errors.Add($"Tier {index + 1} turret must be under its root.");
            }

            if (!IsSameOrChildOf(binding.barrelRecoil, binding.turret))
            {
                errors.Add($"Tier {index + 1} barrel recoil must be under its turret.");
            }

            if (!IsSameOrChildOf(binding.muzzle, binding.barrelRecoil))
            {
                errors.Add($"Tier {index + 1} muzzle must be under its barrel recoil transform.");
            }

            if (!IsSameOrChildOf(binding.hit, root))
            {
                errors.Add($"Tier {index + 1} hit anchor must be under its root.");
            }
        }

        private static void ValidateRenderers(TierBinding binding, Transform root, int index, List<string> errors)
        {
            var actualRenderers = root.GetComponentsInChildren<Renderer>(true);
            if (actualRenderers.Length != 3)
            {
                errors.Add($"Tier {index + 1} must contain exactly three renderers.");
            }

            if (binding.renderers == null || binding.renderers.Length != actualRenderers.Length)
            {
                errors.Add($"Tier {index + 1} configured renderers must exactly match its hierarchy.");
                return;
            }

            var configured = new HashSet<Renderer>();
            for (var rendererIndex = 0; rendererIndex < binding.renderers.Length; rendererIndex++)
            {
                var renderer = binding.renderers[rendererIndex];
                if (renderer == null || !renderer.transform.IsChildOf(root) || !configured.Add(renderer))
                {
                    errors.Add($"Tier {index + 1} configured renderers must be unique descendants of its root.");
                }
            }

            for (var rendererIndex = 0; rendererIndex < actualRenderers.Length; rendererIndex++)
            {
                if (!configured.Contains(actualRenderers[rendererIndex]))
                {
                    errors.Add($"Tier {index + 1} configured renderers omit a hierarchy renderer.");
                    break;
                }
            }
        }

        private static bool IsSameOrChildOf(Transform child, Transform parent)
        {
            return child != null && parent != null && (child == parent || child.IsChildOf(parent));
        }
    }
}
