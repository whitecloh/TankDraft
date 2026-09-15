using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class UIMainMenuCollectionWindow : UIWindow
    {
        [SerializeField] private UILoadoutPanel _loadoutPanel;
        [SerializeField] private UICatalogPanel _catalogPanel;
        [SerializeField] private UIButtonView _unitsButton;
        [SerializeField] private UIButtonView _ordersButton;
        [SerializeField] private TMP_Text _unitsLabel;
        [SerializeField] private TMP_Text _ordersLabel;
        [SerializeField] private TMP_Text _headingText;
        [SerializeField] private TMP_Text _summaryText;
        [SerializeField] private ScrollRect _scroll;
        [SerializeField] private UiLayoutRefresh _layoutRefresh;

        private bool _hasBoundTab;
        private bool _ordersTab;

        public void Initialize(Action units, Action orders, Action<string> openCard)
        {
            ValidateReferences();
            Release();
            _unitsButton.SetClickAction(() => units?.Invoke());
            _ordersButton.SetClickAction(() => orders?.Invoke());
            _loadoutPanel.Initialize(openCard);
            _catalogPanel.Initialize(openCard);
        }

        public void Bind(CollectionViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));

            ValidateReferences();
            bool tabChanged = !_hasBoundTab || _ordersTab != model.Orders;
            Show();
            _loadoutPanel.Show();
            _catalogPanel.Show();
            _loadoutPanel.Bind(model.Loadout);
            _catalogPanel.Bind(model.Catalog);
            _unitsLabel.text = model.UnitsLabel;
            _ordersLabel.text = model.OrdersLabel;
            _headingText.text = model.Heading;
            _summaryText.text = model.Summary;
            _ordersTab = model.Orders;
            _hasBoundTab = true;
            _layoutRefresh.Rebuild();
            if (tabChanged)
                _scroll.normalizedPosition = Vector2.up;
        }

        public void Release()
        {
            if (_unitsButton != null)
                _unitsButton.ClearClickAction();
            if (_ordersButton != null)
                _ordersButton.ClearClickAction();
            if (_loadoutPanel != null)
                _loadoutPanel.Clear();
            if (_catalogPanel != null)
                _catalogPanel.Clear();
            _hasBoundTab = false;
        }

        private void ValidateReferences()
        {
            if (_loadoutPanel == null || _catalogPanel == null || _unitsButton == null || _ordersButton == null || _unitsLabel == null || _ordersLabel == null || _headingText == null || _summaryText == null || _scroll == null || _layoutRefresh == null)
                throw new InvalidOperationException($"{name} has unassigned collection references.");
        }
    }
}
