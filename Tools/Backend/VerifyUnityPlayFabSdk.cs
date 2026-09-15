using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.SceneManagement;
using UnityEngine;

// Execute through Unity MCP in Edit Mode. Inspection only: never starts an SDK or sends API calls.
public static class TankDraftUnityPlayFabSdkValidation
{
    public static string Run()
    {
        Check(!EditorApplication.isCompiling && !EditorApplication.isPlaying, "Editor must be idle in Edit Mode");
        var sdk = AppDomain.CurrentDomain.GetAssemblies().Single(a => a.GetName().Name == "PlayFab");
        var settingsType = sdk.GetType("PlayFab.PlayFabSettings", true);
        Check((string)settingsType.GetField("SdkVersion").GetRawConstantValue() == "2.242.260805", "Unexpected SDK version");
        Check(sdk.GetType("PlayFab.PlayFabClientAPI") != null, "Client API missing");
        Check(sdk.GetType("PlayFab.PlayFabServerAPI") == null && sdk.GetType("PlayFab.PlayFabAdminAPI") == null,
            "Server/admin API compiled into Unity SDK");

        var assets = AssetDatabase.FindAssets("t:PlayFabSharedSettings");
        Check(assets.Length == 1, "Exactly one authored SDK settings asset required");
        var path = AssetDatabase.GUIDToAssetPath(assets[0]);
        Check(path == "Assets/TankDraft/Configs/Infrastructure/PlayFab/Resources/PlayFabSharedSettings.asset", "Settings folder mismatch");
        var settings = new SerializedObject(AssetDatabase.LoadMainAssetAtPath(path));
        Check(string.IsNullOrEmpty(settings.FindProperty("DeveloperSecretKey").stringValue), "Client contains a developer key");
        Check(settings.FindProperty("DisableDeviceInfo").boolValue && settings.FindProperty("DisableFocusTimeCollection").boolValue
            && !settings.FindProperty("EnableRealTimeLogging").boolValue, "SDK telemetry/logging must remain disabled");

        var forbidden = new[] { "ENABLE_PLAYFABSERVER_API", "ENABLE_PLAYFABADMIN_API", "ENABLE_PLAYFAB_SECRETKEY" };
        foreach (var target in new[] { NamedBuildTarget.Standalone, NamedBuildTarget.Android, NamedBuildTarget.iOS })
        {
            var symbols = PlayerSettings.GetScriptingDefineSymbols(target).Split(';');
            Check(!symbols.Intersect(forbidden).Any(), "Forbidden server SDK symbols for " + target.TargetName);
        }
        var definition = JsonUtility.FromJson<SdkAssemblyDefinition>(File.ReadAllText("Assets/PlayFabSDK/PlayFab.asmdef"));
        Check(!definition.autoReferenced, "SDK must use explicit assembly references");
        var scene = EditorSceneManager.GetActiveScene();
        return "PASS Unity PlayFab SDK 2.242.260805: client API, authored settings, no secret/server defines, telemetry off, explicit assembly boundary; titleConfigured="
            + !string.IsNullOrEmpty(settings.FindProperty("TitleId").stringValue)
            + "; scene=" + scene.path + "; dirty=" + scene.isDirty + "; no login or cloud calls performed";
    }

    private static void Check(bool value, string message)
    {
        if (!value) throw new InvalidOperationException(message);
    }

    [Serializable]
    private sealed class SdkAssemblyDefinition
    {
        public bool autoReferenced = true;
    }
}
