using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIRewardSlotView : UIItemView
    {
        [SerializeField] private TMP_Text _captionText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private UIButtonView _button;

        public void Bind(RewardSlotViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _captionText.text = model.Caption;
            _iconImage.sprite = model.Icon;
            _iconImage.enabled = model.Icon != null;
            _button.SetInteractable(model.Interactable);
        }

        public void SetClickAction(UnityAction action)
        {
            ValidateReferences();
            _button.SetClickAction(action);
        }

        public void Clear()
        {
            ValidateReferences();
            _captionText.text = string.Empty;
            _iconImage.sprite = null;
            _iconImage.enabled = false;
            _button.SetInteractable(false);
            _button.ClearClickAction();
        }

        private void ValidateReferences()
        {
            if (_captionText == null || _iconImage == null || _button == null)
                throw new InvalidOperationException($"{name} requires caption, icon, and button references.");
        }
    }
}
