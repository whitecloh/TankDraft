using UnityEngine;
using UnityEngine.UI;

namespace SLVR.UIMotion
{
    /// <summary>Assigns one cached shader variant to a UGUI Graphic for this component's active lifetime.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Graphic))]
    public sealed class UiShaderEffectGraphic : MonoBehaviour, IMeshModifier
    {
        [SerializeField] private Graphic target;
        [SerializeField] private UiMotionSettingsAsset settingsAsset;
        [SerializeField] private UiShaderMaterialDescriptor descriptor = UiShaderMaterialDescriptor.ShimmerDefault;

        private IUiMotionSettingsProvider settingsProvider;
        private UiShaderMaterialLease lease;
        private Material originalMaterial;

        public Material ActiveMaterial => lease?.Material;
        public UiShaderMaterialDescriptor Descriptor => descriptor;

        private void Reset()
        {
            target = GetComponent<Graphic>();
            descriptor = UiShaderMaterialDescriptor.ShimmerDefault;
        }

        private void Awake()
        {
            ResolveTarget();
        }

        private void OnEnable()
        {
            ResolveTarget();
            EnsureShaderChannels();
            SetVerticesDirty();
            SubscribeSettings();
            ApplyMaterial();
        }

        private void OnDisable()
        {
            UnsubscribeSettings();
            RestoreOriginalMaterial();
        }

        private void OnDestroy()
        {
            UnsubscribeSettings();
            RestoreOriginalMaterial();
        }

        public void SetSettingsProvider(IUiMotionSettingsProvider provider)
        {
            if (ReferenceEquals(settingsProvider, provider)) return;
            UnsubscribeSettings();
            settingsProvider = provider;
            SubscribeSettings();
            if (isActiveAndEnabled)
            {
                ApplyMaterial();
            }
        }

        public void SetDescriptor(UiShaderMaterialDescriptor value)
        {
            descriptor = value;
            SetVerticesDirty();
            if (isActiveAndEnabled)
            {
                ApplyMaterial();
            }
        }

        public void SetTarget(Graphic value)
        {
            if (target == value) return;
            RestoreOriginalMaterial();
            target = value;
            EnsureShaderChannels();
            SetVerticesDirty();
            if (isActiveAndEnabled)
            {
                ApplyMaterial();
            }
        }

        public void RefreshMaterial()
        {
            if (isActiveAndEnabled)
            {
                ResolveTarget();
                EnsureShaderChannels();
                SetVerticesDirty();
                target?.SetMaterialDirty();
                ApplyMaterial();
            }
        }

        /// <summary>
        /// Stores normalized local-rect coordinates in UV1. Unlike sprite UV0 these coordinates
        /// remain continuous across Image.Type.Sliced geometry, so procedural effects do not bend
        /// or repeat at nine-slice borders.
        /// </summary>
        public void ModifyMesh(VertexHelper vertexHelper)
        {
            if (!isActiveAndEnabled || vertexHelper == null || vertexHelper.currentVertCount == 0)
            {
                return;
            }

            Graphic ownGraphic = GetComponent<Graphic>();
            RectTransform rectTransform = ownGraphic != null ? ownGraphic.rectTransform : null;
            if (rectTransform == null)
            {
                return;
            }

            Rect rect = rectTransform.rect;
            float inverseWidth = rect.width > Mathf.Epsilon ? 1f / rect.width : 0f;
            float inverseHeight = rect.height > Mathf.Epsilon ? 1f / rect.height : 0f;
            UIVertex vertex = default;
            for (int i = 0; i < vertexHelper.currentVertCount; i++)
            {
                vertexHelper.PopulateUIVertex(ref vertex, i);
                vertex.uv1 = new Vector4(
                    (vertex.position.x - rect.xMin) * inverseWidth,
                    (vertex.position.y - rect.yMin) * inverseHeight,
                    0f,
                    0f);
                vertexHelper.SetUIVertex(vertex, i);
            }
        }

        [System.Obsolete("Use ModifyMesh(VertexHelper) instead.")]
        public void ModifyMesh(Mesh mesh)
        {
            if (mesh == null)
            {
                return;
            }

            using (var vertexHelper = new VertexHelper(mesh))
            {
                ModifyMesh(vertexHelper);
                vertexHelper.FillMesh(mesh);
            }
        }

        private void ApplyMaterial()
        {
            if (target == null) return;

            UiShaderMaterialLease nextLease = UiShaderMaterialCache.Acquire(descriptor, ResolveSettings());
            if (lease == null)
            {
                originalMaterial = target.material;
            }

            UiShaderMaterialLease previousLease = lease;
            lease = nextLease;
            target.material = nextLease.Material;
            previousLease?.Dispose();
        }

        private void RestoreOriginalMaterial()
        {
            if (lease == null) return;
            if (target != null)
            {
                target.material = originalMaterial;
            }

            lease.Dispose();
            lease = null;
            originalMaterial = null;
        }

        private void ResolveTarget()
        {
            if (target == null)
            {
                target = GetComponent<Graphic>();
            }
        }

        private void EnsureShaderChannels()
        {
            Canvas canvas = target != null ? target.canvas : null;
            if (canvas != null)
            {
                canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
            }
        }

        private void SetVerticesDirty()
        {
            Graphic ownGraphic = GetComponent<Graphic>();
            if (ownGraphic != null)
            {
                ownGraphic.SetVerticesDirty();
            }
        }

        private UiMotionSettingsSnapshot ResolveSettings()
        {
            if (settingsProvider != null)
            {
                return settingsProvider.Current;
            }

            return settingsAsset != null ? settingsAsset.CreateSnapshot() : UiMotionDefaults.Settings;
        }

        private void SubscribeSettings()
        {
            if (settingsProvider != null)
            {
                settingsProvider.Changed -= OnSettingsChanged;
                settingsProvider.Changed += OnSettingsChanged;
            }
        }

        private void UnsubscribeSettings()
        {
            if (settingsProvider != null)
            {
                settingsProvider.Changed -= OnSettingsChanged;
            }
        }

        private void OnSettingsChanged()
        {
            if (isActiveAndEnabled)
            {
                ApplyMaterial();
            }
        }
    }
}
