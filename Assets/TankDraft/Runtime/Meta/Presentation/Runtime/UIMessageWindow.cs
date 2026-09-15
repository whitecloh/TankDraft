using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Vareiko.Foundation.UI;
namespace TankDraft.UI
{
    public sealed class UIMessageWindow : UIWindow
    {
        [SerializeField] private TMP_Text _titleText, _bodyText, _closeLabel;
        [SerializeField] private UIButtonView _closeButton;
        public void Bind(string title, string body, string closeLabel, UnityAction close)
        {
            _titleText.text = title; _bodyText.text = body; _closeLabel.text = closeLabel;
            _closeButton.SetClickAction(close); _closeButton.gameObject.SetActive(close != null); Show();
        }
        public void Release() { if (_closeButton) _closeButton.ClearClickAction(); }
        private void OnDestroy() => Release();
    }
}
