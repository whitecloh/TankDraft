using System;
using System.Collections.Generic;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.Battle
{
    public static class DefenseIndicatorValidation
    {
        public static string Run()
        {
            if (!EditorApplication.isPlaying) throw new InvalidOperationException("Requires Play Mode.");
            var catalog = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>("Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset");
            int count = 0;
            foreach (var entry in catalog.Entries)
            {
                if (entry.kind != BattleEntityKind.Unit) continue;
                var view = UnityEngine.Object.Instantiate(entry.prefab);
                try
                {
                    view.ValidateFor(entry.kind); view.Bind(123, Color.green);
                    var hud = view.GetComponentInChildren<BattleDefenseIndicators>(true);
                    view.Render(State(entry.definitionId, 25, 50, 1, 3, 6, 0, 3), 1, 0);
                    Require(hud.ShieldVisible && hud.BlockVisible && hud.MagazineVisible && !hud.IsReloading, "ready indicators");
                    Require(Mathf.Approximately(hud.ShieldFraction, .5f) && Mathf.Approximately(hud.MagazineFraction, .5f), "snapshot ratios");
                    view.Render(State(entry.definitionId, 0, 0, 0, 0, 6, 1.5f, 3), 1, 1);
                    Require(!hud.ShieldVisible && !hud.BlockVisible && hud.IsReloading && Mathf.Approximately(hud.MagazineFraction, .5f), "reload state");
                    view.Render(State(entry.definitionId, 0, 0, 0, 0, 6, 1.5f, 3), 1, 100);
                    Require(Mathf.Approximately(hud.MagazineFraction, .5f), "client must not advance authority timer");
                    view.Clear(); view.Bind(456, Color.red);
                    Require(!hud.ShieldVisible && !hud.BlockVisible && !hud.MagazineVisible && !hud.IsReloading, "pooled clear/rebind");
                    view.Render(State(entry.definitionId, 0, 0, 0, 0, 0, 0, 0), 1, 101);
                    Require(!hud.ShieldVisible && !hud.BlockVisible && !hud.MagazineVisible, "legacy unit indicators hidden");
                    count++;
                }
                finally { UnityEngine.Object.DestroyImmediate(view.gameObject); }
            }
            return "PASS " + count + " authored unit HUDs: snapshot ratios/reload/frozen timer/pool reset/legacy hiding";
        }

        public static BattleEntityState State(string id, int shield, int maxShield, int blocks, int ammo, int size, float remaining, float duration) =>
            new BattleEntityState(123, 0, BattleEntityKind.Unit, id, new BattleVec(0, 0), new BattleVec(0, 0), new BattleVec(0, 1),
                100, 100, .3f, 0, 0, shield, maxShield, blocks, ammo, size, remaining, duration);
        static void Require(bool condition, string reason) { if (!condition) throw new InvalidOperationException("Defense HUD: " + reason); }
    }
}
