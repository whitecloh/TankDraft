using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
using TMPro;
using Vareiko.Foundation.UI;
using TankDraft.UI;

namespace TankDraft.Editor.UI
{
    // Editor-only fixture: illustrative values never enter the authored prefab or runtime services.
    public static class TypedMainMenuValidation
    {
        sealed class Commands : IMainMenuUiCommands
        {
            public int Battle, Profile, Pass, Settings;
            public readonly int[] Rewards = new int[4], Tabs = new int[5];
            public void StartBattle() { Battle++; }
            public void OpenProfile() { Profile++; }
            public void OpenPass() { Pass++; }
            public void OpenSettings() { Settings++; }
            public void OpenReward(int i) { Rewards[i]++; }
            public void SelectTab(int i) { Tabs[i]++; }
        }
        static int checks;
        static void Check(bool condition, string message)
        { if (!condition) throw new Exception("UI validation: " + message); checks++; }
        static T Named<T>(GameObject root, string name) where T : Component
        { return root.GetComponentsInChildren<T>(true).Single(x => x.name == name); }
        static MainMenuViewModel Model()
        {
            return new MainMenuViewModel(
                new[] { new CurrencyCounterViewModel("5/5", null), new CurrencyCounterViewModel("100", null), new CurrencyCounterViewModel("1 250", null), new CurrencyCounterViewModel("10", null) },
                new[] { new RewardSlotViewModel("Пусто", null, false), new RewardSlotViewModel("2ч", null, true), new RewardSlotViewModel("Готово", null, true), new RewardSlotViewModel("Пусто", null, false) },
                "Командир", "Сезон · 3/10", "Учебный полигон", "120 / 200", "В БОЙ",
                new[] { "Магазин", "Армия", "Бой", "События", "Рейтинг" });
        }
        public static string Run()
        {
            checks = 0;
            var active = SceneManager.GetActiveScene(); var wasDirty = active.isDirty;
            var scene = EditorSceneManager.NewPreviewScene();
            RenderTexture rt = null; Texture2D texture = null; var previous = RenderTexture.active;
            try
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/UI/UIRoot.prefab");
                Check(asset != null, "UIRoot missing");
                var root = (GameObject)PrefabUtility.InstantiatePrefab(asset, scene);
                var screen = root.GetComponentInChildren<UIMainMenuScreen>(true);
                Check(root.GetComponentsInChildren<Canvas>(true).Length == 1 && root.GetComponent<Canvas>() != null, "one host Canvas");
                Check(screen.GetComponentsInChildren<Canvas>(true).Length == 0, "screen has a Canvas");
                Check(root.GetComponentsInChildren<UnityEngine.UI.Text>(true).Length == 0, "legacy Text found");
                Check(root.GetComponentsInChildren<ContentSizeFitter>(true).Length == 0, "unexpected fitter");
                Check(root.GetComponentsInChildren<Transform>(true).All(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject) == 0), "missing scripts");
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true)) Check(text.font != null && !text.raycastTarget, "TMP font / raycast");
                foreach (var image in root.GetComponentsInChildren<Image>(true))
                    Check(image.GetComponentInParent<Button>(true) != null || image.GetComponent<UIWindow>() != null || image.GetComponent<ScrollRect>() != null || !image.raycastTarget, "decorative raycast " + image.name);
                foreach (var element in root.GetComponentsInChildren<UIElement>(true))
                    if (element is UIItemView || element is UIButtonView) Check(string.IsNullOrEmpty(element.Id), "shared item ID");
                var registry = root.GetComponent<UIRegistry>(); registry.BuildMap();
                Check(registry.Count == 12 && registry.TryGetScreen("main_menu", out var registered) && registered == screen, "registry");
                foreach (var id in new[] {"main_menu.hud", "main_menu.arena", "main_menu.hud.currencies", "main_menu.hud.navigation", "main_menu.arena.rewards"})
                    Check(registry.TryGetElement(id, out _), "registry ID " + id);
                foreach (var component in root.GetComponentsInChildren<UIElement>(true))
                    if (component is UIScreen || component is UIWindow || component is UIPanel || component is UIItemView)
                        Check(PrefabUtility.IsAnyPrefabInstanceRoot(component.gameObject), "standalone source " + component.name);
                var runtime=root.GetComponent<UIMainMenuRuntimeRoot>();runtime.Collection.Hide();runtime.Details.Hide();runtime.Message.Hide();
                var before = root.GetComponentsInChildren<Transform>(true).Length;
                var commands = new Commands(); screen.Initialize(commands); screen.Initialize(commands);
                screen.Hide(); screen.Bind(Model()); screen.Bind(Model());
                Check(screen.IsVisible && screen.GetComponent<CanvasGroup>().alpha == 1 && screen.GetComponent<CanvasGroup>().blocksRaycasts, "show after bind");
                Check(Named<TMP_Text>(root, "Progress_Text").text == "120 / 200", "single progress label");
                Check(Named<TMP_Text>(root, "PlayerName_Text").text == "Командир", "profile binding");
                Check(Named<UIButtonView>(root, "StartBattle_Button").GetComponentInChildren<TMP_Text>().text == "В БОЙ", "localized battle label");
                foreach (var icon in root.GetComponentsInChildren<Image>(true).Where(i => i.name == "Icon_Image")) Check(!icon.enabled, "null icon hidden");
                foreach (var group in root.GetComponentsInChildren<LayoutGroup>(true))
                {
                    if(!group.gameObject.activeInHierarchy)continue;
                    Check(!group.enabled, "idle layout is active");
                    if (group.name == "Navigation_Container") continue;
                    var slots = group.GetComponentsInChildren<LayoutElement>(true);
                    Check(slots.Length == 4, "four authored slots");
                    for (int i = 1; i < slots.Length; i++)
                    {
                        var a = (RectTransform)slots[i-1].transform; var b = (RectTransform)slots[i].transform;
                        Check(Mathf.Abs(a.rect.width-b.rect.width)<.01f && Mathf.Abs(b.anchoredPosition.x-a.anchoredPosition.x-a.rect.width-10)<.01f, "equal width and 10px gap");
                    }
                }
                foreach (var button in root.GetComponentsInChildren<UIButtonView>(true))
                { Check(button.Button != null && button.Button.onClick.GetPersistentEventCount() == 0 && button.OnClicked.GetPersistentEventCount() == 0, "button ownership"); button.Click(); }
                Check(commands.Battle == 1 && commands.Profile == 1 && commands.Pass == 1 && commands.Settings == 1, "duplicate command subscription");
                Check(commands.Rewards.SequenceEqual(new[] {0,1,1,0}) && commands.Tabs.All(n => n == 1), "slot command indices and gating");
                Check(before == root.GetComponentsInChildren<Transform>(true).Length, "Bind grew hierarchy");

                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                var rect = root.GetComponent<RectTransform>(); rect.sizeDelta = new Vector2(576,1280); rect.pivot = Vector2.one*.5f; rect.position = Vector3.zero; rect.localScale = Vector3.one*.01f;
                var cameraObject = new GameObject("UIValidationCamera", typeof(Camera)); SceneManager.MoveGameObjectToScene(cameraObject,scene);
                var camera = cameraObject.GetComponent<Camera>(); camera.cameraType=CameraType.Game; camera.scene=scene; camera.transform.position=new Vector3(0,0,-10); camera.orthographic=true; camera.orthographicSize=6.4f; camera.aspect=576f/1280; camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=Color.black; canvas.worldCamera=camera;
                screen.Bind(Model());
                rt=new RenderTexture(576,1280,24); camera.targetTexture=rt; Canvas.ForceUpdateCanvases();
                foreach(var graphic in root.GetComponentsInChildren<Graphic>()) {graphic.SetAllDirty();graphic.Rebuild(CanvasUpdate.PreRender);graphic.canvasRenderer.cull=false;}
                Canvas.ForceUpdateCanvases();camera.Render(); RenderTexture.active=rt;
                texture=new Texture2D(576,1280,TextureFormat.RGB24,false);texture.ReadPixels(new Rect(0,0,576,1280),0,0);texture.Apply();
                File.WriteAllBytes("Logs/TankDraftSetup/UIMainMenuScreen_Typed.png",texture.EncodeToPNG());
                screen.Release(); foreach(var button in root.GetComponentsInChildren<UIButtonView>(true)) button.Click();
                Check(commands.Battle==1 && commands.Profile==1 && commands.Pass==1 && commands.Settings==1 && commands.Tabs.All(n=>n==1) && commands.Rewards.SequenceEqual(new[]{0,1,1,0}), "release leaves subscriptions");
                return "PASS " + checks + " editor prefab/binding assertions; image Logs/TankDraftSetup/UIMainMenuScreen_Typed.png. No Play Mode/device/gameplay claim.";
            }
            finally
            {
                RenderTexture.active=previous;if(texture)UnityEngine.Object.DestroyImmediate(texture);EditorSceneManager.ClosePreviewScene(scene);if(rt){rt.Release();UnityEngine.Object.DestroyImmediate(rt);}
                Check(SceneManager.GetActiveScene()==active && active.isDirty==wasDirty,"active scene changed");
            }
        }
    }
}
