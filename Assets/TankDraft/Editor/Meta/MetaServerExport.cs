using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Newtonsoft.Json;
using TankDraft.Content;
using UnityEditor;

namespace TankDraft.Editor
{
    public static class MetaServerExport
    {
        [MenuItem("TankDraft/Backend/Export Meta Rules")]
        public static void Export() => Export(true);

        public static void ExportRulesOnly() => Export(false);

        private static void Export(bool includeProvisioningReview)
        {
            var ids = AssetDatabase.FindAssets("t:MetaCatalogAsset");
            if (ids.Length != 1) throw new InvalidOperationException("Expected one authored meta catalog.");
            var catalog = AssetDatabase.LoadAssetAtPath<MetaCatalogAsset>(AssetDatabase.GUIDToAssetPath(ids[0]));
            var definitions = catalog.CreateDefinitions();
            var profile = catalog.CreateInitialProfile();
            var root = Directory.GetParent(UnityEngine.Application.dataPath).FullName;
            var directory = Path.Combine(root, "Backend", "Content");
            var version = File.ReadAllText(Path.Combine(directory, "local-match.sha256")).Trim();
            var rules = new
            {
                ContentVersion = version, UnitSlots = definitions.Rules.UnitSlots,
                OrderUnlockLevels = definitions.Rules.OrderUnlockLevels.ToArray(),
                Definitions = definitions.Entries.Select(e => new { e.Id, Kind = (int)e.Kind, e.RequiredArena }).ToArray(),
                InitialProfile = new { SchemaVersion = 1, Name = profile.PlayerName, profile.CommanderLevel, profile.ArenaLevel,
                    profile.ArenaProgress, profile.Mastery, UnitIds = profile.UnitIds.ToArray(), OrderIds = profile.OrderIds.ToArray(),
                    LastOperationId = (string)null, LastOperationFingerprint = (string)null }
            };
            var path = Path.Combine(directory, "meta-rules.json");
            File.WriteAllText(path, JsonConvert.SerializeObject(rules, Formatting.Indented));
            using (var hash = SHA256.Create())
                File.WriteAllText(Path.ChangeExtension(path, ".sha256"), BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant());
            if (!includeProvisioningReview) return;
            // Review artifact only. Importing catalog/issuing test items is a separate, explicitly approved operation.
            File.WriteAllText(Path.Combine(directory, "meta-provisioning-review.json"), JsonConvert.SerializeObject(new
            {
                TitleId = "B16D9", Mode = "PlayFabClosedQa", Apply = false,
                Catalog = definitions.Entries.Select(e => new { ContentId = e.Id, ItemType = "catalogItem" }).ToArray(),
                StarterInventory = profile.OwnedIds.Select(id => new { ContentId = id, Amount = 1 }).ToArray(),
                CurrencyGrants = new object[0], RealMoneyProducts = new object[0], Target = "existing two QA identities only"
            }, Formatting.Indented));
        }
    }
}
