using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UICurrencyCounterView : UIItemView
    {
        [SerializeField] private TMP_Text _amountText;
        [SerializeField] private Image _iconImage;

        public void Bind(CurrencyCounterViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            _amountText.text = model.Amount;
            _iconImage.sprite = model.Icon;
            _iconImage.enabled = model.Icon != null;
        }

        public void Clear()
        {
            ValidateReferences();
            _amountText.text = string.Empty;
            _iconImage.sprite = null;
            _iconImage.enabled = false;
        }

        private void ValidateReferences()
        {
            if (_amountText == null || _iconImage == null)
                throw new InvalidOperationException($"{name} requires amount text and icon image references.");
        }
    }
}
