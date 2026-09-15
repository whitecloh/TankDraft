using UnityEngine;

namespace TankDraft.Match.Presentation
{
    public sealed class MatchCardMotion : MonoBehaviour
    {
        [SerializeField] private RectTransform _visualRoot;
        [SerializeField] private CanvasGroup _visualCanvasGroup;
        [SerializeField] private MatchPresentationTimings _timings;

        private Vector3 _baseScale = Vector3.one;
        private float _entranceElapsed = -1f;
        private float _entranceDelay;
        private float _pressElapsed = -1f;
        private int _entranceRunId;

        public int EntranceRunId => _entranceRunId;
        public bool IsEntrancePlaying => _entranceElapsed >= 0f;
        public bool IsAtRest => _entranceElapsed < 0f && _pressElapsed < 0f;
        public RectTransform VisualRoot => _visualRoot;

        private void Awake()
        {
            CaptureBaseScale();
            ResetPresentation();
        }

        private void OnEnable()
        {
            CaptureBaseScale();
        }

        private void OnDisable()
        {
            ResetPresentation();
        }

        public void PlayEntrance(int index)
        {
            if (_visualRoot == null || _timings == null)
                return;

            _timings.Validate();
            CaptureBaseScale();
            _entranceRunId++;
            _entranceElapsed = 0f;
            _entranceDelay = Mathf.Max(0, index) * _timings.StaggerSeconds;
            _pressElapsed = -1f;
            Apply(_timings.StartScale, 0f);
        }

        public void PlayPressedFeedback()
        {
            if (_visualRoot == null || _timings == null)
                return;

            _timings.Validate();
            _pressElapsed = 0f;
        }

        public void ResetPresentation()
        {
            _entranceElapsed = -1f;
            _entranceDelay = 0f;
            _pressElapsed = -1f;
            if (_visualRoot != null)
                Apply(1f, 1f);
        }

        private void Update()
        {
            if (_visualRoot == null || _timings == null)
                return;

            float scale = 1f;
            float alpha = 1f;
            if (_entranceElapsed >= 0f)
            {
                _entranceElapsed += Time.unscaledDeltaTime;
                float elapsed = _entranceElapsed - _entranceDelay;
                if (elapsed <= 0f)
                {
                    scale = _timings.StartScale;
                    alpha = 0f;
                }
                else if (_timings.EnterSeconds <= 0f || elapsed >= _timings.EnterSeconds)
                {
                    _entranceElapsed = -1f;
                }
                else
                {
                    float normalized = elapsed / _timings.EnterSeconds;
                    alpha = Smooth01(normalized);
                    scale = EntranceScale(normalized);
                }
            }

            if (_pressElapsed >= 0f)
            {
                _pressElapsed += Time.unscaledDeltaTime;
                if (_timings.PressSeconds <= 0f || _pressElapsed >= _timings.PressSeconds)
                {
                    _pressElapsed = -1f;
                }
                else
                {
                    float normalized = _pressElapsed / _timings.PressSeconds;
                    float down = normalized <= .5f ? normalized * 2f : (1f - normalized) * 2f;
                    scale *= Mathf.Lerp(1f, _timings.PressedScale, Smooth01(down));
                }
            }

            Apply(scale, alpha);
        }

        private float EntranceScale(float normalized)
        {
            const float overshootAt = .70f;
            if (normalized <= overshootAt)
                return Mathf.Lerp(_timings.StartScale, _timings.OvershootScale, Smooth01(normalized / overshootAt));
            return Mathf.Lerp(_timings.OvershootScale, 1f, Smooth01((normalized - overshootAt) / (1f - overshootAt)));
        }

        private void Apply(float scale, float alpha)
        {
            _visualRoot.localScale = Vector3.Scale(_baseScale, new Vector3(scale, scale, 1f));
            if (_visualCanvasGroup != null)
                _visualCanvasGroup.alpha = alpha;
        }

        private void CaptureBaseScale()
        {
            if (_visualRoot != null && _entranceElapsed < 0f && _pressElapsed < 0f)
                _baseScale = _visualRoot.localScale;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }
    }
}
