using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UICardDetailsWindow : UIWindow
    {
        [SerializeField] private TMP_Text _titleText;
        [SerializeField] private TMP_Text _descriptionText;
        [SerializeField] private TMP_Text _actionLabel;
        [SerializeField] private TMP_Text _closeLabel;
        [SerializeField] private TMP_Text _slotHeading;
        [SerializeField] private Image _iconImage;
        [SerializeField] private UIButtonView _actionButton;
        [SerializeField] private UIButtonView _closeButton;
        [SerializeField] private UIButtonView _progressionButton;
        [SerializeField] private TMP_Text _progressionLabel;
        [SerializeField] private UILoadoutPanel _slotsPanel;
        [SerializeField] private GameObject _detailsContainer;

        public void Initialize(Action equip, Action close, Action<int> chooseSlot, Action progression = null)
        {
            ValidateReferences();
            Release();
            _actionButton.SetClickAction(() => equip?.Invoke());
            _closeButton.SetClickAction(() => close?.Invoke());
            if (_progressionButton != null)
                _progressionButton.SetClickAction(() => progression?.Invoke());
            _slotsPanel.Initialize(id =>
            {
                if (int.TryParse(id, out int slotIndex))
                    chooseSlot?.Invoke(slotIndex);
            });
        }

        public void Bind(CardDetailsViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            Show();
            _titleText.text = model.Title;
            _descriptionText.text = model.Description;
            _actionLabel.text = model.ActionLabel;
            _closeLabel.text = model.CloseLabel;
            _slotHeading.text = model.SlotHeading;
            _iconImage.sprite = model.Icon;
            _iconImage.enabled = model.Icon != null;
            _actionButton.SetInteractable(model.CanEquip && !model.ChoosingSlot);
            if (_progressionButton != null)
            {
                _progressionButton.gameObject.SetActive(model.CanProgression && !model.ChoosingSlot);
                if (_progressionLabel != null) _progressionLabel.text = "ПРОКАЧКА";
            }
            _detailsContainer?.SetActive(true);
            if (model.ChoosingSlot)
            {
                _slotHeading.gameObject.SetActive(true);
                _slotsPanel.Show();
                _slotsPanel.Bind(model.Slots ?? Array.Empty<CardViewModel>());
            }
            else
            {
                _slotHeading.gameObject.SetActive(false);
                _slotsPanel.Clear();
                _slotsPanel.Hide();
            }
        }

        public void Release()
        {
            if (_actionButton != null)
                _actionButton.ClearClickAction();
            if (_closeButton != null)
                _closeButton.ClearClickAction();
            if (_progressionButton != null)
                _progressionButton.ClearClickAction();
            if (_slotsPanel != null)
            {
                _slotsPanel.Clear();
                _slotsPanel.Hide();
            }
        }

        private void ValidateReferences()
        {
            if (_titleText == null || _descriptionText == null || _actionLabel == null || _closeLabel == null || _slotHeading == null || _iconImage == null || _actionButton == null || _closeButton == null || _slotsPanel == null)
                throw new InvalidOperationException($"{name} has unassigned card-details references.");
        }
    }
}
