using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using TankDraft.Bootstrap;
using TankDraft.Infrastructure;
using TankDraft.Match.Bootstrap;
using TankDraft.Match.Content;
using TankDraft.Match.Domain;
using TankDraft.Match.Presentation;
using TankDraft.Contracts.Battle;
using TankDraft.Simulation;
using TankDraft.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using VContainer;
using Vareiko.Foundation.UI;

namespace TankDraft.Match.Editor
{
    [InitializeOnLoad]
    public static class MatchValidation
    {
        const string Evidence = "Logs/TankDraftSetup/MatchQA/";
        const string Key = "TankDraft.MatchQA.";
        const string MenuPath = "Assets/TankDraft/Scenes/Frontend/MainMenu.unity";
        static double _next;
        static bool _captured;
        static MatchValidation()
        {
            EditorApplication.update += Update;
            UnityEngine.Application.logMessageReceived += OnLog;
        }

        static void OnLog(string text, string stack, LogType type)
        {
            if (SessionState.GetBool(Key + "active", false) && (type == LogType.Exception || type == LogType.Error || type == LogType.Assert))
                SessionState.SetString(Key + "error", text + "\n" + stack);
        }

        public static string Validate()
        {
            var settings = AssetDatabase.LoadAssetAtPath<MatchSettingsAsset>(MatchAuthoring.SettingsPath);
            settings.Validate();
            string domain = MatchDomainValidation.Run();
            var report = new StringBuilder(domain).AppendLine();
            var units = settings.CreateUnits();
            var deck = units.Select(x => x.Id).ToArray();
            var defs = settings.BattleCatalog.CreateDefinitions();
            var rules = settings.BattleCatalog.CreateRules();
            int rounds = 0, maxTicks = 0, reviews = 0;
            for (uint seed = 1; seed <= 40; seed++)
            {
                var match = new MatchService(settings.CreateRules(), units, deck, deck, "order.reinforce_armor", seed);
                int guard = 0;
                while (match.Phase != MatchPhase.MatchResult && match.Phase != MatchPhase.ReviewRequired && guard++ < 100)
                {
                    if (match.Phase == MatchPhase.Draft)
                    {
                        long token = match.ChoiceToken;
                        for (int side = 0; side < 2; side++)
                        {
                            if (match.Phase != MatchPhase.Draft || match.ChoiceToken != token)
                                break;
                            if (match.IsComeback && match.BonusSide != side || match.HasCommitted(side))
                                continue;
                            int index = (int)((seed + (uint)token + (uint)side) % 3);
                            bool accepted = side == 0 && token % 7 == 0 && match.CanUseOrder(0) ? match.TryUseOrder(side, token, out _) : match.TryChoose(side, token, index, out _);
                            if (!accepted)
                                throw new Exception("Legal scripted draft rejected.");
                        }
                    }
                    else if (match.Phase == MatchPhase.Battle)
                    {
                        using (var sim = new BattleSimulation(defs, rules, match.CreateScenario()))
                        {
                            while (sim.Outcome == BattleOutcome.Running && sim.Tick < 3600)
                                sim.Step();
                            if (sim.Outcome == BattleOutcome.Running)
                                throw new Exception("Seed " + seed + " round " + match.RoundNumber + ": QA watchdog, no outcome assigned.");
                            maxTicks = Math.Max(maxTicks, (int)sim.Tick);
                            rounds++;
                            match.ResolveBattle(sim.Outcome);
                            if (sim.Outcome == BattleOutcome.ReviewRequired)
                                reviews++;
                        }
                    }
                    else
                        match.Continue();
                }

                if (guard >= 100)
                    throw new Exception("Match progression did not terminate.");
                if (match.Phase == MatchPhase.MatchResult && Math.Max(match.Wins(0), match.Wins(1)) != 4)
                    throw new Exception("Match ended without four wins.");
            }

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/TankDraft/Prefabs/UI/Match/UIMatchScreen.prefab");
            prefab.GetComponent<UIMatchScreen>().Validate();
            var cards = prefab.GetComponentsInChildren<MatchCardView>(true);
            if (cards.Length != 3)
                throw new Exception("Exactly three authored draft cards required.");
            foreach (var card in cards)
                if (!card.GetComponent<LayoutElement>() || !card.transform.parent.GetComponent<HorizontalLayoutGroup>() || !PrefabUtility.GetCorrespondingObjectFromSource(card))
                    throw new Exception("Draft cards must be nested instances sized by a shared layout group.");
            report.AppendFormat("PASS 40 seeded matches, {0} rounds; longest {1:F2}s; simultaneous-review stops {2}. Shared card prefab/layout validated.\n", rounds, maxTicks * rules.TickSeconds, reviews);
            Directory.CreateDirectory(Evidence);
            File.WriteAllText(Evidence + "domain-simulation.txt", report.ToString());
            return report.ToString();
        }

