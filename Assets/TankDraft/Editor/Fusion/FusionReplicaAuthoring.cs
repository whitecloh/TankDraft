using System;
using System.Linq;
using Fusion;
using Fusion.Editor;
using TankDraft.Infrastructure.FusionTransport;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.FusionSetup
{
    public static class FusionReplicaAuthoring
    {
        const string Path = "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionBattleReplica.prefab";
        public static string Configure()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException();
            var config = NetworkProjectConfig.Global;
            config.AssembliesToWeave = config.AssembliesToWeave.Concat(new[] { "TankDraft.Fusion.Transport" }).Distinct().ToArray();
            // 256 entity slots include defensive state; the complete replica exceeds 32 KiB.
            config.Heap.PageShift = (PageSizes)16;
            NetworkProjectConfigUtilities.SaveGlobalConfig(config);
            if (!AssetDatabase.LoadAssetAtPath<GameObject>(Path))
            {
                var root = new GameObject("FusionBattleReplica");
                try { root.AddComponent<NetworkObject>(); root.AddComponent<FusionBattleReplica>(); PrefabUtility.SaveAsPrefabAsset(root, Path); }
                finally { UnityEngine.Object.DestroyImmediate(root); }
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(Path).GetComponent<NetworkObject>();
            prefab.ForceRemoteRenderTimeframe = true; EditorUtility.SetDirty(prefab);
            foreach (var path in new[] { "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionDedicated.prefab", "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionGameClient.prefab" })
            {
                var root = PrefabUtility.LoadPrefabContents(path);
                try
                {
                    var serialized = new SerializedObject(root.GetComponent<FusionDedicatedBootstrap>());
                    serialized.FindProperty("battleReplicaPrefab").objectReferenceValue = prefab;
                    serialized.FindProperty("snapshotPeriod").floatValue = .05f;
                    serialized.ApplyModifiedPropertiesWithoutUndo(); PrefabUtility.SaveAsPrefabAsset(root, path);
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets(); NetworkProjectConfigUtilities.RebuildPrefabTable();
            return "Authored private native replica, 50ms source cadence; Fusion weaving/prefab table configured.";
        }
    }
}
