using System;
using UnityEngine;

namespace TankDraft.UI
{
    public sealed class CurrencyCounterViewModel
    {
        public readonly string Amount;
        public readonly Sprite Icon;

        public CurrencyCounterViewModel(string amount, Sprite icon)
        {
            Amount = amount;
            Icon = icon;
        }
    }

    public sealed class RewardSlotViewModel
    {
        public readonly string Caption;
        public readonly Sprite Icon;
        public readonly bool Interactable;

        public RewardSlotViewModel(string caption, Sprite icon, bool interactable)
        {
            Caption = caption;
            Icon = icon;
            Interactable = interactable;
        }
    }

    public sealed class MainMenuViewModel
    {
        public const int CurrencyCount = 4;
        public const int RewardCount = 4;
        public const int NavigationCount = 5;

        public readonly CurrencyCounterViewModel[] Currency;
        public readonly RewardSlotViewModel[] Rewards;
        public readonly string ProfileName;
        public readonly string PassProgress;
        public readonly string ArenaName;
        public readonly string ArenaProgress;
        public readonly string StartBattleLabel;
        public readonly string[] NavigationLabels;
        public readonly string ResultSummary;

        public MainMenuViewModel(
            CurrencyCounterViewModel[] currency,
            RewardSlotViewModel[] rewards,
            string profileName,
            string passProgress,
            string arenaName,
            string arenaProgress,
            string startBattleLabel,
            string[] navigationLabels,
            string resultSummary = "")
        {
            ValidateLength(currency, CurrencyCount, nameof(currency));
            ValidateLength(rewards, RewardCount, nameof(rewards));
            ValidateLength(navigationLabels, NavigationCount, nameof(navigationLabels));

            Currency = currency;
            Rewards = rewards;
            ProfileName = profileName;
            PassProgress = passProgress;
            ArenaName = arenaName;
            ArenaProgress = arenaProgress;
            StartBattleLabel = startBattleLabel;
            NavigationLabels = navigationLabels;
            ResultSummary = resultSummary ?? string.Empty;
        }

        private static void ValidateLength(Array items, int expectedLength, string name)
        {
            if (items == null)
                throw new ArgumentNullException(name);
            if (items.Length != expectedLength)
                throw new ArgumentException($"{name} must contain exactly {expectedLength} items.", name);
        }
    }

    public interface IMainMenuUiCommands
    {
        void StartBattle();
        void OpenProfile();
        void OpenPass();
        void OpenSettings();
        void OpenReward(int index);
        void SelectTab(int index);
    }
}
