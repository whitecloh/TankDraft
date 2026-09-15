using UnityEngine;
namespace TankDraft.UI
{
    [RequireComponent(typeof(RectTransform))]
    public sealed class UiSafeArea : MonoBehaviour
    {
        [SerializeField] private RectTransform _target;
        private Rect _lastArea;
        private Vector2Int _lastSize;
        private void OnEnable() => Refresh();
        private void Update()
        {
            if (_lastArea != Screen.safeArea || _lastSize.x != Screen.width || _lastSize.y != Screen.height) Refresh();
        }
        public void Refresh()
        {
            if (!_target || Screen.width <= 0 || Screen.height <= 0) return;
            Apply(Screen.safeArea, new Vector2Int(Screen.width, Screen.height));
        }
        public void Apply(Rect area, Vector2Int size)
        {
            if (!_target || size.x <= 0 || size.y <= 0) return;
            _target.anchorMin = new Vector2(area.xMin/size.x, area.yMin/size.y);
            _target.anchorMax = new Vector2(area.xMax/size.x, area.yMax/size.y);
            _target.offsetMin = _target.offsetMax = Vector2.zero;
            _lastArea = area; _lastSize = size;
        }
    }
}
