using System;
using TankDraft.Art.A1;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    /// <summary>
    /// Presentation-only bridge from the existing XY battle plane to an A1 +Z-forward tank model.
    /// It does not read or change battle rules, progression, or catalog selection.
    /// </summary>
    public sealed class BattleTankModelView : MonoBehaviour
    {
        [SerializeField] private A1TankVisual _model;
        [SerializeField] private Transform _projectionRoot;
        [Range(45f, 90f)] [SerializeField] private float _pitchDegrees = 65f;
        [SerializeField] private float _recoilDistance = .12f;
        [SerializeField] private float _recoilSeconds = .14f;
        [SerializeField] private GameObject _muzzleFlash;
        [SerializeField] private float _muzzleFlashSeconds = .045f;

        private Quaternion _modelBindRotation;
        private float _bodyYaw;
        private float _shotAt = float.NegativeInfinity;
        private bool _cached;
        private bool _muzzleFlashActive;

        public A1TankVisual Model => _model;
        public Transform ProjectionRoot => _projectionRoot;
        public Transform ActiveMuzzle => _model != null ? _model.ActiveMuzzle : null;
        public Transform ActiveHit => _model != null ? _model.ActiveHit : null;
        public Transform ActiveTurret => _model != null ? _model.ActiveTurret : null;
        public Transform ActiveBarrel => _model != null ? _model.ActiveBarrel : null;

        private void Awake() => CacheAuthored();

        public void Bind(Color teamColor)
        {
            CacheAuthored();
            ResetForPool();
            _model.SetTeamColor(teamColor);
        }

        public void SetTint(Color teamColor)
        {
            CacheAuthored();
            _model.SetTeamColor(teamColor);
        }

        public void SetFlash(bool active)
        {
            if (_model != null)
                _model.SetFlash(active ? 1f : 0f);
        }

        public void PlayShot(float now)
        {
            _shotAt = now;
        }

        public void Render(BattleEntityState state, float visualTime, bool hasPose)
        {
            CacheAuthored();
            var movement = state.Position - state.PreviousPosition;
            if (!hasPose || movement.LengthSquared > .00001f)
            {
                var direction = movement.LengthSquared > .00001f ? movement : state.Facing;
                _bodyYaw = ProjectedYaw(direction);
            }

            _model.transform.localRotation = _modelBindRotation * Quaternion.Euler(0f, _bodyYaw, 0f);
            var aimYaw = ProjectedYaw(state.Facing);
            _model.SetTurretYaw(aimYaw - _bodyYaw);

            var recoil = _recoilDistance * Mathf.Max(0f, 1f - (visualTime - _shotAt) / _recoilSeconds);
            _model.SetRecoil(recoil);
            UpdateMuzzleFlash(visualTime);
        }

        public void ResetForPool()
        {
            CacheAuthored();
            _bodyYaw = 0f;
            _shotAt = float.NegativeInfinity;
            _projectionRoot.localRotation = Quaternion.Euler(-_pitchDegrees, 0f, 0f);
            _model.transform.localRotation = _modelBindRotation;
            _model.ResetForPool();
            SetFlash(false);
            SetMuzzleFlashActive(false);
        }

        public void Validate()
        {
            if (_model == null || _projectionRoot == null)
                throw new InvalidOperationException(name + " requires model and projection root.");
            if (_projectionRoot == transform || !_projectionRoot.IsChildOf(transform))
                throw new InvalidOperationException(name + " projection root must be a child of the adapter.");
            if (_model.transform == _projectionRoot || !_model.transform.IsChildOf(_projectionRoot))
                throw new InvalidOperationException(name + " model must be under projection root.");
            if (_pitchDegrees < 45f || _pitchDegrees > 90f || _recoilDistance < 0f || _recoilSeconds <= 0f || _muzzleFlashSeconds <= 0f)
                throw new InvalidOperationException(name + " has invalid pitch or shot timing.");
            if (_muzzleFlash != null && !_muzzleFlash.transform.IsChildOf(transform))
                throw new InvalidOperationException(name + " muzzle flash must be a child of the adapter.");
            if (_muzzleFlash != null && _muzzleFlash.transform.IsChildOf(_model.transform))
                throw new InvalidOperationException(name + " muzzle flash must stay outside the A1 model root.");
            if (!_model.Validate(out var message))
                throw new InvalidOperationException(name + " model visual is invalid: " + message);
        }

        private void CacheAuthored()
        {
            if (_cached)
                return;
            Validate();
            _modelBindRotation = _model.transform.localRotation;
            _cached = true;
        }

        private float ProjectedYaw(BattleVec direction)
        {
            var sine = Mathf.Sin(_pitchDegrees * Mathf.Deg2Rad);
            return Mathf.Atan2(direction.X, direction.Y / Mathf.Max(.0001f, sine)) * Mathf.Rad2Deg;
        }

        private void UpdateMuzzleFlash(float now)
        {
            var age = now - _shotAt;
            var visible = age >= 0f && age <= _muzzleFlashSeconds;
            if (visible && _muzzleFlash != null)
            {
                var muzzle = ActiveMuzzle;
                if (muzzle != null)
                    _muzzleFlash.transform.position = muzzle.position;
            }

            SetMuzzleFlashActive(visible);
        }

        private void SetMuzzleFlashActive(bool active)
        {
            if (_muzzleFlash == null || (_muzzleFlashActive == active && _muzzleFlash.activeSelf == active))
                return;
            _muzzleFlash.SetActive(active);
            _muzzleFlashActive = active;
        }
    }
}
