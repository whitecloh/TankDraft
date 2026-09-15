using System;
using System.Collections.Generic;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using UnityEditor;
using UnityEngine;

namespace TankDraft.Editor.Battle
{
    public static class DefenseIndicatorAuthoring
    {
        const string Catalog = "Assets/TankDraft/Configs/Battle/Presentation/BattleViewCatalog.asset";

        [MenuItem("TankDraft/Battle/Author Defense Indicators")]
        public static string Author()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Requires idle Edit Mode.");
            var catalog = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>(Catalog);
            var visited = new HashSet<string>();
            foreach (var entry in catalog.Entries)
            {
                if (entry.kind != BattleEntityKind.Unit) continue;
                string path = AssetDatabase.GetAssetPath(entry.prefab);
                if (!visited.Add(path)) continue;
                var root = PrefabUtility.LoadPrefabContents(path);
                try { Ensure(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.SaveAssets();
            foreach (var entry in catalog.Entries) entry.prefab.ValidateFor(entry.kind);
            return "PASS authored defense indicators on " + visited.Count + " unit prefabs";
        }

        public static void Ensure(GameObject root)
        {
            var view = root.GetComponent<BattleEntityView>();
            var old = root.GetComponentInChildren<BattleDefenseIndicators>(true);
            if (old != null) { old.Validate(); Set(view, "_defenseIndicators", old); return; }
            var square = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Square.png");
            var diamond = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/TankDraft/Art/Battle/Primitives/Primitive_Diamond.png");
            if (!square || !diamond) throw new InvalidOperationException("Prepared primitive sprites missing.");
            var hud = Child(root.transform, "DefenseIndicators", Vector3.zero);
            var indicator = hud.gameObject.AddComponent<BattleDefenseIndicators>();
            var shield = Bar(hud, "Shield", .70f, square, new Color(.23f, .8f, 1));
            var magazine = Bar(hud, "Magazine", .80f, square, new Color(1, .72f, .22f));
            var block = Child(hud, "FirstHitBlock", new Vector3(.43f, .58f, 0));
            Sprite(block, "Ready", diamond, Vector3.zero, new Vector3(.13f, .13f, 1), new Color(.72f, .9f, 1), 44);
            Set(indicator, "_shieldRoot", shield.gameObject); Set(indicator, "_magazineRoot", magazine.gameObject);
            Set(indicator, "_blockRoot", block.gameObject); Set(indicator, "_shieldFill", shield.Find("Fill"));
            Set(indicator, "_magazineFill", magazine.Find("Fill"));
            Set(indicator, "_magazineSprite", magazine.Find("Fill/Sprite").GetComponent<SpriteRenderer>());
            Set(view, "_defenseIndicators", indicator);
            shield.gameObject.SetActive(false); magazine.gameObject.SetActive(false); block.gameObject.SetActive(false);
            indicator.Validate();
        }

        static Transform Bar(Transform parent, string name, float y, Sprite sprite, Color color)
        {
            var root = Child(parent, name, new Vector3(-.3f, y, 0));
            Sprite(root, "Background", sprite, new Vector3(.3f, 0, 0), new Vector3(.64f, .075f, 1), new Color(.08f, .1f, .13f), 42);
            var fill = Child(root, "Fill", Vector3.zero);
            Sprite(fill, "Sprite", sprite, new Vector3(.3f, 0, 0), new Vector3(.6f, .043f, 1), color, 43);
            return root;
        }

        static Transform Child(Transform parent, string name, Vector3 position)
        {
            var child = new GameObject(name).transform;
            child.SetParent(parent, false); child.localPosition = position; return child;
        }
        static void Sprite(Transform parent, string name, Sprite sprite, Vector3 position, Vector3 scale, Color color, int order)
        {
            var child = Child(parent, name, position); child.localScale = scale;
            var renderer = child.gameObject.AddComponent<SpriteRenderer>();
            renderer.sprite = sprite; renderer.color = color; renderer.sortingOrder = order;
        }
        static void Set(UnityEngine.Object target, string name, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target); serialized.FindProperty(name).objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorUtility.SetDirty(target);
        }
    }
}
