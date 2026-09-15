using System;
using TMPro;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>Samples a staggered glyph reveal without splitting localized text into objects.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(TMP_Text))]
    public sealed class UiGlyphPopReveal : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float startScale = 2.2f;
        [SerializeField, Range(0.05f, 1f)] private float glyphDurationFraction = 0.3f;
        private TMP_Text target;
        private Vector3[][] vertices;
        private Color32[][] colors;
        private float progress = 1f;
        private bool scaleEnabled;
        private bool subscribed;

        private void OnEnable()
        {
            EnsureTarget();
            target.ForceMeshUpdate();
        }

        private void EnsureTarget()
        {
            if (target == null) target = GetComponent<TMP_Text>();
            if (subscribed) return;
            target.OnPreRenderText += CaptureAndApply;
            subscribed = true;
        }

        private void OnDisable()
        {
            if (target == null) return;
            Sample(1f, false);
            target.OnPreRenderText -= CaptureAndApply;
            subscribed = false;
        }

        public void Sample(float value, bool animateScale = true)
        {
            if (vertices != null && Mathf.Approximately(progress, Mathf.Clamp01(value)) && scaleEnabled == animateScale) return;
            progress = Mathf.Clamp01(value);
            scaleEnabled = animateScale;
            EnsureTarget();
            // Hidden prefabs can be bound/reset before TMP has created its mesh. Keep the sample
            // for OnPreRenderText on activation; never upload vertex data to an uninitialized TMP.
            if (!isActiveAndEnabled || !target.isActiveAndEnabled) return;
            if (vertices == null) target.ForceMeshUpdate();
            if (vertices == null || target.textInfo.meshInfo == null) return;
            Apply(target.textInfo);
            target.UpdateVertexData(TMP_VertexDataUpdateFlags.Vertices | TMP_VertexDataUpdateFlags.Colors32);
        }

        private void CaptureAndApply(TMP_TextInfo info)
        {
            // Re-capture on localization, font or layout changes, never from our deformed mesh.
            if (vertices == null || vertices.Length < info.meshInfo.Length)
            {
                vertices = new Vector3[info.meshInfo.Length][];
                colors = new Color32[info.meshInfo.Length][];
            }
            for (int m = 0; m < info.meshInfo.Length; m++)
            {
                var mesh = info.meshInfo[m];
                if (vertices[m] == null || vertices[m].Length < mesh.vertices.Length)
                {
                    vertices[m] = new Vector3[mesh.vertices.Length];
                    colors[m] = new Color32[mesh.colors32.Length];
                }
                Array.Copy(mesh.vertices, vertices[m], mesh.vertices.Length);
                Array.Copy(mesh.colors32, colors[m], mesh.colors32.Length);
            }
            Apply(info);
        }

        private void Apply(TMP_TextInfo info)
        {
            if (vertices == null) return;
            for (int i = 0; i < info.characterCount; i++)
            {
                var ch = info.characterInfo[i];
                if (!ch.isVisible) continue;
                int m = ch.materialReferenceIndex, v = ch.vertexIndex;
                if (m >= vertices.Length || vertices[m] == null || v + 3 >= vertices[m].Length) continue;
                float delay = (1f - glyphDurationFraction) * i / Mathf.Max(1, info.characterCount - 1);
                float phase = Mathf.Clamp01((progress - delay) / glyphDurationFraction);
                float size = scaleEnabled ? Mathf.Lerp(startScale, 1f, 1f - Mathf.Pow(1f - phase, 3f)) : 1f;
                Vector3 center = (vertices[m][v] + vertices[m][v + 2]) * 0.5f;
                for (int k = 0; k < 4; k++)
                {
                    info.meshInfo[m].vertices[v + k] = center + (vertices[m][v + k] - center) * size;
                    Color32 color = colors[m][v + k];
                    color.a = (byte)(color.a * Mathf.Clamp01(phase * 3f));
                    info.meshInfo[m].colors32[v + k] = color;
                }
            }
        }
    }
}
