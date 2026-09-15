using System;
using System.IO;
using System.Security.Cryptography;
using TankDraft.Match.ServerClient;
using UnityEditor;

namespace TankDraft.Editor.FusionSetup
{
    public static class FusionContentCompatibility
    {
        public static string Sync()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || BuildPipeline.isBuildingPlayer)
                throw new InvalidOperationException("Content synchronization requires idle Edit Mode.");
            var bytes = File.ReadAllBytes("Backend/Content/local-match.json");
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
            if (!string.Equals(hash, File.ReadAllText("Backend/Content/local-match.sha256").Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("Battle export hash mismatch; export authored content before building.");
            var settings = AssetDatabase.LoadAssetAtPath<ServerClientSettings>("Assets/TankDraft/Configs/Match/Server/ServerClientSettings.asset");
            var queue = AssetDatabase.LoadAssetAtPath<NetworkQueueSettings>("Assets/TankDraft/Configs/Match/Server/NetworkQueueSettings.asset");
            if (!settings || !queue) throw new InvalidOperationException("Client/queue settings are missing.");
            foreach (var asset in new UnityEngine.Object[] { settings, queue })
            {
                var serialized = new SerializedObject(asset);
                var version = serialized.FindProperty("ContentVersion");
                if (version.stringValue == hash) continue;
                version.stringValue = hash;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(asset);
            }
            AssetDatabase.SaveAssets();
            return "Client content synchronized: " + hash;
        }
    }
}
