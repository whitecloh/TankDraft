using System;
using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    // Authored world-space HUD. Snapshot values only; no local combat timers or decisions.
    public sealed class BattleDefenseIndicators : MonoBehaviour
    {
        [SerializeField] private GameObject _shieldRoot, _magazineRoot, _blockRoot;
        [SerializeField] private Transform _shieldFill, _magazineFill;
        [SerializeField] private SpriteRenderer _magazineSprite;
        [SerializeField] private Color _ammoColor = new Color(1f, .72f, .22f);
        [SerializeField] private Color _reloadColor = new Color(.55f, .70f, 1f);
        private Vector3 _shieldScale, _magazineScale;
        private bool _cached;

        public bool ShieldVisible => _shieldRoot.activeSelf;
        public bool BlockVisible => _blockRoot.activeSelf;
        public bool MagazineVisible => _magazineRoot.activeSelf;
        public bool IsReloading { get; private set; }
        public float ShieldFraction { get; private set; }
        public float MagazineFraction { get; private set; }

        public void Validate()
        {
            if (!_shieldRoot || !_magazineRoot || !_blockRoot || !_shieldFill || !_magazineFill || !_magazineSprite)
                throw new InvalidOperationException(name + " requires prepared defense indicator references.");
        }

        private void Cache()
        {
            if (_cached) return;
            Validate();
            _shieldScale = _shieldFill.localScale;
            _magazineScale = _magazineFill.localScale;
            _cached = true;
        }

        public void Render(BattleEntityState state)
        {
            Cache();
            bool unit = state.Kind == BattleEntityKind.Unit && state.Hp > 0;
            _shieldRoot.SetActive(unit && state.ShieldHp > 0);
            _blockRoot.SetActive(unit && state.FirstHitBlocks > 0);
            _magazineRoot.SetActive(unit && state.MagazineSize > 0);
            ShieldFraction = state.ShieldMaxHp > 0 ? Mathf.Clamp01((float)state.ShieldHp / state.ShieldMaxHp) : 0;
            IsReloading = unit && state.ReloadRemaining > 0;
            MagazineFraction = IsReloading ? Mathf.Clamp01(1f - state.ReloadRemaining / state.ReloadDuration) :
                state.MagazineSize > 0 ? Mathf.Clamp01((float)state.Ammo / state.MagazineSize) : 0;
            _shieldFill.localScale = new Vector3(_shieldScale.x * ShieldFraction, _shieldScale.y, _shieldScale.z);
            _magazineFill.localScale = new Vector3(_magazineScale.x * MagazineFraction, _magazineScale.y, _magazineScale.z);
            _magazineSprite.color = IsReloading ? _reloadColor : _ammoColor;
        }

        public void Clear()
        {
            Cache();
            _shieldRoot.SetActive(false); _blockRoot.SetActive(false); _magazineRoot.SetActive(false);
            _shieldFill.localScale = _shieldScale; _magazineFill.localScale = _magazineScale;
            _magazineSprite.color = _ammoColor;
            ShieldFraction = MagazineFraction = 0; IsReloading = false;
        }
    }
}
