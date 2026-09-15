using UnityEditor;

namespace SLVR.UIMotion.Editor
{
    [CustomEditor(typeof(UiMotionTheme))]
    public sealed class UiMotionThemeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            var theme = (UiMotionTheme)target;
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Resolved normal duration", theme.Timings.Get(UiMotionTiming.Normal).ToString("0.### s"));
        }
    }
}
