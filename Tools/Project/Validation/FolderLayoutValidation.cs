using System;
using System.IO;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

public static class FolderLayoutValidation
{
    [Serializable] public sealed class Entry { public string path; public string guid; }
    [Serializable] public sealed class Entries { public Entry[] items; }
    public static string Run()
    {
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
            throw new Exception("Idle compiled Editor required.");
        const string root = "Assets/TankDraft";
        int prefabs = 0, assets = 0, preserved = 0;
        var guids = new HashSet<string>();
        foreach (string path in AssetDatabase.GetAllAssetPaths())
        {
            if (!path.StartsWith(root + "/", StringComparison.Ordinal) || AssetDatabase.IsValidFolder(path)) continue;
            string guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || !guids.Add(guid)) throw new Exception("Missing/duplicate GUID: " + path);
            assets++;
            if (path.EndsWith(".prefab", StringComparison.Ordinal))
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (!prefab) throw new Exception("Prefab cannot load: " + path);
                foreach (var transform in prefab.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(transform.gameObject) != 0)
                        throw new Exception("Missing prefab script: " + path + "/" + transform.name);
                    foreach (var component in transform.GetComponents<Component>())
                    {
                        if (!component) continue;
                        var serialized = new SerializedObject(component); var property = serialized.GetIterator();
                        while (property.Next(true))
                            if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue == null && property.objectReferenceInstanceIDValue != 0)
                                throw new Exception("Broken reference: " + path + "/" + transform.name + ":" + property.propertyPath);
                    }
                }
                prefabs++;
            }
            if (path.EndsWith(".asset", StringComparison.Ordinal) && path.StartsWith(root + "/Configs/", StringComparison.Ordinal))
            {
                string parent = Path.GetDirectoryName(path).Replace('\\', '/');
                if (parent == root + "/Configs" || parent == root + "/Configs/Meta" || parent == root + "/Configs/Battle")
                    throw new Exception("Config lacks a responsibility folder: " + path);
                if (!AssetDatabase.LoadMainAssetAtPath(path)) throw new Exception("Config cannot load: " + path);
            }
        }
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.enabled && !AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path)) throw new Exception("Build scene missing: " + scene.path);
        foreach (string path in new[] {
            root + "/Scenes/Frontend/MainMenu.unity", root + "/Scenes/Gameplay/Match.unity",
            root + "/Scenes/Diagnostics/BattlePrototype.unity",
            root + "/Configs/Battle/Scenarios/Scenario_Standard.asset",
            root + "/Configs/Battle/Scenarios/Scenario_Dense.asset",
            root + "/Configs/Battle/Scenarios/Scenario_Artillery.asset" })
            if (!AssetDatabase.LoadMainAssetAtPath(path)) throw new Exception("Required asset missing: " + path);
        string before = "Logs/TankDraftSetup/FolderMigration/guid-map-before.json";
        if (File.Exists(before))
            foreach (System.Text.RegularExpressions.Match row in System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(before), @"""path""\s*:\s*""([^""]+)""\s*,\s*""guid""\s*:\s*""([0-9a-f]{32})"""))
            {
                string path = row.Groups[1].Value, guid = row.Groups[2].Value;
                if (string.IsNullOrEmpty(Path.GetExtension(path))) continue;
                if (string.IsNullOrEmpty(AssetDatabase.GUIDToAssetPath(guid))) throw new Exception("Migration lost asset GUID: " + path);
                preserved++;
            }
        string report = "PASS " + assets + " own assets with unique GUIDs; " + prefabs + " prefabs without missing scripts/references; " + preserved + " original asset GUIDs retained; config folders and build scenes valid.";
        Directory.CreateDirectory("Logs/TankDraftSetup/FolderMigration");
        File.WriteAllText("Logs/TankDraftSetup/FolderMigration/final-project-audit.txt", report);
        return report;
    }
}
