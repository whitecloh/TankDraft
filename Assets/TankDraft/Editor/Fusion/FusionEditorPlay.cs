using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace TankDraft.Editor.FusionSetup
{
    // Editor-only adapter. Runtime still enters through the authored Fusion lifetime scope.
    [InitializeOnLoad]
    public static class FusionEditorPlay
    {
        const string Entry = "Assets/TankDraft/Scenes/Diagnostics/Fusion/FusionGameClient.unity";
        const string Menu = "Assets/TankDraft/Scenes/Frontend/MainMenu.unity";
        const string Key = "TankDraft.Fusion.EditorPlay.";
        static bool preparing;
        static readonly string[] Variables = { "TANKDRAFT_FUSION_RUNTIME_PATH", "TD_FUSION_STATE_DIRECTORY", "TD_LOCAL_AUTO", "TD_QUEUE_MODE", "TD_FUSION_AUTO_QUEUE", "TD_QUEUE_AUTO_REMAINING", "TD_FUSION_QA_COLD_PENDING", "TD_FUSION_CAPTURE_SCREENSHOTS" };
        static FusionEditorPlay() { EditorApplication.playModeStateChanged += Changed; }
        [MenuItem("TankDraft/Networking/Fusion/Play in Editor")]
        public static void Play()
        {
            if (!preparing && !EditorApplication.isPlayingOrWillChangePlaymode) Prepare();
        }
        [MenuItem("TankDraft/Networking/Fusion/Use Fusion for MainMenu Play")]
        static void Toggle() => EditorPrefs.SetBool(Key + "Enabled", !EditorPrefs.GetBool(Key + "Enabled", true));
        [MenuItem("TankDraft/Networking/Fusion/Use Fusion for MainMenu Play", true)]
        static bool ValidateToggle() { UnityEditor.Menu.SetChecked("TankDraft/Networking/Fusion/Use Fusion for MainMenu Play", EditorPrefs.GetBool(Key + "Enabled", true)); return true; }
        static void Changed(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingEditMode && !SessionState.GetBool(Key + "Armed", false) && !preparing &&
                !Application.isBatchMode && !BuildPipeline.isBuildingPlayer && EditorPrefs.GetBool(Key + "Enabled", true) &&
                (SceneManager.GetActiveScene().path == Menu || SceneManager.GetActiveScene().path == Entry))
            {
                EditorApplication.isPlaying = false;
                EditorApplication.update -= BeginAfterCancelledPlay;
                EditorApplication.update += BeginAfterCancelledPlay;
            }
            if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(Key + "Armed", false) && !preparing) Restore();
        }
        static void BeginAfterCancelledPlay()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
            EditorApplication.update -= BeginAfterCancelledPlay;
            Play();
        }
        static async void Prepare()
        {
            preparing = true;
            try
            {
                var workspace = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
                var script = Path.Combine(workspace, "Tools/Fusion/prepare-editor-play.ps1");
                var start = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"")
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = workspace };
                // Windows PowerShell must load its own modules, not inherited PS7 modules.
                start.EnvironmentVariables.Remove("PSModulePath");
                using (var process = Process.Start(start))
                {
                    if (process == null) throw new InvalidOperationException();
                    var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
                    var deadline = EditorApplication.timeSinceStartup + 60;
                    while (!process.HasExited)
                    {
                        if (EditorApplication.timeSinceStartup > deadline) { process.Kill(); throw new TimeoutException(); }
                        await Task.Delay(100);
                    }
                    await output; await error; // Never put private helper/provider output into the Unity console.
                    if (process.ExitCode != 0) throw new InvalidOperationException();
                }
                var entry = AssetDatabase.LoadAssetAtPath<SceneAsset>(Entry);
                if (!entry) throw new InvalidOperationException("Fusion entry scene missing.");
                EnsureScenes();
                var privateRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-secrets", "TankDraft");
                SessionState.SetString(Key + "PreviousScene", EditorSceneManager.playModeStartScene ? AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene) : "");
                foreach (var name in Variables) { SessionState.SetString(Key + name, Environment.GetEnvironmentVariable(name) ?? ""); Environment.SetEnvironmentVariable(name, null); }
                Environment.SetEnvironmentVariable("TANKDRAFT_FUSION_RUNTIME_PATH", Path.Combine(privateRoot, "fusion-editor-run", "gateway.json"));
                Environment.SetEnvironmentVariable("TD_FUSION_STATE_DIRECTORY", Path.Combine(privateRoot, "fusion-editor-resume"));
                SessionState.SetBool(Key + "Armed", true);
                EditorSceneManager.playModeStartScene = entry;
                UnityEngine.Debug.Log("FUSION_EDITOR_PREPARED: Play connects to Server Manager using tester 0. Press В БОЙ in the menu.");
                EditorApplication.isPlaying = true;
            }
            catch (Exception error)
            {
                if (SessionState.GetBool(Key + "Armed", false)) Restore();
                UnityEngine.Debug.LogError("FUSION_EDITOR_PREPARE_FAILED " + error.GetType().Name + ": запустите сервер в http://127.0.0.1:18878/, дождитесь «Готов к подключениям» и повторите Play. При ошибке входа проверьте VPN. Секреты в консоль не выводятся.");
            }
            finally { preparing = false; }
        }
        static void Restore()
        {
            SessionState.SetBool(Key + "Armed", false);
            EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>(SessionState.GetString(Key + "PreviousScene", ""));
            foreach (var name in Variables) { var previous = SessionState.GetString(Key + name, ""); Environment.SetEnvironmentVariable(name, previous.Length == 0 ? null : previous); SessionState.EraseString(Key + name); }
        }
        public static void EnsureScenes()
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var path in new[] { Entry, Menu, "Assets/TankDraft/Scenes/Gameplay/ServerMatch.unity" })
            {
                if (!AssetDatabase.LoadAssetAtPath<SceneAsset>(path)) throw new InvalidOperationException("Authored Fusion flow scene missing.");
                var index = scenes.FindIndex(s => s.path == path);
                if (index < 0) scenes.Add(new EditorBuildSettingsScene(path, true));
                else scenes[index] = new EditorBuildSettingsScene(path, true);
            }
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
