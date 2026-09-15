using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;
using TankDraft.Match.Content;
using TankDraft.Contracts.Battle;

// Execute through Unity MCP. Reads authored assets; writes only the standalone backend data snapshot.
public static class TankDraftBackendContentExport
{
    public static string Run()
    {
        const string source = "Assets/TankDraft/Configs/Match/MatchSettings.asset";
        var settings = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>(source);
        if (settings == null) throw new InvalidOperationException("Match settings missing.");
        settings.Validate();
        var definitions = settings.BattleCatalog.CreateDefinitions();
        var profile = settings.MetaCatalog.CreateInitialProfile();
        var rules = settings.BattleCatalog.CreateRules();
        // Server security policy disables debug commands; authored balance is otherwise unchanged.
        var serverRules = new BattleRules(rules.TickSeconds, rules.HalfWidth, rules.HalfHeight,
            rules.FrontOffset, rules.RowGap, rules.UnitGap, rules.SeparationIterations,
            rules.SeparationSpeed, rules.MaxEntities, false);
        var snapshot = new
        {
            SchemaVersion = 1,
            SourceAsset = source,
            MatchRules = settings.CreateRules(),
            MatchUnits = settings.CreateUnits(),
            BattleRules = serverRules,
            Units = definitions.Units,
            Projectiles = definitions.Projectiles,
            Zones = definitions.Zones,
            Deck = profile.UnitIds.ToArray(),
            OrderId = profile.OrderIds.Count > 0 ? profile.OrderIds[0] : "",
            Seed = settings.Seed
        };
        var jsonType = AppDomain.CurrentDomain.GetAssemblies()
            .First(a => a.GetName().Name == "Newtonsoft.Json").GetType("Newtonsoft.Json.JsonConvert");
        var serialize = jsonType.GetMethod("SerializeObject", new[] { typeof(object) });
        var json = ((string)serialize.Invoke(null, new object[] { snapshot })).Replace("\r\n", "\n") + "\n";
        var folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../Backend/Content"));
        Directory.CreateDirectory(folder);
        var bytes = new UTF8Encoding(false).GetBytes(json);
        string hash;
        using (var sha = SHA256.Create()) hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        File.WriteAllBytes(Path.Combine(folder, "local-match.json"), bytes);
        File.WriteAllText(Path.Combine(folder, "local-match.sha256"), hash + "\n", new UTF8Encoding(false));
        TankDraft.Editor.MetaServerExport.ExportRulesOnly();
        TankDraft.Editor.FusionSetup.FusionContentCompatibility.Sync();
        return "EXPORTED schema=1 units=" + definitions.Units.Count + " contentVersion=" + hash + " source=" + source;
    }
}
