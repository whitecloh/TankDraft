using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using TMPro;

namespace SLVR.UIMotion.Editor
{
    public static class UiMotionSampleGenerator
    {
        private const string TempRoot = "Assets/SLVR_UI_Motion_Generated";
        private const string SampleRoot = "Packages/com.slvr.ui-motion-kit/Samples~/UI Motion Gallery";
        private static readonly Color Yellow = new Color(1f, 0.72f, 0.08f, 1f);
        private static readonly Color Dark = new Color(0.055f, 0.07f, 0.11f, 1f);

        [MenuItem("Tools/SLVR/UI Motion/Generate Package Samples")]
        public static void GeneratePackageSamples()
        {
            if (!EditorUtility.DisplayDialog("Generate UI Motion samples?", "Rebuild the package sample prefabs and gallery scene using Unity Editor APIs? Production scenes are not touched.", "Generate", "Cancel")) return;
            Generate(false);
        }

        public static void GenerateForAutomation() => Generate(true);

        private static void Generate(bool automation)
        {
            if (AssetDatabase.IsValidFolder(TempRoot)) AssetDatabase.DeleteAsset(TempRoot);
            Directory.CreateDirectory(TempRoot + "/Prefabs");
            AssetDatabase.Refresh();

            var paths = new Dictionary<string, string>();
            paths["AnimatedButton_Basic"] = Save("AnimatedButton_Basic", CreateButton("BASIC", new Color(0.18f, 0.24f, 0.34f, 1f)));
            paths["AnimatedButton_CTA"] = Save("AnimatedButton_CTA", CreateButton("PLAY", Yellow));
            paths["AnimatedIconButton"] = Save("AnimatedIconButton", CreateButton("★", new Color(0.2f, 0.45f, 0.8f, 1f), new Vector2(64f, 64f)));
            paths["NavigationItem"] = Save("NavigationItem", CreateNavigation(false));
            paths["TabButton"] = Save("TabButton", CreateNavigation(true));
            paths["NotificationBadge"] = Save("NotificationBadge", CreateBadge());
            paths["ModalRoot"] = Save("ModalRoot", CreatePanel("ModalRoot", true));
            paths["Tooltip"] = Save("Tooltip", CreateTooltip());
            paths["ProgressBar_Juicy"] = Save("ProgressBar_Juicy", CreateProgress());
            paths["RewardPopup_Generic"] = Save("RewardPopup_Generic", CreateReward());
            paths["CurrencyFlyEmitter"] = Save("CurrencyFlyEmitter", CreateCurrency());
            paths["CardSelectionFrame"] = Save("CardSelectionFrame", CreateCard());
            paths["ShimmerOverlay"] = Save("ShimmerOverlay", CreateShimmer());
            paths["FocusRing"] = Save("FocusRing", CreateFocusRing());
            paths["ScreenDimmer"] = Save("ScreenDimmer", CreateDimmer());

            string scenePath = CreateGallery(paths);
            CopyGeneratedAssets(scenePath);
            AssetDatabase.DeleteAsset(TempRoot);
            AssetDatabase.Refresh();
            Debug.Log($"SLVR UI Motion: generated 15 prefabs and gallery scene at {SampleRoot}.");
            if (!automation) EditorUtility.DisplayDialog("SLVR UI Motion", "Package samples generated. Import “UI Motion Gallery” from Package Manager to use them.", "OK");
        }

        private static GameObject CreateButton(string label, Color color, Vector2? size = null)
        {
            GameObject root = CreateGraphicRoot("Button", color, size ?? new Vector2(240f, 72f), true);
            root.AddComponent<Button>();
            root.AddComponent<UiAnimatedButton>();
            CreateText(root.transform, "Label", label, 24, Color.white);
            return root;
        }

