using System;
using System.IO;
using Fusion;
using TankDraft.Infrastructure.FusionTransport;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankDraft.Editor.FusionSetup
{
    public static class FusionServerAuthoring
    {
        public const string ScenePath = "Assets/TankDraft/Scenes/Diagnostics/Fusion/FusionDedicated.unity";
        const string PrefabPath = "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionDedicated.prefab";
        static bool buildQueued;
        public static string ConfigureDiagnosticSceneManager()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Editor is playing.");
            var root = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var manager = root.GetComponentInChildren<FusionDiagnosticSceneManager>(true);
                if (manager == null) manager = root.AddComponent<FusionDiagnosticSceneManager>();
                var bootstrap = new SerializedObject(root.GetComponent<FusionDedicatedBootstrap>());
                bootstrap.FindProperty("sceneManager").objectReferenceValue = manager;
                bootstrap.ApplyModifiedPropertiesWithoutUndo();
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            return "Diagnostic scene manager authored; no network Addressable scenes.";
        }
        public static string RequestBuild()
        {
            if (buildQueued || BuildPipeline.isBuildingPlayer) return "Build already pending";
            buildQueued = true;
            EditorApplication.delayCall += () =>
            {
                try { Build(); }
                catch (Exception error) { Debug.LogError("Fusion build failed: " + error.GetType().Name); }
                finally { buildQueued = false; }
            };
            return "Build queued; inspect Logs/FusionMigration/dedicated-build.txt and Editor state";
        }
        [MenuItem("TankDraft/Networking/Fusion/Create Dedicated Diagnostic Assets")]
        public static void Create()
        {
            if (File.Exists(ScenePath) && File.Exists(PrefabPath)) return;
            Folder("Assets/TankDraft/Scenes/Diagnostics/Fusion"); Folder("Assets/TankDraft/Prefabs/Diagnostics/Fusion");
            var prior = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (!File.Exists(PrefabPath))
                {
                    var root = new GameObject("FusionDedicated");
                    var runner = root.AddComponent<NetworkRunner>();
                    var bootstrap = root.AddComponent<FusionDedicatedBootstrap>();
                    var serialized = new SerializedObject(bootstrap); serialized.FindProperty("runner").objectReferenceValue = runner; serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); UnityEngine.Object.DestroyImmediate(root);
                }
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                if (!File.Exists(ScenePath)) EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); if (prior.IsValid()) SceneManager.SetActiveScene(prior); }
            AssetDatabase.SaveAssets();
        }
        [MenuItem("TankDraft/Networking/Fusion/Build Windows Dedicated Diagnostic")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) throw new InvalidOperationException("Editor is busy.");
            FusionProjectSetup.Validate(); Create(); ConfigureDiagnosticSceneManager();
            Directory.CreateDirectory("Builds/Fusion/Server"); Directory.CreateDirectory("Logs/FusionMigration");
            // A separate headless Windows player in GameMode.Server. No user scenes are saved or added to build settings.
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath }, locationPathName = "Builds/Fusion/Server/TankDraftFusionServer.exe",
                target = BuildTarget.StandaloneWindows64, subtarget = (int)StandaloneBuildSubtarget.Player,
                options = BuildOptions.None
            });
            File.WriteAllText("Logs/FusionMigration/dedicated-build.txt", report.summary.result + " errors=" + report.summary.totalErrors + " bytes=" + report.summary.totalSize);
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Fusion diagnostic build failed. See Editor/build log.");
        }
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent); AssetDatabase.CreateFolder(parent, path.Substring(parent.Length + 1));
        }
    }
}
