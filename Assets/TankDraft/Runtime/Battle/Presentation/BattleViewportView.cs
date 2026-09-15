using System;
using UnityEngine;

namespace TankDraft.BattlePresentation
{
    public sealed class BattleViewportView : MonoBehaviour
    {
        [SerializeField]
        private Camera _camera;
        [SerializeField]
        private RectTransform _viewport;
        [SerializeField, Min(0)]
        private float _padding = .35f;
        private readonly Vector3[] _corners = new Vector3[4];
        private float _halfWidth, _halfHeight;
        public void ConfigureBounds(float halfWidth, float halfHeight)
        {
            if (!_camera || !_viewport)
                throw new InvalidOperationException("Battle viewport is missing camera/rect references.");
            _halfWidth = halfWidth;
            _halfHeight = halfHeight;
            Apply();
        }

        private void LateUpdate()
        {
            if (_halfWidth > 0)
                Apply();
        }

        private void Apply()
        {
            if (Screen.width <= 0 || Screen.height <= 0)
                return;
            _viewport.GetWorldCorners(_corners);
            var min = RectTransformUtility.WorldToScreenPoint(null, _corners[0]);
            var max = RectTransformUtility.WorldToScreenPoint(null, _corners[2]);
            float w = max.x - min.x, h = max.y - min.y;
            if (w <= 0 || h <= 0)
                return;
            _camera.rect = new Rect(min.x / Screen.width, min.y / Screen.height, w / Screen.width, h / Screen.height);
            _camera.orthographicSize = Mathf.Max(_halfHeight + _padding, (_halfWidth + _padding) / (w / h));
        }
    }
}