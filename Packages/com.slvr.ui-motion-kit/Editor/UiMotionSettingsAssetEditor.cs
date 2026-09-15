using UnityEditor;

namespace SLVR.UIMotion.Editor
{
    [CustomEditor(typeof(UiMotionSettingsAsset))]
    public sealed class UiMotionSettingsAssetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox(
                "Persistence is owned by the host project. This asset only supplies defaults and runtime snapshots.",
                MessageType.Info);
            DrawDefaultInspector();
        }
    }
}
