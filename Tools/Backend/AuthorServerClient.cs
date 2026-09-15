using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TankDraft.BattlePresentation;
using TankDraft.Match.Content;
using TankDraft.Match.Presentation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Execute through Unity MCP after TankDraft.Match.ServerClient has compiled.
public static class TankDraftServerClientAuthoring
{
    const string LocalScenePath = "Assets/TankDraft/Scenes/Gameplay/Match.unity";
    const string ServerScenePath = "Assets/TankDraft/Scenes/Gameplay/ServerMatch.unity";
    const string LocalSettingsPath = "Assets/TankDraft/Configs/Match/MatchSettings.asset";
    const string SettingsPath = "Assets/TankDraft/Configs/Match/Server/ServerClientSettings.asset";
    const string ContentHashPath = "Backend/Content/local-match.sha256";
    const string ScopeTypeName = "TankDraft.Match.ServerClient.ServerClientScope, TankDraft.Match.ServerClient";
    const string SessionTypeName = "TankDraft.Match.ServerClient.ServerClientSession, TankDraft.Match.ServerClient";
    const string SettingsTypeName = "TankDraft.Match.ServerClient.ServerClientSettings, TankDraft.Match.ServerClient";
    const string LocalScopeTypeName = "TankDraft.Match.Bootstrap.MatchLifetimeScope";

    public static string Run()
    {
        RequireIdleCompiled();
        RequireCleanOpenScenes();
        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EnsureServerScene();
            ScriptableObject settings = CreateOrUpdateSettings();
            Scene scene = EditorSceneManager.OpenScene(ServerScenePath, OpenSceneMode.Single);
            AuthorScope(scene, settings);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Validate();
            return "PASS: server client settings and scene authored.";
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
    }