        private static GameObject CreateNavigation(bool tab)
        {
            GameObject root = CreateGraphicRoot(tab ? "TabButton" : "NavigationItem", new Color(0.12f, 0.16f, 0.24f, 1f), new Vector2(160f, 82f), true);
            root.AddComponent<Button>();
            if (tab) root.AddComponent<UiTabMotion>(); else root.AddComponent<UiNavigationItem>();
            Image plate = CreateImage(root.transform, "SelectionPlate", Yellow, new Vector2(140f, 8f), false);
            plate.rectTransform.anchoredPosition = new Vector2(0f, -32f);
            CreateText(root.transform, "Label", tab ? "TAB" : "HOME", 18, Color.white);
            return root;
        }

        private static GameObject CreateBadge()
        {
            GameObject root = CreateGraphicRoot("NotificationBadge", new Color(0.9f, 0.08f, 0.12f, 1f), new Vector2(48f, 48f), false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            Text label = CreateLegacyText(root.transform, "Count", "3", 20, Color.white);
            UiNotificationBadge badge = root.AddComponent<UiNotificationBadge>();
            badge.Configure((RectTransform)root.transform, root, label, group, label.gameObject);
            badge.SetCount(3, true);
            return root;
        }

        private static GameObject CreatePanel(string name, bool backdrop)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(CanvasGroup), typeof(UiPanelTransition));
            ((RectTransform)root.transform).sizeDelta = new Vector2(560f, 360f);
            if (backdrop)
            {
                Image dim = CreateImage(root.transform, "BackgroundDimmer", new Color(0f, 0f, 0f, 0.72f), new Vector2(900f, 600f), false);
                dim.transform.SetAsFirstSibling();
                dim.gameObject.AddComponent<CanvasGroup>();
                dim.gameObject.AddComponent<UiModalBackdrop>();
            }
            Image panel = CreateImage(root.transform, "Panel", new Color(0.11f, 0.14f, 0.21f, 1f), new Vector2(520f, 320f), false);
            CreateText(panel.transform, "Title", "SAMPLE MODAL", 30, Color.white);
            return root;
        }

        private static GameObject CreateTooltip()
        {
            GameObject root = CreateGraphicRoot("Tooltip", new Color(0.08f, 0.1f, 0.15f, 0.96f), new Vector2(320f, 96f), false);
            CanvasGroup group = root.AddComponent<CanvasGroup>();
            UiTooltipMotion tooltip = root.AddComponent<UiTooltipMotion>();
            tooltip.Configure(group, (RectTransform)root.transform, (RectTransform)root.transform);
            CreateText(root.transform, "Label", "Helpful contextual hint", 18, Color.white);
            return root;
        }

        private static GameObject CreateProgress()
        {
            GameObject root = CreateGraphicRoot("ProgressBar_Juicy", new Color(0.08f, 0.1f, 0.15f, 1f), new Vector2(420f, 42f), false);
            Image ghost = CreateFill(root.transform, "GhostFill", new Color(1f, 0.45f, 0.1f, 0.65f));
            Image main = CreateFill(root.transform, "MainFill", Yellow);
            CanvasGroup sweep = CreateImage(root.transform, "Shine", new Color(1f, 1f, 1f, 0.5f), new Vector2(32f, 42f), false).gameObject.AddComponent<CanvasGroup>();
            UiProgressMotion motion = root.AddComponent<UiProgressMotion>();
            motion.Configure(main, ghost, sweep);
            motion.SetValue(0.68f, true);
            return root;
        }

        private static GameObject CreateReward()
        {
            GameObject root = CreatePanel("RewardPopup_Generic", true);
            Image card = CreateImage(root.transform, "RewardCard", new Color(0.36f, 0.18f, 0.55f, 1f), new Vector2(180f, 210f), false);
            CanvasGroup cardGroup = card.gameObject.AddComponent<CanvasGroup>();
            CreateText(card.transform, "RewardLabel", "RARE\nREWARD", 24, Color.white);
            Text amount = CreateLegacyText(root.transform, "Amount", "0", 28, Yellow);
            UiNumberTicker ticker = amount.gameObject.AddComponent<UiNumberTicker>();
            ticker.Configure(amount, amount.rectTransform, amount);
            UiRewardSequence sequence = root.AddComponent<UiRewardSequence>();
            sequence.Configure(new[]
            {
                new UiRewardSequenceStep(UiRewardStepKind.RewardReveal, cardGroup, card.rectTransform, 0.28f),
                new UiRewardSequenceStep(UiRewardStepKind.Ticker, null, null, 0.18f),
                new UiRewardSequenceStep(UiRewardStepKind.Cta, root.GetComponent<CanvasGroup>(), (RectTransform)root.transform, 0.18f),
            }, ticker);
            sequence.ConfigureValues(250d, 0);
            return root;
        }

