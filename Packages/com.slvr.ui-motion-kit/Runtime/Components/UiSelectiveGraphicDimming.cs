using System;
using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>
    /// Applies a renderer-level color multiplier to selected UGUI graphics without changing their
    /// authored colors or creating material instances. Excluded subtrees stay fully bright.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class UiSelectiveGraphicDimming : MonoBehaviour
    {
        [SerializeField] private Transform visualRoot;
        [SerializeField] private Transform[] excludedRoots = Array.Empty<Transform>();
        [SerializeField] private Color dimMultiplier = new Color(0.34f, 0.37f, 0.43f, 1f);

        private Graphic[] graphics = Array.Empty<Graphic>();
        private bool captured;
        private bool dimmed;

        public bool IsDimmed => dimmed;
        public int GraphicCount => graphics.Length;

        public void Configure(
            Transform root,
            Transform excludedRoot = null,
            Color? multiplier = null)
        {
            if (captured && dimmed)
            {
                ApplyMultiplier(Color.white);
            }

            dimmed = false;
            visualRoot = root != null ? root : transform;
            excludedRoots = excludedRoot != null
                ? new[] { excludedRoot }
                : Array.Empty<Transform>();
            if (multiplier.HasValue)
            {
                Color value = multiplier.Value;
                dimMultiplier = new Color(
                    Mathf.Clamp01(value.r),
                    Mathf.Clamp01(value.g),
                    Mathf.Clamp01(value.b),
                    Mathf.Clamp01(value.a));
            }

            captured = false;
            CaptureGraphics();
        }

        public void SetDimmed(bool value)
        {
            CaptureGraphics();
            if (dimmed == value)
            {
                return;
            }

            dimmed = value;
            ApplyMultiplier(value ? dimMultiplier : Color.white);
        }

        private void OnDisable()
        {
            if (captured && dimmed)
            {
                ApplyMultiplier(Color.white);
                dimmed = false;
            }
        }

        private void CaptureGraphics()
        {
            if (captured)
            {
                return;
            }

            Transform root = visualRoot != null ? visualRoot : transform;
            Graphic[] candidates = root.GetComponentsInChildren<Graphic>(true);
            int includedCount = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                if (!IsExcluded(candidates[i].transform))
                {
                    includedCount++;
                }
            }

            graphics = new Graphic[includedCount];
            int destination = 0;
            for (int i = 0; i < candidates.Length; i++)
            {
                Graphic candidate = candidates[i];
                if (!IsExcluded(candidate.transform))
                {
                    graphics[destination++] = candidate;
                }
            }

            captured = true;
        }

        private void ApplyMultiplier(Color multiplier)
        {
            for (int i = 0; i < graphics.Length; i++)
            {
                Graphic graphic = graphics[i];
                if (graphic != null)
                {
                    graphic.canvasRenderer.SetColor(multiplier);
                }
            }
        }

        private bool IsExcluded(Transform candidate)
        {
            for (int i = 0; i < excludedRoots.Length; i++)
            {
                Transform excluded = excludedRoots[i];
                if (excluded != null && (candidate == excluded || candidate.IsChildOf(excluded)))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
