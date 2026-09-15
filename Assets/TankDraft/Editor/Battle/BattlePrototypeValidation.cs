using System;
using System.IO;
using System.Linq;
using System.Reflection;
using TankDraft.BattleBootstrap;
using TankDraft.BattleContent;
using TankDraft.BattlePresentation;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Vareiko.Foundation.UI;

namespace TankDraft.Editor.Battle
{
    [InitializeOnLoad]
    public static class BattlePrototypeValidation
    {
        const string Key = "TankDraft.BattlePrototypeQA.";
        const string Evidence = "Logs/TankDraftSetup/BattleQA/";
        static double _next;
        static bool _captured;
        static BattlePrototypeValidation()
        {
            EditorApplication.update += Update;
            UnityEngine.Application.logMessageReceived += OnLog;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (SessionState.GetBool(Key + "active", false) && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                SessionState.SetString(Key + "error", message + "\n" + stack);
        }

        public static string Validate()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>(BattlePrototypeAuthoring.CatalogPath);
            var views = AssetDatabase.LoadAssetAtPath<BattleViewCatalogAsset>(BattlePrototypeAuthoring.ViewsPath);
            var settings = AssetDatabase.LoadAssetAtPath<BattlePresentationSettings>(BattlePrototypeAuthoring.PresentationPath);
            if (!catalog || !views || !settings)
                throw new Exception("Missing battle configuration assets.");
            catalog.Validate();
            settings.Validate();
            views.Validate(catalog.CreateDefinitions());
            foreach (var entry in views.Entries)
                foreach (var renderer in entry.prefab.GetComponentsInChildren<SpriteRenderer>(true))
                    if (renderer.sprite == null)
                        throw new Exception("Missing authored sprite: " + entry.definitionId + "/" + renderer.name);
            var scenarios = new BattleScenarioDefinition[catalog.ScenarioCount];
            for (int i = 0; i < scenarios.Length; i++)
                scenarios[i] = catalog.CreateScenario(i);
            string result = SimulationValidation.Run(catalog.CreateDefinitions(), catalog.CreateRules(), scenarios);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/Diagnostics/Battle/UIBattlePrototypeRoot.prefab");
            var screen = prefab.GetComponentInChildren<UIBattlePrototypeScreen>(true);
            screen.Validate();
            foreach (var button in screen.GetComponentsInChildren<UIButtonView>(true))
            {
                if (!button.GetComponent<LayoutElement>() || !button.transform.parent.GetComponent<HorizontalLayoutGroup>())
                    throw new Exception("Playback button must be sized by the shared layout.");
                if (PrefabUtility.GetCorrespondingObjectFromSource(button) == null)
                    throw new Exception("Playback button must be a nested shared prefab.");
            }

            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "simulation.txt", "PASS " + result);
            return "PASS " + result + " Authored catalogs, anchors, shared buttons and layout validated.";
        }

