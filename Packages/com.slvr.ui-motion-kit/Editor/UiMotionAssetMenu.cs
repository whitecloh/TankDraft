using System;
using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Editor
{
    public static class UiMotionAssetMenu
    {
        private const string PresetsRoot = "Packages/com.slvr.ui-motion-kit/Runtime/Presets";
        private const string ThemesRoot = PresetsRoot + "/Themes";
        private const string QualityRoot = PresetsRoot + "/Quality";

        [MenuItem("Tools/SLVR/UI Motion/Create or Repair Default Assets")]
        public static void CreateOrRepairDefaultAssets()
        {
            AssetDatabase.StartAssetEditing();
            try
            {
                UiMotionBudgetProfile budget = LoadOrCreate<UiMotionBudgetProfile>(PresetsRoot + "/Default_Budget.asset");

                UiMotionTheme subtle = CreateTheme(
                    ThemesRoot + "/HMD_Subtle.asset",
                    new UiMotionTimingTokens(0f, 0.08f, 0.16f, 0.26f, 0.36f),
                    UiMotionEase.OutCubic,
                    UiMotionEase.OutCubic,
                    budget);
                CreateTheme(
                    ThemesRoot + "/Archero_Juicy.asset",
                    new UiMotionTimingTokens(0f, 0.1f, 0.22f, 0.38f, 0.58f),
                    UiMotionEase.OutCubic,
                    UiMotionEase.OutBack,
                    budget);
                UiMotionTheme balanced = CreateTheme(
                    ThemesRoot + "/Mobile_Balanced.asset",
                    UiMotionTimingTokens.Balanced,
                    UiMotionEase.OutCubic,
                    UiMotionEase.OutBack,
                    budget);
                CreateTheme(
                    ThemesRoot + "/Reduced_Motion.asset",
                    new UiMotionTimingTokens(0f, 0.06f, 0.12f, 0.18f, 0.24f),
                    UiMotionEase.Linear,
                    UiMotionEase.OutCubic,
                    budget);

                CreateQuality(QualityRoot + "/Quality_Low.asset", UiMotionQualityTier.Low, 6, false, false);
                UiMotionQualityProfile medium = CreateQuality(
                    QualityRoot + "/Quality_Medium.asset",
                    UiMotionQualityTier.Medium,
                    12,
                    true,
                    true);
                CreateQuality(QualityRoot + "/Quality_High.asset", UiMotionQualityTier.High, 20, true, true);

                UiMotionSettingsAsset settings = LoadOrCreate<UiMotionSettingsAsset>(
                    PresetsRoot + "/SLVR_UIMotion_Defaults.asset");
                var serializedSettings = new SerializedObject(settings);
                serializedSettings.FindProperty("activeTheme").objectReferenceValue = balanced != null ? balanced : subtle;
                serializedSettings.FindProperty("qualityProfile").objectReferenceValue = medium;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(settings);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }

        private static UiMotionTheme CreateTheme(
            string path,
            UiMotionTimingTokens timings,
            UiMotionEase standardEase,
            UiMotionEase emphasisEase,
            UiMotionBudgetProfile budget)
        {
            UiMotionTheme theme = LoadOrCreate<UiMotionTheme>(path);
            var serialized = new SerializedObject(theme);
            SerializedProperty timingsProperty = serialized.FindProperty("timings");
            SetTiming(timingsProperty, "immediate", timings.Get(UiMotionTiming.Immediate));
            SetTiming(timingsProperty, "fast", timings.Get(UiMotionTiming.Fast));
            SetTiming(timingsProperty, "normal", timings.Get(UiMotionTiming.Normal));
            SetTiming(timingsProperty, "slow", timings.Get(UiMotionTiming.Slow));
            SetTiming(timingsProperty, "emphasis", timings.Get(UiMotionTiming.Emphasis));
            serialized.FindProperty("standardEase").enumValueIndex = (int)standardEase;
            serialized.FindProperty("emphasisEase").enumValueIndex = (int)emphasisEase;
            serialized.FindProperty("budgetProfile").objectReferenceValue = budget;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(theme);
            return theme;
        }

        private static UiMotionQualityProfile CreateQuality(
            string path,
            UiMotionQualityTier tier,
            int currencyIconLimit,
            bool particles,
            bool ambientShaders)
        {
            UiMotionQualityProfile quality = LoadOrCreate<UiMotionQualityProfile>(path);
            var serialized = new SerializedObject(quality);
            serialized.FindProperty("tier").enumValueIndex = (int)tier;
            serialized.FindProperty("currencyIconLimit").intValue = currencyIconLimit;
            serialized.FindProperty("particlesEnabled").boolValue = particles;
            serialized.FindProperty("ambientShadersEnabled").boolValue = ambientShaders;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(quality);
            return quality;
        }

        private static void SetTiming(SerializedProperty parent, string name, float value)
        {
            SerializedProperty property = parent.FindPropertyRelative(name);
            if (property == null)
            {
                throw new InvalidOperationException($"Missing timing property: {name}");
            }

            property.floatValue = value;
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null)
            {
                return asset;
            }

            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }
    }
}
