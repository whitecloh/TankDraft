using System;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UILoadoutPanel : UIPanel
    {
        [SerializeField] private UICatalogCardView[] _items;
        [SerializeField] private HorizontalLayoutGroup _group;

        private Action<string> _onClick;
        private bool _layoutDirty;

        public void Initialize(Action<string> onClick)
        {
            ValidateReferences();
            Clear();
            _onClick = onClick;
        }

        public void Bind(CardViewModel[] cards)
        {
            if (cards == null)
                throw new ArgumentNullException(nameof(cards));

            ValidateReferences();
            if (cards.Length > _items.Length)
                throw new ArgumentException("Loadout contains more cards than authored slots.", nameof(cards));

            Show();
            for (var index = 0; index < _items.Length; index++)
            {
                UICatalogCardView item = _items[index];
                if (index >= cards.Length)
                {
                    item.Clear();
                    item.Hide();
                    continue;
                }

                CardViewModel card = cards[index];
                if (card == null)
                    throw new ArgumentException("Loadout cards cannot contain null.", nameof(cards));

                string id = card.Id;
                item.Bind(card);
                item.SetClickAction(() =>
                {
                    if (!string.IsNullOrEmpty(id))
                        _onClick?.Invoke(id);
                });
                item.Show();
            }

            RebuildGroup();
        }

        public void Clear()
        {
            if (_items == null)
                return;

            for (var index = 0; index < _items.Length; index++)
            {
                if (_items[index] == null)
                    continue;

                _items[index].Clear();
                _items[index].Hide();
            }
        }

        private void RebuildGroup()
        {
            _group.enabled = true;
            try
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_group.transform);
            }
            finally
            {
                _group.enabled = false;
            }
        }

        private void OnRectTransformDimensionsChange()
        {
            if (UnityEngine.Application.isPlaying)
                _layoutDirty = true;
        }

        private void LateUpdate()
        {
            if (!_layoutDirty || !isActiveAndEnabled)
                return;

            _layoutDirty = false;
            RebuildGroup();
        }

        private void ValidateReferences()
        {
            if (_items == null || _items.Length != 4 || _group == null)
                throw new InvalidOperationException($"{name} requires four authored item views and a layout group.");

            for (var index = 0; index < _items.Length; index++)
            {
                if (_items[index] == null)
                    throw new InvalidOperationException($"{name} has an unassigned item view.");
            }
        }
    }
}
