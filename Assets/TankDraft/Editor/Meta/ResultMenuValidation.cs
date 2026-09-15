using System;
using System.IO;
using System.Linq;
using TMPro;
using TankDraft.Application;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace TankDraft.Editor.UI
{
    // Editor-only fixture. It renders authored UI against an immutable local server snapshot.
    public static class ResultMenuValidation
    {
        private const string OutputDirectory = "Logs/TankDraftSetup/R33";
        private static int checks;

        private sealed class ThrowingRepository : IProfileRepository
        {
            public ProfileSnapshot Load() => throw new InvalidOperationException("Result UI fixture must not read the repository.");
            public void Save(ProfileSnapshot profile) => throw new InvalidOperationException("Result UI fixture must not write the repository.");
        }

        private static void Check(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Result UI validation: " + message);
            checks++;
        }

        private static T Named<T>(GameObject root, string name) where T : Component
        {
            return root.GetComponentsInChildren<T>(true).Single(component => component.name == name);
        }

        private static RecentMatchResult Result(char identity, string matchId, int ownWins, int opponentWins, bool won, int amount, RewardDeliveryState state)
        {
            return new RecentMatchResult(new string(identity, 64), matchId, ownWins, opponentWins, won, false, "CO", amount, state);
        }

        private static ProfileSnapshot Fixture(MetaCatalogAsset catalog)
        {
            ProfileSnapshot seed = catalog.CreateInitialProfile();
            return new ProfileSnapshot(
                seed.PlayerName,
                seed.CommanderLevel,
                seed.ArenaLevel,
                seed.ArenaProgress,
                seed.Energy,
                seed.Gems,
                seed.Coins,
                seed.Mastery,
                seed.OwnedIds.ToArray(),
                seed.UnitIds.ToArray(),
                seed.OrderIds.ToArray(),
                new[]
                {
                    Result('a', "fixture-applied", 4, 1, true, 1, RewardDeliveryState.Applied),
                    Result('b', "fixture-pending", 1, 4, false, 1, RewardDeliveryState.Pending),
                    Result('c', "fixture-review", 2, 4, false, 1, RewardDeliveryState.NeedsReview)
                });
        }

        private static void Render(Camera camera, GameObject root, string path, ref RenderTexture renderTexture, ref Texture2D texture)
        {
            if (renderTexture == null) renderTexture = new RenderTexture(576, 1280, 24);
            camera.targetTexture = renderTexture;
            Canvas.ForceUpdateCanvases();
            foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true))
            {
                graphic.SetAllDirty();
                graphic.Rebuild(CanvasUpdate.PreRender);
                graphic.canvasRenderer.cull = false;
            }
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
            Canvas.ForceUpdateCanvases();
            camera.Render();
            RenderTexture.active = renderTexture;
            if (texture == null) texture = new Texture2D(576, 1280, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0, 0, 576, 1280), 0, 0);
            texture.Apply();
            File.WriteAllBytes(path, texture.EncodeToPNG());
        }

        private static void CheckNoOverflow(GameObject root)
        {
            Canvas.ForceUpdateCanvases();
            foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!text.gameObject.activeInHierarchy) continue;
                text.ForceMeshUpdate();
                Check(!text.isTextOverflowing, "TMP overflow " + text.name);
            }
        }

        public static string Run()
        {
            if (UnityEngine.Application.isPlaying || EditorApplication.isCompiling)
                return "SKIP ResultMenuValidation requires Edit Mode with compilation complete.";

            checks = 0;
            Scene activeScene = SceneManager.GetActiveScene();
            bool activeWasDirty = activeScene.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            RenderTexture renderTexture = null;
            Texture2D texture = null;
            RenderTexture previous = RenderTexture.active;
            MainMenuPresenter presenter = null;

            try
            {
                MetaCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<MetaCatalogAsset>("Assets/TankDraft/Configs/Meta/Catalogs/MetaCatalog.asset");
                GameObject authoredRoot = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/UI/UIRoot.prefab");
                Check(catalog != null, "Meta catalog missing");
                Check(authoredRoot != null, "UIRoot missing");

                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(authoredRoot, preview);
                UIMainMenuRuntimeRoot runtime = root.GetComponent<UIMainMenuRuntimeRoot>();
                Check(runtime != null, "runtime root missing");
                MetaDefinitions definitions = catalog.CreateDefinitions();
                var profiles = new ProfileService(definitions, catalog.CreateInitialProfile(), new ThrowingRepository());
                profiles.InitializeServer(Fixture(catalog));
                presenter = new MainMenuPresenter(profiles, definitions, catalog, runtime);
                presenter.Initialize();

                Button summaryButton = Named<Button>(root, "LastResult_Button");
                TMP_Text summary = summaryButton.GetComponentInChildren<TMP_Text>(true);
                Check(summary != null, "summary label missing");
                Check(summary.text.Contains("4:1"), "summary omits applied own score");
                Check(summary.text.Contains("Получено монет: 1"), "summary omits applied reward");
                Check(!summary.text.Contains("ожидается"), "applied summary uses pending wording");

                Canvas canvas = root.GetComponent<Canvas>();
                RectTransform rect = root.GetComponent<RectTransform>();
                Check(canvas != null && rect != null, "host Canvas missing");
                canvas.renderMode = RenderMode.WorldSpace;
                rect.sizeDelta = new Vector2(576, 1280);
                rect.pivot = Vector2.one * .5f;
                rect.position = Vector3.zero;
                rect.localScale = Vector3.one * .01f;
                GameObject cameraObject = new GameObject("ResultMenuValidationCamera", typeof(Camera));
                SceneManager.MoveGameObjectToScene(cameraObject, preview);
                Camera camera = cameraObject.GetComponent<Camera>();
                camera.cameraType = CameraType.Game;
                camera.scene = preview;
                camera.transform.position = new Vector3(0, 0, -10);
                camera.orthographic = true;
                camera.orthographicSize = 6.4f;
                camera.aspect = 576f / 1280f;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = Color.black;
                canvas.worldCamera = camera;

                Directory.CreateDirectory(OutputDirectory);
                Render(camera, root, Path.Combine(OutputDirectory, "result-menu.png"), ref renderTexture, ref texture);
                CheckNoOverflow(root);

                // Edit-mode preview does not run the MonoBehaviour OnEnable subscription.
                summaryButton.GetComponent<Vareiko.Foundation.UI.UIButtonView>().Click();
                TMP_Text profileBody = runtime.Message.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Body_Text");
                Check(profileBody.text.Contains("4:1") && profileBody.text.Contains("Получено монет: 1"), "profile omits Applied result");
                Check(profileBody.text.Contains("1:4") && profileBody.text.Contains("Награда ожидается: 1"), "profile omits Pending result");
                Check(profileBody.text.Contains("2:4") && profileBody.text.Contains("Награда на проверке"), "profile omits NeedsReview result");
                Check(!profileBody.text.Contains("Лимит тестовых наград"), "profile unexpectedly shows skipped reward");
                Render(camera, root, Path.Combine(OutputDirectory, "result-profile.png"), ref renderTexture, ref texture);
                CheckNoOverflow(root);

                string status = "PASS " + checks + " checks; fixture UI structural/visual only, not live/network.";
                File.WriteAllText(Path.Combine(OutputDirectory, "status.txt"), status);
                return status + " Images: " + OutputDirectory + "/result-menu.png, result-profile.png.";
            }
            finally
            {
                if (presenter != null) presenter.Dispose();
                RenderTexture.active = previous;
                if (texture) UnityEngine.Object.DestroyImmediate(texture);
                if (renderTexture)
                {
                    renderTexture.Release();
                    UnityEngine.Object.DestroyImmediate(renderTexture);
                }
                EditorSceneManager.ClosePreviewScene(preview);
                if (SceneManager.GetActiveScene() != activeScene || activeScene.isDirty != activeWasDirty)
                    throw new InvalidOperationException("Result UI validation changed the active scene or dirty state.");
            }
        }
    }
}
