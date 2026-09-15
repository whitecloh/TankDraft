using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TankDraft.Application;
using TankDraft.Content;
using TankDraft.Contracts;
using TankDraft.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Vareiko.Foundation.UI;

namespace TankDraft.Editor.UI
{
    // Editor-only fixture. It never contacts Fusion or PlayFab and uses a deterministic
    // in-memory authority to validate binding, idempotency retry, and reward pages.
    public static class ProgressionMenuValidation
    {
        private const string OutputDirectory = "Logs/TankDraftSetup/R33";
        private static int checks;

        private sealed class Repository : IProfileRepository
        {
            public ProfileSnapshot Load() => null;
            public void Save(ProfileSnapshot profile) { }
        }

        private sealed class FakeProgression : IServerProgressionClient
        {
            public readonly List<Guid> Operations = new List<Guid>();
            public bool ThrowOnce;
            public bool RejectOnce;
            private long sequence;
            private readonly string rulesVersion;

            public FakeProgression(string version) { rulesVersion = version; }
            public Task<ProgressionSnapshot> GetAsync(CancellationToken token) => Task.FromResult(Snapshot(null));
            public Task<ProgressionSnapshot> ExecuteAsync(string kind, string targetId, Guid operationId, long expectedSequence, CancellationToken token)
            {
                Operations.Add(operationId);
                if (ThrowOnce) { ThrowOnce = false; return Task.FromException<ProgressionSnapshot>(new IOException("fixture_unconfirmed")); }
                if (RejectOnce) { RejectOnce = false; return Task.FromException<ProgressionSnapshot>(new ProgressionRejectedException("insufficient_funds")); }
                sequence++;
                return Task.FromResult(Snapshot("Completed"));
            }

            private ProgressionSnapshot Snapshot(string status)
            {
                string[] resolved = status == "Completed" && Operations.Count > 0 ? new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer" } : null;
                return new ProgressionSnapshot(sequence, new[]
                {
                    new UnitProgressSnapshot("unit.mines", 1, 111, 10), new UnitProgressSnapshot("unit.heavy_tank", 1, 5, 0),
                    new UnitProgressSnapshot("unit.tank_destroyer", 1, 11, 0)
                }, 3, status, Array.Empty<string>(), rulesVersion, resolved);
            }
        }

        private static void Check(bool value, string text)
        {
            if (!value) throw new InvalidOperationException("Progression UI validation: " + text);
            checks++;
        }

        private static UIButtonView Action(UIProgressionWindow window) => window.GetComponentsInChildren<UIButtonView>(true).Single(button => button.name == "Action_1_Button");
        private static TMP_Text Title(UIProgressionWindow window) => window.GetComponentsInChildren<TMP_Text>(true).Single(text => text.name == "Title_Text");

        private static RecentMatchResult RewardResult()
        {
            return new RecentMatchResult(new string('d', 64), "progression-fixture", 4, 2, true, false, "CO", 570, RewardDeliveryState.Applied,
                new[] { "unit.mines", "unit.heavy_tank", "unit.tank_destroyer", "unit.field_artillery" }, 10, true);
        }

