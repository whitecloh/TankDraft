using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace SLVR.UIMotion.Tests
{
    public sealed class UiShaderMaterialCacheTests
    {
        private static readonly string[] RequiredUguiProperties =
        {
            "_MainTex",
            "_StencilComp",
            "_Stencil",
            "_StencilOp",
            "_StencilWriteMask",
            "_StencilReadMask",
            "_ColorMask",
            "_ClipRect",
            "_TextureSampleAdd",
            "_UseUIAlphaClip",
        };

        [TestCase("SLVR/UI/Shimmer", "SLVR_UIMotion_Shimmer")]
        [TestCase("SLVR/UI/GradientFlow", "SLVR_UIMotion_GradientFlow")]
        [TestCase("SLVR/UI/SoftGlowOverlay", "SLVR_UIMotion_SoftGlowOverlay")]
        public void Shader_IsSupportedAndHasStandardUguiContract(string shaderName, string resourceName)
        {
            Shader shader = Shader.Find(shaderName);
            Assert.That(shader, Is.Not.Null, shaderName);
            Assert.That(shader.isSupported, Is.True, shaderName);
            Assert.That(
                ShaderUtil.GetShaderMessages(shader)
                    .Where(message => message.severity == ShaderCompilerMessageSeverity.Error),
                Is.Empty,
                shaderName);

            Material template = Resources.Load<Material>(resourceName);
            Assert.That(template, Is.Not.Null, $"Missing build-retained material: {resourceName}");
            Assert.That(template.shader, Is.EqualTo(shader));
            foreach (string property in RequiredUguiProperties)
            {
                Assert.That(template.HasProperty(property), Is.True, $"{shaderName} missing {property}");
            }
        }

        [Test]
        public void Cache_SharesExactVariantUntilLastLeaseIsReleased()
        {
            int initialCount = UiShaderMaterialCache.CachedMaterialCount;
            UiShaderMaterialLease first = null;
            UiShaderMaterialLease second = null;
            try
            {
                first = UiShaderMaterialCache.Acquire(
                    UiShaderMaterialDescriptor.ShimmerDefault,
                    UiMotionDefaults.Settings);
                second = UiShaderMaterialCache.Acquire(
                    UiShaderMaterialDescriptor.ShimmerDefault,
                    UiMotionDefaults.Settings);

                Assert.That(second.Material, Is.SameAs(first.Material));
                Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount + 1));

                first.Dispose();
                first = null;
                Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount + 1));
            }
            finally
            {
                first?.Dispose();
                second?.Dispose();
            }

            Assert.That(UiShaderMaterialCache.CachedMaterialCount, Is.EqualTo(initialCount));
        }

        [Test]
        public void LowQuality_ResolvesAmbientVariantWithoutAnimation()
        {
            UiMotionQualityProfile lowQuality = AssetDatabase.LoadAssetAtPath<UiMotionQualityProfile>(
                "Packages/com.slvr.ui-motion-kit/Runtime/Presets/Quality/Quality_Low.asset");
            Assert.That(lowQuality, Is.Not.Null);
            var settings = new UiMotionSettingsSnapshot(
                null,
                lowQuality,
                1f,
                false,
                false,
                true,
                true,
                false);

            using (UiShaderMaterialLease lease = UiShaderMaterialCache.Acquire(
                       UiShaderMaterialDescriptor.GradientFlowDefault,
                       settings))
            {
                Assert.That(lease.Material.GetFloat("_AnimationEnabled"), Is.Zero);
                Assert.That(lease.Material.GetFloat("_Speed"), Is.Zero);
            }
        }
    }
}
