using System;
using TMPro;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuArenaWindow : UIWindow
    {
        [SerializeField] private UIMainMenuRewardsPanel _rewardsPanel;
        [SerializeField] private UIButtonView _battleButton;
        [SerializeField] private TMP_Text _battleLabel;
        [SerializeField] private TMP_Text _arenaNameText;
        [SerializeField] private TMP_Text _progressText;
        [SerializeField] private UIButtonView _resultButton;
        [SerializeField] private TMP_Text _resultLabel;

        public void Initialize(IMainMenuUiCommands commands, Action openLastResult = null)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            ValidateReferences();
            Release();
            _battleButton.SetClickAction(commands.StartBattle);
            _resultButton.SetClickAction(() => (openLastResult ?? commands.OpenProfile)?.Invoke());
            _rewardsPanel.Initialize(commands);
        }

        public void Bind(MainMenuViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _rewardsPanel.Bind(model.Rewards);
            _battleLabel.text = model.StartBattleLabel;
            _arenaNameText.text = model.ArenaName;
            _progressText.text = model.ArenaProgress;
            _resultLabel.text = model.ResultSummary;
            _resultButton.gameObject.SetActive(!string.IsNullOrEmpty(model.ResultSummary));
            Show();
        }

        public void Release()
        {
            if (_battleButton != null)
                _battleButton.ClearClickAction();
            if (_rewardsPanel != null)
                _rewardsPanel.Release();
            if (_resultButton != null) _resultButton.ClearClickAction();
        }

        private void ValidateReferences()
        {
            if (_rewardsPanel == null || _battleButton == null || _battleLabel == null || _arenaNameText == null || _progressText == null || _resultButton == null || _resultLabel == null)
                throw new InvalidOperationException($"{name} has unassigned arena references.");
        }
    }
}
