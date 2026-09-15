using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TankDraft.Editor.Previz
{
    /// <summary>Editor-only layout authoring. Generated prefabs contain no gameplay.</summary>
    public sealed class PrevizImporter : EditorWindow
    {
        [Serializable] public sealed class Viewport { public float width, height, safeTop, safeBottom; }
        [Serializable] public sealed class Theme { public string background, accent, text; }
        [Serializable] public sealed class Element
        {
            public string id, kind, label;
            public float x, y, width, height;
            public int row = -1;
            public string parentId;
        }
        [Serializable] public sealed class Group
        {
            public string id, type, alignment;
            public float x, y, width, height, cellWidth, cellHeight, spacingX, spacingY;
            public int paddingLeft, paddingRight, paddingTop, paddingBottom, columns;
            public string[] children;
        }
        [Serializable] public sealed class Layout
        {
            public int schemaVersion;
            public string screen, state;
            public Viewport viewport;
            public Theme theme;
            public Element[] elements;
            public Group[] groups;
        }

        private const string OutputRoot = "Assets/TankDraft/Art/UI/Previz/";
        private const float GeometryTolerance = 0.05f;
        private string sourcePath = "";
        private string status = "Выберите Layout JSON из Tools/Previz. Это макет, не игровой экран.";

        [MenuItem("Tools/TankDraft/Previz/Import Layout JSON")]
        public static void Open() { GetWindow<PrevizImporter>("TankDraft Previz"); }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox(status, MessageType.Info);
            EditorGUILayout.LabelField("JSON", sourcePath);
            if (GUILayout.Button("Выбрать Layout JSON"))
            {
                var path = EditorUtility.OpenFilePanel("TankDraft Layout v1/v2", "", "json");
                if (!string.IsNullOrEmpty(path)) sourcePath = path;
            }
            using (new EditorGUI.DisabledScope(!File.Exists(sourcePath)))
            {
                if (!GUILayout.Button("Проверить и создать новый prefab")) return;
                try
                {
                    var json = File.ReadAllText(sourcePath);
                    var layout = ParseAndValidate(json);
                    Directory.CreateDirectory(OutputRoot);
                    AssetDatabase.Refresh();
                    var path = AssetDatabase.GenerateUniqueAssetPath(OutputRoot + layout.screen + ".prefab");
                    status = Import(json, path);
                    EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                }
                catch (Exception exception) { status = exception.Message; }
            }
        }

        public static Layout ParseAndValidate(string json)
        {
            if (string.IsNullOrWhiteSpace(json) || json.Length > 500000) throw new ArgumentException("Invalid JSON size");
            var data = JsonUtility.FromJson<Layout>(json);
            if (data == null || (data.schemaVersion != 1 && data.schemaVersion != 2) || data.viewport == null || data.theme == null)
                throw new ArgumentException("Expected layout schemaVersion 1 or 2");
            if (data.schemaVersion == 1 && data.groups != null && data.groups.Length > 0) throw new ArgumentException("groups are only supported by schemaVersion 2");

            var screens = new HashSet<string> { "arena", "collection", "card", "draft", "battle", "result", "shop", "commander", "pass", "rating", "profile" };
            var states = new HashSet<string> { "normal", "locked", "loading", "error", "empty", "upgrade-ready", "comeback", "waiting" };
            if (!screens.Contains(data.screen) || !states.Contains(data.state)) throw new ArgumentException("Unknown screen/state");
            var v = data.viewport;
            if (!Finite(v.width) || !Finite(v.height) || v.width < 240 || v.height < 240 || v.width > 4096 || v.height > 4096 ||
                !Finite(v.safeTop) || !Finite(v.safeBottom) || v.safeTop < 0 || v.safeBottom < 0 || v.safeTop + v.safeBottom >= v.height)
                throw new ArgumentException("Invalid viewport/safe area");
            ParseColor(data.theme.background); ParseColor(data.theme.accent); ParseColor(data.theme.text);
            if (data.elements == null || data.elements.Length < 1 || data.elements.Length > 200) throw new ArgumentException("Invalid element count");

            var ids = new HashSet<string>();
            var byId = new Dictionary<string, Element>();
            var kinds = new HashSet<string> { "panel", "button", "label", "card", "vehicle" };
            foreach (var e in data.elements)
            {
                if (e == null || string.IsNullOrWhiteSpace(e.id) || e.id.Length > 100 || !ids.Add(e.id) || !kinds.Contains(e.kind) || e.label == null || e.label.Length > 500)
                    throw new ArgumentException("Invalid element id/kind/label");
                if (!Finite(e.x) || !Finite(e.y) || !Finite(e.width) || !Finite(e.height) || e.x < 0 || e.y < 0 || e.width <= 0 || e.height <= 0 ||
                    e.x + e.width > v.width + 0.01f || e.y + e.height > v.height + 0.01f || e.row < -1 || e.row > 3)
                    throw new ArgumentException("Invalid geometry/row: " + e.id);
                byId.Add(e.id, e);
            }

            var directGroupOwners = new Dictionary<string, Group>();
            if (data.groups != null)
            {
                if (data.groups.Length > 50) throw new ArgumentException("Invalid group count");
                var groupIds = new HashSet<string>();
                foreach (var group in data.groups)
                {
                    ValidateGroup(group, v, ids, groupIds, byId, directGroupOwners);
                }
            }

            foreach (var e in data.elements)
            {
                if (string.IsNullOrEmpty(e.parentId)) continue;
                if (string.IsNullOrWhiteSpace(e.parentId) || !byId.ContainsKey(e.parentId) || e.parentId == e.id)
                    throw new ArgumentException("Invalid parentId: " + e.id);
                if (directGroupOwners.ContainsKey(e.id))
                    throw new ArgumentException("A direct group child cannot also have parentId: " + e.id);
            }
            ValidateElementCycles(data.elements, byId);
            return data;
        }

        public static string Import(string json, string prefabPath)
        {
            var data = ParseAndValidate(json);
            prefabPath = prefabPath.Replace('\\', '/');
            if (!prefabPath.StartsWith(OutputRoot, StringComparison.Ordinal) || prefabPath.Contains("..") || !prefabPath.EndsWith(".prefab", StringComparison.Ordinal))
                throw new ArgumentException("Output must be a prefab under " + OutputRoot);

            var groupPaths = GetGroupPrefabPaths(data, prefabPath);
            EnsureOutputsDoNotExist(prefabPath, groupPaths);
            Directory.CreateDirectory(Path.GetDirectoryName(prefabPath));
            AssetDatabase.Refresh();
            EnsureOutputsDoNotExist(prefabPath, groupPaths);

            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("Previz_" + data.screen, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                SceneManager.MoveGameObjectToScene(root, scene);
                var rootRect = root.GetComponent<RectTransform>();
                rootRect.anchorMin = rootRect.anchorMax = rootRect.pivot = new Vector2(0, 1);
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.sizeDelta = new Vector2(data.viewport.width, data.viewport.height);
                root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
                var scaler = root.GetComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(data.viewport.width, data.viewport.height);
                scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                scaler.matchWidthOrHeight = 0.5f;
                var background = Rect("Background", root.transform, 0, 0, data.viewport.width, data.viewport.height);
                background.gameObject.AddComponent<Image>().color = ParseColor(data.theme.background);

                var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                var scale = Mathf.Min(data.viewport.width / 576f, data.viewport.height / 1280f);
                var byId = ToElementMap(data.elements);
                var groupMembers = ToDirectGroupMap(data.groups);
                var elementRects = new Dictionary<string, RectTransform>();
                foreach (var e in data.elements) CreateElementRecursive(e, root.transform, byId, groupMembers, elementRects, font, scale, data.theme);

                var groupCount = data.groups == null ? 0 : data.groups.Length;
                if (data.groups != null)
                {
                    for (var index = 0; index < data.groups.Length; index++)
                    {
                        var group = data.groups[index];
                        var template = CreateSlotTemplate(group.children[0], byId, font, scale, data.theme);
                        SceneManager.MoveGameObjectToScene(template.gameObject, scene);
                        PrefabUtility.SaveAsPrefabAsset(template.gameObject, groupPaths[index], out var templateSuccess);
                        if (!templateSuccess) throw new IOException("Group item prefab save failed: " + group.id);
                        UnityEngine.Object.DestroyImmediate(template.gameObject);

                        var groupRect = Rect(group.id, root.transform, group.x, group.y, group.width, group.height);
                        ConfigureLayoutGroup(groupRect, group);
                        var templateAsset = AssetDatabase.LoadAssetAtPath<GameObject>(groupPaths[index]);
                        if (templateAsset == null) throw new IOException("Saved group item prefab could not be loaded: " + group.id);
                        foreach (var childId in group.children)
                        {
                            var oldRect = elementRects[childId];
                            var instance = (GameObject)PrefabUtility.InstantiatePrefab(templateAsset, groupRect);
                            if (instance == null) throw new IOException("Group item prefab instantiate failed: " + group.id);
                            instance.name = childId;
                            var element = byId[childId];
                            var text = instance.GetComponentInChildren<Text>(true);
                            text.text = element.label;
                            text.name = "Label";
                            MoveChildren(oldRect, instance.transform);
                            UnityEngine.Object.DestroyImmediate(oldRect.gameObject);
                            elementRects[childId] = instance.GetComponent<RectTransform>();
                        }
                    }
                }
                Canvas.ForceUpdateCanvases();
                LayoutRebuilder.ForceRebuildLayoutImmediate(rootRect);
                if (data.groups != null)
                    foreach (var group in data.groups)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(root.transform.Find(group.id) as RectTransform);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out var success);
                if (!success) throw new IOException("Prefab save failed");
                return "Created " + prefabPath + "; elements=" + data.elements.Length + "; groups=" + groupCount + "; placeholder typography/art";
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void ValidateGroup(Group group, Viewport viewport, HashSet<string> elementIds, HashSet<string> groupIds,
            Dictionary<string, Element> byId, Dictionary<string, Group> owners)
        {
            var validTypes = new HashSet<string> { "horizontal", "vertical", "grid" };
            if (group == null || string.IsNullOrWhiteSpace(group.id) || !Regex.IsMatch(group.id, "^[a-zA-Z0-9_.-]+$") || group.id.Length > 100 || elementIds.Contains(group.id) || !groupIds.Add(group.id) ||
                !validTypes.Contains(group.type) || !IsAlignment(group.alignment) || group.children == null || group.children.Length < 1 || group.children.Length > 200)
                throw new ArgumentException("Invalid group id/type/alignment/children");
            if (!Finite(group.x) || !Finite(group.y) || !Finite(group.width) || !Finite(group.height) || !Finite(group.cellWidth) || !Finite(group.cellHeight) || !Finite(group.spacingX) || !Finite(group.spacingY) ||
                group.x < 0 || group.y < 0 || group.width <= 0 || group.height <= 0 || group.cellWidth <= 0 || group.cellHeight <= 0 || group.spacingX < 0 || group.spacingY < 0 ||
                group.paddingLeft < 0 || group.paddingRight < 0 || group.paddingTop < 0 || group.paddingBottom < 0 || group.x + group.width > viewport.width + 0.01f || group.y + group.height > viewport.height + 0.01f)
                throw new ArgumentException("Invalid group geometry: " + group.id);
            if (group.columns < 1 || group.columns > 200)
                throw new ArgumentException("Invalid group columns: " + group.id);

            var columns = GroupColumns(group);
            var rows = Mathf.CeilToInt(group.children.Length / (float)columns);
            var contentWidth = columns * group.cellWidth + (columns - 1) * group.spacingX;
            var contentHeight = rows * group.cellHeight + (rows - 1) * group.spacingY;
            var availableWidth = group.width - group.paddingLeft - group.paddingRight;
            var availableHeight = group.height - group.paddingTop - group.paddingBottom;
            if (availableWidth < 0 || availableHeight < 0 || contentWidth > availableWidth + 0.01f || contentHeight > availableHeight + 0.01f)
                throw new ArgumentException("Group content overflows bounds: " + group.id);
            var offsetX = AlignmentX(group.alignment) * (availableWidth - contentWidth);
            var offsetY = AlignmentY(group.alignment) * (availableHeight - contentHeight);
            var localIds = new HashSet<string>();
            string memberKind = null;
            for (var index = 0; index < group.children.Length; index++)
            {
                var childId = group.children[index];
                if (string.IsNullOrWhiteSpace(childId) || !localIds.Add(childId) || !byId.TryGetValue(childId, out var element) || owners.ContainsKey(childId))
                    throw new ArgumentException("Invalid or duplicate group child: " + group.id);
                if (memberKind == null) memberKind = element.kind;
                else if (memberKind != element.kind) throw new ArgumentException("Group children must have the same kind: " + group.id);
                owners.Add(childId, group);
                var column = index % columns;
                var row = index / columns;
                var expectedX = group.x + group.paddingLeft + offsetX + column * (group.cellWidth + group.spacingX);
                var expectedY = group.y + group.paddingTop + offsetY + row * (group.cellHeight + group.spacingY);
                if (!Near(element.x, expectedX) || !Near(element.y, expectedY) || !Near(element.width, group.cellWidth) || !Near(element.height, group.cellHeight))
                    throw new ArgumentException("Group geometry does not match member bounds: " + group.id + "/" + childId);
            }
        }

        private static void ValidateElementCycles(Element[] elements, Dictionary<string, Element> byId)
        {
            var state = new Dictionary<string, int>();
            foreach (var element in elements) VisitForCycle(element, byId, state);
        }

        private static void VisitForCycle(Element element, Dictionary<string, Element> byId, Dictionary<string, int> state)
        {
            if (state.TryGetValue(element.id, out var known))
            {
                if (known == 1) throw new ArgumentException("Element parentId cycle: " + element.id);
                return;
            }
            state[element.id] = 1;
            if (!string.IsNullOrEmpty(element.parentId)) VisitForCycle(byId[element.parentId], byId, state);
            state[element.id] = 2;
        }

        private static Dictionary<string, Element> ToElementMap(Element[] elements)
        {
            var result = new Dictionary<string, Element>();
            foreach (var element in elements) result.Add(element.id, element);
            return result;
        }

        private static Dictionary<string, Group> ToDirectGroupMap(Group[] groups)
        {
            var result = new Dictionary<string, Group>();
            if (groups != null) foreach (var group in groups) foreach (var child in group.children) result.Add(child, group);
            return result;
        }

        private static void CreateElementRecursive(Element element, Transform root, Dictionary<string, Element> byId, Dictionary<string, Group> groupMembers,
            Dictionary<string, RectTransform> created, Font font, float scale, Theme theme)
        {
            if (created.ContainsKey(element.id)) return;
            Transform parent = root;
            var localX = element.x;
            var localY = element.y;
            if (!string.IsNullOrEmpty(element.parentId))
            {
                var parentElement = byId[element.parentId];
                CreateElementRecursive(parentElement, root, byId, groupMembers, created, font, scale, theme);
                parent = created[element.parentId];
                localX -= parentElement.x;
                localY -= parentElement.y;
            }
            var rect = Rect(element.id, parent, localX, localY, element.width, element.height);
            created.Add(element.id, rect);
            if (!groupMembers.ContainsKey(element.id)) AddElementVisual(rect, element, font, scale, theme);
        }

        private static RectTransform CreateSlotTemplate(string firstChildId, Dictionary<string, Element> byId, Font font, float scale, Theme theme)
        {
            var element = byId[firstChildId];
            var slot = new GameObject("GroupItem", typeof(RectTransform), typeof(LayoutElement)).GetComponent<RectTransform>();
            slot.anchorMin = slot.anchorMax = slot.pivot = new Vector2(0, 1);
            slot.sizeDelta = new Vector2(element.width, element.height);
            var layoutElement = slot.GetComponent<LayoutElement>();
            layoutElement.minWidth = layoutElement.preferredWidth = element.width;
            layoutElement.minHeight = layoutElement.preferredHeight = element.height;
            layoutElement.flexibleWidth = layoutElement.flexibleHeight = 0;
            var visual = StretchRect("Visual", slot);
            if (element.kind != "label")
            {
                var image = visual.gameObject.AddComponent<Image>();
                image.color = element.kind == "button" ? ParseColor(theme.accent) : ParseColor("#1e303d");
                image.raycastTarget = element.kind == "button";
                if (element.kind == "button") visual.gameObject.AddComponent<Button>().targetGraphic = image;
            }
            var label = StretchRect("Label", visual).gameObject.AddComponent<Text>();
            ConfigureLabel(label, element, font, scale, theme);
            return slot;
        }

        private static void AddElementVisual(RectTransform rect, Element element, Font font, float scale, Theme theme)
        {
            if (element.kind != "label")
            {
                var image = rect.gameObject.AddComponent<Image>();
                image.color = element.kind == "button" ? ParseColor(theme.accent) : ParseColor("#1e303d");
                image.raycastTarget = element.kind == "button";
                if (element.kind == "button") rect.gameObject.AddComponent<Button>().targetGraphic = image;
            }
            var label = Rect("Label", rect, 0, 0, element.width, element.height).gameObject.AddComponent<Text>();
            ConfigureLabel(label, element, font, scale, theme);
        }

        private static void ConfigureLabel(Text label, Element element, Font font, float scale, Theme theme)
        {
            label.text = element.label;
            label.font = font;
            label.supportRichText = false;
            label.fontSize = Mathf.Max(10, Mathf.RoundToInt((element.kind == "label" ? 16 : 18) * scale));
            label.fontStyle = element.kind == "label" ? FontStyle.Normal : FontStyle.Bold;
            label.color = ParseColor(element.kind == "button" ? "#101820" : theme.text);
            label.alignment = TextAnchor.MiddleCenter;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
        }

        private static void ConfigureLayoutGroup(RectTransform rect, Group group)
        {
            if (group.type == "horizontal")
            {
                var layout = rect.gameObject.AddComponent<HorizontalLayoutGroup>();
                ConfigureLayout(layout, group);
                layout.spacing = group.spacingX;
            }
            else if (group.type == "vertical")
            {
                var layout = rect.gameObject.AddComponent<VerticalLayoutGroup>();
                ConfigureLayout(layout, group);
                layout.spacing = group.spacingY;
            }
            else
            {
                var layout = rect.gameObject.AddComponent<GridLayoutGroup>();
                layout.padding = Padding(group);
                layout.childAlignment = ParseAlignment(group.alignment);
                layout.cellSize = new Vector2(group.cellWidth, group.cellHeight);
                layout.spacing = new Vector2(group.spacingX, group.spacingY);
                layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                layout.constraintCount = GroupColumns(group);
                layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
                layout.startAxis = GridLayoutGroup.Axis.Horizontal;
            }
        }

        private static void ConfigureLayout(HorizontalOrVerticalLayoutGroup layout, Group group)
        {
            layout.padding = Padding(group);
            layout.childAlignment = ParseAlignment(group.alignment);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;
            layout.childScaleWidth = false;
            layout.childScaleHeight = false;
        }

        private static void MoveChildren(RectTransform source, Transform destination)
        {
            var children = new List<Transform>();
            for (var index = 0; index < source.childCount; index++) children.Add(source.GetChild(index));
            foreach (var child in children) child.SetParent(destination, false);
        }

        private static string[] GetGroupPrefabPaths(Layout data, string prefabPath)
        {
            if (data.groups == null) return new string[0];
            var directory = Path.GetDirectoryName(prefabPath).Replace('\\', '/');
            var stem = Path.GetFileNameWithoutExtension(prefabPath);
            var paths = new string[data.groups.Length];
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < data.groups.Length; index++)
            {
                var safeId = Regex.Replace(data.groups[index].id, "[^a-zA-Z0-9_-]", "_");
                if (string.IsNullOrEmpty(safeId)) safeId = "Group";
                paths[index] = directory + "/" + stem + "_" + safeId + "_Item.prefab";
                if (!unique.Add(paths[index])) throw new ArgumentException("Group ids produce colliding prefab paths");
            }
            return paths;
        }

        private static void EnsureOutputsDoNotExist(string prefabPath, string[] groupPaths)
        {
            var paths = new List<string>(groupPaths) { prefabPath };
            foreach (var path in paths)
                if (File.Exists(path) || AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path) != null)
                    throw new IOException("Refusing to overwrite an existing prefab: " + path);
        }

        private static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        private static RectTransform StretchRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static int GroupColumns(Group group) { return group.type == "horizontal" ? group.children.Length : group.type == "vertical" ? 1 : Mathf.Min(group.columns, group.children.Length); }
        private static bool Near(float actual, float expected) { return Mathf.Abs(actual - expected) <= GeometryTolerance; }
        private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
        private static bool IsAlignment(string value) { return value == "UpperLeft" || value == "UpperCenter" || value == "UpperRight" || value == "MiddleLeft" || value == "MiddleCenter" || value == "MiddleRight" || value == "LowerLeft" || value == "LowerCenter" || value == "LowerRight"; }
        private static TextAnchor ParseAlignment(string value) { return (TextAnchor)Enum.Parse(typeof(TextAnchor), value); }
        private static float AlignmentX(string value) { return value.EndsWith("Left", StringComparison.Ordinal) ? 0f : value.EndsWith("Center", StringComparison.Ordinal) ? 0.5f : 1f; }
        private static float AlignmentY(string value) { return value.StartsWith("Upper", StringComparison.Ordinal) ? 0f : value.StartsWith("Middle", StringComparison.Ordinal) ? 0.5f : 1f; }
        private static RectOffset Padding(Group group) { return new RectOffset(group.paddingLeft, group.paddingRight, group.paddingTop, group.paddingBottom); }
        private static Color ParseColor(string value)
        {
            if (value == null || !Regex.IsMatch(value, "^#[0-9a-fA-F]{6}$") || !ColorUtility.TryParseHtmlString(value, out var color))
                throw new ArgumentException("Expected #RRGGBB color");
            return color;
        }
    }
}
