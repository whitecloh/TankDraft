using System;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuRewardsPanel : UIPanel
    {
        [SerializeField] private UIRewardSlotView[] _items;

        public void Initialize(IMainMenuUiCommands commands)
        {
            if (commands == null)
                throw new ArgumentNullException(nameof(commands));

            ValidateItems();
            Release();
            for (var index = 0; index < _items.Length; index++)
            {
                var capturedIndex = index;
                _items[index].SetClickAction(() => commands.OpenReward(capturedIndex));
            }
        }

        public void Bind(RewardSlotViewModel[] items)
        {
            ValidateItems();
            ValidateBinding(items, MainMenuViewModel.RewardCount, nameof(items));
            for (var index = 0; index < _items.Length; index++)
            {
                _items[index].Bind(items[index]);
                _items[index].Show();
            }
        }

        public void Release()
        {
            if (_items == null)
                return;

            foreach (var item in _items)
            {
                if (item != null)
                    item.Clear();
            }
        }

        private void ValidateItems()
        {
            ValidateBinding(_items, MainMenuViewModel.RewardCount, nameof(_items));
            foreach (var item in _items)
            {
                if (item == null)
                    throw new InvalidOperationException($"{name} has an unassigned reward slot.");
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
