using System;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    public sealed class BattleEntityView : MonoBehaviour
    {
        [SerializeField]
        private Transform _visualRoot, _turretRoot, _muzzleAnchor, _hitAnchor, _healthFill;
        [SerializeField]
        private SpriteRenderer[] _teamRenderers;
        [SerializeField] private BattleTankModelView _modelVisual;
        [SerializeField] private BattleDefenseIndicators _defenseIndicators;
        private float _flashSeconds = .08f;
        private float _configuredVisualScale = 1f;
        private Vector3 _configuredVisualOffset;
        private float _transformedVisualScale = 1f, _transformationSeconds = .25f;
        private float _transformationAt;
        private int _transformationStage;
        private int _runtimeId = -1;
        private Color _teamColor;
        private float _flashUntil;
        private float _spawnStartAt, _spawnEndAt, _spawnStartScale, _spawnOvershoot;
        private bool _spawnActive;
        private bool _formationMoving;
        private Vector3 _formationFrom, _formationTo;
        private float _formationStarted, _formationDuration;
        private bool _cached, _hasPose;
        private Vector3 _basePosition, _baseScale, _visualPosition, _visualScale, _healthScale;
        private Quaternion _baseRotation, _visualRotation, _turretRotation;
        private Color[] _rendererColors;
        public Transform VisualRoot => _visualRoot;
        public BattleTankModelView ModelVisual => _modelVisual;
        public Transform MuzzleAnchor => _modelVisual != null && _modelVisual.ActiveMuzzle != null ? _modelVisual.ActiveMuzzle : _muzzleAnchor;
        public Transform HitAnchor => _modelVisual != null && _modelVisual.ActiveHit != null ? _modelVisual.ActiveHit : _hitAnchor;
        public int RuntimeId => _runtimeId;

        private void Awake() => CacheAuthored();
        public void Validate()
        {
            if (_visualRoot == null)
                throw new InvalidOperationException(name + " requires visual root.");
            if (_modelVisual != null)
            {
                _modelVisual.Validate();
                return;
            }
            if (_teamRenderers == null || _teamRenderers.Length == 0)
                throw new InvalidOperationException(name + " requires team renderers when no model visual is assigned.");
            for (var i = 0; i < _teamRenderers.Length; i++)
                if (_teamRenderers[i] == null)
                    throw new InvalidOperationException(name + " contains a null team renderer.");
        }

        public void ValidateFor(BattleEntityKind kind)
        {
            Validate();
            if (kind == BattleEntityKind.Unit && (MuzzleAnchor == null || HitAnchor == null || _healthFill == null))
                throw new InvalidOperationException(name + " unit view requires muzzle, hit, and health anchors.");
            if (kind == BattleEntityKind.Unit)
            {
                if (_defenseIndicators == null) throw new InvalidOperationException(name + " requires prepared defense indicators.");
                _defenseIndicators.Validate();
            }
            if (kind == BattleEntityKind.Projectile && HitAnchor == null)
                throw new InvalidOperationException(name + " projectile view requires hit anchor.");
        }

        public void Bind(int id, Color color)
        {
            CacheAuthored();
            _runtimeId = id;
            _teamColor = color;
            CancelSpawn();
            if (_defenseIndicators != null) _defenseIndicators.Clear();
            if (_modelVisual != null) _modelVisual.Bind(color);
            SetTint(color, 1f);
            gameObject.SetActive(true);
        }

        public void ConfigurePresentation(BattleViewCatalogAsset.Entry entry, BattlePresentationSettings settings)
        {
            _configuredVisualScale = entry.visualScale;
            _configuredVisualOffset = new Vector3(entry.visualOffset.x, entry.visualOffset.y, 0f);
            _flashSeconds = settings.HitFlashSeconds;
            _transformedVisualScale = entry.transformedVisualScale;
            _transformationSeconds = settings.TransformationSeconds;
        }

        public void PlaySpawn(float now, float delay, BattlePresentationSettings settings)
        {
            if (settings == null)
                throw new ArgumentNullException(nameof(settings));
            CacheAuthored();
            _spawnStartAt = now + Mathf.Max(0f, delay);
            _spawnEndAt = _spawnStartAt + settings.UnitSpawnSeconds;
            _spawnStartScale = settings.UnitSpawnStartScale;
            _spawnOvershoot = settings.UnitSpawnOvershoot;
            _spawnActive = true;
        }

        // Identity may change when an authoritative draft rebuild inserts an earlier group.
        // Preserve the pooled view and its current animation instead of binding another unit.
        public void RebindPreparation(int id, BattleVec target, float now, float duration)
        {
            _runtimeId = id;
            var destination = new Vector3(target.X, target.Y, 0f);
            if (_formationMoving && (_formationTo - destination).sqrMagnitude < .000001f) return;
            if ((transform.position - destination).sqrMagnitude < .000001f) return;
            _formationFrom = transform.position;
            _formationTo = destination;
            _formationStarted = now;
            _formationDuration = duration;
            _formationMoving = true;
        }

        public void FinishPreparation() => _formationMoving = false;

        public void Render(BattleEntityState state, float alpha, float visualTime)
        {
            CacheAuthored();
            _visualRoot.localPosition = _visualPosition + _configuredVisualOffset;
            if (state.TransformationStage != _transformationStage)
            {
                _transformationStage = state.TransformationStage;
                // Reconnect renders the current stage directly; only an observed transition animates.
                _transformationAt = _hasPose ? visualTime : visualTime - _transformationSeconds;
                if (_hasPose && _transformationStage > 0) Flash(visualTime);
            }
            var position = state.PreviousPosition + (state.Position - state.PreviousPosition) * Mathf.Clamp01(alpha);
            var renderedPosition = new Vector3(position.X, position.Y, 0f);
            if (_formationMoving)
            {
                float progress = Mathf.Clamp01((visualTime - _formationStarted) / Mathf.Max(.001f, _formationDuration));
                renderedPosition = Vector3.Lerp(_formationFrom, _formationTo, Smooth(progress));
                if (progress >= 1f) _formationMoving = false;
            }
            transform.position = renderedPosition;
            if (state.Kind == BattleEntityKind.Zone)
            {
                var d = state.Radius * 2f * _configuredVisualScale;
                _visualRoot.localScale = new Vector3(_visualScale.x * d, _visualScale.y * d, _visualScale.z);
            }
            else
            {
                if (state.Kind == BattleEntityKind.Unit)
                {
                    var movement = state.Position - state.PreviousPosition;
                    if (_modelVisual != null)
                        _modelVisual.Render(state, visualTime, _hasPose);
                    else
                    {
                        if (!_hasPose || movement.LengthSquared > .00001f)
                            _visualRoot.rotation = Quaternion.Euler(0f, 0f, Angle(movement.LengthSquared > .00001f ? movement : state.Facing)) * _visualRotation;
                        if (_turretRoot != null)
                            _turretRoot.rotation = Quaternion.Euler(0f, 0f, Angle(state.Facing)) * _turretRotation;
                    }
                }
                else if (state.Kind == BattleEntityKind.Projectile)
                    _visualRoot.rotation = Quaternion.Euler(0f, 0f, Angle(state.Facing)) * _visualRotation;

                if (state.Kind == BattleEntityKind.Unit)
                    ApplySpawnScale(visualTime);
                else
                    _visualRoot.localScale = new Vector3(_visualScale.x * _configuredVisualScale, _visualScale.y * _configuredVisualScale, _visualScale.z);
            }

            _hasPose = true;
            if (_healthFill != null)
            {
                var hp = state.MaxHp > 0 ? Mathf.Clamp01((float)state.Hp / state.MaxHp) : 0f;
                _healthFill.localScale = new Vector3(_healthScale.x * hp, _healthScale.y, _healthScale.z);
            }

            if (_defenseIndicators != null) _defenseIndicators.Render(state);
            if (_modelVisual != null)
            {
                _modelVisual.SetFlash(_flashUntil > visualTime);
                SetTint(_teamColor, state.Kind == BattleEntityKind.Zone ? 1f - Mathf.Clamp01(state.Progress) : 1f);
            }
            else
                SetTint(_flashUntil > visualTime ? Color.white : _teamColor, state.Kind == BattleEntityKind.Zone ? 1f - Mathf.Clamp01(state.Progress) : 1f);
        }

        public void Flash(float visualTime)
        {
            _flashUntil = visualTime + _flashSeconds;
            if (_modelVisual != null) _modelVisual.SetFlash(true);
            else SetTint(Color.white, 1f);
        }

        public void PlayShot(float visualTime)
        {
            if (_modelVisual != null) _modelVisual.PlayShot(visualTime);
        }

        public void Clear()
        {
            CacheAuthored();
            _runtimeId = -1;
            _configuredVisualScale = 1f;
            _configuredVisualOffset = Vector3.zero;
            _transformationStage = 0;
            _transformationAt = 0f;
            _formationMoving = false;
            _flashUntil = 0f;
            if (_defenseIndicators != null) _defenseIndicators.Clear();
            if (_modelVisual != null) _modelVisual.ResetForPool();
            CancelSpawn();
            _hasPose = false;
            transform.localPosition = _basePosition;
            transform.localRotation = _baseRotation;
            transform.localScale = _baseScale;
            _visualRoot.localPosition = _visualPosition;
            _visualRoot.localRotation = _visualRotation;
            _visualRoot.localScale = _visualScale;
            _hasPose = false;
            if (_turretRoot != null)
                _turretRoot.localRotation = _turretRotation;
            _hasPose = false;
            if (_healthFill != null)
                _healthFill.localScale = _healthScale;
            RestoreColors();
            gameObject.SetActive(false);
        }

        private void ApplySpawnScale(float now)
        {
            float multiplier = 1f;
            if (_spawnActive)
            {
                if (now < _spawnStartAt)
                    multiplier = _spawnStartScale;
                else if (now < _spawnEndAt)
                {
                    float progress = Mathf.Clamp01((now - _spawnStartAt) / Mathf.Max(.0001f, _spawnEndAt - _spawnStartAt));
                    if (progress < .72f)
                        multiplier = Mathf.Lerp(_spawnStartScale, _spawnOvershoot, EaseOut(progress / .72f));
                    else
                        multiplier = Mathf.Lerp(_spawnOvershoot, 1f, Smooth((progress - .72f) / .28f));
                }
                else
                    CancelSpawn();
            }

            if (_transformationStage > 0)
                multiplier *= Mathf.Lerp(1f, _transformedVisualScale, Smooth(Mathf.Clamp01((now - _transformationAt) / _transformationSeconds)));
            var scale = multiplier * _configuredVisualScale;
            _visualRoot.localScale = _modelVisual != null
                ? new Vector3(_visualScale.x * scale, _visualScale.y * scale, _visualScale.z * scale)
                : new Vector3(_visualScale.x * scale, _visualScale.y * scale, _visualScale.z);
        }

        private void CancelSpawn()
        {
            _spawnActive = false;
            _spawnStartAt = _spawnEndAt = 0f;
            _spawnStartScale = _spawnOvershoot = 1f;
        }

        private static float EaseOut(float value)
        {
            value = Mathf.Clamp01(value);
            return 1f - (1f - value) * (1f - value) * (1f - value);
        }

        private static float Smooth(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void CacheAuthored()
        {
            if (_cached)
                return;
            Validate();
            _cached = true;
            _basePosition = transform.localPosition;
            _baseRotation = transform.localRotation;
            _baseScale = transform.localScale;
            _visualPosition = _visualRoot.localPosition;
            _visualRotation = _visualRoot.localRotation;
            _visualScale = _visualRoot.localScale;
            _turretRotation = _turretRoot != null ? _turretRoot.localRotation : Quaternion.identity;
            _healthScale = _healthFill != null ? _healthFill.localScale : Vector3.one;
            if (_teamRenderers != null && _teamRenderers.Length > 0)
            {
                _rendererColors = new Color[_teamRenderers.Length];
                for (var i = 0; i < _teamRenderers.Length; i++)
                    _rendererColors[i] = _teamRenderers[i].color;
            }
        }

        private static float Angle(BattleVec direction) => Mathf.Atan2(direction.Y, direction.X) * Mathf.Rad2Deg - 90f;
        private void SetTint(Color color, float alpha)
        {
            if (_modelVisual != null)
            {
                _modelVisual.SetTint(color);
                return;
            }
            if (_rendererColors == null)
                return;
            for (var i = 0; i < _teamRenderers.Length; i++)
                _teamRenderers[i].color = new Color(color.r, color.g, color.b, _rendererColors[i].a * alpha);
        }

        private void RestoreColors()
        {
            if (_rendererColors == null)
                return;
            for (var i = 0; i < _teamRenderers.Length; i++)
                _teamRenderers[i].color = _rendererColors[i];
        }
    }
}
