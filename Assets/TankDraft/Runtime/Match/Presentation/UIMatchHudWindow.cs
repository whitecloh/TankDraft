using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Presentation
{
    public sealed class UIMatchHudWindow : UIWindow
    {
        [SerializeField]
        private TMP_Text _titleText, _scoreText, _statusText, _army0Text, _army1Text, _hintText, _nextLabelText, _menuLabelText;
        [SerializeField]
        private GameObject _nextContainer;
        [SerializeField]
        private UIButtonView _nextButton, _menuButton;
        [SerializeField]
        private UIMatchTransitionView _transitions;
        [SerializeField]
        private GameObject _heartsContainer;
        [SerializeField]
        private Image[] _ownHearts, _opponentHearts;
        [SerializeField]
        private RectTransform[] _ownHeartVisuals, _opponentHeartVisuals;
        [SerializeField]
        private MatchPresentationTimings _presentationTimings;
        [SerializeField]
        private Color _heartFilled = new Color(1f, .88f, .32f, 1f);
        [SerializeField]
        private Color _heartLost = new Color(.18f, .18f, .21f, 1f);
        private Action _next, _menu;
        private bool _hasHeartBinding;
        private int _lastOwnRemaining, _lastOpponentRemaining;
        private string _lastMatchPresentationId;
        private float _ownPulseElapsed = -1f, _opponentPulseElapsed = -1f;
        public void Initialize(Action next, Action menu)
        {
            Validate();
            Unbind();
            _next = next;
            _menu = menu;
            _nextButton.SetClickAction(() => _next?.Invoke());
            _menuButton.SetClickAction(() => _menu?.Invoke());
        }

        public void Render(MatchViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Validate();
            _titleText.text = model.Title ?? string.Empty;
            _scoreText.text = model.Score ?? string.Empty;
            _statusText.text = model.Status ?? string.Empty;
            _army0Text.text = model.Army0 ?? string.Empty;
            _army1Text.text = model.Army1 ?? string.Empty;
            _hintText.text = model.Hint ?? string.Empty;
            _nextLabelText.text = model.NextLabel ?? string.Empty;
            _menuLabelText.text = model.MenuLabel ?? string.Empty;
            _nextContainer.SetActive(model.NextVisible);
            _menuButton.gameObject.SetActive(model.MenuVisible);
            // Activate before child presentation so Awake/OnEnable cannot erase this snapshot.
            Show();
            RenderHearts(model);
            _transitions.Render(model);
        }

        public void Unbind()
        {
            _next = null;
            _menu = null;
            if (_nextButton != null)
                _nextButton.ClearClickAction();
            if (_menuButton != null)
                _menuButton.ClearClickAction();
            ResetHeartPresentation();
            if (_transitions != null)
                _transitions.Hide();
        }

        public void Validate()
        {
            if (_titleText == null || _scoreText == null || _statusText == null || _army0Text == null || _army1Text == null || _hintText == null || _nextLabelText == null || _menuLabelText == null || _nextContainer == null || _nextButton == null || _menuButton == null || _transitions == null || _heartsContainer == null || _presentationTimings == null)
                throw new InvalidOperationException(name + " has incomplete match HUD references.");
            ValidateHeartSlots(_ownHearts, _ownHeartVisuals, nameof(_ownHearts));
            ValidateHeartSlots(_opponentHearts, _opponentHeartVisuals, nameof(_opponentHearts));
            _presentationTimings.Validate();
            _transitions.Validate();
        }

        private void Update()
        {
            UpdatePulse(_ownHeartVisuals, ref _ownPulseElapsed);
            UpdatePulse(_opponentHeartVisuals, ref _opponentPulseElapsed);
        }

        private void OnDisable()
        {
            ResetHeartPresentation();
            if (_transitions != null)
                _transitions.Hide();
        }

        private void RenderHearts(MatchViewModel model)
        {
            bool valid = model.WinsRequired > 0 && model.WinsRequired <= _ownHearts.Length;
            _heartsContainer.SetActive(valid);
            if (!valid)
            {
                ResetHeartPresentation();
                return;
            }

            bool identityChanged = !_hasHeartBinding || model.PresentationReset || !string.Equals(_lastMatchPresentationId, model.MatchPresentationId, StringComparison.Ordinal);
            int ownRemaining = Mathf.Clamp(model.WinsRequired - model.OpponentWins, 0, model.WinsRequired);
            int opponentRemaining = Mathf.Clamp(model.WinsRequired - model.OwnWins, 0, model.WinsRequired);
            RenderHeartSlots(_ownHearts, ownRemaining, model.WinsRequired);
            RenderHeartSlots(_opponentHearts, opponentRemaining, model.WinsRequired);

            if (!identityChanged)
            {
                if (ownRemaining < _lastOwnRemaining)
                    _ownPulseElapsed = 0f;
                if (opponentRemaining < _lastOpponentRemaining)
                    _opponentPulseElapsed = 0f;
            }
            else
            {
                ResetVisualScales(_ownHeartVisuals);
                ResetVisualScales(_opponentHeartVisuals);
                _ownPulseElapsed = _opponentPulseElapsed = -1f;
            }

            _hasHeartBinding = true;
            _lastMatchPresentationId = model.MatchPresentationId;
            _lastOwnRemaining = ownRemaining;
            _lastOpponentRemaining = opponentRemaining;
        }

        private void RenderHeartSlots(Image[] slots, int remaining, int winsRequired)
        {
            for (int index = 0; index < slots.Length; index++)
            {
                bool used = index < winsRequired;
                slots[index].gameObject.SetActive(used);
                if (used)
                    slots[index].color = index < remaining ? _heartFilled : _heartLost;
            }
        }

        private void UpdatePulse(RectTransform[] visuals, ref float elapsed)
        {
            if (elapsed < 0f)
                return;
            elapsed += Time.unscaledDeltaTime;
            float duration = _presentationTimings == null ? 0f : _presentationTimings.HeartChangeSeconds;
            if (duration <= 0f || elapsed >= duration)
            {
                ResetVisualScales(visuals);
                elapsed = -1f;
                return;
            }

            float normalized = elapsed / duration;
            float pulse = 1f + Mathf.Sin(normalized * Mathf.PI) * .16f;
            for (int index = 0; index < visuals.Length; index++)
                visuals[index].localScale = new Vector3(pulse, pulse, 1f);
        }

        private void ResetHeartPresentation()
        {
            _hasHeartBinding = false;
            _lastMatchPresentationId = null;
            _lastOwnRemaining = _lastOpponentRemaining = 0;
            _ownPulseElapsed = _opponentPulseElapsed = -1f;
            if (_ownHeartVisuals != null)
                ResetVisualScales(_ownHeartVisuals);
            if (_opponentHeartVisuals != null)
                ResetVisualScales(_opponentHeartVisuals);
        }

        private static void ResetVisualScales(RectTransform[] visuals)
        {
            for (int index = 0; index < visuals.Length; index++)
                if (visuals[index] != null)
                    visuals[index].localScale = Vector3.one;
        }

        private static void ValidateHeartSlots(Image[] slots, RectTransform[] visuals, string name)
        {
            if (slots == null || visuals == null || slots.Length != 4 || visuals.Length != slots.Length)
                throw new InvalidOperationException("Match HUD " + name + " must have exactly four authored slots and visual roots.");
            for (int index = 0; index < slots.Length; index++)
                if (slots[index] == null || visuals[index] == null || visuals[index].parent != slots[index].transform.parent)
                    throw new InvalidOperationException("Match HUD " + name + " has an incomplete heart slot at " + index + ".");
        }
    }
}
