using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Presentation
{
    public sealed class UIMatchDraftWindow : UIWindow
    {
        [SerializeField]
        private MatchCardView[] _cards;
        [SerializeField]
        private GameObject _orderContainer;
        [SerializeField]
        private TMP_Text _orderLabelText;
        [SerializeField]
        private UIButtonView _orderButton;
        [SerializeField]
        private HorizontalLayoutGroup _cardsLayout;
        private Action<int> _choose;
        private Action _order;
        private bool _resizePending, _disableLayoutPending;
        private bool _presentationVisible;
        private string _lastOfferPresentationKey;
        [SerializeField] private TMP_Text _waitingLabelText;
        private MatchCardModel _selectedCard;
        private string _selectedOfferKey;
        protected override void Awake()
        {
            base.Awake();
            if (_cardsLayout != null)
                _cardsLayout.enabled = false;
        }

        public void Initialize(Action<int> choose, Action order)
        {
            Validate();
            Unbind();
            _choose = choose;
            _order = order;
            _orderButton.SetClickAction(() => _order?.Invoke());
        }

        public void Render(MatchViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Validate();
            if (!model.DraftVisible && !model.WaitingForOpponent)
            {
                Hide();
                return;
            }

            string presentationKey = ResolvePresentationKey(model);
            if (!string.Equals(_selectedOfferKey, presentationKey, StringComparison.Ordinal) || model.PresentationReset)
            {
                _selectedCard = null;
                _selectedOfferKey = null;
            }
            if (_waitingLabelText != null)
            {
                _waitingLabelText.gameObject.SetActive(model.WaitingForOpponent);
                _waitingLabelText.text = model.WaitingLabel ?? string.Empty;
            }
            if (model.WaitingForOpponent)
            {
                Show();
                _orderContainer.SetActive(false);
                _orderButton.SetInteractable(false);
                var selected = model.WaitingCard ?? _selectedCard;
                for (int index = 0; index < _cards.Length; index++)
                {
                    var view = _cards[index];
                    if (index == 1 && selected != null)
                    {
                        view.Bind(DisabledCopy(selected));
                        view.ClearClickAction();
                        view.Show();
                        view.ResetPresentation();
                    }
                    else { view.Unbind(); view.Hide(); }
                }
                RebuildCardsLayout();
                _presentationVisible = true;
                _lastOfferPresentationKey = presentationKey;
                return;
            }
            bool playEntrance = !_presentationVisible || !string.Equals(_lastOfferPresentationKey, presentationKey, StringComparison.Ordinal);

            if (model.Cards != null && model.Cards.Length > _cards.Length)
                throw new ArgumentException("Match has more cards than authored slots.", nameof(model));
            // Activate before starting child motion: Awake/OnEnable must not reset its first entrance.
            Show();
            _orderContainer.SetActive(model.OrderVisible);
            _orderLabelText.text = model.OrderLabel ?? string.Empty;
            _orderButton.SetInteractable(model.OrderEnabled);
            for (var index = 0; index < _cards.Length; index++)
            {
                MatchCardModel card = model.Cards != null && index < model.Cards.Length ? model.Cards[index] : null;
                MatchCardView view = _cards[index];
                if (card == null)
                {
                    view.Unbind();
                    view.Hide();
                    continue;
                }

                int choice = index;
                view.Bind(card);
                view.SetClickAction(() =>
                {
                    if (card.Enabled)
                    {
                        _selectedCard = DisabledCopy(card);
                        _selectedOfferKey = presentationKey;
                        _choose?.Invoke(choice);
                    }
                });
                view.Show();
                if (playEntrance)
                    view.PlayEntrance(index);
            }

            RebuildCardsLayout();
            _presentationVisible = true;
            _lastOfferPresentationKey = presentationKey;
        }

        public override void Hide(bool instant = true)
        {
            ResetPresentation();
            base.Hide(instant);
        }

        public void Unbind()
        {
            ResetPresentation();
            _choose = null;
            _order = null;
            if (_cards != null)
                for (var index = 0; index < _cards.Length; index++)
                    if (_cards[index] != null)
                    {
                        _cards[index].Unbind();
                        _cards[index].Hide();
                    }

            if (_orderButton != null)
            {
                _orderButton.ClearClickAction();
                _orderButton.SetInteractable(false);
            }
        }

        private void OnDisable() => ResetPresentation();

        public void Validate()
        {
            if (_cards == null || _cards.Length != 3 || _orderContainer == null || _orderLabelText == null || _orderButton == null || _cardsLayout == null)
                throw new InvalidOperationException(name + " has incomplete match draft references.");
            for (var index = 0; index < _cards.Length; index++)
                if (_cards[index] == null)
                    throw new InvalidOperationException(name + " has an unassigned draft card.");
        }

        private void OnRectTransformDimensionsChange()
        {
            if (UnityEngine.Application.isPlaying)
                _resizePending = true;
        }

        private void LateUpdate()
        {
            if (_resizePending && isActiveAndEnabled)
            {
                _resizePending = false;
                RebuildCardsLayout();
            }

            if (_disableLayoutPending && _cardsLayout != null)
            {
                _cardsLayout.enabled = false;
                _disableLayoutPending = false;
            }
        }

        private void RebuildCardsLayout()
        {
            _cardsLayout.enabled = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)_cardsLayout.transform);
            _disableLayoutPending = true;
        }

        private void ResetPresentation()
        {
            _presentationVisible = false;
            _lastOfferPresentationKey = null;
            _selectedCard = null;
            _selectedOfferKey = null;
            if (_cards == null)
                return;

            for (var index = 0; index < _cards.Length; index++)
                if (_cards[index] != null)
                    _cards[index].ResetPresentation();
        }

        private static MatchCardModel DisabledCopy(MatchCardModel value) => new MatchCardModel
        {
            Title = value.Title, ActionLabel = value.ActionLabel, Description = value.Description,
            Icon = value.Icon, Enabled = false
        };

        private static string ResolvePresentationKey(MatchViewModel model)
        {
            if (!string.IsNullOrEmpty(model.OfferPresentationKey))
                return model.OfferPresentationKey;

            if (model.Cards == null || model.Cards.Length == 0)
                return "empty";

            var signature = string.Empty;
            for (var index = 0; index < model.Cards.Length; index++)
            {
                MatchCardModel card = model.Cards[index];
                signature += "|" + (card?.Title ?? string.Empty) + "\u001f" + (card?.ActionLabel ?? string.Empty) + "\u001f" + (card?.Description ?? string.Empty) + "\u001f" + (card?.Icon == null ? 0 : card.Icon.GetInstanceID());
            }

            return signature;
        }
    }
}
