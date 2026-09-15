using System;
using UnityEngine;

namespace TankDraft.Match.Presentation
{
    [CreateAssetMenu(menuName = "TankDraft/Match/Reference presentation timings")]
    public sealed class MatchPresentationTimings : ScriptableObject
    {
        [SerializeField, Min(0f)] private float _enterSeconds = .25f;
        [SerializeField, Min(0f)] private float _staggerSeconds = .10f;
        [SerializeField, Min(0f)] private float _startScale = .80f;
        [SerializeField, Min(0f)] private float _overshootScale = 1.06f;
        [SerializeField, Min(0f)] private float _pressSeconds = .08f;
        [SerializeField, Min(0f)] private float _pressedScale = .96f;
        [SerializeField, Min(0f)] private float _roundLabelSeconds = 1f;
        [SerializeField, Min(0f)] private float _fightLabelSeconds = .8f;
        [SerializeField, Min(0f)] private float _heartChangeSeconds = .3f;
        [SerializeField, Min(0f)] private float _transitionFadeSeconds = .12f;
        [SerializeField] private string _roundFormat = "РАУНД {0}";
        [SerializeField] private string _fight = "СРАЖАЙСЯ";
        [SerializeField] private string _waiting = "Ожидание соперника";

        public float EnterSeconds => _enterSeconds;
        public float StaggerSeconds => _staggerSeconds;
        public float StartScale => _startScale;
        public float OvershootScale => _overshootScale;
        public float PressSeconds => _pressSeconds;
        public float PressedScale => _pressedScale;
        public float RoundLabelSeconds => _roundLabelSeconds;
        public float FightLabelSeconds => _fightLabelSeconds;
        public float HeartChangeSeconds => _heartChangeSeconds;
        public float TransitionFadeSeconds => _transitionFadeSeconds;
        public string RoundFormat => _roundFormat;
        public string Fight => _fight;
        public string Waiting => _waiting;

        public void Validate()
        {
            ValidateNonNegativeFinite(_enterSeconds, nameof(_enterSeconds));
            ValidateNonNegativeFinite(_staggerSeconds, nameof(_staggerSeconds));
            ValidateNonNegativeFinite(_startScale, nameof(_startScale));
            ValidateNonNegativeFinite(_overshootScale, nameof(_overshootScale));
            ValidateNonNegativeFinite(_pressSeconds, nameof(_pressSeconds));
            ValidateNonNegativeFinite(_pressedScale, nameof(_pressedScale));
            ValidateNonNegativeFinite(_roundLabelSeconds, nameof(_roundLabelSeconds));
            ValidateNonNegativeFinite(_fightLabelSeconds, nameof(_fightLabelSeconds));
            ValidateNonNegativeFinite(_heartChangeSeconds, nameof(_heartChangeSeconds));
            ValidateNonNegativeFinite(_transitionFadeSeconds, nameof(_transitionFadeSeconds));
            if (string.IsNullOrEmpty(_roundFormat) || string.IsNullOrEmpty(_fight) || string.IsNullOrEmpty(_waiting))
                throw new InvalidOperationException("Match presentation text must not be empty.");
        }

        private static void ValidateNonNegativeFinite(float value, string fieldName)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new InvalidOperationException("Match presentation timing " + fieldName + " must be finite and non-negative.");
        }
    }
}
