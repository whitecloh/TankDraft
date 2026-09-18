using System;
using System.IO;
using TankDraft.Infrastructure.FusionGameplay;
using TankDraft.Infrastructure.FusionTransport;
using TankDraft.Match.ServerClient;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankDraft.Editor.FusionSetup
{
    public static class FusionGameClientAuthoring
    {
        const string ScenePath = "Assets/TankDraft/Scenes/Diagnostics/Fusion/FusionGameClient.unity";
        const string PrefabPath = "Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionGameClient.prefab";
        static bool queued;
        public static string RequestBuild()
        {
            if (queued || BuildPipeline.isBuildingPlayer) return "Build already pending";
            queued = true;
            EditorApplication.update += RunQueued;
            return "Gateway and gameplay PC builds queued; inspect dedicated-build.txt and game-client-build.txt.";
        }
        static void RunQueued()
        {
            if (EditorApplication.isCompiling || BuildPipeline.isBuildingPlayer) return;
            EditorApplication.update -= RunQueued;
            try { FusionContentCompatibility.Sync(); FusionServerAuthoring.Build(); BuildClient(); }
            catch (Exception error) { File.WriteAllText("Logs/FusionMigration/game-client-build.txt", "Failed " + error.GetType().Name); Debug.LogError("FUSION_GAME_CLIENT_BUILD_FAILED " + error.GetType().Name); }
            finally { queued = false; }
        }
        public static void Create()
        {
            FusionServerAuthoring.Create(); FusionServerAuthoring.ConfigureDiagnosticSceneManager();
            var prior = SceneManager.GetActiveScene();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                if (!File.Exists(PrefabPath))
                {
                    var root = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/Diagnostics/Fusion/FusionDedicated.prefab"), scene);
                    root.name = "FusionGameClient";
                    var flow = root.AddComponent<FusionGameClientFlow>();
                    root.AddComponent<FusionClientLifetimeScope>();
                    var serialized = new SerializedObject(flow);
                    serialized.FindProperty("connection").objectReferenceValue = root.GetComponent<FusionDedicatedBootstrap>();
                    serialized.FindProperty("settings").objectReferenceValue = AssetDatabase.LoadAssetAtPath<ServerClientSettings>("Assets/TankDraft/Configs/Match/Server/ServerClientSettings.asset");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath); UnityEngine.Object.DestroyImmediate(root);
                }
                PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath), scene);
                if (!File.Exists(ScenePath)) EditorSceneManager.SaveScene(scene, ScenePath);
            }
            finally { EditorSceneManager.CloseScene(scene, true); if(prior.IsValid()) SceneManager.SetActiveScene(prior); }
            AssetDatabase.SaveAssets();
        }
        static void BuildClient()
        {
            Create(); Directory.CreateDirectory("Builds/Fusion/Client");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[] { ScenePath, "Assets/TankDraft/Scenes/Gameplay/ServerMatch.unity", "Assets/TankDraft/Scenes/Frontend/MainMenu.unity" },
                locationPathName = "Builds/Fusion/Client/TankDraftFusionClient.exe", target = BuildTarget.StandaloneWindows64,
                subtarget = (int)StandaloneBuildSubtarget.Player, options = BuildOptions.None });
            File.WriteAllText("Logs/FusionMigration/game-client-build.txt", report.summary.result + " errors=" + report.summary.totalErrors + " bytes=" + report.summary.totalSize);
            if(report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Client build failed");
            File.Copy("Tools/Fusion/open-game-client.cmd", "Builds/Fusion/Client/StartGame.cmd", true);
        }
    }
}
