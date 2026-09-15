using Fusion;
using TankDraft.Infrastructure.FusionTransport;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.FusionSetup
{
    public static class FusionReconnectAuthoring
    {
        public static string Configure()
        {
            const string path = "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionRunner.prefab";
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(path))
            {
                var template = new GameObject("FusionRunner");
                template.AddComponent<NetworkRunner>(); template.AddComponent<FusionDiagnosticSceneManager>(); template.AddComponent<FusionRunnerContext>();
                PrefabUtility.SaveAsPrefabAsset(template, path); Object.DestroyImmediate(template);
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefab.GetComponent<FusionRunnerContext>())
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try { root.AddComponent<FusionRunnerContext>(); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            }
            foreach (var target in new[] { "FusionDedicated", "FusionGameClient" })
            {
                var targetPath = "Assets/TankDraft/Prefabs/Diagnostics/Fusion/" + target + ".prefab";
                var root = PrefabUtility.LoadPrefabContents(targetPath);
                try
                {
                    var child = root.transform.Find("FusionRunner");
                    var instance = child ? child.gameObject : (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
                    var data = new SerializedObject(root.GetComponent<FusionDedicatedBootstrap>());
                    data.FindProperty("runner").objectReferenceValue = instance.GetComponent<NetworkRunner>();
                    data.FindProperty("sceneManager").objectReferenceValue = instance.GetComponent<FusionDiagnosticSceneManager>();
                    data.FindProperty("runnerPrefab").objectReferenceValue = prefab.GetComponent<NetworkRunner>();
                    data.ApplyModifiedPropertiesWithoutUndo();
                    var prior = root.GetComponent<NetworkRunner>(); if (prior) Object.DestroyImmediate(prior);
                    var manager = root.GetComponent<FusionDiagnosticSceneManager>(); if (manager) Object.DestroyImmediate(manager);
                    PrefabUtility.SaveAsPrefabAsset(root, targetPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); return "Authored replaceable runner child; persistent client scope retained.";
        }
    }
}
