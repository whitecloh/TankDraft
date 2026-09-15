using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Non-interactive progress presentation made from a fixed, authored set of segments.
    /// The component never creates visual objects at runtime, so shape, spacing and decoration
    /// remain fully editable in the prefab.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSegmentedProgress : MonoBehaviour
    {
        [SerializeField] private Graphic[] segments;
        [SerializeField] private Color filledColor = Color.white;
        [SerializeField] private Color emptyColor = new Color(0.08f, 0.2f, 0.45f, 1f);
        [SerializeField] private bool revealCurrentSegment = true;
        [SerializeField, Range(0f, 1f)] private float value;

        public float Value => value;
        public int SegmentCount => segments?.Length ?? 0;
        public int FilledSegmentCount => ResolveFilledCount(value, SegmentCount, revealCurrentSegment);

        private void Awake()
        {
            Apply();
        }

        private void OnEnable()
        {
            Apply();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            value = Mathf.Clamp01(value);
            Apply();
        }
#endif

        public void Configure(
            Graphic[] authoredSegments,
            Color activeColor,
            Color inactiveColor,
            bool showCurrentSegment = true)
        {
            segments = authoredSegments;
            filledColor = activeColor;
            emptyColor = inactiveColor;
            revealCurrentSegment = showCurrentSegment;
            Apply();
        }

        public void SetValue(float progress)
        {
            value = Mathf.Clamp01(progress);
            Apply();
        }

        public static int ResolveFilledCount(float progress, int segmentCount, bool showCurrentSegment = true)
        {
            if (segmentCount <= 0 || progress <= 0f)
            {
                return 0;
            }

            float scaled = Mathf.Clamp01(progress) * segmentCount;
            int filled = showCurrentSegment ? Mathf.CeilToInt(scaled) : Mathf.FloorToInt(scaled);
            return Mathf.Clamp(filled, 0, segmentCount);
        }

        private void Apply()
        {
            if (segments == null)
            {
                return;
            }

            int filledCount = ResolveFilledCount(value, segments.Length, revealCurrentSegment);
            for (int i = 0; i < segments.Length; i++)
            {
                Graphic segment = segments[i];
                if (segment != null)
                {
                    segment.color = i < filledCount ? filledColor : emptyColor;
                    segment.raycastTarget = false;
                }
            }
        }
    }
}
