using System;
using UnityEngine;

namespace Vareiko.Foundation.UI
{
    public class UIElement : MonoBehaviour
    {
        [SerializeField] private string _id;
        [SerializeField] private bool _hideOnAwake = true;
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private bool _disableRaycastsWhenHidden = true;

        public event Action<UIElement, bool> VisibilityChanged;

        public string Id => _id;
        public bool IsVisible => gameObject.activeSelf;

        protected virtual void Awake()
        {
            if (_hideOnAwake)
            {
                SetVisible(false, true);
            }
        }

        public virtual void Show(bool instant = true)
        {
            SetVisible(true, instant);
        }

        public virtual void Hide(bool instant = true)
        {
            SetVisible(false, instant);
        }

        protected virtual void SetVisible(bool visible, bool instant)
        {
            if (IsVisible == visible)
            {
                return;
            }

            if (_canvasGroup == null)
            {
                gameObject.SetActive(visible);
                VisibilityChanged?.Invoke(this, visible);
                return;
            }

            gameObject.SetActive(true);
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.interactable = visible;

            if (_disableRaycastsWhenHidden)
            {
                _canvasGroup.blocksRaycasts = visible;
            }

            if (!visible)
            {
                gameObject.SetActive(false);
            }

            VisibilityChanged?.Invoke(this, visible);
        }
    }
}