        private static GameObject CreateCurrency()
        {
            GameObject root = new GameObject("CurrencyFlyEmitter", typeof(RectTransform));
            RectTransform source = CreateImage(root.transform, "Source", Yellow, new Vector2(36f, 36f), false).rectTransform;
            RectTransform target = CreateImage(root.transform, "Target", Color.cyan, new Vector2(48f, 48f), false).rectTransform;
            target.anchoredPosition = new Vector2(260f, 120f);
            Image iconImage = CreateImage(root.transform, "IconPrefab", Yellow, new Vector2(28f, 28f), false);
            UiCurrencyFlyIcon icon = iconImage.gameObject.AddComponent<UiCurrencyFlyIcon>();
            icon.gameObject.SetActive(false);
            UiCurrencyFlyEffect effect = root.AddComponent<UiCurrencyFlyEffect>();
            effect.Configure(icon, root.transform, source, target);
            return root;
        }

        private static GameObject CreateCard()
        {
            GameObject root = CreateGraphicRoot("CardSelectionFrame", new Color(0.13f, 0.17f, 0.25f, 1f), new Vector2(200f, 250f), true);
            root.AddComponent<Button>();
            root.AddComponent<UiCardSelectionMotion>();
            Image frame = CreateImage(root.transform, "SelectionGlowFrame", Yellow, new Vector2(208f, 258f), false);
            frame.transform.SetAsFirstSibling();
            CreateText(root.transform, "Label", "CARD", 24, Color.white);
            return root;
        }

        private static GameObject CreateShimmer()
        {
            GameObject root = CreateGraphicRoot("ShimmerOverlay", new Color(1f, 1f, 1f, 0.35f), new Vector2(280f, 96f), false);
            root.GetComponent<Image>().raycastTarget = false;
            root.AddComponent<UiShaderEffectGraphic>();
            return root;
        }

        private static GameObject CreateFocusRing()
        {
            GameObject root = CreateGraphicRoot("FocusRing", new Color(1f, 0.75f, 0.1f, 0.3f), new Vector2(252f, 84f), false);
            root.GetComponent<Image>().raycastTarget = false;
            root.AddComponent<CanvasGroup>().blocksRaycasts = false;
            return root;
        }

        private static GameObject CreateDimmer()
        {
            GameObject root = CreateGraphicRoot("ScreenDimmer", new Color(0f, 0f, 0f, 0.7f), new Vector2(900f, 600f), false);
            root.GetComponent<Image>().raycastTarget = false;
            root.AddComponent<CanvasGroup>();
            root.AddComponent<UiModalBackdrop>();
            return root;
        }

