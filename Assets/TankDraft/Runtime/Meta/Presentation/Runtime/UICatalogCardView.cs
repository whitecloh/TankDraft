using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UICatalogCardView : UIItemView
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _subtitleText;
        [SerializeField] private Image _iconImage;
        [SerializeField] private GameObject _selectedState;
        [SerializeField] private UIButtonView _button;

        public void Bind(CardViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _titleText.text = model.Title;
            _subtitleText.text = model.Subtitle;
            _iconImage.sprite = model.Icon;
            _iconImage.enabled = model.Icon != null;
            _selectedState.SetActive(model.Selected);
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
            _titleText.text = string.Empty;
            _subtitleText.text = string.Empty;
            _iconImage.sprite = null;
            _iconImage.enabled = false;
            _selectedState.SetActive(false);
            _button.SetInteractable(false);
            _button.ClearClickAction();
        }

        private void ValidateReferences()
        {
            if (_titleText == null || _subtitleText == null || _iconImage == null || _selectedState == null || _button == null)
                throw new InvalidOperationException($"{name} requires title, subtitle, icon, selected-state, and button references.");
        }
    }
}
