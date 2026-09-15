using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Presentation
{
    public sealed class MatchCardView : UIButtonView
    {
        [SerializeField]
        private TMP_Text _titleText;
        [SerializeField]
        private TMP_Text _actionLabel;
        [SerializeField]
        private TMP_Text _descriptionText;
        [SerializeField]
        private Image _iconImage;
        [SerializeField]
        private MatchCardMotion _motion;
        public void Bind(MatchCardModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Validate();
            _titleText.text = model.Title ?? string.Empty;
            _actionLabel.text = model.ActionLabel ?? string.Empty;
            _descriptionText.text = model.Description ?? string.Empty;
            _iconImage.sprite = model.Icon;
            _iconImage.enabled = model.Icon != null;
            SetInteractable(model.Enabled);
        }

        public void Unbind()
        {
            ClearClickAction();
            ResetPresentation();
            if (_titleText != null)
                _titleText.text = string.Empty;
            if (_actionLabel != null)
                _actionLabel.text = string.Empty;
            if (_descriptionText != null)
                _descriptionText.text = string.Empty;
            if (_iconImage != null)
            {
                _iconImage.sprite = null;
                _iconImage.enabled = false;
            }

            SetInteractable(false);
        }

        public void PlayEntrance(int index) => _motion?.PlayEntrance(index);

        public void ResetPresentation() => _motion?.ResetPresentation();

        public override void Click()
        {
            if (!Interactable || (Button != null && !Button.interactable))
                return;

            // Visual feedback is deliberately asynchronous: choice submission is never delayed.
            _motion?.PlayPressedFeedback();
            base.Click();
        }

        protected override void OnDisable()
        {
            ResetPresentation();
            base.OnDisable();
        }

        public void Validate()
        {
            if (_titleText == null || _actionLabel == null || _descriptionText == null || _iconImage == null || Button == null)
                throw new InvalidOperationException(name + " has unassigned match card references.");
        }
    }
}
