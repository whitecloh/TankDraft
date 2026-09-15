using System;
using TMPro;
using UnityEngine;

namespace TankDraft.Match.Presentation
{
    public sealed class UIMatchTransitionView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private TMP_Text _label;
        [SerializeField] private MatchPresentationTimings _timings;

        private string _presentationId, _phaseName;
        private int _round;
        private double _lastServerAge = double.NaN, _visualAge;
        private double _ageAnchorRealtime;
        private bool _hasState, _suppressed, _completed;

        public bool IsVisible => _canvasGroup != null && _canvasGroup.alpha > 0f && !string.IsNullOrEmpty(CurrentText);
        public string CurrentText => _label == null ? string.Empty : _label.text;

        public void Render(MatchViewModel model)
        {
            if (model == null)
                throw new ArgumentNullException(nameof(model));
            Validate();
            Receive(model);
            UpdateVisual();
        }

        // Explicit parent teardown only. A normal no-banner state retains identity so stale
        // snapshots cannot restart an intro that has already completed.
        public void Hide()
        {
            ResetAge();
            ClearVisual();
        }

        public void Validate()
        {
            if (_canvasGroup == null || _label == null || _timings == null)
                throw new InvalidOperationException(name + " has incomplete match transition references.");
            _timings.Validate();
        }

        private void OnDisable()
        {
            ResetAge();
            ClearVisual();
        }

        private void Update()
        {
            if (_hasState)
                UpdateVisual();
        }

        private void Receive(MatchViewModel model)
        {
            bool boundary = !_hasState || !string.Equals(_presentationId, model.MatchPresentationId, StringComparison.Ordinal) ||
                !string.Equals(_phaseName, model.PhaseName, StringComparison.Ordinal) || _round != model.RoundNumber;
            if (boundary)
            {
                _hasState = true;
                _presentationId = model.MatchPresentationId;
                _phaseName = model.PhaseName;
                _round = model.RoundNumber;
                _lastServerAge = NormalizeAge(model.BattleElapsedSeconds);
                _visualAge = _lastServerAge;
                _ageAnchorRealtime = Time.unscaledTimeAsDouble;
                _suppressed = model.PresentationReset;
                _completed = false;
                return;
            }

            if (model.PresentationReset)
                _suppressed = true;
            double receivedAge = NormalizeAge(model.BattleElapsedSeconds);
            if (receivedAge > _lastServerAge)
            {
                _lastServerAge = receivedAge;
                _ageAnchorRealtime = Time.unscaledTimeAsDouble;
            }
        }

        private void UpdateVisual()
        {
            if (_timings == null || _canvasGroup == null || _label == null)
                return;
            double total = MatchRoundPresentation.TotalLabelSecondsFor(_timings.RoundLabelSeconds, _timings.FightLabelSeconds);
            double candidate = Math.Min(total, _lastServerAge + Math.Max(0d, Time.unscaledTimeAsDouble - _ageAnchorRealtime));
            _visualAge = Math.Max(_visualAge, candidate);
            if (_suppressed || _completed || _visualAge >= total)
            {
                if (_visualAge >= total)
                    _completed = true;
                ClearVisual();
                return;
            }

            MatchRoundBanner banner = MatchRoundPresentation.ClassifyBanner(_phaseName, false, _visualAge, _timings.RoundLabelSeconds, _timings.FightLabelSeconds);
            if (banner == MatchRoundBanner.None)
            {
                ClearVisual();
                return;
            }
            _label.text = banner == MatchRoundBanner.Round
                ? string.Format(_timings.RoundFormat, Math.Max(1, _round))
                : _timings.Fight;
            _canvasGroup.alpha = Alpha(banner, _visualAge);
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
        }

        private float Alpha(MatchRoundBanner banner, double age)
        {
            float fade = _timings.TransitionFadeSeconds;
            if (fade <= 0f)
                return 1f;
            double start = banner == MatchRoundBanner.Round ? 0d : _timings.RoundLabelSeconds;
            double end = banner == MatchRoundBanner.Round ? _timings.RoundLabelSeconds : MatchRoundPresentation.TotalLabelSecondsFor(_timings.RoundLabelSeconds, _timings.FightLabelSeconds);
            double localAge = age - start;
            return Mathf.Clamp01((float)Math.Min(localAge / fade, (end - age) / fade));
        }

        private void ResetAge()
        {
            _presentationId = _phaseName = null;
            _round = 0;
            _lastServerAge = double.NaN;
            _visualAge = 0d;
            _ageAnchorRealtime = 0d;
            _hasState = _suppressed = _completed = false;
        }

        private void ClearVisual()
        {
            if (_canvasGroup == null)
                return;
            _canvasGroup.alpha = 0f;
            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            if (_label != null)
                _label.text = string.Empty;
        }

        private static double NormalizeAge(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0d ? 0d : value;
    }
}
