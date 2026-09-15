using TankDraft.Contracts.Battle;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    public sealed class BattleEffectView : MonoBehaviour
    {
        [SerializeField]
        private Transform _visualRoot;
        [SerializeField]
        private SpriteRenderer[] _renderers;
        [SerializeField, Range(0, 1)]
        private float _opacity = .45f;
        [SerializeField, Min(0)]
        private float _expansion = .3f;
        private float _started, _duration, _diameter;
        private bool _alive, _cached;
        private Color _color;
        private Vector3 _authoredScale;
        private Color[] _authoredColors;
        private void Cache()
        {
            if (_cached)
                return;
            if (!_visualRoot || _renderers == null || _renderers.Length == 0)
                throw new System.InvalidOperationException(name + " lacks effect bindings.");
            _authoredScale = _visualRoot.localScale;
            _authoredColors = new Color[_renderers.Length];
            for (int i = 0; i < _renderers.Length; i++)
            {
                if (!_renderers[i])
                    throw new System.InvalidOperationException(name + " has null renderer.");
                _authoredColors[i] = _renderers[i].color;
            }

            _cached = true;
        }

        public void Play(BattleVec pos, float radius, Color color, float now, float duration)
        {
            Cache();
            _started = now;
            _duration = Mathf.Max(.001f, duration);
            _diameter = Mathf.Max(.01f, radius * 2);
            _color = color;
            _alive = true;
            transform.position = new Vector3(pos.X, pos.Y, 0);
            _visualRoot.localScale = _authoredScale * _diameter;
            Set(1);
            gameObject.SetActive(true);
        }

        public bool Tick(float now)
        {
            if (!_alive)
                return false;
            float progress = Mathf.Clamp01((now - _started) / _duration);
            _visualRoot.localScale = _authoredScale * (_diameter * (1 + _expansion * progress));
            Set(1 - progress);
            if (progress < 1)
                return true;
            Clear();
            return false;
        }

        public void Clear()
        {
            Cache();
            _alive = false;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            _visualRoot.localScale = _authoredScale;
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].color = _authoredColors[i];
            gameObject.SetActive(false);
        }

        private void Set(float alpha)
        {
            for (int i = 0; i < _renderers.Length; i++)
                _renderers[i].color = new Color(_color.r, _color.g, _color.b, _authoredColors[i].a * _opacity * alpha);
        }
    }
}