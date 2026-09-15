using System;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Vareiko.Foundation.UI;

namespace TankDraft.UI
{
    public sealed class ProgressionCardModel
    {
        public ProgressionCardModel(string title, string subtitle, Sprite icon, string primaryProgress, string secondaryProgress)
        {
            Title = title ?? throw new ArgumentNullException(nameof(title));
            Subtitle = subtitle ?? string.Empty;
            Icon = icon;
            PrimaryProgress = primaryProgress ?? string.Empty;
            SecondaryProgress = secondaryProgress ?? string.Empty;
        }

        public string Title { get; }
        public string Subtitle { get; }
        public Sprite Icon { get; }
        public string PrimaryProgress { get; }
        public string SecondaryProgress { get; }
    }

    public sealed class ProgressionActionModel
    {
        public ProgressionActionModel(string label, bool interactable, Action callback)
        {
            Label = label ?? throw new ArgumentNullException(nameof(label));
            Interactable = interactable;
            Callback = callback;
        }

        public string Label { get; }
        public bool Interactable { get; }
        public Action Callback { get; }
    }

    public sealed class UIProgressionWindow : UIWindow
    {
        [Serializable]
        private sealed class CardSlot
        {
            [SerializeField] private GameObject _root;
            [SerializeField] private UnityEngine.UI.Image _icon;
            [SerializeField] private TMP_Text _title;
            [SerializeField] private TMP_Text _subtitle;
            [SerializeField] private TMP_Text _primaryProgress;
            [SerializeField] private TMP_Text _secondaryProgress;

            public void Bind(ProgressionCardModel model)
            {
                _root.SetActive(true);
                _title.text = model.Title;
                _subtitle.text = model.Subtitle;
                _primaryProgress.text = model.PrimaryProgress;
                _secondaryProgress.text = model.SecondaryProgress;
                _icon.sprite = model.Icon;
                _icon.enabled = model.Icon != null;
            }

            public void Hide() => _root.SetActive(false);
            public bool IsValid => _root != null && _icon != null && _title != null && _subtitle != null && _primaryProgress != null && _secondaryProgress != null;
        }

        [Serializable]
        private sealed class ActionSlot
        {
            [SerializeField] private GameObject _root;
            [SerializeField] private UIButtonView _button;
            [SerializeField] private TMP_Text _label;

            public void Bind(ProgressionActionModel model)
            {
                _root.SetActive(true);
                _label.text = model.Label;
                _button.SetInteractable(model.Interactable);
                _button.SetClickAction(model.Callback == null ? null : new UnityAction(model.Callback.Invoke));
            }

            public void Hide()
            {
                if (_button != null) _button.ClearClickAction();
                if (_root != null) _root.SetActive(false);
            }

            public bool IsValid => _root != null && _button != null && _label != null;
        }

        [SerializeField] private TMP_Text _title;
        [SerializeField] private TMP_Text _body;
        [SerializeField] private CardSlot[] _cards;
        [SerializeField] private ActionSlot[] _actions;
        [SerializeField] private UIButtonView _closeButton;

        public void Bind(string title, string body, ProgressionCardModel[] cards, ProgressionActionModel[] actions, Action close)
        {
            if (title == null) throw new ArgumentNullException(nameof(title));
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (cards == null) throw new ArgumentNullException(nameof(cards));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            ValidateReferences();
            if (cards.Length > _cards.Length) throw new ArgumentOutOfRangeException(nameof(cards));
            if (actions.Length > _actions.Length) throw new ArgumentOutOfRangeException(nameof(actions));

            _title.text = title;
            _body.text = body;
            for (int index = 0; index < _cards.Length; index++)
            {
                if (index < cards.Length)
                {
                    if (cards[index] == null) throw new ArgumentException("Cards cannot contain null.", nameof(cards));
                    _cards[index].Bind(cards[index]);
                }
                else _cards[index].Hide();
            }
            for (int index = 0; index < _actions.Length; index++)
            {
                if (index < actions.Length)
                {
                    if (actions[index] == null) throw new ArgumentException("Actions cannot contain null.", nameof(actions));
                    _actions[index].Bind(actions[index]);
                }
                else _actions[index].Hide();
            }
            _closeButton.SetClickAction(close == null ? null : new UnityAction(close.Invoke));
            _closeButton.gameObject.SetActive(close != null);
            Show();
        }

        public void Release()
        {
            if (_actions != null)
                foreach (ActionSlot action in _actions)
                    if (action != null) action.Hide();
            if (_closeButton != null) _closeButton.ClearClickAction();
        }

        private void OnDestroy() => Release();

        private void ValidateReferences()
        {
            if (_title == null || _body == null || _closeButton == null || _cards == null || _cards.Length != 4 || _actions == null || _actions.Length != 4)
                throw new InvalidOperationException(name + " has incomplete progression window references.");
            foreach (CardSlot card in _cards) if (card == null || !card.IsValid) throw new InvalidOperationException(name + " has incomplete progression card references.");
            foreach (ActionSlot action in _actions) if (action == null || !action.IsValid) throw new InvalidOperationException(name + " has incomplete progression action references.");
        }
    }
}
