using System;
using System.Collections.Generic;
using UnityEngine;

namespace SLVR.UIMotion
{
    /// <summary>A ref-counted handle to one shared material variant.</summary>
    public sealed class UiShaderMaterialLease : IDisposable
    {
        private UiShaderMaterialDescriptor descriptor;
        private bool disposed;

        internal UiShaderMaterialLease(UiShaderMaterialDescriptor descriptor, Material material)
        {
            this.descriptor = descriptor;
            Material = material;
        }

        public Material Material { get; private set; }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            UiShaderMaterialCache.Release(descriptor);
            Material = null;
        }
    }

    /// <summary>Shares exact UI shader variants and destroys them when their last owner releases.</summary>
    public static class UiShaderMaterialCache
    {
        private sealed class Entry
        {
            public Material Material;
            public int ReferenceCount;
        }

        private static readonly Dictionary<UiShaderMaterialDescriptor, Entry> Entries =
            new Dictionary<UiShaderMaterialDescriptor, Entry>();

        private static readonly int ShimmerColorId = Shader.PropertyToID("_ShimmerColor");
        private static readonly int GradientAId = Shader.PropertyToID("_GradientA");
        private static readonly int GradientBId = Shader.PropertyToID("_GradientB");
        private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");
        private static readonly int WidthId = Shader.PropertyToID("_Width");
        private static readonly int SoftnessId = Shader.PropertyToID("_Softness");
        private static readonly int AngleId = Shader.PropertyToID("_Angle");
        private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        private static readonly int PhaseId = Shader.PropertyToID("_Phase");
        private static readonly int SpeedId = Shader.PropertyToID("_Speed");
        private static readonly int ScaleId = Shader.PropertyToID("_Scale");
        private static readonly int PulseAmountId = Shader.PropertyToID("_PulseAmount");
        private static readonly int AnimationEnabledId = Shader.PropertyToID("_AnimationEnabled");

        public static int CachedMaterialCount => Entries.Count;

        public static UiShaderMaterialLease Acquire(
            UiShaderMaterialDescriptor descriptor,
            UiMotionSettingsSnapshot settings)
        {
            UiShaderMaterialDescriptor resolved = descriptor.Resolve(settings);
            if (!Entries.TryGetValue(resolved, out Entry entry))
            {
                entry = new Entry
                {
                    Material = CreateMaterial(resolved),
                    ReferenceCount = 0,
                };
                Entries.Add(resolved, entry);
            }

            entry.ReferenceCount++;
            return new UiShaderMaterialLease(resolved, entry.Material);
        }

        internal static void Release(UiShaderMaterialDescriptor descriptor)
        {
            if (!Entries.TryGetValue(descriptor, out Entry entry)) return;

            entry.ReferenceCount--;
            if (entry.ReferenceCount > 0) return;

            Entries.Remove(descriptor);
            DestroyMaterial(entry.Material);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            foreach (Entry entry in Entries.Values)
            {
                DestroyMaterial(entry.Material);
            }

            Entries.Clear();
        }

        private static Material CreateMaterial(UiShaderMaterialDescriptor descriptor)
        {
            string shaderName = GetShaderName(descriptor.Effect);
            string resourceName = GetResourceName(descriptor.Effect);
            Material template = Resources.Load<Material>(resourceName);
            Shader shader = template != null ? template.shader : Shader.Find(shaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required UI shader was not found: {shaderName}");
            }

            Material material = template != null ? new Material(template) : new Material(shader);
            material.name = $"SLVR UI {descriptor.Effect} (Shared Runtime)";
            material.hideFlags = HideFlags.HideAndDontSave;
            ApplyDescriptor(material, descriptor);
            return material;
        }

        private static void ApplyDescriptor(Material material, UiShaderMaterialDescriptor descriptor)
        {
            material.SetFloat(IntensityId, descriptor.Intensity);
            material.SetFloat(PhaseId, descriptor.Phase);
            material.SetFloat(SpeedId, descriptor.Animate ? descriptor.Speed : 0f);
            material.SetFloat(AnimationEnabledId, descriptor.Animate ? 1f : 0f);

            switch (descriptor.Effect)
            {
                case UiShaderEffect.Shimmer:
                    material.SetColor(ShimmerColorId, descriptor.PrimaryColor);
                    material.SetFloat(WidthId, descriptor.Width);
                    material.SetFloat(SoftnessId, descriptor.Softness);
                    material.SetFloat(AngleId, descriptor.Angle);
                    break;
                case UiShaderEffect.GradientFlow:
                    material.SetColor(GradientAId, descriptor.PrimaryColor);
                    material.SetColor(GradientBId, descriptor.SecondaryColor);
                    material.SetFloat(ScaleId, descriptor.Scale);
                    material.SetFloat(AngleId, descriptor.Angle);
                    break;
                case UiShaderEffect.SoftGlowOverlay:
                    material.SetColor(GlowColorId, descriptor.PrimaryColor);
                    material.SetFloat(PulseAmountId, descriptor.PulseAmount);
                    break;
            }
        }

        private static string GetShaderName(UiShaderEffect effect)
        {
            switch (effect)
            {
                case UiShaderEffect.Shimmer:
                    return "SLVR/UI/Shimmer";
                case UiShaderEffect.GradientFlow:
                    return "SLVR/UI/GradientFlow";
                case UiShaderEffect.SoftGlowOverlay:
                    return "SLVR/UI/SoftGlowOverlay";
                default:
                    throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
            }
        }

        private static string GetResourceName(UiShaderEffect effect)
        {
            switch (effect)
            {
                case UiShaderEffect.Shimmer:
                    return "SLVR_UIMotion_Shimmer";
                case UiShaderEffect.GradientFlow:
                    return "SLVR_UIMotion_GradientFlow";
                case UiShaderEffect.SoftGlowOverlay:
                    return "SLVR_UIMotion_SoftGlowOverlay";
                default:
                    throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
            }
        }

        private static void DestroyMaterial(Material material)
        {
            if (material == null) return;
            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(material);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(material);
            }
        }
    }
}