        private static string CreateGallery(Dictionary<string, string> prefabs)
        {
            Scene previousActive = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            SceneManager.SetActiveScene(scene);
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            GameObject canvasObject = new GameObject("UI Motion Gallery", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(UiMotionGalleryController));
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            Image background = CreateImage(canvasObject.transform, "Background", Dark, new Vector2(1920f, 1080f), false);
            background.rectTransform.anchorMin = Vector2.zero; background.rectTransform.anchorMax = Vector2.one; background.rectTransform.sizeDelta = Vector2.zero;

            GameObject content = new GameObject("Sections", typeof(RectTransform), typeof(VerticalLayoutGroup));
            content.transform.SetParent(canvasObject.transform, false);
            RectTransform contentRect = (RectTransform)content.transform;
            contentRect.anchorMin = new Vector2(0.04f, 0.06f); contentRect.anchorMax = new Vector2(0.68f, 0.94f); contentRect.offsetMin = contentRect.offsetMax = Vector2.zero;
            VerticalLayoutGroup layout = content.GetComponent<VerticalLayoutGroup>(); layout.spacing = 8f; layout.childForceExpandHeight = false;
            string[] sections = { "Buttons", "Tabs & Navigation", "Modals & Tooltips", "Cards", "Progress & Counters", "Badges", "Idle & Attention", "Currency", "Rewards", "Quality", "Reduced Motion", "Debug Stats" };
            foreach (string section in sections) CreateText(content.transform, section.Replace(" & ", "_"), section, 22, section == "Rewards" ? Yellow : Color.white).gameObject.AddComponent<LayoutElement>().preferredHeight = 32f;

            RectTransform controls = CreateImage(canvasObject.transform, "RuntimeControls", new Color(0.08f, 0.1f, 0.15f, 0.96f), new Vector2(500f, 860f), false).rectTransform;
            controls.anchorMin = controls.anchorMax = new Vector2(0.84f, 0.5f);
            Button themeButton = CreateButton("NEXT THEME", Yellow).GetComponent<Button>(); themeButton.transform.SetParent(controls, false); ((RectTransform)themeButton.transform).anchoredPosition = new Vector2(0f, 320f);
            Button qualityButton = CreateButton("NEXT QUALITY", new Color(0.2f, 0.45f, 0.8f, 1f)).GetComponent<Button>(); qualityButton.transform.SetParent(controls, false); ((RectTransform)qualityButton.transform).anchoredPosition = new Vector2(0f, 230f);
            Slider intensitySlider = CreateSlider(controls); intensitySlider.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 130f);
            Toggle reducedToggle = CreateToggle(controls); reducedToggle.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, 50f);
            Text benchmark = CreateLegacyText(controls, "Benchmark", "Benchmark initializes in Play Mode", 18, Color.white); benchmark.rectTransform.sizeDelta = new Vector2(440f, 220f); benchmark.rectTransform.anchoredPosition = new Vector2(0f, -150f);
            UiMotionGalleryController controller = canvasObject.GetComponent<UiMotionGalleryController>();
            controller.Configure(LoadThemes(), LoadQualities(), benchmark);
            UnityEventTools.AddPersistentListener(themeButton.onClick, controller.NextTheme);
            UnityEventTools.AddPersistentListener(qualityButton.onClick, controller.NextQuality);
            UnityEventTools.AddPersistentListener(intensitySlider.onValueChanged, controller.SetIntensity);
            UnityEventTools.AddPersistentListener(reducedToggle.onValueChanged, controller.SetReducedMotion);

            string path = TempRoot + "/UI_Motion_Gallery.unity";
            EditorSceneManager.SaveScene(scene, path);
            if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
            EditorSceneManager.CloseScene(scene, true);
            return path;
        }

        private static Slider CreateSlider(Transform parent)
        {
            GameObject root = CreateGraphicRoot("Intensity", new Color(0.15f, 0.18f, 0.25f, 1f), new Vector2(400f, 34f), true);
            root.transform.SetParent(parent, false);
            Slider slider = root.AddComponent<Slider>(); slider.minValue = 0f; slider.maxValue = 1f; slider.value = 1f;
            Image fill = CreateImage(root.transform, "Fill", Yellow, new Vector2(390f, 22f), false); slider.fillRect = fill.rectTransform; slider.targetGraphic = root.GetComponent<Image>();
            return slider;
        }

        private static Toggle CreateToggle(Transform parent)
        {
            GameObject root = CreateGraphicRoot("ReducedMotion", new Color(0.15f, 0.18f, 0.25f, 1f), new Vector2(260f, 48f), true); root.transform.SetParent(parent, false);
            Toggle toggle = root.AddComponent<Toggle>(); toggle.targetGraphic = root.GetComponent<Image>();
            CreateText(root.transform, "Label", "REDUCED MOTION", 18, Color.white);
            return toggle;
        }

        private static UiMotionTheme[] LoadThemes() => new[]
        {
            AssetDatabase.LoadAssetAtPath<UiMotionTheme>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Themes/Mobile_Balanced.asset"),
            AssetDatabase.LoadAssetAtPath<UiMotionTheme>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Themes/HMD_Subtle.asset"),
            AssetDatabase.LoadAssetAtPath<UiMotionTheme>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Themes/Archero_Juicy.asset"),
            AssetDatabase.LoadAssetAtPath<UiMotionTheme>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Themes/Reduced_Motion.asset"),
        };

        private static UiMotionQualityProfile[] LoadQualities() => new[]
        {
            AssetDatabase.LoadAssetAtPath<UiMotionQualityProfile>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Quality/Quality_Low.asset"),
            AssetDatabase.LoadAssetAtPath<UiMotionQualityProfile>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Quality/Quality_Medium.asset"),
            AssetDatabase.LoadAssetAtPath<UiMotionQualityProfile>("Packages/com.slvr.ui-motion-kit/Runtime/Presets/Quality/Quality_High.asset"),
        };

        private static string Save(string name, GameObject root)
        {
            string path = $"{TempRoot}/Prefabs/{name}.prefab";
            PrefabUtility.SaveAsPrefabAsset(root, path);
            UnityEngine.Object.DestroyImmediate(root);
            return path;
        }

        private static void CopyGeneratedAssets(string scenePath)
        {
            string physicalSample = Path.GetFullPath(SampleRoot);
            string physicalPrefabs = Path.Combine(physicalSample, "Prefabs");
            Directory.CreateDirectory(physicalPrefabs);
            foreach (string path in AssetDatabase.FindAssets("t:Prefab", new[] { TempRoot + "/Prefabs" }))
            {
                string source = AssetDatabase.GUIDToAssetPath(path);
                CopyWithMeta(source, Path.Combine(physicalPrefabs, Path.GetFileName(source)));
            }
            CopyWithMeta(scenePath, Path.Combine(physicalSample, "UI_Motion_Gallery.unity"));
        }

        private static void CopyWithMeta(string sourceAssetPath, string destination)
        {
            string source = Path.GetFullPath(sourceAssetPath);
            File.Copy(source, destination, true);
            if (File.Exists(source + ".meta")) File.Copy(source + ".meta", destination + ".meta", true);
        }

        private static GameObject CreateGraphicRoot(string name, Color color, Vector2 size, bool raycast)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = (RectTransform)go.transform; rect.sizeDelta = size;
            Image image = go.GetComponent<Image>(); image.color = color; image.raycastTarget = raycast;
            return go;
        }

        private static Image CreateImage(Transform parent, string name, Color color, Vector2 size, bool raycast)
        {
            GameObject go = CreateGraphicRoot(name, color, size, raycast); go.transform.SetParent(parent, false); return go.GetComponent<Image>();
        }

        private static Image CreateFill(Transform parent, string name, Color color)
        {
            Image image = CreateImage(parent, name, color, new Vector2(400f, 34f), false); image.type = Image.Type.Filled; image.fillMethod = Image.FillMethod.Horizontal; image.fillAmount = 0.68f; return image;
        }

        private static TextMeshProUGUI CreateText(Transform parent, string name, string value, int size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI)); go.transform.SetParent(parent, false);
            TextMeshProUGUI text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size; text.color = color; text.alignment = TextAlignmentOptions.Center; text.raycastTarget = false;
            text.font = null;
            RectTransform rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            return text;
        }

        private static Text CreateLegacyText(Transform parent, string name, string value, int size, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text)); go.transform.SetParent(parent, false);
            Text text = go.GetComponent<Text>(); text.text = value; text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); text.fontSize = size; text.color = color; text.alignment = TextAnchor.MiddleCenter; text.raycastTarget = false;
            RectTransform rect = text.rectTransform; rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.sizeDelta = Vector2.zero;
            return text;
        }
    }
}
