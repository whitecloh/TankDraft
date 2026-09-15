using UnityEngine;
namespace TankDraft.UI
{
    // Preserves the authored portrait composition inside the available safe-area region.
    public sealed class UiFitAuthoredRect : MonoBehaviour
    {
        [SerializeField] private RectTransform _available, _content;
        [SerializeField] private Vector2 _designSize = new Vector2(576,1280);
        private bool _dirty;
        private void OnEnable() => _dirty=true;
        private void OnRectTransformDimensionsChange() => _dirty=true;
        private void LateUpdate() { if(_dirty) { _dirty=false;Refresh(); } }
        public void Refresh()
        {
            if(!_available || !_content || _designSize.x<=0 || _designSize.y<=0) return;
            var size=_available.rect.size;
            float scale=Mathf.Min(1,Mathf.Min(size.x/_designSize.x,size.y/_designSize.y));
            _content.sizeDelta=_designSize;_content.localScale=Vector3.one*Mathf.Max(0,scale);
        }
    }
}