        private static void Capture(GameObject root, Scene scene, string output)
        {
            Canvas canvas = root.GetComponent<Canvas>();
            RectTransform rect = root.GetComponent<RectTransform>();
            canvas.renderMode = RenderMode.WorldSpace;
            rect.sizeDelta = new Vector2(576, 1280);
            rect.pivot = Vector2.one * .5f;
            rect.position = Vector3.zero;
            rect.localScale = Vector3.one * .01f;
            GameObject cameraObject = new GameObject("ProgressionValidationCamera", typeof(Camera));
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.GetComponent<Camera>();
            camera.scene = scene; camera.transform.position = new Vector3(0, 0, -10); camera.orthographic = true;
            camera.orthographicSize = 6.4f; camera.aspect = 576f / 1280f; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.black;
            canvas.worldCamera = camera;
            var renderTexture = new RenderTexture(576, 1280, 24);
            var texture = new Texture2D(576, 1280, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                camera.targetTexture = renderTexture;
                Canvas.ForceUpdateCanvases();
                foreach (Graphic graphic in root.GetComponentsInChildren<Graphic>(true)) { graphic.SetAllDirty(); graphic.Rebuild(CanvasUpdate.PreRender); graphic.canvasRenderer.cull = false; }
                foreach (TMP_Text text in root.GetComponentsInChildren<TMP_Text>(true)) text.ForceMeshUpdate();
                camera.Render(); RenderTexture.active = renderTexture;
                texture.ReadPixels(new Rect(0, 0, 576, 1280), 0, 0); texture.Apply();
                File.WriteAllBytes(output, texture.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(texture);
                renderTexture.Release();
                UnityEngine.Object.DestroyImmediate(renderTexture);
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }

        private static void CheckVisibleTextFits(GameObject root)
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
            if (UnityEngine.Application.isPlaying || EditorApplication.isCompiling) return "SKIP ProgressionMenuValidation requires Edit Mode with compilation complete.";
            checks = 0;
            Scene active = SceneManager.GetActiveScene();
            bool dirty = active.isDirty;
            Scene preview = EditorSceneManager.NewPreviewScene();
            string scope = new string('a', 64);
            string journal = Path.Combine(UnityEngine.Application.persistentDataPath, "TankDraft", "Progression", scope, "intent-v1.json");
            try
            {
                MetaCatalogAsset catalog = AssetDatabase.LoadAssetAtPath<MetaCatalogAsset>("Assets/TankDraft/Configs/Meta/Catalogs/MetaCatalog.asset");
                GameObject authored = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/UI/UIRoot.prefab");
                Check(catalog != null && catalog.ProgressionRules != null, "authored progression rules missing");
                Check(authored != null, "authored UI root missing");
                GameObject root = (GameObject)PrefabUtility.InstantiatePrefab(authored, preview);
                UIMainMenuRuntimeRoot runtime = root.GetComponent<UIMainMenuRuntimeRoot>();
                Check(runtime != null && runtime.Progression != null, "authored progression window missing");
                UIRegistry registry = root.GetComponent<UIRegistry>();
                Check(registry != null, "authored UI registry missing");
                registry.BuildMap();
                Check(registry.TryGetWindow("main_menu.progression", out UIWindow registeredProgression) && registeredProgression == runtime.Progression,
                    "progression window registry Id missing or duplicated");

                MetaDefinitions definitions = catalog.CreateDefinitions();
                var profiles = new ProfileService(definitions, catalog.CreateInitialProfile(), new Repository());
                profiles.Initialize();
                var fake = new FakeProgression(ProgressionUiRules.Load(catalog.ProgressionRules).Version);
                var presenter = new ProgressionPresenter(fake, profiles, catalog, runtime.Progression, scope);
                Check(presenter.IsAvailable, "valid account-scoped progression unavailable");

                if (File.Exists(journal)) throw new InvalidOperationException("Fixture journal scope is unexpectedly occupied.");
                presenter.OpenUnit("unit.mines");
                Check(Action(runtime.Progression).Button.interactable, "actions remain disabled after loaded state");
                Directory.CreateDirectory(OutputDirectory);
                Capture(root, preview, Path.Combine(OutputDirectory, "progression-unit.png"));
                CheckVisibleTextFits(runtime.Progression.gameObject);
                fake.ThrowOnce = true;
                Action(runtime.Progression).Click();
                Check(fake.Operations.Count == 1 && File.Exists(journal), "unconfirmed operation was not journaled");
                Guid stable = fake.Operations[0];
                presenter.OpenUnit("unit.mines");
                Check(fake.Operations.Count == 2 && fake.Operations[1] == stable && !File.Exists(journal), "retry did not retain operation id or clear completed journal");

                presenter.OpenShop();
                Check(runtime.Progression.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("Кристаллы") && text.text.Contains("Монеты")), "shop wallet summary missing");
                Check(runtime.Progression.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("1700 CO")), "shop authored price missing");
                Capture(root, preview, Path.Combine(OutputDirectory, "progression-shop.png"));
                CheckVisibleTextFits(runtime.Progression.gameObject);
                UIButtonView packAction = runtime.Progression.GetComponentsInChildren<UIButtonView>(true).Single(button => button.name.StartsWith("Action_") && button.GetComponentInChildren<TMP_Text>(true).text.Contains("100 GM"));
                packAction.Click();
                Check(Title(runtime.Progression).text == "ПАК ОТКРЫТ", "server-resolved pack page missing");
                Check(runtime.Progression.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("Всего: 111")), "pack page omits current server bits");
                Capture(root, preview, Path.Combine(OutputDirectory, "progression-pack.png"));
                CheckVisibleTextFits(runtime.Progression.gameObject);
                Action(runtime.Progression).Click();
                presenter.OpenUnit("unit.mines");

                fake.RejectOnce = true;
                Action(runtime.Progression).Click();
                Check(!File.Exists(journal), "definitive rejection retained journal");
                Check(runtime.Progression.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("Недостаточно")), "definitive rejection was not localized");

                int completed = 0;
                Check(presenter.ShowResult(RewardResult(), () => completed++), "two-page reward view unavailable");
                Check(Title(runtime.Progression).text == "ПОБЕДА", "mastery reward page missing");
                Capture(root, preview, Path.Combine(OutputDirectory, "progression-mastery.png"));
                CheckVisibleTextFits(runtime.Progression.gameObject);
                Action(runtime.Progression).Click();
                Check(Title(runtime.Progression).text == "НАГРАДА", "coin reward page missing");
                Check(runtime.Progression.GetComponentsInChildren<TMP_Text>(true).Any(text => text.text.Contains("570 CO")), "server coin amount missing");
                Capture(root, preview, Path.Combine(OutputDirectory, "progression-coins.png"));
                CheckVisibleTextFits(runtime.Progression.gameObject);
                Action(runtime.Progression).Click();
                Check(completed == 1, "reward completion callback missing");
                presenter.Dispose();

                foreach (UIButtonView action in runtime.Progression.GetComponentsInChildren<UIButtonView>(true).Where(button => button.name.StartsWith("Action_")))
                    UnityEngine.Object.DestroyImmediate(action.gameObject);
                runtime.Progression.Release();
                Check(true, "progression release tolerates destroyed action children");

                var teardownPresenter = new ProgressionPresenter(fake, profiles, catalog, runtime.Progression, scope);
                UnityEngine.Object.DestroyImmediate(root);
                teardownPresenter.Dispose();
                Check(true, "progression dispose tolerates destroyed window root");
                string status = "PASS " + checks + " progression UI editor assertions; images in " + OutputDirectory + ".";
                File.WriteAllText(Path.Combine(OutputDirectory, "progression-status.txt"), status);
                return status;
            }
            finally
            {
                if (File.Exists(journal)) File.Delete(journal);
                EditorSceneManager.ClosePreviewScene(preview);
                if (SceneManager.GetActiveScene() != active || active.isDirty != dirty) throw new InvalidOperationException("Progression UI validation changed the active scene or dirty state.");
            }
        }
    }
}