        public static string Begin()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed || UnityEngine.SceneManagement.SceneManager.GetActiveScene().isDirty)
                throw new Exception("Idle compiled editor and saved scene required.");
            Directory.CreateDirectory(Evidence);
            SetResolution(576, 1280);
            var scene = EditorSceneManager.OpenScene(MenuPath);
            const string qaSettingsPath = "Assets/TankDraft/Configs/Diagnostics/QA/MatchProfileSettings.asset";
            var settings = AssetDatabase.LoadAssetAtPath<LocalProfileSettings>(qaSettingsPath);
            if (!settings)
            {
                settings = ScriptableObject.CreateInstance<LocalProfileSettings>();
                AssetDatabase.CreateAsset(settings, qaSettingsPath);
            }

            var data = new SerializedObject(settings);
            data.FindProperty("_fileName").stringValue = "qa-match-" + Guid.NewGuid().ToString("N") + ".json";
            data.ApplyModifiedPropertiesWithoutUndo();
            var scope = UnityEngine.Object.FindFirstObjectByType<MainMenuLifetimeScope>();
            var scopeData = new SerializedObject(scope);
            scopeData.FindProperty("_profileSettings").objectReferenceValue = settings;
            scopeData.ApplyModifiedPropertiesWithoutUndo();
            EditorSceneManager.SaveScene(scene, "Assets/TankDraft/Scenes/Diagnostics/QA/MatchMenuQA.unity");
            AssetDatabase.SaveAssets();
            SessionState.SetString(Key + "profilePath", settings.GetPath());
            SessionState.SetBool(Key + "combatCaptured", false);
            SessionState.SetString(Key + "error", "");
            SessionState.SetString(Key + "start", DateTime.UtcNow.ToString("O"));
            SessionState.SetInt(Key + "phase", 0);
            SessionState.SetInt(Key + "checks", 0);
            SessionState.SetBool(Key + "active", true);
            File.WriteAllText(Evidence + "playmode.txt", "RUNNING");
            EditorApplication.isPlaying = true;
            return "Full match Play Mode QA queued with an isolated profile.";
        }

        static void Check(bool valid, string text)
        {
            if (!valid)
                throw new Exception(text);
            SessionState.SetInt(Key + "checks", SessionState.GetInt(Key + "checks", 0) + 1);
        }

        static void Capture(string name)
        {
            ScreenCapture.CaptureScreenshot(Path.GetFullPath(Evidence + name + ".png"));
            _captured = true;
        }

        static void Click(Component parent, string name)
        {
            var button = parent.GetComponentsInChildren<UIButtonView>(true).Single(x => x.name == name);
            Check(button.isActiveAndEnabled && button.Interactable, "Button disabled: " + name);
            button.Button.onClick.Invoke();
        }

        static void Update()
        {
            if (!SessionState.GetBool(Key + "active", false) || EditorApplication.isCompiling || EditorApplication.timeSinceStartup < _next)
                return;
            _next = EditorApplication.timeSinceStartup + .3;
            try
            {
                if (DateTime.UtcNow - DateTime.Parse(SessionState.GetString(Key + "start", "")) > TimeSpan.FromMinutes(4))
                    throw new Exception("QA watchdog elapsed; no game outcome was assigned.");
                string error = SessionState.GetString(Key + "error", "");
                if (error.Length > 0)
                    throw new Exception(error);
                if (_captured)
                {
                    _captured = false;
                    return;
                }

                int phase = SessionState.GetInt(Key + "phase", 0);
                if (!EditorApplication.isPlaying)
                {
                    if (phase == 5)
                    {
                        SessionState.SetBool(Key + "active", false);
                        SetResolution(576, 1280);
                        EditorSceneManager.OpenScene(MenuPath);
                        File.WriteAllText(Evidence + "playmode.txt", "PASS " + SessionState.GetInt(Key + "checks", 0) + " assertions: menu launch, empty army, draft/order, complete combat match to four wins, results, menu return, unchanged profile, portrait layout, error-free teardown. Custom Editor harness, not Test Runner/device/network validation.");
                    }

                    return;
                }

                if (phase == 0 || phase == 4)
                {
                    var menu = UnityEngine.Object.FindFirstObjectByType<MainMenuLifetimeScope>();
                    if (!menu || menu.Container == null)
                        return;
                    var startup = menu.Container.Resolve<MainMenuStartup>();
                    if (startup.Failure != null)
                        throw startup.Failure;
                    if (!startup.IsReady)
                        return;
                    var view = UnityEngine.Object.FindFirstObjectByType<UIMainMenuRuntimeRoot>();
                    if (phase == 0)
                    {
                        Check(startup.Profile.Current.UnitIds[0] == "unit.mines", "QA initial deck wrong");
                        SessionState.SetString(Key + "profileBytes", File.ReadAllText(SessionState.GetString(Key + "profilePath", "")));
                        Click(view.Arena, "StartBattle_Button");
                        SessionState.SetInt(Key + "phase", 1);
                    }
                    else
                    {
                        Check(view.Arena.IsVisible, "Return did not show main menu");
                        Check(File.ReadAllText(SessionState.GetString(Key + "profilePath", "")) == SessionState.GetString(Key + "profileBytes", ""), "Match mutated the saved profile");
                        Capture("06-return-menu");
                        SessionState.SetInt(Key + "phase", 5);
                        EditorApplication.isPlaying = false;
                    }

                    return;
                }

                var scope = UnityEngine.Object.FindFirstObjectByType<MatchLifetimeScope>();
                if (!scope || scope.Container == null)
                    return;
                var session = scope.Container.Resolve<MatchSession>();
                if (session.Failure != null)
                    throw session.Failure;
                if (!session.IsReady)
                    return;
                var screen = UnityEngine.Object.FindFirstObjectByType<UIMatchScreen>();
                var match = session.Match;
                if (phase == 1)
                {
                    Check(match.Phase == MatchPhase.Draft && match.GetArmy(0).Count == 0 && match.GetArmy(1).Count == 0, "Match must start with empty armies and hidden pending bot choice");
                    Check(screen.GetComponentsInChildren<MatchCardView>().Length == 3, "Draft does not display three cards");
                    Check(screen.GetComponentsInChildren<UIMatchHudWindow>(true).Length == 1 && screen.GetComponentsInChildren<UIMatchDraftWindow>(true).Length == 1, "Typed match window hierarchy missing");
                    Check(!screen.GetComponentInChildren<HorizontalLayoutGroup>().enabled, "Draft layout remains enabled while idle");
                    CheckLayout(screen);
                    Capture("01-initial-draft");
                    SessionState.SetInt(Key + "phase", 2);
                    return;
                }

                if (phase == 2)
                {
                    if (match.Phase == MatchPhase.Draft)
                    {
                        if (match.RoundNumber == 1 && match.ChoiceNumber == 2 && match.CanUseOrder(0))
                            Click(screen, "Order_Button");
                        else
                        {
                            var card = screen.GetComponentsInChildren<MatchCardView>()[0];
                            var button = card.GetComponent<UIButtonView>();
                            Check(button.Interactable, "Draft card disabled");
                            button.Button.onClick.Invoke();
                        }

                        if (match.Phase == MatchPhase.Battle)
                        {
                            Check(session.Simulation != null && session.States.Count > 0, "Battle has no simulation/views");
                            Capture("02-round-start");
                        }
                    }
                    else if (match.Phase == MatchPhase.Battle)
                    {
                        session.AdvanceForValidation(90);
                        if (session.Simulation.TotalShots > 0 && !SessionState.GetBool(Key + "combatCaptured", false))
                        {
                            Capture("03-combat");
                            SessionState.SetBool(Key + "combatCaptured", true);
                        }
                    }
                    else if (match.Phase == MatchPhase.RoundResult)
                    {
                        Check(match.Wins(0) + match.Wins(1) == match.RoundNumber, "Round score mismatch");
                        Capture("04-round-result");
                        Click(screen, "Next_Button");
                    }
                    else if (match.Phase == MatchPhase.MatchResult)
                    {
                        Check(Math.Max(match.Wins(0), match.Wins(1)) == 4, "Final match score wrong");
                        SetResolution(576, 1024);
                        SessionState.SetInt(Key + "phase", 3);
                    }
                    else
                        throw new Exception("Selected QA match stopped for simultaneous-outcome review.");
                    return;
                }

                if (phase == 3)
                {
                    CheckLayout(screen);
                    Check(screen.GetComponentsInChildren<MatchCardView>().Length == 0, "Draft still visible after final win");
                    Capture("05-final-result-9x16");
                    SessionState.SetInt(Key + "phase", 6);
                    return;
                }

                if (phase == 6)
                {
                    Click(screen, "Menu_Button");
                    SessionState.SetInt(Key + "phase", 4);
                }
            }
            catch (Exception exception)
            {
                SessionState.SetBool(Key + "active", false);
                File.WriteAllText(Evidence + "playmode.txt", "FAIL " + exception);
                EditorApplication.isPlaying = false;
                Debug.LogException(exception);
            }
        }

        static void CheckLayout(UIMatchScreen screen)
        {
            // EditorApplication.update can expose Scene View dimensions through Screen.
            // Validate against the actual Game View render target selected by this harness.
            var gameViewType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.GameView");
            var gameView = EditorWindow.GetWindow(gameViewType);
            var target = (Vector2)gameViewType.GetProperty("targetSize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).GetValue(gameView);
            Check(target.x == 576 && (target.y == 1280 || target.y == 1024), "Unexpected portrait Game View target: " + target);
            Canvas.ForceUpdateCanvases();
            foreach (var button in screen.GetComponentsInChildren<UIButtonView>())
            {
                var corners = new Vector3[4];
                button.GetComponent<RectTransform>().GetWorldCorners(corners);
                Check(corners[0].x >= -1 && corners[2].x <= target.x + 1 && corners[0].y >= -1 && corners[2].y <= target.y + 1, "Button outside portrait screen: " + button.name + " screen=" + Screen.width + "x" + Screen.height + " corners=" + corners[0] + ".." + corners[2] + " canvasScale=" + button.GetComponentInParent<Canvas>().scaleFactor);
            }
        }

        static void SetResolution(int width, int height)
        {
            Type.GetType("TankDraft.Editor.UI.MainMenuPlayModeValidation, TankDraft.UI.Editor", true).GetMethod("SetResolution", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { width, height });
        }
    }
}
