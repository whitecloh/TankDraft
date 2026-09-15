using System;
using TMPro;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuNavigationPanel : UIPanel
    {
        [SerializeField] private UIButtonView[] _buttons;
        [SerializeField] private TMP_Text[] _labels;

        public void Initialize(IMainMenuUiCommands commands)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            ValidateReferences();
            Release();
            for (var index = 0; index < _buttons.Length; index++)
            {
                var capturedIndex = index;
                _buttons[index].SetClickAction(() => commands.SelectTab(capturedIndex));
            }
        }

        public void Bind(string[] labels)
        {
            ValidateReferences();
            ValidateBinding(labels, MainMenuViewModel.NavigationCount, nameof(labels));
            for (var index = 0; index < _labels.Length; index++)
                _labels[index].text = labels[index];
        }

        [SerializeField] private UnityEngine.UI.Image[] _backgrounds;
        [SerializeField] private Color _normalColor, _selectedColor;
        [SerializeField] private UnityEngine.UI.LayoutElement[] _layouts;
        [SerializeField] private UnityEngine.UI.HorizontalLayoutGroup _group;
        [SerializeField] private float _normalWidth=107, _selectedWidth=148;
        public void SetSelected(int index)
        {
            if (_backgrounds == null || _backgrounds.Length != _buttons.Length)
                throw new InvalidOperationException("Navigation backgrounds must be authored.");
            for (var i = 0; i < _backgrounds.Length; i++)
            {
                _backgrounds[i].color = i == index ? _selectedColor : _normalColor;
                _layouts[i].minWidth = _layouts[i].preferredWidth = i == index ? _selectedWidth : _normalWidth;
            }
            try { _group.enabled=true;UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_group.transform); }
            finally { _group.enabled=false; }
        }

        public void Release()
        {
            if (_buttons == null)
                return;

            foreach (var button in _buttons)
            {
                if (button != null)
                    button.ClearClickAction();
            }
        }

        private void ValidateReferences()
        {
            ValidateBinding(_buttons, MainMenuViewModel.NavigationCount, nameof(_buttons));
            ValidateBinding(_labels, MainMenuViewModel.NavigationCount, nameof(_labels));
            for (var index = 0; index < MainMenuViewModel.NavigationCount; index++)
            {
                if (_buttons[index] == null || _labels[index] == null)
                    throw new InvalidOperationException($"{name} has an unassigned navigation reference at index {index}.");
            }
        }

        private static void ValidateBinding(Array items, int expectedLength, string name)
        {
            if (items == null)
                throw new ArgumentNullException(name);
            if (items.Length != expectedLength)
                throw new InvalidOperationException($"{name} must contain exactly {expectedLength} authored items.");
        }
    }
}

