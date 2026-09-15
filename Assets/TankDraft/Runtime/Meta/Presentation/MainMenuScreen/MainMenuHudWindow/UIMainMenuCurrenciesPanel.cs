using System;
using UnityEngine;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuCurrenciesPanel : UIPanel
    {
        [SerializeField] private UICurrencyCounterView[] _items;

        public void Bind(CurrencyCounterViewModel[] items)
        {
            ValidateItems();
            ValidateBinding(items, MainMenuViewModel.CurrencyCount, nameof(items));
            for (var index = 0; index < _items.Length; index++)
            {
                _items[index].Bind(items[index]);
                _items[index].Show();
            }
        }

        private void ValidateItems()
        {
            ValidateBinding(_items, MainMenuViewModel.CurrencyCount, nameof(_items));
            foreach (var item in _items)
            {
                if (item == null)
                    throw new InvalidOperationException($"{name} has an unassigned currency counter.");
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