        public static string Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
                throw new Exception("Idle compiled editor required.");
            if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new Exception("Save the current scene before QA.");
            Directory.CreateDirectory(Evidence);
            SetResolution(576, 1280);
            EditorSceneManager.OpenScene(BattlePrototypeAuthoring.ScenePath);
            SessionState.SetString(Key + "error", "");
            SessionState.SetBool(Key + "active", true);
            SessionState.SetInt(Key + "phase", 0);
            SessionState.SetInt(Key + "checks", 0);
            SessionState.SetString(Key + "start", DateTime.UtcNow.ToString("O"));
            File.WriteAllText(Evidence + "playmode.txt", "RUNNING");
            EditorApplication.isPlaying = true;
            return "Battle Play Mode QA queued.";
        }

        static void Check(bool ok, string message)
        {
            if (!ok)
                throw new Exception(message);
            SessionState.SetInt(Key + "checks", SessionState.GetInt(Key + "checks", 0) + 1);
        }

        static void Click(UIBattlePrototypeScreen screen, string name)
        {
            var button = screen.GetComponentsInChildren<UIButtonView>(true).Single(x => x.name == name);
            Check(button.isActiveAndEnabled && button.Interactable, "Disabled button " + name);
            button.Button.onClick.Invoke();
        }

        static void Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + name + ".png"));
            _captured = true;
        }

        static void Update()
        {
            if (!SessionState.GetBool(Key + "active", false) || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < _next)
                return;
            _next = EditorApplication.timeSinceStartup + .25;
            try
            {
                if (DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Key + "start", "")) > TimeSpan.FromMinutes(4))
                    throw new Exception("QA watchdog exceeded; no game outcome was assigned.");
                if (SessionState.GetString(Key + "error", "").Length > 0)
                    throw new Exception("Unity logged an error during battle QA: " + SessionState.GetString(Key + "error", ""));
                if (_captured)
                {
                    _captured = false;
                    return;
                }

                int phase = SessionState.GetInt(Key + "phase", 0);
                if (!EditorApplication.isPlaying)
                {
                    if (phase == 15)
                    {
                        SessionState.SetBool(Key + "active", false);
                        SetResolution(576, 1280);
                        EditorSceneManager.OpenScene(BattlePrototypeAuthoring.ScenePath);
                        File.WriteAllText(Evidence + "playmode.txt", "PASS " + SessionState.GetInt(Key + "checks", 0) + " assertions: local battle, projectiles/zones, pause/step/reset, command, dense scenario, authored pools, portrait layout and error-free teardown. Custom Editor harness; not Unity Test Runner or device/network QA.");
                    }

                    return;
                }

                var scope = UnityEngine.Object.FindFirstObjectByType<BattlePrototypeLifetimeScope>();
                if (!scope || scope.Container == null)
                    return;
                var session = scope.Container.Resolve<BattlePrototypeSession>();
                if (session.Failure != null)
                    throw new Exception("Battle startup/runtime failed.", session.Failure);
                if (!session.IsReady)
                    return;
                var world = UnityEngine.Object.FindFirstObjectByType<BattleWorldView>();
                var screen = UnityEngine.Object.FindFirstObjectByType<UIBattlePrototypeScreen>();
                switch (phase)
                {
                    case 0:
                        Check(world.ActiveUnitViews == 28 && session.Simulation.Tick == 0, "Standard initial formation");
                        Check(screen.IsVisible, "Battle HUD hidden");
                        SessionState.SetInt(Key + "pool", world.CreatedViewCount);
                        CheckLayout(screen);
                        Capture("01-ready");
                        break;
                    case 1:
                        Click(screen, "Start_Button");
                        break;
                    case 2:
                        if (session.Simulation.Tick < 130)
                            return;
                        Check(session.Simulation.TotalShots > 0 && session.Simulation.TotalImpacts > 0, "No shots/impacts");
                        Check(world.ActiveProjectileViews > 0, "No visible projectiles at battle sample");
                        Click(screen, "Pause_Button");
                        SessionState.SetInt(Key + "tick", (int)session.Simulation.Tick);
                        Capture("02-combat-paused");
                        break;
                    case 3:
                        Check(session.IsPaused && session.Simulation.Tick == SessionState.GetInt(Key + "tick", -1), "Pause still advances simulation");
                        Click(screen, "Step_Button");
                        Check(session.Simulation.Tick == SessionState.GetInt(Key + "tick", -1) + 1, "Step must advance exactly once");
                        Click(screen, "DebugZone_Button");
                        Click(screen, "Step_Button");
                        Check(world.ActiveZoneViews > 0, "Debug zone not displayed on next tick");
                        Capture("03-zones");
                        break;
                    case 4:
                        Click(screen, "Start_Button");
                        break;
                    case 5:
                        if (session.Simulation.Outcome == BattleOutcome.Running)
                            return;
                        Check(session.Simulation.Outcome != BattleOutcome.ReviewRequired, "Standard scenario requires simultaneous-outcome review");
                        Check(session.Simulation.TotalZoneTicks > 0 && session.Simulation.TotalDeaths > 0, "No zone damage/deaths");
                        Check(world.ActiveProjectileViews == 0 && world.ActiveZoneViews == 0, "Transient views left after outcome");
                        Check(session.Simulation.Tick * session.Simulation.Rules.TickSeconds < 60, "Balance exceeds one minute");
                        Capture("04-result");
                        break;
                    case 6:
                        Click(screen, "Reset_Button");
                        Check(session.Simulation.Tick == 0 && world.ActiveUnitViews == 28 && world.ActiveEffectViews == 0, "Reset did not clear battle state");
                        SessionState.SetInt(Key + "pool", world.CreatedViewCount);
                        Click(screen, "Start_Button");
                        break;
                    case 7:
                        if (session.Simulation.Tick < 130)
                            return;
                        Click(screen, "Reset_Button");
                        Check(world.CreatedViewCount == SessionState.GetInt(Key + "pool", -1), "Repeated battle grew prefab pool");
                        Click(screen, "Scenario_Button");
                        Check(world.ActiveUnitViews == 103 && session.Simulation.Tick == 0, "Dense initial formation");
                        Capture("05-dense-ready");
                        break;
                    case 8:
                        Click(screen, "Start_Button");
                        break;
                    case 9:
                        if (session.Simulation.Tick < 130)
                            return;
                        Click(screen, "Pause_Button");
                        Check(world.ActiveUnitViews > 20 && session.Simulation.TotalShots > 0, "Dense battle not active");
                        Capture("06-dense-combat");
                        break;
                    case 10:
                        SetResolution(576, 1024);
                        break;
                    case 11:
                        Check(UnityEngine.Screen.width == 576 && UnityEngine.Screen.height == 1024, "9:16 resolution mismatch");
                        CheckLayout(screen);
                        var unit = world.GetComponentsInChildren<BattleEntityView>().First(v => v.RuntimeId > 0 && v.name.Contains("heavy"));
                        SessionState.SetInt(Key + "artId", unit.RuntimeId);
                        unit.VisualRoot.localScale = new Vector3(1.2f, 1.2f, 1);
                        Capture("07-dense-9x16");
                        break;
                    case 12:
                        var changed = world.GetComponentsInChildren<BattleEntityView>().Single(v => v.RuntimeId == SessionState.GetInt(Key + "artId", -1));
                        Check(Mathf.Abs(changed.VisualRoot.localScale.x - 1.2f) < .001f, "Rendering overwrites art scale");
                        Check(session.Simulation.Tick > 0 && session.IsPaused, "Art mutation changed session state");
                        Click(screen, "Start_Button");
                        break;
                    case 13:
                        if (session.Simulation.Outcome == BattleOutcome.Running)
                            return;
                        Check(session.Simulation.Outcome != BattleOutcome.ReviewRequired, "Dense outcome requires review");
                        Check(session.Simulation.Tick * session.Simulation.Rules.TickSeconds < 60, "Dense balance exceeds one minute");
                        Capture("08-dense-result");
                        break;
                    case 14:
                        SessionState.SetInt(Key + "phase", 15);
                        EditorApplication.isPlaying = false;
                        return;
                }

                SessionState.SetInt(Key + "phase", phase + 1);
            }
            catch (Exception e)
            {
                File.WriteAllText(Evidence + "playmode.txt", "FAIL phase " + SessionState.GetInt(Key + "phase", 0) + "\n" + e);
                SessionState.SetBool(Key + "active", false);
                EditorApplication.isPlaying = false;
                Debug.LogException(e);
            }
        }

        static void CheckLayout(UIBattlePrototypeScreen screen)
        {
            Canvas.ForceUpdateCanvases();
            foreach (var row in screen.GetComponentsInChildren<HorizontalLayoutGroup>())
            {
                var buttons = row.GetComponentsInChildren<UIButtonView>();
                if (buttons.Length == 0)
                    continue;
                float width = buttons[0].GetComponent<RectTransform>().rect.width;
                foreach (var button in buttons)
                {
                    var rect = button.GetComponent<RectTransform>();
                    Check(Mathf.Abs(rect.rect.width - width) < .1f, "Repeated controls have unequal width");
                    var corners = new Vector3[4];
                    rect.GetWorldCorners(corners);
                    Check(corners[0].y >= 0 && corners[1].y <= UnityEngine.Screen.height, "Controls outside screen");
                }
            }

            var camera = Camera.main;
            var rules = AssetDatabase.LoadAssetAtPath<BattleScenarioCatalogAsset>(BattlePrototypeAuthoring.CatalogPath).CreateRules();
            var a = camera.WorldToViewportPoint(new Vector3(-rules.HalfWidth, -rules.HalfHeight));
            var b = camera.WorldToViewportPoint(new Vector3(rules.HalfWidth, rules.HalfHeight));
            Check(a.x >= 0 && a.y >= 0 && b.x <= 1 && b.y <= 1, "Arena clipped by camera");
        }

        static void SetResolution(int width, int height)
        {
            var type = Type.GetType("TankDraft.Editor.UI.MainMenuPlayModeValidation, TankDraft.UI.Editor", true);
            type.GetMethod("SetResolution", BindingFlags.NonPublic | BindingFlags.Static).Invoke(null, new object[] { width, height });
        }
    }
}
