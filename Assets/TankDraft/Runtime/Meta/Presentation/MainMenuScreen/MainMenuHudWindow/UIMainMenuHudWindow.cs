using System;
using TMPro;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuHudWindow : UIWindow
    {
        [SerializeField] private UIMainMenuCurrenciesPanel _currenciesPanel;
        [SerializeField] private UIMainMenuNavigationPanel _navigationPanel;
        [SerializeField] private UIButtonView _profileButton;
        [SerializeField] private UIButtonView _passButton;
        [SerializeField] private UIButtonView _settingsButton;
        [SerializeField] private TMP_Text _profileNameText;
        [SerializeField] private TMP_Text _passProgressText;

        public void Initialize(IMainMenuUiCommands commands)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            ValidateReferences();
            Release();
            _profileButton.SetClickAction(commands.OpenProfile);
            _passButton.SetClickAction(commands.OpenPass);
            _settingsButton.SetClickAction(commands.OpenSettings);
            _navigationPanel.Initialize(commands);
        }

        public void Bind(MainMenuViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _currenciesPanel.Bind(model.Currency);
            _navigationPanel.Bind(model.NavigationLabels);
            _profileNameText.text = model.ProfileName;
            _passProgressText.text = model.PassProgress;
            Show();
        }

        public void SetCollectionMode(bool collection)
        {
            _profileButton.gameObject.SetActive(!collection);
            _passButton.gameObject.SetActive(!collection);
            _settingsButton.gameObject.SetActive(!collection);
        }
        public void SetSelectedTab(int index) => _navigationPanel.SetSelected(index);

        public void Release()
        {
            if (_profileButton != null)
                _profileButton.ClearClickAction();
            if (_passButton != null)
                _passButton.ClearClickAction();
            if (_settingsButton != null)
                _settingsButton.ClearClickAction();
            if (_navigationPanel != null)
                _navigationPanel.Release();
        }

        private void ValidateReferences()
        {
            if (_currenciesPanel == null || _navigationPanel == null || _profileButton == null || _passButton == null || _settingsButton == null || _profileNameText == null || _passProgressText == null)
                throw new InvalidOperationException($"{name} has unassigned HUD references.");
        }
    }
}