    public static string Validate()
    {
        RequireIdleCompiled();
        Type settingsType = RequiredType(SettingsTypeName);
        Type scopeType = RequiredType(ScopeTypeName);
        RequiredType(SessionTypeName);
        ScriptableObject settings = AssetDatabase.LoadAssetAtPath(SettingsPath, settingsType) as ScriptableObject;
        if (!settings) throw new InvalidOperationException("ServerClientSettings is missing.");
        MatchSettingsAsset local = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>(LocalSettingsPath);
        if (!local) throw new InvalidOperationException("Local MatchSettings is missing.");
        if (Read(settings, "MatchSettings") != local)
            throw new InvalidOperationException("ServerClientSettings must reference MatchSettings.asset.");
        if (string.IsNullOrWhiteSpace(Read(settings, "ContentVersion") as string))
            throw new InvalidOperationException("ServerClientSettings requires ContentVersion.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ServerScenePath) == null)
            throw new InvalidOperationException("ServerMatch scene is missing.");

        SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
        try
        {
            EditorSceneManager.OpenScene(ServerScenePath, OpenSceneMode.Single);
            UIMatchScreen screen = UnityEngine.Object.FindFirstObjectByType<UIMatchScreen>();
            BattleWorldView world = UnityEngine.Object.FindFirstObjectByType<BattleWorldView>();
            if (!screen || !world) throw new InvalidOperationException("ServerMatch requires UIMatchScreen and BattleWorldView.");
            screen.Validate();
            Component scope = UnityEngine.Object.FindFirstObjectByType(scopeType) as Component;
            if (!scope) throw new InvalidOperationException("ServerClientLifetimeScope is missing.");
            if (FindComponents(LocalScopeTypeName).Any())
                throw new InvalidOperationException("Local MatchLifetimeScope remains in ServerMatch.");
            if (FindPhotonComponents().Any())
                throw new InvalidOperationException("ServerMatch must not contain Photon components.");
            if (Read(scope, "_settings") != settings || Read(scope, "_world") != world || Read(scope, "_screen") != screen)
                throw new InvalidOperationException("ServerClientLifetimeScope references are not persisted.");
        }
        finally
        {
            EditorSceneManager.RestoreSceneManagerSetup(previous);
        }
        return "PASS: server client config, screen, world and scope bindings.";
    }

    static void EnsureServerScene()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ServerScenePath) != null) return;
        Directory.CreateDirectory(Path.GetDirectoryName(ServerScenePath));
        if (!AssetDatabase.CopyAsset(LocalScenePath, ServerScenePath))
            throw new IOException("Could not copy Match scene to ServerMatch.");
    }

    static ScriptableObject CreateOrUpdateSettings()
    {
        Type settingsType = RequiredType(SettingsTypeName);
        MatchSettingsAsset local = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>(LocalSettingsPath);
        if (!local) throw new InvalidOperationException("MatchSettings.asset is missing.");
        string hashPath = Path.GetFullPath(ContentHashPath);
        if (!File.Exists(hashPath)) throw new FileNotFoundException("Content version file is missing.", hashPath);
        string contentVersion = File.ReadAllText(hashPath).Trim();
        if (string.IsNullOrWhiteSpace(contentVersion)) throw new InvalidOperationException("Content version file is empty.");
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath));
        ScriptableObject settings = AssetDatabase.LoadAssetAtPath(SettingsPath, settingsType) as ScriptableObject;
        if (!settings)
        {
            settings = ScriptableObject.CreateInstance(settingsType);
            AssetDatabase.CreateAsset(settings, SettingsPath);
        }
        Set(settings, "MatchSettings", local);
        Set(settings, "ContentVersion", contentVersion);
        Set(settings, "Protocol", "tankdraft-server-v2");
        Set(settings, "PollMilliseconds", 100);
        Set(settings, "RetryMilliseconds", 1000);
        Set(settings, "TimeoutMilliseconds", 5000);
        Set(settings, "MaxResponseBytes", 1048576);
        Set(settings, "PresentationDelaySeconds", .2f);
        Set(settings, "MaxBufferedFrames", 8);
        Set(settings, "Connecting", "Подключение к серверу");
        Set(settings, "Recovering", "Восстанавливаем связь…");
        Set(settings, "Waiting", "Ожидаем сервер");
        Set(settings, "Failed", "Соединение недоступно");
        Set(settings, "Title", "Сетевой бой");
        Set(settings, "TimerFormat", "Выбор: {0} сек.");
        EditorUtility.SetDirty(settings);
        return settings;
    }

    static void AuthorScope(Scene scene, ScriptableObject settings)
    {
        foreach (Component localScope in FindComponents(LocalScopeTypeName).ToArray())
            UnityEngine.Object.DestroyImmediate(localScope.gameObject);
        UIMatchScreen screen = UnityEngine.Object.FindFirstObjectByType<UIMatchScreen>();
        BattleWorldView world = UnityEngine.Object.FindFirstObjectByType<BattleWorldView>();
        if (!screen || !world) throw new InvalidOperationException("Copied Match scene requires UIMatchScreen and BattleWorldView.");
        Type scopeType = RequiredType(ScopeTypeName);
        Component scope = UnityEngine.Object.FindFirstObjectByType(scopeType) as Component;
        if (!scope)
        {
            GameObject root = new GameObject("ServerClientLifetimeScope");
            scope = root.AddComponent(scopeType);
        }
        Set(scope, "_settings", settings);
        Set(scope, "_world", world);
        Set(scope, "_screen", screen);
        EditorSceneManager.MarkSceneDirty(scene);
    }

    static void RequireIdleCompiled()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorUtility.scriptCompilationFailed)
            throw new InvalidOperationException("Idle compiled Editor in Edit Mode required.");
    }

    static void RequireCleanOpenScenes()
    {
        for (int index = 0; index < SceneManager.sceneCount; index++)
            if (SceneManager.GetSceneAt(index).isDirty)
                throw new InvalidOperationException("Save open scenes before server client authoring.");
    }

    static Type RequiredType(string qualifiedName) => Type.GetType(qualifiedName, true);

    static Component[] FindComponents(string fullName) => UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(component => component.GetType().FullName == fullName).ToArray();

    static Component[] FindPhotonComponents() => UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
        .Where(component => (component.GetType().Namespace ?? string.Empty).StartsWith("Photon", StringComparison.Ordinal)).ToArray();

    static object Read(object target, string name)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) return field.GetValue(target);
        PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property != null && property.CanRead) return property.GetValue(target);
        throw new MissingMemberException(target.GetType().Name, name);
    }

    static void Set(object target, string name, object value)
    {
        FieldInfo field = target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (field != null) field.SetValue(target, value);
        else
        {
            PropertyInfo property = target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null || !property.CanWrite) throw new MissingMemberException(target.GetType().Name, name);
            property.SetValue(target, value);
        }
        if (target is UnityEngine.Object unityObject) EditorUtility.SetDirty(unityObject);
    }
}
