using UnityEditor;
using UnityEngine;

namespace SLVR.UIMotion.Editor
{
    public static class UiMotionPresetValidator
    {
        [MenuItem("Tools/SLVR/UI Motion/Validate Preset Assets")]
        public static void Validate()
        {
            string[] themeGuids = AssetDatabase.FindAssets("t:UiMotionTheme");
            int invalid = 0;
            foreach (string guid in themeGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UiMotionTheme theme = AssetDatabase.LoadAssetAtPath<UiMotionTheme>(path);
                if (theme == null || theme.Timings.Get(UiMotionTiming.Normal) <= 0f)
                {
                    invalid++;
                    Debug.LogError($"[SLVR UI Motion] Invalid theme: {path}", theme);
                }
            }

            Debug.Log($"[SLVR UI Motion] Validation complete. Themes: {themeGuids.Length}, invalid: {invalid}.");
        }
    }
}
