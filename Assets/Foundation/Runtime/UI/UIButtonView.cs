using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace Vareiko.Foundation.UI
{
    public class UIButtonView : UIElement
    {
        [SerializeField] private Button _button;
        [SerializeField] private bool _interactable = true;
        [SerializeField] private UnityEvent _onClicked = new UnityEvent();

        private UnityAction _ownedClickAction;
        private bool _buttonSubscribed;

        public Button Button => _button;
        public bool Interactable => _interactable;
        public UnityEvent OnClicked => _onClicked;

        public void SetButton(Button button)
        {
            if (_button == button)
            {
                ApplyButtonInteractable();
                SubscribeButton();
                return;
            }

            UnsubscribeButton();
            _button = button;
            ApplyButtonInteractable();
            SubscribeButton();
        }

        public void SetClickAction(UnityAction action)
        {
            ClearClickAction();
            _ownedClickAction = action;
            if (_ownedClickAction != null)
            {
                _onClicked.AddListener(_ownedClickAction);
            }
        }

        public void ClearClickAction()
        {
            if (_ownedClickAction == null)
            {
                return;
            }

            _onClicked.RemoveListener(_ownedClickAction);
            _ownedClickAction = null;
        }

        public virtual void SetInteractable(bool interactable)
        {
            _interactable = interactable;
            ApplyButtonInteractable();
        }

        public virtual void Click()
        {
            if (!_interactable || (_button != null && !_button.interactable))
            {
                return;
            }

            _onClicked?.Invoke();
        }

        protected virtual void Reset()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }
        }

        protected override void Awake()
        {
            if (_button == null)
            {
                _button = GetComponent<Button>();
            }

            if (_button == null)
            {
                base.Awake();
            }

            ApplyButtonInteractable();
        }

        protected virtual void OnEnable()
        {
            SubscribeButton();
        }

        protected virtual void OnDisable()
        {
            UnsubscribeButton();
        }

        protected virtual void OnDestroy()
        {
            UnsubscribeButton();
            ClearClickAction();
        }

        private void SubscribeButton()
        {
            // A runtime owner can configure this view from its own OnEnable before Unity invokes
            // UIButtonView.OnEnable. In that narrow activation window isActiveAndEnabled is still
            // false even though the component is enabled on an active hierarchy, which used to
            // leave Button.onClick unsubscribed until a later rebind. Check the actual input
            // prerequisites instead so cold Player activation is deterministic.
            if (_buttonSubscribed || _button == null || !enabled || !gameObject.activeInHierarchy)
            {
                return;
            }

            _button.onClick.AddListener(Click);
            _buttonSubscribed = true;
        }

        private void UnsubscribeButton()
        {
            if (!_buttonSubscribed || _button == null)
            {
                _buttonSubscribed = false;
                return;
            }

            _button.onClick.RemoveListener(Click);
            _buttonSubscribed = false;
        }

        private void ApplyButtonInteractable()
        {
            if (_button == null)
            {
                return;
            }

            // Interaction gating must not make authored UI pale or transparent. Unity's default
            // ColorBlock uses a half-alpha disabled color, while Chibi Arena communicates the
            // unavailable state through the authored sprite/content. Keep Button.interactable as
            // the input/accessibility gate, but preserve the normal tint in the disabled state.
            ColorBlock colors = _button.colors;
            if (colors.disabledColor != colors.normalColor)
            {
                colors.disabledColor = colors.normalColor;
                _button.colors = colors;
            }

            if (_button.interactable != _interactable)
            {
                _button.interactable = _interactable;
            }
        }
    }
}
